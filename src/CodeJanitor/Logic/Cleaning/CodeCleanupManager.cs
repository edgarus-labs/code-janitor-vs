using CodeJanitor.Helpers;
using CodeJanitor.Logic.Ai;
using CodeJanitor.Logic.Formatting;
using CodeJanitor.Logic.Reorganizing;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Model;
using CodeJanitor.Model.CodeItems;
using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.CleanupProgress;
using CodeJanitor.UI.Enumerations;
using EnvDTE;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Document = EnvDTE.Document;
using TextDocument = EnvDTE.TextDocument;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A manager class for cleaning up code.
/// </summary>
/// <remarks>
///
/// Note: All POSIXRegEx text replacements search against '\n' but insert/replace with
/// Environment.NewLine. This handles line endings correctly.
/// </remarks>
internal sealed class CodeCleanupManager
{
    /// <summary>
    /// DelegateSourceTransformation represents a named transformation operation whose logic is supplied through a delegate and applied to produce results.
    /// </summary>
    private sealed class DelegateSourceTransformation : ISourceTransformation
    {
        private readonly Func<string, string> _apply;

        internal DelegateSourceTransformation(string name, Func<string, string> apply)
        {
            Name = name;
            _apply = apply;
        }

        /// <summary>
        /// Gets the name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Invokes `_apply` on the source and returns its result, falling back to the original source when `_apply` returns null, with no exceptions thrown by this method itself.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <returns>A string value produced by this method.</returns>
        public string Apply(string source)
        {
            return _apply(source) ?? source;
        }
    }

    /// <summary>
    /// Represents the outcome of a headless cleanup operation, indicating whether the operation was not applicable, produced no changes, or resulted in changes.
    /// </summary>
    internal enum HeadlessCleanupResult
    {
        NotApplicable,
        NoChanges,
        Changed
    }

    /// <summary>
    /// HeadlessPreCleanupOutcome represents the result of a headless pre-cleanup operation, capturing its status, any files created, and whether a split operation occurred.
    /// </summary>
    internal struct HeadlessPreCleanupOutcome
    {
        /// <summary>
        /// The result.
        /// </summary>
        internal HeadlessCleanupResult Result;

        /// <summary>
        /// The created files.
        /// </summary>
        internal List<string> CreatedFiles;

        /// <summary>
        /// The split operation occurred.
        /// </summary>
        internal bool SplitOperationOccurred;
    }

    /// <summary>
    /// statistics structure that aggregates counts of items processed during a cleanup operation, tracking changed, no-op, failed, editor-related, and split-operation outcomes.
    /// </summary>
    internal struct CleanupExecutionStats
    {
        /// <summary>
        /// Gets or sets the headless changed items.
        /// </summary>
        internal int HeadlessChangedItems { get; set; }

        /// <summary>
        /// Gets or sets the headless no op items.
        /// </summary>
        internal int HeadlessNoOpItems { get; set; }

        /// <summary>
        /// Gets or sets the failed items.
        /// </summary>
        internal int FailedItems { get; set; }

        /// <summary>
        /// Gets or sets the editor items.
        /// </summary>
        internal int EditorItems { get; set; }

        /// <summary>
        /// Gets or sets the split operations.
        /// </summary>
        internal int SplitOperations { get; set; }

        /// <summary>
        /// Gets or sets the split created files.
        /// </summary>
        internal int SplitCreatedFiles { get; set; }

        /// <summary>
        /// Gets or sets the number of C# files changed by .editorconfig/Roslyn diagnostic cleanup.
        /// </summary>
        internal int DiagnosticChangedItems { get; set; }

        /// <summary>
        /// Gets or sets the number of C# files left with unresolved actionable diagnostics after
        /// .editorconfig/Roslyn diagnostic cleanup (unsupported, rejected as unsafe, or not converged).
        /// </summary>
        internal int DiagnosticUnresolvedItems { get; set; }

        /// <summary>
        /// Gets the total processed items.
        /// </summary>
        internal int TotalProcessedItems => HeadlessChangedItems + HeadlessNoOpItems + EditorItems + FailedItems;
    }

    private readonly CodeJanitorPackage _package;
    private readonly object _cleanupStatsLock = new object();

    private readonly CodeModelManager _codeModelManager;
    private readonly CodeReorganizationManager _codeReorganizationManager;
    private readonly CodeReorganizationAvailabilityLogic _codeReorganizationAvailabilityLogic;
    private readonly CommandHelper _commandHelper;
    private readonly TopLevelTypeToFileSplitPlanner _topLevelTypeToFileSplitPlanner;
    private readonly TopLevelTypeToFileSplitFileProcessor _topLevelTypeToFileSplitFileProcessor;

    private readonly CodeCleanupAvailabilityLogic _codeCleanupAvailabilityLogic;
    private readonly CommentFormatLogic _commentFormatLogic;
    private readonly CollectionExpressionLogic _collectionExpressionLogic;
    private readonly InsertBlankLinePaddingLogic _insertBlankLinePaddingLogic;
    private readonly InsertExplicitAccessModifierLogic _insertExplicitAccessModifierLogic;
    private readonly InsertWhitespaceLogic _insertWhitespaceLogic;
    private readonly JsonSerializerOptionsReuseLogic _jsonSerializerOptionsReuseLogic;
    private readonly FileHeaderLogic _fileHeaderLogic;
    private readonly FileScopedNamespaceLogic _fileScopedNamespaceLogic;
    private readonly UsingDirectivePlacementLogic _usingDirectivePlacementLogic;
    private readonly VarWhenApparentLogic _varWhenApparentLogic;
    private readonly ReadonlyFieldLogic _readonlyFieldLogic;
    private readonly RazorFormatterLogic _razorFormatterLogic;
    private readonly ReturnThrowBlankLinePaddingLogic _returnThrowBlankLinePaddingLogic;
    private readonly NullCheckPatternMatchingLogic _nullCheckPatternMatchingLogic;
    private readonly SealedClassLogic _sealedClassLogic;
    private readonly AiXmlDocumentationLogic _aiXmlDocumentationLogic;
    private readonly SingleStatementLambdaLogic _singleStatementLambdaLogic;
    private readonly RemoveRegionLogic _removeRegionLogic;
    private readonly RemoveWhitespaceLogic _removeWhitespaceLogic;
    private readonly RemoveByteOrderMarkLogic _removeByteOrderMarkLogic;
    private readonly UpdateLogic _updateLogic;
    private readonly EditorConfigDiagnosticCleanupLogic _editorConfigDiagnosticCleanupLogic;
    private readonly UsingStatementCleanupLogic _usingStatementCleanupLogic;

    private static readonly CachedSettingSet<string> OtherCleaningCommands =
        new CachedSettingSet<string>(() => Settings.Default.ThirdParty_OtherCleaningCommandsExpression,
                                     expression =>
                                     expression.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries)
                                               .Select(x => x.Trim())
                                               .Where(y => !string.IsNullOrEmpty(y))
                                               .ToList());

    private CleanupExecutionStats _cleanupExecutionStats;

    /// <summary>
    /// The singleton instance of the <see cref="CodeCleanupManager" /> class.
    /// </summary>
    private static CodeCleanupManager _instance;

    /// <summary>
    /// Gets an instance of the <see cref="CodeCleanupManager" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>An instance of the <see cref="CodeCleanupManager" /> class.</returns>
    internal static CodeCleanupManager GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new CodeCleanupManager(package));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeCleanupManager" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    private CodeCleanupManager(CodeJanitorPackage package)
    {
        _package = package;

        _codeModelManager = CodeModelManager.GetInstance(_package);
        _codeReorganizationManager = CodeReorganizationManager.GetInstance(_package);
        _codeReorganizationAvailabilityLogic = CodeReorganizationAvailabilityLogic.GetInstance(_package);
        _commandHelper = CommandHelper.GetInstance(_package);
        _topLevelTypeToFileSplitPlanner = new TopLevelTypeToFileSplitPlanner();
        _topLevelTypeToFileSplitFileProcessor = new TopLevelTypeToFileSplitFileProcessor(_topLevelTypeToFileSplitPlanner);

        _codeCleanupAvailabilityLogic = CodeCleanupAvailabilityLogic.GetInstance(_package);
        _commentFormatLogic = CommentFormatLogic.GetInstance(_package);
        _collectionExpressionLogic = CollectionExpressionLogic.GetInstance(_package);
        _jsonSerializerOptionsReuseLogic = JsonSerializerOptionsReuseLogic.GetInstance(_package);
        _insertBlankLinePaddingLogic = InsertBlankLinePaddingLogic.GetInstance(_package);
        _insertExplicitAccessModifierLogic = InsertExplicitAccessModifierLogic.GetInstance();
        _insertWhitespaceLogic = InsertWhitespaceLogic.GetInstance(_package);
        _editorConfigDiagnosticCleanupLogic = EditorConfigDiagnosticCleanupLogic.GetInstance(_package);
        _fileHeaderLogic = FileHeaderLogic.GetInstance(_package);
        _fileScopedNamespaceLogic = FileScopedNamespaceLogic.GetInstance(_package);
        _usingDirectivePlacementLogic = UsingDirectivePlacementLogic.GetInstance(_package);
        _varWhenApparentLogic = VarWhenApparentLogic.GetInstance(_package);
        _readonlyFieldLogic = ReadonlyFieldLogic.GetInstance(_package);
        _razorFormatterLogic = RazorFormatterLogic.GetInstance(_package);
        _returnThrowBlankLinePaddingLogic = ReturnThrowBlankLinePaddingLogic.GetInstance(_package);
        _nullCheckPatternMatchingLogic = NullCheckPatternMatchingLogic.GetInstance(_package);
        _sealedClassLogic = SealedClassLogic.GetInstance(_package);
        _aiXmlDocumentationLogic = AiXmlDocumentationLogic.GetInstance(_package);
        _singleStatementLambdaLogic = SingleStatementLambdaLogic.GetInstance(_package);
        _removeRegionLogic = RemoveRegionLogic.GetInstance(_package);
        _removeWhitespaceLogic = RemoveWhitespaceLogic.GetInstance(_package);
        _removeByteOrderMarkLogic = RemoveByteOrderMarkLogic.GetInstance(_package);
        _updateLogic = UpdateLogic.GetInstance(_package);
        _usingStatementCleanupLogic = UsingStatementCleanupLogic.GetInstance(_package);
    }

    /// <summary>
    /// Attempts to run code cleanup on the specified project item.
    /// </summary>
    /// <param name="projectItem">The project item for cleanup.</param>
    internal void Cleanup(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!_codeCleanupAvailabilityLogic.CanCleanupProjectItem(projectItem)) return;

        // Instrumentation for BL-018: measure per-item cleanup cost and whether the document
        // had to be opened by cleanup (opening documents is the primary performance concern).
        var stopwatch = Stopwatch.StartNew();

        var projectItemFileName = projectItem.GetFileName();

        // Skip the disk-based headless path for documents that are already open - the editor
        // buffer is then the source of truth and cleanup must operate on the live document.
        bool wasOpen = projectItem.IsOpen[Constants.vsViewKindTextView] || projectItem.IsOpen[Constants.vsViewKindCode];
        var headlessResult = HeadlessCleanupResult.NotApplicable;
        var usingsMoveOutcome = UsingsMoveOutcome.NotApplicable;
        if (!wasOpen)
        {
            // The semantic steps (using directive placement, class sealing, null check conversion) need the Visual
            // Studio workspace and run first, so the headless steps (header, using organization, type splitting) see
            // their result.
            usingsMoveOutcome = ThreadHelper.JoinableTaskFactory.Run(() => _usingDirectivePlacementLogic.PlaceUsingDirectivesAsync(projectItem));
            var classesSealed = ThreadHelper.JoinableTaskFactory.Run(() => _sealedClassLogic.SealWhenSafeAsync(projectItem));
            var nullChecksConverted = ThreadHelper.JoinableTaskFactory.Run(() => _nullCheckPatternMatchingLogic.ConvertWhenSafeAsync(projectItem));
            headlessResult = TryRunHeadlessPreCleanupForCSharp(projectItem);
            if ((usingsMoveOutcome == UsingsMoveOutcome.Moved || classesSealed || nullChecksConverted) && headlessResult == HeadlessCleanupResult.NoChanges)
            {
                headlessResult = HeadlessCleanupResult.Changed;
            }
        }

        if (headlessResult != HeadlessCleanupResult.NotApplicable && !RequiresEditorCleanupForCSharp())
        {
            if (headlessResult == HeadlessCleanupResult.Changed)
            {
                _cleanupExecutionStats.HeadlessChangedItems++;
            }
            else
            {
                _cleanupExecutionStats.HeadlessNoOpItems++;
            }

            // Diagnostic cleanup runs after the headless cleanup, against the file it wrote.
            ThreadHelper.JoinableTaskFactory.Run(() => RunDiagnosticCleanupAsync(projectItem));
            ThreadHelper.JoinableTaskFactory.Run(() => RunXmlDocumentationDuringCleanupAsync(projectItem));

            stopwatch.Stop();
            OutputWindowHelper.DiagnosticWriteLine(
                $"CodeCleanupManager.Cleanup for '{projectItem.Name}' took {stopwatch.ElapsedMilliseconds}ms (openedByCleanup: False, headlessOnly: True, changed: {headlessResult == HeadlessCleanupResult.Changed})");

            return;
        }

        // Attempt to open the document if not already opened.
        if (!wasOpen)
        {
            try
            {
                projectItem.Open(Constants.vsViewKindTextView);
            }
            catch (Exception ex)
            {
                OutputWindowHelper.WarningWriteLine(
                    $"Unable to open '{projectItemFileName}' for editor cleanup: {ex.Message}");
            }
        }

        if (projectItem.Document is not null)
        {
            if (CleanupDocument(projectItem.Document, usingsMoveOutcome == UsingsMoveOutcome.LeftInPlace, semanticStepsDone: !wasOpen))
            {
                ThreadHelper.JoinableTaskFactory.Run(() => RunXmlDocumentationDuringCleanupAsync(projectItem));
            }

            // Close the document if it was opened for cleanup.
            if (Settings.Default.Cleaning_AutoSaveAndCloseIfOpenedByCleanup && !wasOpen)
            {
                projectItem.Document.Close(vsSaveChanges.vsSaveChangesYes);
            }
        }
        else
        {
            RecordCleanupFailure(
                projectItemFileName,
                new InvalidOperationException("The project item did not expose an open document."));
        }

        stopwatch.Stop();
        OutputWindowHelper.DiagnosticWriteLine(
            $"CodeCleanupManager.Cleanup for '{projectItem.Name}' took {stopwatch.ElapsedMilliseconds}ms (openedByCleanup: {!wasOpen})");
    }

    /// <summary>
    /// Attempts to run code cleanup on the specified project item without blocking the calling
    /// thread (typically the UI thread) for the duration of any slow headless work, most
    /// notably AI-assisted XML documentation HTTP requests. The small EnvDTE-dependent checks
    /// still run on the main thread, but the file I/O, Roslyn transformations and AI network
    /// calls run on a background thread so the IDE stays responsive and can be canceled.
    /// </summary>
    /// <param name="projectItem">The project item for cleanup.</param>
    /// <param name="cancellationToken">Cancels the semantic using directive placement of a closed file.</param>
    /// <exception cref="OperationCanceledException">The using directive placement was canceled; the file is left unchanged.</exception>
    internal async Task CleanupAsync(ProjectItem projectItem, CancellationToken cancellationToken = default)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (!_codeCleanupAvailabilityLogic.CanCleanupProjectItem(projectItem)) return;

        // Instrumentation for BL-018: measure per-item cleanup cost and whether the document
        // had to be opened by cleanup (opening documents is the primary performance concern).
        var stopwatch = Stopwatch.StartNew();

        var projectItemFileName = projectItem.GetFileName();

        // Skip the disk-based headless path for documents that are already open - the editor
        // buffer is then the source of truth and cleanup must operate on the live document.
        bool wasOpen = projectItem.IsOpen[Constants.vsViewKindTextView] || projectItem.IsOpen[Constants.vsViewKindCode];

        var headlessResult = HeadlessCleanupResult.NotApplicable;
        var usingsMoveOutcome = UsingsMoveOutcome.NotApplicable;
        var changedBySemanticSteps = false;

        if (!wasOpen)
        {
            // The semantic steps (using directive placement, class sealing, null check conversion) need the Visual
            // Studio workspace and run first, so the headless steps (header, using organization, type splitting) see
            // their result. A file that only the headless-only branch below finishes is counted as changed as soon as a
            // step rewrites it, so it is counted even when a later step fails. When editor cleanup is required the file
            // is counted as an editor item by CleanupDocument instead, and must not be counted as changed as well.
            var countedWhenRewritten = !RequiresEditorCleanupForCSharp();
            await CleanupProgressViewModel.RunSemanticStepsAsync(
                new Func<Task<bool>>[]
                {
                    async () =>
                    {
                        usingsMoveOutcome = await _usingDirectivePlacementLogic.PlaceUsingDirectivesAsync(projectItem, cancellationToken);

                        return usingsMoveOutcome == UsingsMoveOutcome.Moved;
                    },
                    () => _sealedClassLogic.SealWhenSafeAsync(projectItem, cancellationToken),
                    () => _nullCheckPatternMatchingLogic.ConvertWhenSafeAsync(projectItem, cancellationToken),
                },
                () =>
                {
                    if (!changedBySemanticSteps)
                    {
                        changedBySemanticSteps = true;
                        if (countedWhenRewritten)
                        {
                            IncrementHeadlessChanged();
                        }
                    }
                });

            // Run the COM-free portion (file read/write, Roslyn transforms, and any AI HTTP
            // calls) on a background thread so the main thread's message pump keeps running
            // and the cleanup progress dialog's Cancel button remains responsive.
            var outcome = await Task.Run(() => TryRunHeadlessPreCleanupForCSharpCore(projectItemFileName));

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            headlessResult = changedBySemanticSteps && outcome.Result == HeadlessCleanupResult.NoChanges ? HeadlessCleanupResult.Changed : outcome.Result;

            if (outcome.SplitOperationOccurred)
            {
                foreach (var createdFile in outcome.CreatedFiles)
                {
                    AddGeneratedFileToProject(projectItem, createdFile);
                }

                _cleanupExecutionStats.SplitOperations++;
                _cleanupExecutionStats.SplitCreatedFiles += outcome.CreatedFiles.Count;
            }
        }

        if (headlessResult != HeadlessCleanupResult.NotApplicable && !RequiresEditorCleanupForCSharp())
        {
            // A file rewritten by the semantic steps was already counted as changed.
            if (!changedBySemanticSteps)
            {
                if (headlessResult == HeadlessCleanupResult.Changed)
                {
                    _cleanupExecutionStats.HeadlessChangedItems++;
                }
                else
                {
                    _cleanupExecutionStats.HeadlessNoOpItems++;
                }
            }

            // Diagnostic cleanup runs after the headless cleanup, against the file it wrote.
            await RunDiagnosticCleanupAsync(projectItem);
            await RunXmlDocumentationDuringCleanupAsync(projectItem);

            stopwatch.Stop();
            OutputWindowHelper.DiagnosticWriteLine(
                $"CodeCleanupManager.Cleanup for '{projectItem.Name}' took {stopwatch.ElapsedMilliseconds}ms (openedByCleanup: False, headlessOnly: True, changed: {headlessResult == HeadlessCleanupResult.Changed})");

            return;
        }

        // Attempt to open the document if not already opened.
        if (!wasOpen)
        {
            try
            {
                projectItem.Open(Constants.vsViewKindTextView);
            }
            catch (Exception ex)
            {
                OutputWindowHelper.WarningWriteLine(
                    $"Unable to open '{projectItemFileName}' for editor cleanup: {ex.Message}");
            }
        }

        if (projectItem.Document is not null)
        {
            if (CleanupDocument(projectItem.Document, usingsMoveOutcome == UsingsMoveOutcome.LeftInPlace, semanticStepsDone: !wasOpen))
            {
                await RunXmlDocumentationDuringCleanupAsync(projectItem);
            }

            // Close the document if it was opened for cleanup.
            if (Settings.Default.Cleaning_AutoSaveAndCloseIfOpenedByCleanup && !wasOpen)
            {
                projectItem.Document.Close(vsSaveChanges.vsSaveChangesYes);
            }
        }
        else
        {
            RecordCleanupFailure(
                projectItemFileName,
                new InvalidOperationException("The project item did not expose an open document."));
        }

        stopwatch.Stop();
        OutputWindowHelper.DiagnosticWriteLine(
            $"CodeCleanupManager.Cleanup for '{projectItem.Name}' took {stopwatch.ElapsedMilliseconds}ms (openedByCleanup: {!wasOpen})");
    }

    /// <summary>
    /// Runs the subset of C# cleanup steps that can safely execute on raw file text without
    /// opening the document in the editor.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>The outcome of the headless pre-cleanup attempt.</returns>
    private HeadlessCleanupResult TryRunHeadlessPreCleanupForCSharp(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var projectItemFileName = projectItem.GetFileName();
        var outcome = TryRunHeadlessPreCleanupForCSharpCore(projectItemFileName);

        if (outcome.SplitOperationOccurred)
        {
            foreach (var createdFile in outcome.CreatedFiles)
            {
                AddGeneratedFileToProject(projectItem, createdFile);
            }

            _cleanupExecutionStats.SplitOperations++;
            _cleanupExecutionStats.SplitCreatedFiles += outcome.CreatedFiles.Count;
        }

        return outcome.Result;
    }

    /// <summary>
    /// Runs the file I/O, Roslyn transformation and AI-assisted documentation portion of the
    /// headless pre-cleanup without touching any EnvDTE/COM objects, so it can safely run on a
    /// background thread. Any files created by the top-level-type-to-file split feature are
    /// returned to the caller, which must register them with the project on the main thread.
    /// </summary>
    /// <param name="projectItemFileName">The full path of the C# file to clean up.</param>
    /// <returns>The outcome of the headless pre-cleanup attempt.</returns>
    internal HeadlessPreCleanupOutcome TryRunHeadlessPreCleanupForCSharpCore(string projectItemFileName)
    {
        var createdFiles = new List<string>();

        if (string.IsNullOrEmpty(projectItemFileName) ||
            !projectItemFileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(projectItemFileName))
        {
            return new HeadlessPreCleanupOutcome { Result = HeadlessCleanupResult.NotApplicable, CreatedFiles = createdFiles };
        }

        try
        {
            var diskSource = FileTextStyle.ReadAllText(projectItemFileName, out var encoding);
            var originalSource = diskSource;

            var settings = EffectiveCleanupSettings.For(projectItemFileName);
            bool splitChanged = false;
            bool splitOperationOccurred = false;
            if (settings.GetBoolean(nameof(Settings.Cleaning_MoveTopLevelTypesToSeparateFiles)))
            {
                var splitResult = _topLevelTypeToFileSplitFileProcessor.Apply(
                    originalSource,
                    projectItemFileName,
                    encoding,
                    ApplyHeadlessCSharpTransformations,
                    transformUpdatedSource: false,
                    transformCreatedFile: ApplyHeadlessCSharpTransformationsForCreatedFile);
                if (splitResult.Changed)
                {
                    splitChanged = true;
                    splitOperationOccurred = true;
                    originalSource = splitResult.UpdatedSource;
                    createdFiles.AddRange(splitResult.CreatedFiles);

                    OutputWindowHelper.DiagnosticWriteLine(
                        $"Headless top-level type split for '{projectItemFileName}' created {splitResult.CreatedFiles.Count} file(s).");
                }
                else
                {
                    OutputWindowHelper.DiagnosticWriteLine(
                        $"Headless top-level type split skipped for '{projectItemFileName}': {splitResult.SkipReason}.");
                }
            }

            var transformedSource = CreateHeadlessCSharpPipeline(originalSource, projectItemFileName, settings).Run(originalSource);
            var removeByteOrderMark = settings.GetBoolean(nameof(Settings.Cleaning_RemoveByteOrderMark));
            var fileHadBom = removeByteOrderMark &&
                             RemoveByteOrderMarkLogic.HasByteOrderMark(File.ReadAllBytes(projectItemFileName));
            var targetEncoding = removeByteOrderMark
                ? new UTF8Encoding(false)
                : encoding;

            if (splitChanged || fileHadBom || !string.Equals(originalSource, transformedSource, StringComparison.Ordinal))
            {
                FileTextStyle.WriteAllText(projectItemFileName, transformedSource, targetEncoding, diskSource);

                return new HeadlessPreCleanupOutcome
                {
                    Result = HeadlessCleanupResult.Changed,
                    CreatedFiles = createdFiles,
                    SplitOperationOccurred = splitOperationOccurred
                };
            }

            return new HeadlessPreCleanupOutcome
            {
                Result = HeadlessCleanupResult.NoChanges,
                CreatedFiles = createdFiles,
                SplitOperationOccurred = splitOperationOccurred
            };
        }
        catch (Exception ex)
        {
            // The original file was not persisted, so it still declares the moved types: the files created for them
            // would duplicate their declarations (CS0101).
            var removedFileCount = createdFiles.Count;
            TopLevelTypeToFileSplitFileProcessor.DeleteCreatedFiles(createdFiles);
            createdFiles.Clear();

            OutputWindowHelper.WarningWriteLine(
                $"Headless C# pre-cleanup skipped for '{projectItemFileName}' due to an error: {ex.Message}" +
                (removedFileCount > 0 ? $" The {removedFileCount} file(s) created by the top-level type split were removed." : string.Empty));

            return new HeadlessPreCleanupOutcome { Result = HeadlessCleanupResult.NotApplicable, CreatedFiles = createdFiles };
        }
    }

    /// <summary>
    /// Progress information for parallel headless cleanup operations.
    /// </summary>
    public sealed class ParallelCleanupProgress
    {
        /// <summary>
        /// Gets or sets the file path.
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Gets or sets the processed count.
        /// </summary>
        public int ProcessedCount { get; set; }

        /// <summary>
        /// Gets or sets the total count.
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Gets or sets the changed.
        /// </summary>
        public bool Changed { get; set; }

        /// <summary>
        /// Gets or sets the error.
        /// </summary>
        public Exception Error { get; set; }
    }

    /// <summary>
    /// Aggregate result of a parallel headless cleanup batch operation.
    /// </summary>
    public sealed class ParallelCleanupResult
    {
        /// <summary>
        /// Gets or sets the total files.
        /// </summary>
        public int TotalFiles { get; set; }

        /// <summary>
        /// Gets or sets the changed files.
        /// </summary>
        public int ChangedFiles { get; set; }

        /// <summary>
        /// Gets or sets the unchanged files.
        /// </summary>
        public int UnchangedFiles { get; set; }

        /// <summary>
        /// Gets or sets the failed files.
        /// </summary>
        public int FailedFiles { get; set; }

        /// <summary>
        /// Gets or sets the modified file paths.
        /// </summary>
        public IReadOnlyList<string> ModifiedFilePaths { get; set; }

        /// <summary>
        /// Gets or sets the failures.
        /// </summary>
        public IReadOnlyDictionary<string, Exception> Failures { get; set; }
    }

    /// <summary>
    /// Processes a collection of C# source files in parallel using the headless cleanup transformation pipeline.
    /// </summary>
    /// <param name="filePaths">The collection of file paths to clean.</param>
    /// <param name="maxDegreeOfParallelism">The maximum degree of parallelism (defaults to processor count).</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A summary of the parallel cleanup operation.</returns>
    public static ParallelCleanupResult ApplyHeadlessCSharpTransformationsToFiles(
        IEnumerable<string> filePaths,
        int? maxDegreeOfParallelism = null,
        IProgress<ParallelCleanupProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        if (filePaths is null)
        {
            return new ParallelCleanupResult
            {
                ModifiedFilePaths = new List<string>(),
                Failures = new Dictionary<string, Exception>()
            };
        }

        var filesList = filePaths.Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f)).ToList();
        var total = filesList.Count;
        var processed = 0;
        var changedCount = 0;
        var unchangedCount = 0;
        var failedCount = 0;

        var modifiedPaths = new ConcurrentBag<string>();
        var failures = new ConcurrentDictionary<string, Exception>();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism ?? Math.Max(1, Environment.ProcessorCount),
            CancellationToken = cancellationToken
        };

        var manager = GetInstance(null);

        Parallel.ForEach(filesList, options, file =>
        {
            options.CancellationToken.ThrowIfCancellationRequested();

            try
            {
                var outcome = manager.TryRunHeadlessPreCleanupForCSharpCore(file);
                var isChanged = outcome.Result == HeadlessCleanupResult.Changed;
                Exception syntaxError = null;

                if (isChanged)
                {
                    // Syntax verification on transformed output: a transformation that
                    // corrupts the file's syntax must be reported as a failure, not a
                    // successful change.
                    try
                    {
                        var transformedText = File.ReadAllText(file);
                        var tree = CSharpSyntaxTree.ParseText(transformedText);
                        var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                        if (errors.Count > 0)
                        {
                            syntaxError = new InvalidOperationException($"Cleanup produced {errors.Count} syntax error(s) in '{file}': {errors[0].GetMessage()}");
                        }
                    }
                    catch (Exception ex)
                    {
                        syntaxError = ex;
                    }

                    if (syntaxError is not null)
                    {
                        failures.TryAdd(file, syntaxError);
                        Interlocked.Increment(ref failedCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref changedCount);
                        modifiedPaths.Add(file);
                    }
                }
                else
                {
                    Interlocked.Increment(ref unchangedCount);
                }

                var currentProcessed = Interlocked.Increment(ref processed);
                progress?.Report(new ParallelCleanupProgress
                {
                    FilePath = file,
                    ProcessedCount = currentProcessed,
                    TotalCount = total,
                    Changed = isChanged && syntaxError is null,
                    Error = syntaxError
                });
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failedCount);
                failures.TryAdd(file, ex);

                var currentProcessed = Interlocked.Increment(ref processed);
                progress?.Report(new ParallelCleanupProgress
                {
                    FilePath = file,
                    ProcessedCount = currentProcessed,
                    TotalCount = total,
                    Changed = false,
                    Error = ex
                });
            }
        });

        return new ParallelCleanupResult
        {
            TotalFiles = total,
            ChangedFiles = changedCount,
            UnchangedFiles = unchangedCount,
            FailedFiles = failedCount,
            ModifiedFilePaths = modifiedPaths.ToList(),
            Failures = failures
        };
    }

    /// <summary>
    /// Applies enabled, Roslyn-based C# source transformations in the same order used by the
    /// in-editor cleanup path.
    /// </summary>
    /// <param name="source">The source text.</param>
    /// <param name="filePath">The file path whose effective cleanup settings select the transformations.</param>
    /// <returns>Transformed source text.</returns>
    internal static string ApplyHeadlessCSharpTransformations(string source, string filePath)
    {
        return CreateHeadlessCSharpPipeline(source, filePath).Run(source);
    }

    /// <summary>
    /// Creates the closed-file C# cleanup pipeline for a file from its effective cleanup settings
    /// (.editorconfig, then the .codejanitor repository policy, then the Visual Studio settings).
    /// </summary>
    /// <param name="source">The source text the pipeline will run on.</param>
    /// <param name="filePath">The file path.</param>
    /// <returns>The pipeline.</returns>
    internal static SourceTransformationPipeline CreateHeadlessCSharpPipeline(string source, string filePath)
    {
        return CreateHeadlessCSharpPipeline(source, filePath, EffectiveCleanupSettings.For(filePath));
    }

    /// <summary>
    /// Creates the closed-file C# cleanup pipeline for a file from its already resolved effective cleanup settings.
    /// </summary>
    /// <param name="source">The source text the pipeline will run on.</param>
    /// <param name="filePath">The file path.</param>
    /// <param name="settings">The effective cleanup settings of the file.</param>
    /// <returns>The pipeline.</returns>
    private static SourceTransformationPipeline CreateHeadlessCSharpPipeline(
        string source,
        string filePath,
        EffectiveCleanupSettings settings)
    {
        bool IsEnabled(string settingName) => settings.GetBoolean(settingName);
        var transformations = new List<ISourceTransformation>();

        // A step whose output needs a C# version newer than C# 7.3 runs only when every project compiling the file uses
        // that version; otherwise it is skipped, with a warning when it would have changed the file.
        var languageVersion = CSharpLanguageVersionSupport.For(filePath);
        void AddForLanguageVersion(ISourceTransformation transformation, CSharpLanguageVersionSupport.SyntaxRequirement requirement)
        {
            transformations.Add(languageVersion.Supports(requirement, out var skipMessage)
                ? transformation
                : CreateSkippedTransformation(transformation, skipMessage));
        }

        // Region directives are policy-only structure and are removed unless the repository
        // policy (.codejanitor) explicitly opts out via removeRegions.
        if (settings.RemovesRegions)
        {
            transformations.Add(new RegionDirectiveRemover());
        }

        if (IsEnabled(nameof(Settings.Cleaning_RemoveByteOrderMark)))
        {
            transformations.Add(new ByteOrderMarkConverter());
        }

        // Using directive placement is not a text transformation: it needs the semantic model and runs against the
        // Visual Studio workspace before this pipeline (UsingDirectivePlacementLogic.PlaceUsingDirectivesAsync).

        switch (settings.NamespaceDeclarations)
        {
            case NamespaceDeclarationPreference.FileScoped:
                var fileScopedConverter = FileScopedNamespaceLogic.CreateConverter(settings);
                if (!fileScopedConverter.HasMultipleNamespaces(source))
                {
                    AddForLanguageVersion(fileScopedConverter, CSharpLanguageVersionSupport.FileScopedNamespaces);
                }

                break;

            case NamespaceDeclarationPreference.BlockScoped:
                transformations.Add(new DelegateSourceTransformation("Block-Scoped Namespace", FileScopedNamespaceLogic.CreateConverter(settings).ConvertToBlockScoped));
                break;
        }

        if (IsEnabled(nameof(Settings.Cleaning_ConvertToVarWhenApparent)))
        {
            transformations.Add(new VarWhenApparentConverter());
        }

        if (IsEnabled(nameof(Settings.Cleaning_MakeFieldsReadonlyWhenSafe)))
        {
            transformations.Add(new ReadonlyFieldConverter());
        }

        if (IsEnabled(nameof(Settings.Cleaning_InsertBlankLineBeforeReturnAndThrowStatements)))
        {
            transformations.Add(new ReturnThrowBlankLinePaddingConverter());
        }

        if (IsEnabled(nameof(Settings.Cleaning_ConvertToCollectionExpressions)))
        {
            AddForLanguageVersion(new CollectionExpressionConverter(), CSharpLanguageVersionSupport.CollectionExpressions);
        }

        if (IsEnabled(nameof(Settings.Cleaning_ReuseJsonSerializerOptionsForCA1869)))
        {
            transformations.Add(new JsonSerializerOptionsReuseConverter());
        }

        if (IsEnabled(nameof(Settings.Cleaning_SimplifySingleStatementLambdas)))
        {
            transformations.Add(new SingleStatementLambdaConverter());
        }

        if (IsEnabled(nameof(Settings.Cleaning_ConvertStringFormatToInterpolation)))
        {
            transformations.Add(new StringInterpolationConverter());
        }

        if (IsEnabled(nameof(Settings.Cleaning_ConvertToStringNameOf)))
        {
            transformations.Add(new NameOfOperatorConverter());
        }

        if (IsEnabled(nameof(Settings.Cleaning_InlineOutVariableDeclarations)))
        {
            transformations.Add(new OutVarInliningConverter());
        }

        if (IsEnabled(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnClasses)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnDelegates)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnEnumerations)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnEvents)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnFields)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnInterfaces)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnMethods)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnProperties)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnStructs)))
        {
            transformations.Add(new ExplicitAccessModifierConverter(settings));
        }

        if (IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeClasses)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterClasses)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeDelegates)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterDelegates)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeEnumerations)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterEnumerations)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeEvents)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterEvents)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeFieldsMultiLine)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterFieldsMultiLine)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeFieldsSingleLine)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterFieldsSingleLine)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeInterfaces)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterInterfaces)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeMethods)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterMethods)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeNamespaces)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterNamespaces)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforePropertiesMultiLine)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterPropertiesMultiLine)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeStructs)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterStructs)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeUsingStatementBlocks)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterUsingStatementBlocks)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeRegionTags)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterRegionTags)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeEndRegionTags)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterEndRegionTags)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeCaseStatements)) ||
            IsEnabled(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeSingleLineComments)))
        {
            transformations.Add(new BlankLinePaddingConverter(settings));
        }

        if (IsEnabled(nameof(Settings.Cleaning_UpdateEndRegionDirectives)))
        {
            transformations.Add(new UpdateEndRegionDirectivesConverter());
        }

        if (IsEnabled(nameof(Settings.Cleaning_UpdateSingleLineMethods)))
        {
            transformations.Add(new UpdateSingleLineMethodsConverter(settings));
        }

        if (IsEnabled(nameof(Settings.Cleaning_UpdateAccessorsToBothBeSingleLineOrMultiLine)))
        {
            transformations.Add(new UpdateAccessorsToBothBeSingleLineOrMultiLineConverter(settings));
        }

        if (IsEnabled(nameof(Settings.Formatting_CommentRunDuringCleanup)))
        {
            transformations.Add(new CommentFormatConverter());
        }

        var fileHeader = settings.GetString(nameof(Settings.Cleaning_UpdateFileHeaderCSharp));
        if (!string.IsNullOrWhiteSpace(fileHeader))
        {
            var fileHeaderPosition = (HeaderPosition)settings.GetInt32(nameof(Settings.Cleaning_UpdateFileHeader_HeaderPosition));
            var fileHeaderUpdateMode = (HeaderUpdateMode)settings.GetInt32(nameof(Settings.Cleaning_UpdateFileHeader_HeaderUpdateMode));
            transformations.Add(new DelegateSourceTransformation(
                "Update C# file header",
                text => ApplyConfiguredCSharpFileHeader(text, fileHeader, fileHeaderPosition, fileHeaderUpdateMode)));
        }

        switch (settings.Indentation)
        {
            case IndentationPreference.Spaces:
                transformations.Add(new TabToSpaceConverter(settings.TabSize));
                break;

            case IndentationPreference.Tabs:
                transformations.Add(new SpaceToTabConverter(settings.TabSize));
                break;
        }

        if (!IsEnabled(nameof(Settings.Cleaning_RunVisualStudioRemoveAndSortUsingStatements)) && settings.OrganizeUsings)
        {
            transformations.Add(new UsingDirectiveOrganizer());
        }

        if (IsEnabled(nameof(Settings.Cleaning_RemoveEndOfLineWhitespace)))
        {
            transformations.Add(new RemoveTrailingWhitespaceConverter());
        }

        if (IsEnabled(nameof(Settings.Cleaning_RemoveBlankLinesAtTop)))
        {
            transformations.Add(new DelegateSourceTransformation("Remove blank lines at top", RemoveBlankLinesAtTop));
        }

        if (IsEnabled(nameof(Settings.Cleaning_RemoveBlankLinesAtBottom)))
        {
            transformations.Add(new DelegateSourceTransformation("Remove blank lines at bottom", RemoveBlankLinesAtBottom));
        }

        if (IsEnabled(nameof(Settings.Cleaning_RemoveBlankLinesAfterAttributes)))
        {
            transformations.Add(new DelegateSourceTransformation("Remove blank lines after attributes", RemoveBlankLinesAfterAttributes));
        }

        transformations.Add(new DelegateSourceTransformation("Remove blank lines after documentation comments", RemoveBlankLinesAfterDocumentationComments));

        if (IsEnabled(nameof(Settings.Cleaning_RemoveBlankLinesAfterOpeningBrace)))
        {
            transformations.Add(new DelegateSourceTransformation("Remove blank lines after opening brace", RemoveBlankLinesAfterOpeningBrace));
        }

        if (IsEnabled(nameof(Settings.Cleaning_RemoveBlankLinesBeforeClosingBrace)))
        {
            transformations.Add(new DelegateSourceTransformation("Remove blank lines before closing brace", RemoveBlankLinesBeforeClosingBrace));
        }

        if (IsEnabled(nameof(Settings.Cleaning_RemoveBlankLinesBetweenChainedStatements)))
        {
            transformations.Add(new DelegateSourceTransformation("Remove blank lines between chained statements", RemoveBlankLinesBetweenChainedStatements));
        }

        if (IsEnabled(nameof(Settings.Cleaning_RemoveMultipleConsecutiveBlankLines)))
        {
            transformations.Add(new NormalizeBlankLinesConverter());
        }

        transformations.Add(settings.InsertFinalNewline
            ? new EnsureFinalNewlineConverter()
            : (ISourceTransformation)new RemoveFinalNewlineConverter());

        return new SourceTransformationPipeline(transformations);
    }

    /// <summary>
    /// Creates the pipeline step that replaces a transformation whose output the file's C# language version does not
    /// support: it leaves the source unchanged and logs the skip message when the transformation would have changed it.
    /// </summary>
    /// <param name="transformation">The skipped transformation.</param>
    /// <param name="skipMessage">The warning explaining why the transformation is skipped.</param>
    /// <returns>The pipeline step.</returns>
    private static ISourceTransformation CreateSkippedTransformation(ISourceTransformation transformation, string skipMessage)
    {
        return new DelegateSourceTransformation(transformation.Name, source =>
        {
            if (transformation.Apply(source) != source)
            {
                OutputWindowHelper.WarningWriteLine(skipMessage);
            }

            return source;
        });
    }

    /// <summary>
    /// Transformations for a file produced by the top-level type split.
    /// </summary>
    internal static string ApplyHeadlessCSharpTransformationsForCreatedFile(string source, string filePath)
    {
        return ApplyHeadlessCSharpTransformations(source, filePath);
    }

    /// <summary>
    /// Determines whether the C# cleanup of a closed file still requires the editor-backed DTE path. "Remove and Sort
    /// Usings" and "Format Document" do not: diagnostic cleanup runs their Roslyn equivalents on closed files.
    /// </summary>
    /// <returns>True if editor-backed cleanup must run, otherwise false.</returns>
    internal static bool RequiresEditorCleanupForCSharp()
    {
        if (Settings.Default.Reorganizing_RunAtStartOfCleanup) return true;

        if (Settings.Default.ThirdParty_UseJetBrainsReSharperCleanup ||
            Settings.Default.ThirdParty_UseTelerikJustCodeCleanup ||
            Settings.Default.ThirdParty_UseXAMLStylerCleanup ||
            OtherCleaningCommands.Value.Any())
        {
            return true;
        }

        if (Settings.Default.Cleaning_AiXmlDocumentationEnabled &&
            Settings.Default.Cleaning_AiXmlDocumentationRunDuringCleanup)
        {
            return Settings.Default.Cleaning_AiXmlDocumentationPreviewChanges;
        }

        return false;
    }

    /// <summary>
    /// Applies the configured C# file header to the source by normalizing line endings and inserting or replacing it at the document start or after usings, returning the source unchanged if the header is blank or the position is unsupported.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="settingsFileHeader">The effective file header text.</param>
    /// <param name="headerPosition">The effective header position.</param>
    /// <param name="headerUpdateMode">The effective header update mode.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string ApplyConfiguredCSharpFileHeader(
        string source,
        string settingsFileHeader,
        HeaderPosition headerPosition,
        HeaderUpdateMode headerUpdateMode)
    {
        if (string.IsNullOrWhiteSpace(settingsFileHeader))
        {
            return source;
        }

        var newline = source.Contains("\r\n") ? "\r\n" : Environment.NewLine;
        settingsFileHeader = NormalizeLineEndings(settingsFileHeader, newline);
        if (!settingsFileHeader.EndsWith(newline, StringComparison.Ordinal))
        {
            settingsFileHeader += newline;
        }

        switch (headerPosition)
        {
            case HeaderPosition.DocumentStart:
                return headerUpdateMode == HeaderUpdateMode.Insert
                    ? InsertHeaderAtDocumentStart(source, settingsFileHeader)
                    : ReplaceHeaderAtDocumentStart(source, settingsFileHeader);

            case HeaderPosition.AfterUsings:
                return headerUpdateMode == HeaderUpdateMode.Insert
                    ? InsertHeaderAfterUsings(source, settingsFileHeader)
                    : ReplaceHeaderAfterUsings(source, settingsFileHeader);

            default:
                return source;
        }
    }

    /// <summary>
    /// Returns the source unchanged if it already starts with the trimmed header; otherwise prepends the header to the source, with no side effects.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="settingsFileHeader">The settings file header.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string InsertHeaderAtDocumentStart(string source, string settingsFileHeader)
    {
        return source.StartsWith(settingsFileHeader.Trim(), StringComparison.Ordinal)
            ? source
            : settingsFileHeader + source;
    }

    /// <summary>
    /// Replaces the leading header in the source with the trimmed settings header if they differ, otherwise returns the original source unchanged.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="settingsFileHeader">The settings file header.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string ReplaceHeaderAtDocumentStart(string source, string settingsFileHeader)
    {
        TryExtractLeadingHeaderSegment(source, out var currentHeaderLength, out var currentHeader);

        return string.Equals(currentHeader, settingsFileHeader.Trim(), StringComparison.Ordinal)
            ? source
            : settingsFileHeader + source.Substring(currentHeaderLength);
    }

    /// <summary>
    /// Inserts the given header string after any top-level using directives, returning the original source unchanged if the existing header already starts with the trimmed settings.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="settingsFileHeader">The settings file header.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string InsertHeaderAfterUsings(string source, string settingsFileHeader)
    {
        var insertionIndex = GetTopLevelUsingInsertionIndex(source);
        TryExtractLeadingHeaderSegment(source.Substring(insertionIndex), out _, out var currentHeader);

        if (currentHeader.StartsWith(settingsFileHeader.Trim(), StringComparison.Ordinal))
        {
            return source;
        }

        var headerWithLeadingNewline = EnsureHeaderStartsOnNewLine(settingsFileHeader);

        return source.Insert(insertionIndex, headerWithLeadingNewline);
    }

    /// <summary>
    /// Replaces the file header immediately after the top-level using block with the specified settings header, returning the original source unchanged if the existing header already matches, and otherwise reconstructs the source by inserting the new header at the computed insertion index while removing the old header segment.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="settingsFileHeader">The settings file header.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string ReplaceHeaderAfterUsings(string source, string settingsFileHeader)
    {
        var insertionIndex = GetTopLevelUsingInsertionIndex(source);
        var suffix = source.Substring(insertionIndex);

        TryExtractLeadingHeaderSegment(suffix, out var currentHeaderLength, out var currentHeader);
        if (string.Equals(currentHeader, settingsFileHeader.Trim(), StringComparison.Ordinal))
        {
            return source;
        }

        var headerWithLeadingNewline = EnsureHeaderStartsOnNewLine(settingsFileHeader);

        return source.Substring(0, insertionIndex) +
               headerWithLeadingNewline +
               suffix.Substring(currentHeaderLength);
    }

    /// <summary>
    /// Ensures the given header string begins with a newline by detecting CRLF if present otherwise using the environment newline, prepending it if needed and returning the result without modifying state or throwing exceptions.
    /// </summary>
    /// <param name="header">The header.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string EnsureHeaderStartsOnNewLine(string header)
    {
        var newline = header.Contains("\r\n") ? "\r\n" : Environment.NewLine;

        return header.StartsWith(newline, StringComparison.Ordinal) ? header : newline + header;
    }

    /// <summary>
    /// Parses the C# source into a syntax tree and returns the end position of the last top-level using directive, or 0 if none exist, to indicate the insertion point for a new using.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>A int value produced by this method.</returns>
    private static int GetTopLevelUsingInsertionIndex(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();

        return root.Usings.Count == 0 ? 0 : root.Usings.Last().FullSpan.End;
    }

    /// <summary>
    /// Attempts to match a leading header consisting of optional blank lines followed by either consecutive // line comments or a /* */ block comment, and if successful sets segmentLength and trimmedHeader (trimmed of whitespace/newlines) and returns true, otherwise sets them to 0 and empty and returns false.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="segmentLength">The segment length.</param>
    /// <param name="trimmedHeader">The trimmed header.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool TryExtractLeadingHeaderSegment(string source, out int segmentLength, out string trimmedHeader)
    {
        var lineHeaderMatch = Regex.Match(
            source,
            @"\A(?<segment>(?:[ \t]*\r?\n)*(?://[^\r\n]*(?:\r?\n//[^\r\n]*)*(?:\r?\n)?))",
            RegexOptions.Multiline);

        if (lineHeaderMatch.Success)
        {
            var segment = lineHeaderMatch.Groups["segment"].Value;
            segmentLength = segment.Length;
            trimmedHeader = segment.Trim();

            return true;
        }

        var blockHeaderMatch = Regex.Match(
            source,
            @"\A(?<segment>(?:[ \t]*\r?\n)*/\*.*?\*/(?:\r?\n)?)",
            RegexOptions.Singleline);

        if (blockHeaderMatch.Success)
        {
            var segment = blockHeaderMatch.Groups["segment"].Value;
            segmentLength = segment.Length;
            trimmedHeader = segment.Trim();

            return true;
        }

        segmentLength = 0;
        trimmedHeader = string.Empty;

        return false;
    }

    /// <summary>
    /// Normalizes all line endings in the input string by converting CRLF and CR sequences to LF and then replacing every LF with the specified newline string, returning a new string without modifying the original.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="newline">The newline.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string NormalizeLineEndings(string value, string newline)
    {
        return value.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", newline);
    }

    /// <summary>
    /// Removes all leading lines that contain only spaces or tabs followed by a newline from the start of the string, returning a new string with no side effects.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string RemoveBlankLinesAtTop(string source)
    {
        return Regex.Replace(source, @"\A(?:[ \t]*\r?\n)+", string.Empty);
    }

    /// <summary>
    /// The method removes all trailing blank lines (consisting of newline characters followed by optional spaces/tabs) from the end of.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string RemoveBlankLinesAtBottom(string source)
    {
        return Regex.Replace(source, @"(?:\r?\n[ \t]*)+\z", string.Empty);
    }

    /// <summary>
    /// Removes a blank line immediately following an attribute declaration, unless the next non-blank line is a comment, by replacing the double newline with a single newline while preserving the file&apos;s line ending style.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string RemoveBlankLinesAfterAttributes(string source)
    {
        return ReplaceUsingFileLineEnding(source, @"(^[ \t]*\[[^\]]+\][ \t]*(//[^\r\n]*)*)(\r?\n){2}(?![ \t]*//)", "$1{NL}");
    }

    /// <summary>
    /// Removes the blank lines between a documentation comment and the declaration it documents, keeping the
    /// file's line ending style.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>The source without blank lines after documentation comments.</returns>
    private static string RemoveBlankLinesAfterDocumentationComments(string source)
    {
        return ReplaceUsingFileLineEnding(source, RemoveWhitespaceLogic.BlankLinesAfterDocumentationCommentPattern, "$1{NL}");
    }

    /// <summary>
    /// The user asks for exactly one concise summary sentence (plain text only, no XML, no quotes) about the C# method `RemoveBlankLinesAfterOpeningBrace`. The body: `return ReplaceUsingFileLineEnding(source, @&quot;\{([ \t]*(//[^\r\n]*)*)(\r?\n){2,}&quot;, &quot;{$1{NL}&quot;);` Let&apos;s analyze. The method calls `ReplaceUsingFileLineEnding(source, pattern, replacement)`. The pattern matches an opening brace `\{`, then captures only whitespace (spaces/tabs) and optional `//` comments (the `[ \t]*(//[^\r\n]*)*` part) but... Actually `([ \t]*(//[^\r\n]*)*)` captures zero or more sequences of optional whitespace followed by a comment. Then `(\r?\n){2,}` matches two or more line endings. Replacement is `{$1{NL}`. Presumably `{NL}` is a placeholder for the file line ending, or the method replaces it. But the replacement is `&quot;{$1{NL}&quot;` — note there&apos;s no newline after the opening brace? Let&apos;s think. `ReplaceUsingFileLineEnding.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string RemoveBlankLinesAfterOpeningBrace(string source)
    {
        return ReplaceUsingFileLineEnding(source, @"\{([ \t]*(//[^\r\n]*)*)(\r?\n){2,}", "{$1{NL}");
    }

    /// <summary>
    /// Replaces two or more newline sequences preceding a closing brace with a single newline, preserving any preceding indentation and using the source file&apos;s line ending, with no exceptions thrown.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string RemoveBlankLinesBeforeClosingBrace(string source)
    {
        return ReplaceUsingFileLineEnding(source, @"(\r?\n){2,}([ \t]*)\}", "{NL}$2}");
    }

    /// <summary>
    /// Removes extra blank lines preceding else, catch, or finally clauses by collapsing multiple line endings into one while preserving indentation and the keyword, with no side effects or exceptions detected.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string RemoveBlankLinesBetweenChainedStatements(string source)
    {
        return ReplaceUsingFileLineEnding(source, @"(\r?\n){2,}([ \t]*)(else|catch|finally)( |\t|\r?\n)", "{NL}$2$3$4");
    }

    /// <summary>
    /// Determines whether the source string uses CRLF or LF line endings and performs a multiline regex replacement, substituting any &quot;{NL}&quot; placeholder in the replacement string with that detected newline sequence, without throwing exceptions.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="replacement">The replacement.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string ReplaceUsingFileLineEnding(string source, string pattern, string replacement)
    {
        var newline = source.Contains("\r\n") ? "\r\n" : "\n";

        return Regex.Replace(source, pattern, replacement.Replace("{NL}", newline), RegexOptions.Multiline);
    }

    /// <summary>
    /// Attempts to run code cleanup on the specified document.
    /// </summary>
    /// <param name="document">The document for cleanup.</param>
    internal void Cleanup(Document document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var xmlDocumentationItem = CleanupWithoutXmlDocumentation(document);
        if (xmlDocumentationItem is not null)
        {
            ThreadHelper.JoinableTaskFactory.Run(() => RunXmlDocumentationDuringCleanupAsync(xmlDocumentationItem));
        }
    }

    /// <summary>
    /// Attempts to run code cleanup on the specified document, without the AI XML documentation step.
    /// </summary>
    /// <param name="document">The document for cleanup.</param>
    /// <returns>
    /// The project item to run the XML documentation step for (<see cref="RunXmlDocumentationDuringCleanupAsync" />),
    /// or null when the document was not cleaned, has no project item or is cleaned up on save: AI requests take
    /// seconds per file, so they never run for the automatic cleanup on save.
    /// </returns>
    internal ProjectItem CleanupWithoutXmlDocumentation(Document document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!CleanupDocument(document, usingsLeftInPlace: false, semanticStepsDone: false) || _package.IsAutoSaveContext)
        {
            return null;
        }

        return document.ProjectItem;
    }

    /// <summary>
    /// Attempts to run code cleanup on the specified document.
    /// </summary>
    /// <param name="document">The document for cleanup.</param>
    /// <param name="usingsLeftInPlace">
    /// True when the semantic using directive placement was already attempted for the file in this cleanup and left
    /// the using directives in place: it is not retried, since that would repeat the analysis and the warning.
    /// </param>
    /// <param name="semanticStepsDone">
    /// True when class sealing and null check conversion already ran for the closed file in this cleanup: they are
    /// not repeated, since each attempt analyzes the semantic model of every project compiling the file.
    /// </param>
    /// <returns>
    /// True when the document was cleaned; false when it cannot be cleaned up (for example an excluded or
    /// auto-generated file) or a designer window of it is active.
    /// </returns>
    private bool CleanupDocument(Document document, bool usingsLeftInPlace, bool semanticStepsDone)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _cleanupExecutionStats.EditorItems++;

        if (!_codeCleanupAvailabilityLogic.CanCleanupDocument(document, true)) return false;

        // Make sure the document to be cleaned up is active, required for some commands like format document.
        document.Activate();

        // Check for designer windows being active, which should not proceed with cleanup as the code isn't truly active.
        if (document.ActiveWindow.Caption.EndsWith(" [Design]"))
        {
            return false;
        }

        if (_package.ActiveDocument != document)
        {
            OutputWindowHelper.WarningWriteLine($"Activation was not completed before cleaning began for '{document.Name}'");
        }

        // The cleanup steps of this document follow its effective settings: .editorconfig, then the .codejanitor
        // repository policy, then the Visual Studio settings.
        var settings = EffectiveCleanupSettings.For(document.FullName);

        // When types are split into their own files, the semantic using directive placement must run before the split
        // so the created files inherit the placed directives. It is then its own undo unit, like the split, and the
        // calls in the cleanup undo transaction (RunCodeCleanupCSharp) find nothing left to move. Once a placement left
        // the directives in place (including the closed-file placement of this cleanup), no later step retries it.
        // Class sealing and null check conversion also run before the split, for the same reason: a type moved to a
        // created file is no longer in this document when the cleanup steps below run, and the created file is not
        // cleaned in this pass. They are then not repeated in RunCodeCleanupCSharp, since each attempt analyzes the
        // semantic model of every project compiling the file.
        var splitsCSharpTypes = settings.GetBoolean(nameof(Settings.Cleaning_MoveTopLevelTypesToSeparateFiles)) && document.GetCodeLanguage() == CodeLanguage.CSharp;
        if (!usingsLeftInPlace && splitsCSharpTypes)
        {
            usingsLeftInPlace = _usingDirectivePlacementLogic.PlaceUsingDirectives(document.GetTextDocument()) == UsingsMoveOutcome.LeftInPlace;
        }

        if (splitsCSharpTypes && !semanticStepsDone)
        {
            _sealedClassLogic.SealWhenSafe(document.GetTextDocument(), settings);
            _nullCheckPatternMatchingLogic.ConvertWhenSafe(document.GetTextDocument(), settings);
        }

        TrySplitTopLevelTypesToSeparateFiles(document, settings);

        // Conditionally start cleanup with reorganization.
        if (Settings.Default.Reorganizing_RunAtStartOfCleanup)
        {
            if (_codeReorganizationAvailabilityLogic.CanReorganize(document, false))
            {
                _codeReorganizationManager.Reorganize(document);
            }
            else
            {
                OutputWindowHelper.DiagnosticWriteLine(
                    $"Skipped start-of-cleanup reorganization for '{document.FullName}' because it is not safe to reorganize without prompting.");
            }
        }

        new UndoTransactionHelper(_package, string.Format(Resources.CodeJanitorCleanupFor0, document.Name)).Run(
            delegate
            {
                var cleanupMethod = FindCodeCleanupMethod(document, settings, usingsLeftInPlace, semanticStepsDone: semanticStepsDone || splitsCSharpTypes);
                if (cleanupMethod is not null)
                {
                    OutputWindowHelper.InfoWriteLine($"Cleanup started for '{document.FullName}'");
                    _package.IDE.StatusBar.Text = string.Format(Resources.CodeJanitorIsCleaning0, document.Name);

                    // Perform the set of configured cleanups based on the language.
                    cleanupMethod(document);

                    _package.IDE.StatusBar.Text = string.Format(Resources.CodeJanitorCleaned0, document.Name);
                    OutputWindowHelper.InfoWriteLine($"Cleanup completed for '{document.FullName}'");
                }
            });

        // Diagnostic cleanup runs after the Janitor cleanup of a C# document, as its own undo unit,
        // against the cleaned editor buffer.
        if (document.GetCodeLanguage() == CodeLanguage.CSharp)
        {
            var outcome = ThreadHelper.JoinableTaskFactory.Run(() => _editorConfigDiagnosticCleanupLogic.CleanupAsync(document));
            if (!RecordDiagnosticCleanupOutcome(document.FullName, outcome))
            {
                _package.IDE.StatusBar.Text = string.Format(Resources.CodeJanitorCleaned0WithUnresolvedDiagnostics, document.Name);
            }
        }

        return true;
    }

    /// <summary>
    /// Resets execution statistics for the next cleanup batch.
    /// </summary>
    internal void ResetCleanupExecutionStats()
    {
        _cleanupExecutionStats = default(CleanupExecutionStats);
    }

    /// <summary>
    /// Records a failure that was isolated to one cleanup item.
    /// </summary>
    /// <param name="filePath">The item path.</param>
    /// <param name="exception">The failure.</param>
    internal void RecordCleanupFailure(string filePath, Exception exception)
    {
        lock (_cleanupStatsLock)
        {
            _cleanupExecutionStats.FailedItems++;
        }

        OutputWindowHelper.ExceptionWriteLine(
            $"Cleanup failed for '{filePath}'", exception);
    }

    /// <summary>
    /// Places the using directives of a closed C# project item inside or outside its namespaces, as its effective
    /// settings require. Must run before the headless cleanup of the item (see <see cref="UsingDirectivePlacementLogic" />).
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <param name="cancellationToken">Cancels the semantic analysis of the move; the file is then left unchanged.</param>
    /// <returns>True when the file was rewritten with the placed directives.</returns>
    /// <exception cref="OperationCanceledException">The move was canceled.</exception>
    internal async Task<bool> PlaceUsingDirectivesAsync(ProjectItem projectItem, CancellationToken cancellationToken = default) =>
        await _usingDirectivePlacementLogic.PlaceUsingDirectivesAsync(projectItem, cancellationToken) == UsingsMoveOutcome.Moved;

    /// <summary>
    /// Seals the classes of a closed C# project item that are safe to seal, when its effective settings enable it. Must
    /// run before the headless cleanup of the item (see <see cref="SealedClassLogic" />).
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <param name="cancellationToken">Cancels the semantic analysis; the file is then left unchanged.</param>
    /// <returns>True when the file was rewritten with sealed classes.</returns>
    /// <exception cref="OperationCanceledException">The analysis was canceled.</exception>
    internal Task<bool> SealClassesWhenSafeAsync(ProjectItem projectItem, CancellationToken cancellationToken = default) =>
        _sealedClassLogic.SealWhenSafeAsync(projectItem, cancellationToken);

    /// <summary>
    /// Converts the null checks of a closed C# project item that are safe to convert to pattern matching, when its
    /// effective settings enable it. Must run before the headless cleanup of the item (see
    /// <see cref="NullCheckPatternMatchingLogic" />).
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <param name="cancellationToken">Cancels the semantic analysis; the file is then left unchanged.</param>
    /// <returns>True when the file was rewritten with converted null checks.</returns>
    /// <exception cref="OperationCanceledException">The analysis was canceled.</exception>
    internal Task<bool> ConvertNullChecksWhenSafeAsync(ProjectItem projectItem, CancellationToken cancellationToken = default) =>
        _nullCheckPatternMatchingLogic.ConvertWhenSafeAsync(projectItem, cancellationToken);

    /// <summary>
    /// Adds AI-generated XML documentation to a cleaned file (open in the editor or closed on disk), when AI XML
    /// documentation is enabled with "Run during cleanup". The AI requests run off the UI thread.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>A task.</returns>
    internal async Task RunXmlDocumentationDuringCleanupAsync(ProjectItem projectItem)
    {
        if (!Settings.Default.Cleaning_AiXmlDocumentationEnabled || !Settings.Default.Cleaning_AiXmlDocumentationRunDuringCleanup)
        {
            return;
        }

        await _aiXmlDocumentationLogic.ApplyXmlDocumentationAsync(projectItem);
    }

    /// <summary>
    /// Runs .editorconfig/Roslyn diagnostic cleanup for a C# project item after its Janitor cleanup
    /// and records the outcome in the execution statistics. Does nothing when no diagnostic cleanup
    /// category is enabled for the item.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>A task.</returns>
    internal async Task RunDiagnosticCleanupAsync(ProjectItem projectItem)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var outcome = await _editorConfigDiagnosticCleanupLogic.CleanupAsync(projectItem);

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        RecordDiagnosticCleanupOutcome(projectItem.GetFileName(), outcome);
    }

    /// <summary>
    /// Records a diagnostic cleanup outcome: failures are counted as failed items, fixed files and
    /// files left with unresolved actionable diagnostics are counted separately.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="outcome">The diagnostic cleanup outcome.</param>
    /// <returns>True when diagnostic cleanup did not run or fully succeeded, otherwise false.</returns>
    private bool RecordDiagnosticCleanupOutcome(string filePath, DiagnosticCleanupOutcome outcome)
    {
        if (outcome.Failure is not null)
        {
            RecordCleanupFailure(filePath, outcome.Failure);

            return false;
        }

        lock (_cleanupStatsLock)
        {
            if (outcome.Changed)
            {
                _cleanupExecutionStats.DiagnosticChangedItems++;
            }

            if (outcome.UnresolvedCount > 0)
            {
                _cleanupExecutionStats.DiagnosticUnresolvedItems++;
            }
        }

        return outcome.UnresolvedCount == 0;
    }

    /// <summary>
    /// Returns the current execution statistics for the ongoing cleanup batch.
    /// </summary>
    /// <returns>The current cleanup execution statistics.</returns>
    internal CleanupExecutionStats GetCleanupExecutionStats()
    {
        lock (_cleanupStatsLock)
        {
            return _cleanupExecutionStats;
        }
    }

    /// <summary>
    /// Finds a code cleanup method appropriate for the specified document, otherwise null.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>
    /// <param name="usingsLeftInPlace">
    /// True when the semantic using directive placement already left the using directives in place in this cleanup.
    /// </param>
    /// <param name="semanticStepsDone">True when class sealing and null check conversion already ran for the document in this cleanup.</param>
    /// <returns>The code cleanup method, otherwise null.</returns>
    private Action<Document> FindCodeCleanupMethod(Document document, EffectiveCleanupSettings settings, bool usingsLeftInPlace, bool semanticStepsDone)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        switch (document.GetCodeLanguage())
        {
            case CodeLanguage.CSharp:
                return csharpDocument => RunCodeCleanupCSharp(csharpDocument, settings, usingsLeftInPlace, semanticStepsDone);

            case CodeLanguage.VisualBasic:
                return vbDocument => RunCodeCleanupVB(vbDocument, settings);

            case CodeLanguage.CPlusPlus:
            case CodeLanguage.CSS:
            case CodeLanguage.JavaScript:
            case CodeLanguage.JSON:
            case CodeLanguage.LESS:
            case CodeLanguage.PHP:
            case CodeLanguage.PowerShell:
            case CodeLanguage.R:
            case CodeLanguage.SCSS:
            case CodeLanguage.TypeScript:
                return cDocument => RunCodeCleanupC(cDocument, settings);

            case CodeLanguage.HTML:
            case CodeLanguage.XAML:
            case CodeLanguage.XML:
                return markupDocument => RunCodeCleanupMarkup(markupDocument, settings);

            case CodeLanguage.FSharp:
            case CodeLanguage.Unknown:
                return genericDocument => RunCodeCleanupGeneric(genericDocument, settings);

            default:
                OutputWindowHelper.WarningWriteLine($"FindCodeCleanupMethod does not recognize document language '{document.Language}'");
                return null;
        }
    }

    /// <summary>
    /// Splits top-level C# types in the active document into separate files when enabled, adding generated files to the project, updating execution stats, writing a diagnostic message, and replacing the document&apos;s source with the updated content.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>
    private void TrySplitTopLevelTypesToSeparateFiles(Document document, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_MoveTopLevelTypesToSeparateFiles)) ||
            document is null ||
            document.GetCodeLanguage() != CodeLanguage.CSharp)
        {
            return;
        }

        var projectItem = document.ProjectItem;
        var filePath = projectItem?.GetFileName();
        if (projectItem is null || string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var textDocument = document.GetTextDocument();
        if (textDocument is null)
        {
            return;
        }

        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var originalSource = startPoint.GetText(textDocument.EndPoint);
        Encoding encoding = Encoding.UTF8;
        if (File.Exists(filePath))
        {
            FileTextStyle.ReadAllText(filePath, out encoding);
        }

        var splitResult = _topLevelTypeToFileSplitFileProcessor.Apply(
            originalSource,
            filePath,
            encoding,
            ApplyHeadlessCSharpTransformations,
            transformCreatedFile: ApplyHeadlessCSharpTransformationsForCreatedFile);
        if (!splitResult.Changed)
        {
            return;
        }

        // The original must drop the moved types before anything else depends on the new files: if replacing its
        // text fails, the new files would duplicate the declarations that stay in the original (CS0101).
        try
        {
            var endPoint = textDocument.EndPoint.CreateEditPoint();
            startPoint.ReplaceText(endPoint, splitResult.UpdatedSource, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
        }
        catch
        {
            TopLevelTypeToFileSplitFileProcessor.DeleteCreatedFiles(splitResult.CreatedFiles);

            throw;
        }

        foreach (var createdFile in splitResult.CreatedFiles)
        {
            AddGeneratedFileToProject(projectItem, createdFile);
        }

        _cleanupExecutionStats.SplitOperations++;
        _cleanupExecutionStats.SplitCreatedFiles += splitResult.CreatedFiles.Count;

        OutputWindowHelper.DiagnosticWriteLine(
            $"Top-level type split for '{filePath}' created {splitResult.CreatedFiles.Count} file(s).");
    }

    /// <summary>
    /// Adds a file split out of a project item to the source item's collection of the project. Does nothing when the
    /// file does not exist or is already part of the solution; a failure is logged as a warning.
    /// </summary>
    /// <param name="sourceProjectItem">The source project item.</param>
    /// <param name="filePath">The file path.</param>
    internal void AddGeneratedFileToProject(ProjectItem sourceProjectItem, string filePath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (sourceProjectItem is null || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        if (_package.IDE.Solution.FindProjectItem(filePath) is not null)
        {
            return;
        }

        try
        {
            ProjectItems projectItems = sourceProjectItem.Collection ?? sourceProjectItem.ContainingProject?.ProjectItems;
            projectItems?.AddFromFile(filePath);
        }
        catch (Exception ex)
        {
            OutputWindowHelper.WarningWriteLine($"Unable to add generated file '{filePath}' to the project: {ex.Message}");
        }
    }

    /// <summary>
    /// Thread-safely increments the count of headless changed items.
    /// </summary>
    internal void IncrementHeadlessChanged()
    {
        lock (_cleanupStatsLock)
        {
            _cleanupExecutionStats.HeadlessChangedItems++;
        }
    }

    /// <summary>
    /// Thread-safely increments the count of headless no-op items.
    /// </summary>
    internal void IncrementHeadlessNoOp()
    {
        lock (_cleanupStatsLock)
        {
            _cleanupExecutionStats.HeadlessNoOpItems++;
        }
    }

    /// <summary>
    /// Thread-safely records a split operation.
    /// </summary>
    internal void RecordSplitOperation(int createdFilesCount)
    {
        lock (_cleanupStatsLock)
        {
            _cleanupExecutionStats.SplitOperations++;
            _cleanupExecutionStats.SplitCreatedFiles += createdFilesCount;
        }
    }

    /// <summary>
    /// Attempts to run code cleanup on the specified CSharp document.
    /// </summary>
    /// <param name="document">The document for cleanup.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>
    /// <param name="usingsLeftInPlace">
    /// True when the semantic using directive placement already left the using directives in place in this cleanup;
    /// it is then not retried.
    /// </param>
    /// <param name="semanticStepsDone">True when class sealing and null check conversion already ran for the document in this cleanup.</param>
    private void RunCodeCleanupCSharp(Document document, EffectiveCleanupSettings settings, bool usingsLeftInPlace, bool semanticStepsDone)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var textDocument = document.GetTextDocument();

        // Place using directives inside or outside the namespace, as the effective settings require. Each attempt
        // re-analyzes the document semantically, so once the placement left the directives in place it is not
        // attempted again.
        if (!usingsLeftInPlace)
        {
            usingsLeftInPlace = _usingDirectivePlacementLogic.PlaceUsingDirectives(textDocument) == UsingsMoveOutcome.LeftInPlace;
        }

        // Convert to the enforced namespace declaration style first (changes file structure).
        _fileScopedNamespaceLogic.ApplyNamespaceDeclarationStyle(textDocument, settings);

        // Convert local variable declarations to 'var' when the type is apparent, when enabled.
        _varWhenApparentLogic.ConvertToVarWhenApparent(textDocument, settings);

        // Add 'readonly' to fields provably never written outside their constructor, when enabled.
        _readonlyFieldLogic.AddReadonlyWhenSafe(textDocument, settings);

        // Add 'sealed' to classes proven safe to seal across the solution, and convert the null checks proven safe to
        // pattern matching, when enabled and not already done.
        if (!semanticStepsDone)
        {
            _sealedClassLogic.SealWhenSafe(textDocument, settings);
            _nullCheckPatternMatchingLogic.ConvertWhenSafe(textDocument, settings);
        }

        // Insert a blank line before return/throw statements that end a block, when enabled.
        _returnThrowBlankLinePaddingLogic.InsertPaddingBeforeReturnAndThrowStatements(textDocument, settings);

        // Convert List<T>/array initializations to collection expression syntax, when enabled.
        _collectionExpressionLogic.ConvertToCollectionExpressions(textDocument, settings);

        // Replace direct JsonSerializerOptions allocations in JsonSerializer calls, when enabled.
        _jsonSerializerOptionsReuseLogic.ReuseJsonSerializerOptionsForCA1869(textDocument, settings);

        // Simplify single-statement lambda blocks to expression-bodied lambdas, when enabled.
        _singleStatementLambdaLogic.SimplifySingleStatementLambdas(textDocument, settings);

        // Perform any actions that can modify the file code model first.
        RunExternalFormatting(textDocument, settings);
        if (!document.IsExternal())
        {
            _usingStatementCleanupLogic.RemoveAndSortUsingStatements(textDocument, settings);

            // External cleanup (e.g. ReSharper, or Format Document running a code cleanup profile) can move using
            // directives across the namespace boundary; place them again unless the placement already proved unsafe.
            // When every directive is already where it belongs this is a syntax-only check, without semantic analysis.
            if (!usingsLeftInPlace)
            {
                _usingDirectivePlacementLogic.PlaceUsingDirectives(textDocument);
            }
        }

        // Interpret the document into a collection of elements.
        var codeItems = _codeModelManager.RetrieveAllCodeItems(document);

        var regions = codeItems.OfType<CodeItemRegion>().ToList();
        var usingStatements = codeItems.OfType<CodeItemUsingStatement>().ToList();
        var namespaces = codeItems.OfType<CodeItemNamespace>().ToList();
        var classes = codeItems.OfType<CodeItemClass>().ToList();
        var delegates = codeItems.OfType<CodeItemDelegate>().ToList();
        var enumerations = codeItems.OfType<CodeItemEnum>().ToList();
        var events = codeItems.OfType<CodeItemEvent>().ToList();
        var fields = codeItems.OfType<CodeItemField>().ToList();
        var interfaces = codeItems.OfType<CodeItemInterface>().ToList();
        var methods = codeItems.OfType<CodeItemMethod>().ToList();
        var properties = codeItems.OfType<CodeItemProperty>().ToList();
        var structs = codeItems.OfType<CodeItemStruct>().ToList();

        // Build up more complicated collections.
        var usingStatementBlocks = CodeModelHelper.GetCodeItemBlocks(usingStatements).ToList();
        var usingStatementsThatStartBlocks = (from IEnumerable<CodeItemUsingStatement> block in usingStatementBlocks select block.First()).ToList();
        var usingStatementsThatEndBlocks = (from IEnumerable<CodeItemUsingStatement> block in usingStatementBlocks select block.Last()).ToList();

        // Perform file header cleanup.
        _fileHeaderLogic.UpdateFileHeader(textDocument, settings);

        // Perform removal cleanup.
        _removeByteOrderMarkLogic.RemoveByteOrderMark(textDocument, settings);
        if (settings.RemovesRegions)
        {
            _removeRegionLogic.RemoveRegions(regions);
        }

        _removeWhitespaceLogic.RemoveEOLWhitespace(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesAtTop(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesAtBottom(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesAfterAttributes(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesAfterDocumentationComments(textDocument, RemoveWhitespaceLogic.BlankLinesAfterDocumentationCommentPattern);
        _removeWhitespaceLogic.RemoveBlankLinesAfterOpeningBrace(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesBeforeClosingBrace(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesBetweenChainedStatements(textDocument, settings);
        _removeWhitespaceLogic.RemoveMultipleConsecutiveBlankLines(textDocument, settings);

        // Perform insertion of blank line padding cleanup.
        _insertBlankLinePaddingLogic.InsertPaddingBeforeRegionTags(regions, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterRegionTags(regions, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeEndRegionTags(regions, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterEndRegionTags(regions, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(usingStatementsThatStartBlocks, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(usingStatementsThatEndBlocks, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(namespaces, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(namespaces, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(classes, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(classes, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(delegates, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(delegates, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(enumerations, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(enumerations, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(events, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(events, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(fields, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(fields, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(interfaces, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(interfaces, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(methods, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(methods, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(properties, settings);
        _insertBlankLinePaddingLogic.InsertPaddingBetweenMultiLinePropertyAccessors(properties, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(properties, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(structs, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(structs, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCaseStatements(textDocument, settings);
        _insertBlankLinePaddingLogic.InsertPaddingBeforeSingleLineComments(textDocument, settings);

        // Perform insertion of explicit access modifier cleanup.
        _insertExplicitAccessModifierLogic.InsertExplicitAccessModifiersOnClasses(classes, settings);
        _insertExplicitAccessModifierLogic.InsertExplicitAccessModifiersOnDelegates(delegates, settings);
        _insertExplicitAccessModifierLogic.InsertExplicitAccessModifiersOnEnumerations(enumerations, settings);
        _insertExplicitAccessModifierLogic.InsertExplicitAccessModifiersOnEvents(events, settings);
        _insertExplicitAccessModifierLogic.InsertExplicitAccessModifiersOnFields(fields, settings);
        _insertExplicitAccessModifierLogic.InsertExplicitAccessModifiersOnInterfaces(interfaces, settings);
        _insertExplicitAccessModifierLogic.InsertExplicitAccessModifiersOnMethods(methods, settings);
        _insertExplicitAccessModifierLogic.InsertExplicitAccessModifiersOnProperties(properties, settings);
        _insertExplicitAccessModifierLogic.InsertExplicitAccessModifiersOnStructs(structs, settings);

        // Perform the final newline cleanup (insert or remove, as the effective settings require).
        _insertWhitespaceLogic.InsertEOFTrailingNewLine(textDocument, settings);
        _removeWhitespaceLogic.RemoveEOFTrailingNewLine(textDocument, settings);

        // Perform update cleanup.
        _updateLogic.UpdateEndRegionDirectives(textDocument, settings);
        _updateLogic.UpdateEventAccessorsToBothBeSingleLineOrMultiLine(events, settings);
        _updateLogic.UpdatePropertyAccessorsToBothBeSingleLineOrMultiLine(properties, settings);
        _updateLogic.UpdateSingleLineMethods(methods, settings);

        // Perform comment cleaning.
        _commentFormatLogic.FormatComments(textDocument);
    }

    /// <summary>
    /// Attempts to run code cleanup on the specified VB.Net document.
    /// </summary>
    /// <param name="document">The document for cleanup.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>
    private void RunCodeCleanupVB(Document document, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var textDocument = document.GetTextDocument();

        // Perform any actions that can modify the file code model first.
        RunExternalFormatting(textDocument, settings);
        if (!document.IsExternal())
        {
            _usingStatementCleanupLogic.RemoveAndSortUsingStatements(textDocument, settings);
        }

        // Interpret the document into a collection of elements.
        var codeItems = _codeModelManager.RetrieveAllCodeItems(document);

        var regions = codeItems.OfType<CodeItemRegion>().ToList();
        var usingStatements = codeItems.OfType<CodeItemUsingStatement>().ToList();
        var namespaces = codeItems.OfType<CodeItemNamespace>().ToList();
        var classes = codeItems.OfType<CodeItemClass>().ToList();
        var delegates = codeItems.OfType<CodeItemDelegate>().ToList();
        var enumerations = codeItems.OfType<CodeItemEnum>().ToList();
        var events = codeItems.OfType<CodeItemEvent>().ToList();
        var fields = codeItems.OfType<CodeItemField>().ToList();
        var interfaces = codeItems.OfType<CodeItemInterface>().ToList();
        var methods = codeItems.OfType<CodeItemMethod>().ToList();
        var properties = codeItems.OfType<CodeItemProperty>().ToList();
        var structs = codeItems.OfType<CodeItemStruct>().ToList();

        // Build up more complicated collections.
        var usingStatementBlocks = CodeModelHelper.GetCodeItemBlocks(usingStatements).ToList();
        var usingStatementsThatStartBlocks = (from IEnumerable<CodeItemUsingStatement> block in usingStatementBlocks select block.First()).ToList();
        var usingStatementsThatEndBlocks = (from IEnumerable<CodeItemUsingStatement> block in usingStatementBlocks select block.Last()).ToList();

        // Perform file header cleanup.
        _fileHeaderLogic.UpdateFileHeader(textDocument, settings);

        // Perform removal cleanup.
        _removeByteOrderMarkLogic.RemoveByteOrderMark(textDocument, settings);
        if (settings.RemovesRegions)
        {
            _removeRegionLogic.RemoveRegions(regions);
        }

        _removeWhitespaceLogic.RemoveEOLWhitespace(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesAtTop(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesAtBottom(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesAfterAttributes(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesAfterDocumentationComments(textDocument, RemoveWhitespaceLogic.BlankLinesAfterVisualBasicDocumentationCommentPattern);
        _removeWhitespaceLogic.RemoveBlankLinesBetweenChainedStatements(textDocument, settings);
        _removeWhitespaceLogic.RemoveMultipleConsecutiveBlankLines(textDocument, settings);

        // Perform insertion of blank line padding cleanup.
        _insertBlankLinePaddingLogic.InsertPaddingBeforeRegionTags(regions, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterRegionTags(regions, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeEndRegionTags(regions, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterEndRegionTags(regions, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(usingStatementsThatStartBlocks, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(usingStatementsThatEndBlocks, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(namespaces, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(namespaces, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(classes, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(classes, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(delegates, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(delegates, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(enumerations, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(enumerations, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(events, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(events, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(fields, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(fields, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(interfaces, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(interfaces, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(methods, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(methods, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(properties, settings);
        _insertBlankLinePaddingLogic.InsertPaddingBetweenMultiLinePropertyAccessors(properties, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(properties, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(structs, settings);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(structs, settings);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCaseStatements(textDocument, settings);
        _insertBlankLinePaddingLogic.InsertPaddingBeforeSingleLineComments(textDocument, settings);

        // Perform the final newline cleanup (insert or remove, as the effective settings require).
        _insertWhitespaceLogic.InsertEOFTrailingNewLine(textDocument, settings);
        _removeWhitespaceLogic.RemoveEOFTrailingNewLine(textDocument, settings);

        // Perform comment cleaning.
        _commentFormatLogic.FormatComments(textDocument);
    }

    /// <summary>
    /// Attempts to run code cleanup on the specified C/C++ document.
    /// </summary>
    /// <param name="document">The document for cleanup.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>
    private void RunCodeCleanupC(Document document, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var textDocument = document.GetTextDocument();

        RunExternalFormatting(textDocument, settings);

        // Perform file header cleanup.
        _fileHeaderLogic.UpdateFileHeader(textDocument, settings);

        // Perform removal cleanup.
        _removeByteOrderMarkLogic.RemoveByteOrderMark(textDocument, settings);
        _removeWhitespaceLogic.RemoveEOLWhitespace(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesAtTop(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesAtBottom(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesAfterOpeningBrace(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesBeforeClosingBrace(textDocument, settings);
        _removeWhitespaceLogic.RemoveMultipleConsecutiveBlankLines(textDocument, settings);

        // Perform insertion of blank line padding cleanup.
        _insertBlankLinePaddingLogic.InsertPaddingBeforeSingleLineComments(textDocument, settings);

        // Perform the final newline cleanup (insert or remove, as the effective settings require).
        _insertWhitespaceLogic.InsertEOFTrailingNewLine(textDocument, settings);
        _removeWhitespaceLogic.RemoveEOFTrailingNewLine(textDocument, settings);
    }

    /// <summary>
    /// Attempts to run code cleanup on the specified markup document.
    /// </summary>
    /// <param name="document">The document for cleanup.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>
    private void RunCodeCleanupMarkup(Document document, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var textDocument = document.GetTextDocument();

        RunExternalFormatting(textDocument, settings);

        // Run Razor-specific formatting in a safe, scoped way for .razor files only.
        _razorFormatterLogic.FormatRazorDocument(textDocument, settings);

        if (!document.IsExternal())
        {
            _usingStatementCleanupLogic.RemoveAndSortUsingStatements(textDocument, settings);
        }

        // Perform file header cleanup.
        _fileHeaderLogic.UpdateFileHeader(textDocument, settings);

        // Perform removal cleanup.
        _removeByteOrderMarkLogic.RemoveByteOrderMark(textDocument, settings);
        _removeWhitespaceLogic.RemoveEOLWhitespace(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesAtTop(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesAtBottom(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesBeforeClosingTag(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankSpacesBeforeClosingAngleBracket(textDocument, settings);
        _removeWhitespaceLogic.RemoveMultipleConsecutiveBlankLines(textDocument, settings);

        // Perform insertion cleanup.
        _insertWhitespaceLogic.InsertBlankSpaceBeforeSelfClosingAngleBracket(textDocument, settings);
        _insertWhitespaceLogic.InsertEOFTrailingNewLine(textDocument, settings);
        _removeWhitespaceLogic.RemoveEOFTrailingNewLine(textDocument, settings);
    }

    /// <summary>
    /// Attempts to run code cleanup on the specified generic document.
    /// </summary>
    /// <param name="document">The document for cleanup.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>
    private void RunCodeCleanupGeneric(Document document, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var textDocument = document.GetTextDocument();

        RunExternalFormatting(textDocument, settings);

        // Perform file header cleanup.
        _fileHeaderLogic.UpdateFileHeader(textDocument, settings);

        // Perform removal cleanup.
        _removeByteOrderMarkLogic.RemoveByteOrderMark(textDocument, settings);
        _removeWhitespaceLogic.RemoveEOLWhitespace(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesAtTop(textDocument, settings);
        _removeWhitespaceLogic.RemoveBlankLinesAtBottom(textDocument, settings);
        _removeWhitespaceLogic.RemoveMultipleConsecutiveBlankLines(textDocument, settings);

        // Perform the final newline cleanup (insert or remove, as the effective settings require).
        _insertWhitespaceLogic.InsertEOFTrailingNewLine(textDocument, settings);
        _removeWhitespaceLogic.RemoveEOFTrailingNewLine(textDocument, settings);
    }

    /// <summary>
    /// Runs external formatting tools (e.g. Visual Studio, JetBrains ReSharper, Telerik JustCode).
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>
    private void RunExternalFormatting(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        RunVisualStudioFormatDocument(textDocument, settings);
        RunJetBrainsReSharperCleanup(textDocument);
        RunTelerikJustCodeCleanup(textDocument);
        RunXAMLStylerCleanup(textDocument);
        RunOtherCleanupCommands(textDocument);
    }

    /// <summary>
    /// Runs the Visual Studio built-in format document command.
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>
    private void RunVisualStudioFormatDocument(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_RunVisualStudioFormatDocumentCommand))) return;

        _commandHelper.ExecuteCommand(textDocument, "Edit.FormatDocument");
    }

    /// <summary>
    /// Runs the JetBrains ReSharper cleanup command.
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>
    private void RunJetBrainsReSharperCleanup(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!Settings.Default.ThirdParty_UseJetBrainsReSharperCleanup) return;

        // This command changed to include the leading 'ReSharper.' in version 2016.1.
        // Execute both commands for backwards compatibility.
        _commandHelper.ExecuteCommand(textDocument, "ReSharper_SilentCleanupCode", "ReSharper.ReSharper_SilentCleanupCode");
    }

    /// <summary>
    /// Runs the Telerik JustCode cleanup command.
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>
    private void RunTelerikJustCodeCleanup(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!Settings.Default.ThirdParty_UseTelerikJustCodeCleanup) return;

        _commandHelper.ExecuteCommand(textDocument, "JustCode.JustCode_CleanCodeWithDefaultProfile");
    }

    /// <summary>
    /// Runs the XAML Styler cleanup command.
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>
    private void RunXAMLStylerCleanup(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!Settings.Default.ThirdParty_UseXAMLStylerCleanup) return;

        _commandHelper.ExecuteCommand(textDocument, "EditorContextMenus.XAMLEditor.BeautifyXaml", "EditorContextMenus.XAMLEditor.FormatXAML", "EditorContextMenus.CodeWindow.FormatXAML");
    }

    /// <summary>
    /// Runs the other cleanup commands.
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>
    private void RunOtherCleanupCommands(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!OtherCleaningCommands.Value.Any()) return;

        foreach (var commandName in OtherCleaningCommands.Value)
        {
            _commandHelper.ExecuteCommand(textDocument, commandName);
        }
    }
}
