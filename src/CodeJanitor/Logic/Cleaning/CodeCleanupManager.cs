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
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Formatting;
using CodeJanitor.Logic.Reorganizing;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Model;
using CodeJanitor.Model.CodeItems;
using CodeJanitor.Properties;
using CodeJanitor.UI.Enumerations;
using EnvDTE;
using Document = EnvDTE.Document;
using TextDocument = EnvDTE.TextDocument;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.Shell;

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
    private readonly MoveUsingsOutsideNamespaceLogic _moveUsingsOutsideNamespaceLogic;
    private readonly VarWhenApparentLogic _varWhenApparentLogic;
    private readonly ReadonlyFieldLogic _readonlyFieldLogic;
    private readonly RazorFormatterLogic _razorFormatterLogic;
    private readonly ReturnThrowBlankLinePaddingLogic _returnThrowBlankLinePaddingLogic;
    private readonly SealedClassLogic _sealedClassLogic;
    private readonly SingleStatementLambdaLogic _singleStatementLambdaLogic;
    private readonly RemoveRegionLogic _removeRegionLogic;
    private readonly RemoveWhitespaceLogic _removeWhitespaceLogic;
    private readonly RemoveByteOrderMarkLogic _removeByteOrderMarkLogic;
    private readonly UpdateLogic _updateLogic;
    private readonly EditorConfigDiagnosticCleanupLogic _editorConfigDiagnosticCleanupLogic;
    private readonly UsingStatementCleanupLogic _usingStatementCleanupLogic;

    private readonly CachedSettingSet<string> _otherCleaningCommands =
        new CachedSettingSet<string>(() => Settings.Default.ThirdParty_OtherCleaningCommandsExpression,
                                     expression =>
                                     expression.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries)
                                               .Select(x => x.Trim())
                                               .Where(y => !string.IsNullOrEmpty(y))
                                               .ToList());

    private CleanupExecutionStats _cleanupExecutionStats;
    private IReadOnlyCollection<string> _currentBatchDisqualifiedTypes;

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
        _moveUsingsOutsideNamespaceLogic = MoveUsingsOutsideNamespaceLogic.GetInstance(_package);
        _varWhenApparentLogic = VarWhenApparentLogic.GetInstance(_package);
        _readonlyFieldLogic = ReadonlyFieldLogic.GetInstance(_package);
        _razorFormatterLogic = RazorFormatterLogic.GetInstance(_package);
        _returnThrowBlankLinePaddingLogic = ReturnThrowBlankLinePaddingLogic.GetInstance(_package);
        _sealedClassLogic = SealedClassLogic.GetInstance(_package);
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
            // The semantic using move needs the Visual Studio workspace and runs first, so the headless steps
            // (header, using organization, type splitting) see the moved directives.
            usingsMoveOutcome = ThreadHelper.JoinableTaskFactory.Run(() => _moveUsingsOutsideNamespaceLogic.MoveUsingsOutsideNamespaceAsync(projectItem));
            headlessResult = TryRunHeadlessPreCleanupForCSharp(projectItem);
            if (usingsMoveOutcome == UsingsMoveOutcome.Moved && headlessResult == HeadlessCleanupResult.NoChanges)
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
            CleanupDocument(projectItem.Document, usingsMoveOutcome == UsingsMoveOutcome.LeftInPlace);

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

    internal async Task CleanupAsync(ProjectItem projectItem)
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

        if (!wasOpen)
        {
            // The semantic using move needs the Visual Studio workspace and runs first, so the headless steps
            // (header, using organization, type splitting) see the moved directives.
            usingsMoveOutcome = await _moveUsingsOutsideNamespaceLogic.MoveUsingsOutsideNamespaceAsync(projectItem);

            // Run the COM-free portion (file read/write, Roslyn transforms, and any AI HTTP
            // calls) on a background thread so the main thread's message pump keeps running
            // and the cleanup progress dialog's Cancel button remains responsive.
            var outcome = await Task.Run(() => TryRunHeadlessPreCleanupForCSharpCore(projectItemFileName, _currentBatchDisqualifiedTypes));

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            headlessResult = usingsMoveOutcome == UsingsMoveOutcome.Moved && outcome.Result == HeadlessCleanupResult.NoChanges ? HeadlessCleanupResult.Changed : outcome.Result;

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
            if (headlessResult == HeadlessCleanupResult.Changed)
            {
                _cleanupExecutionStats.HeadlessChangedItems++;
            }
            else
            {
                _cleanupExecutionStats.HeadlessNoOpItems++;
            }

            // Diagnostic cleanup runs after the headless cleanup, against the file it wrote.
            await RunDiagnosticCleanupAsync(projectItem);

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
            CleanupDocument(projectItem.Document, usingsMoveOutcome == UsingsMoveOutcome.LeftInPlace);

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
        var outcome = TryRunHeadlessPreCleanupForCSharpCore(projectItemFileName, _currentBatchDisqualifiedTypes);

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

    internal HeadlessPreCleanupOutcome TryRunHeadlessPreCleanupForCSharpCore(
        string projectItemFileName,
        IReadOnlyCollection<string> solutionDisqualifiedTypes = null)
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
            string originalSource;
            Encoding encoding;

            using (var reader = new StreamReader(projectItemFileName, detectEncodingFromByteOrderMarks: true))
            {
                originalSource = reader.ReadToEnd();
                encoding = reader.CurrentEncoding;
            }

            bool splitChanged = false;
            bool splitOperationOccurred = false;
            if (Settings.Default.Cleaning_MoveTopLevelTypesToSeparateFiles)
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

            var transformedSource = ApplyHeadlessCSharpTransformations(originalSource, projectItemFileName, solutionDisqualifiedTypes);
            var removeByteOrderMark = RepositoryCleanupSettings.LoadForFile(projectItemFileName)
                .TryGetBoolean("Cleaning_RemoveByteOrderMark", Settings.Default.Cleaning_RemoveByteOrderMark);
            var fileHadBom = removeByteOrderMark &&
                             RemoveByteOrderMarkLogic.HasByteOrderMark(File.ReadAllBytes(projectItemFileName));
            var targetEncoding = removeByteOrderMark
                ? new UTF8Encoding(false)
                : encoding;

            if (splitChanged || fileHadBom || !string.Equals(originalSource, transformedSource, StringComparison.Ordinal))
            {
                File.WriteAllText(projectItemFileName, transformedSource, targetEncoding);

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
            OutputWindowHelper.WarningWriteLine(
                $"Headless C# pre-cleanup skipped for '{projectItemFileName}' due to an error: {ex.Message}");

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
        var solutionDisqualifiedTypes = DiscoverDisqualifiedTypes(filesList);

        Parallel.ForEach(filesList, options, file =>
        {
            options.CancellationToken.ThrowIfCancellationRequested();

            try
            {
                var outcome = manager.TryRunHeadlessPreCleanupForCSharpCore(file, solutionDisqualifiedTypes);
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

                    if (syntaxError != null)
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
    /// <returns>Transformed source text.</returns>

    internal static string ApplyHeadlessCSharpTransformations(string source, string filePath)
    {
        return ApplyHeadlessCSharpTransformations(source, filePath, null);
    }

    internal static string ApplyHeadlessCSharpTransformations(
        string source,
        string filePath,
        IReadOnlyCollection<string> solutionDisqualifiedTypes)
    {
        return CreateHeadlessCSharpPipeline(source, filePath, solutionDisqualifiedTypes).Run(source);
    }

    internal static SourceTransformationPipeline CreateHeadlessCSharpPipeline(
        string source,
        string filePath,
        IReadOnlyCollection<string> solutionDisqualifiedTypes = null)
    {
        var editorConfig = EditorConfigHelper.LoadCSharpOptions(filePath);
        var repositoryOverrides = RepositoryCleanupSettings.LoadForFile(filePath);
        bool IsEnabled(string settingName, bool fallback) => repositoryOverrides.TryGetBoolean(settingName, fallback);
        var transformations = new List<ISourceTransformation>();

        // Region directives are policy-only structure and are removed unless the repository
        // policy (.codejanitor) explicitly opts out via removeRegions.
        if (repositoryOverrides.RemovesRegions)
        {
            transformations.Add(new RegionDirectiveRemover());
        }

        if (IsEnabled("Cleaning_RemoveByteOrderMark", Settings.Default.Cleaning_RemoveByteOrderMark))
        {
            transformations.Add(new ByteOrderMarkConverter());
        }

        // "Move using directives outside namespace" is not a text transformation: it needs the semantic model and
        // runs against the Visual Studio workspace before this pipeline (MoveUsingsOutsideNamespaceAsync).

        if (IsEnabled("Cleaning_ConvertToFileScopedNamespace", Settings.Default.Cleaning_ConvertToFileScopedNamespace))
        {
            var fileScopedConverter = new FileScopedNamespaceConverter();
            if (!fileScopedConverter.HasMultipleNamespaces(source))
            {
                transformations.Add(fileScopedConverter);
            }
        }

        if (IsEnabled("Cleaning_ConvertToVarWhenApparent", Settings.Default.Cleaning_ConvertToVarWhenApparent))
        {
            transformations.Add(new VarWhenApparentConverter());
        }

        if (IsEnabled("Cleaning_MakeFieldsReadonlyWhenSafe", Settings.Default.Cleaning_MakeFieldsReadonlyWhenSafe))
        {
            transformations.Add(new ReadonlyFieldConverter());
        }

        if (IsEnabled("Cleaning_SealClassesWhenSafe", Settings.Default.Cleaning_SealClassesWhenSafe))
        {
            var disqualified = solutionDisqualifiedTypes ?? DiscoverDisqualifiedTypesForFile(filePath);
            transformations.Add(new SealedClassConverter(disqualified));
        }

        if (IsEnabled("Cleaning_InsertBlankLineBeforeReturnAndThrowStatements", Settings.Default.Cleaning_InsertBlankLineBeforeReturnAndThrowStatements))
        {
            transformations.Add(new ReturnThrowBlankLinePaddingConverter());
        }

        if (IsEnabled("Cleaning_ConvertToCollectionExpressions", Settings.Default.Cleaning_ConvertToCollectionExpressions))
        {
            transformations.Add(new CollectionExpressionConverter());
        }


        if (IsEnabled("Cleaning_ReuseJsonSerializerOptionsForCA1869", Settings.Default.Cleaning_ReuseJsonSerializerOptionsForCA1869))
        {
            transformations.Add(new JsonSerializerOptionsReuseConverter());
        }

        if (IsEnabled("Cleaning_SimplifySingleStatementLambdas", Settings.Default.Cleaning_SimplifySingleStatementLambdas))
        {
            transformations.Add(new SingleStatementLambdaConverter());
        }

        if (IsEnabled("Cleaning_ConvertToPatternMatchingNullChecks", Settings.Default.Cleaning_ConvertToPatternMatchingNullChecks))
        {
            transformations.Add(new NullCheckPatternMatchingConverter());
        }

        if (IsEnabled("Cleaning_ConvertStringFormatToInterpolation", Settings.Default.Cleaning_ConvertStringFormatToInterpolation))
        {
            transformations.Add(new StringInterpolationConverter());
        }

        if (IsEnabled("Cleaning_ConvertToStringNameOf", Settings.Default.Cleaning_ConvertToStringNameOf))
        {
            transformations.Add(new NameOfOperatorConverter());
        }

        if (IsEnabled("Cleaning_InlineOutVariableDeclarations", Settings.Default.Cleaning_InlineOutVariableDeclarations))
        {
            transformations.Add(new OutVarInliningConverter());
        }

        if (IsEnabled("Cleaning_InsertExplicitAccessModifiersOnClasses", Settings.Default.Cleaning_InsertExplicitAccessModifiersOnClasses) ||
            IsEnabled("Cleaning_InsertExplicitAccessModifiersOnDelegates", Settings.Default.Cleaning_InsertExplicitAccessModifiersOnDelegates) ||
            IsEnabled("Cleaning_InsertExplicitAccessModifiersOnEnumerations", Settings.Default.Cleaning_InsertExplicitAccessModifiersOnEnumerations) ||
            IsEnabled("Cleaning_InsertExplicitAccessModifiersOnEvents", Settings.Default.Cleaning_InsertExplicitAccessModifiersOnEvents) ||
            IsEnabled("Cleaning_InsertExplicitAccessModifiersOnFields", Settings.Default.Cleaning_InsertExplicitAccessModifiersOnFields) ||
            IsEnabled("Cleaning_InsertExplicitAccessModifiersOnInterfaces", Settings.Default.Cleaning_InsertExplicitAccessModifiersOnInterfaces) ||
            IsEnabled("Cleaning_InsertExplicitAccessModifiersOnMethods", Settings.Default.Cleaning_InsertExplicitAccessModifiersOnMethods) ||
            IsEnabled("Cleaning_InsertExplicitAccessModifiersOnProperties", Settings.Default.Cleaning_InsertExplicitAccessModifiersOnProperties) ||
            IsEnabled("Cleaning_InsertExplicitAccessModifiersOnStructs", Settings.Default.Cleaning_InsertExplicitAccessModifiersOnStructs))
        {
            transformations.Add(new ExplicitAccessModifierConverter());
        }

        if (IsEnabled("Cleaning_InsertBlankLinePaddingBeforeClasses", Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingAfterClasses", Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingBeforeDelegates", Settings.Default.Cleaning_InsertBlankLinePaddingBeforeDelegates) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingAfterDelegates", Settings.Default.Cleaning_InsertBlankLinePaddingAfterDelegates) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingBeforeEnumerations", Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEnumerations) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingAfterEnumerations", Settings.Default.Cleaning_InsertBlankLinePaddingAfterEnumerations) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingBeforeEvents", Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEvents) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingAfterEvents", Settings.Default.Cleaning_InsertBlankLinePaddingAfterEvents) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingBeforeFieldsMultiLine", Settings.Default.Cleaning_InsertBlankLinePaddingBeforeFieldsMultiLine) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingAfterFieldsMultiLine", Settings.Default.Cleaning_InsertBlankLinePaddingAfterFieldsMultiLine) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingBeforeFieldsSingleLine", Settings.Default.Cleaning_InsertBlankLinePaddingBeforeFieldsSingleLine) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingAfterFieldsSingleLine", Settings.Default.Cleaning_InsertBlankLinePaddingAfterFieldsSingleLine) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingBeforeInterfaces", Settings.Default.Cleaning_InsertBlankLinePaddingBeforeInterfaces) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingAfterInterfaces", Settings.Default.Cleaning_InsertBlankLinePaddingAfterInterfaces) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingBeforeMethods", Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingAfterMethods", Settings.Default.Cleaning_InsertBlankLinePaddingAfterMethods) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingBeforeNamespaces", Settings.Default.Cleaning_InsertBlankLinePaddingBeforeNamespaces) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingAfterNamespaces", Settings.Default.Cleaning_InsertBlankLinePaddingAfterNamespaces) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingBeforePropertiesMultiLine", Settings.Default.Cleaning_InsertBlankLinePaddingBeforePropertiesMultiLine) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingAfterPropertiesMultiLine", Settings.Default.Cleaning_InsertBlankLinePaddingAfterPropertiesMultiLine) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine", Settings.Default.Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine", Settings.Default.Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingBeforeStructs", Settings.Default.Cleaning_InsertBlankLinePaddingBeforeStructs) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingAfterStructs", Settings.Default.Cleaning_InsertBlankLinePaddingAfterStructs) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingBeforeUsingStatementBlocks", Settings.Default.Cleaning_InsertBlankLinePaddingBeforeUsingStatementBlocks) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingAfterUsingStatementBlocks", Settings.Default.Cleaning_InsertBlankLinePaddingAfterUsingStatementBlocks) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingBeforeRegionTags", Settings.Default.Cleaning_InsertBlankLinePaddingBeforeRegionTags) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingAfterRegionTags", Settings.Default.Cleaning_InsertBlankLinePaddingAfterRegionTags) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingBeforeEndRegionTags", Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEndRegionTags) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingAfterEndRegionTags", Settings.Default.Cleaning_InsertBlankLinePaddingAfterEndRegionTags) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingBeforeCaseStatements", Settings.Default.Cleaning_InsertBlankLinePaddingBeforeCaseStatements) ||
            IsEnabled("Cleaning_InsertBlankLinePaddingBeforeSingleLineComments", Settings.Default.Cleaning_InsertBlankLinePaddingBeforeSingleLineComments))
        {
            transformations.Add(new BlankLinePaddingConverter());
        }

        if (IsEnabled("Cleaning_UpdateEndRegionDirectives", Settings.Default.Cleaning_UpdateEndRegionDirectives))
        {
            transformations.Add(new UpdateEndRegionDirectivesConverter());
        }

        if (IsEnabled("Cleaning_UpdateSingleLineMethods", Settings.Default.Cleaning_UpdateSingleLineMethods))
        {
            transformations.Add(new UpdateSingleLineMethodsConverter());
        }

        if (IsEnabled("Cleaning_UpdateAccessorsToBothBeSingleLineOrMultiLine", Settings.Default.Cleaning_UpdateAccessorsToBothBeSingleLineOrMultiLine))
        {
            transformations.Add(new UpdateAccessorsToBothBeSingleLineOrMultiLineConverter());
        }

        if (IsEnabled("Formatting_CommentRunDuringCleanup", Settings.Default.Formatting_CommentRunDuringCleanup))
        {
            transformations.Add(new CommentFormatConverter());
        }

        var fileHeader = repositoryOverrides.TryGetString("Cleaning_UpdateFileHeaderCSharp", Settings.Default.Cleaning_UpdateFileHeaderCSharp);
        if (!string.IsNullOrWhiteSpace(fileHeader))
        {
            var fileHeaderPosition = (HeaderPosition)repositoryOverrides.TryGetInt32("Cleaning_UpdateFileHeader_HeaderPosition", Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition);
            var fileHeaderUpdateMode = (HeaderUpdateMode)repositoryOverrides.TryGetInt32("Cleaning_UpdateFileHeader_HeaderUpdateMode", Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode);
            transformations.Add(new DelegateSourceTransformation(
                "Update C# file header",
                text => ApplyConfiguredCSharpFileHeader(text, fileHeader, fileHeaderPosition, fileHeaderUpdateMode)));
        }

        if (string.Equals(editorConfig.IndentStyle, "space", StringComparison.OrdinalIgnoreCase))
        {
            var tabSize = editorConfig.TabWidth ?? editorConfig.IndentSize ?? 4;
            transformations.Add(new TabToSpaceConverter(tabSize));
        }

        if (!IsEnabled("Cleaning_RunVisualStudioRemoveAndSortUsingStatements", Settings.Default.Cleaning_RunVisualStudioRemoveAndSortUsingStatements) &&
            (repositoryOverrides.OrganizeUsings == true ||
             (editorConfig.SortSystemDirectivesFirst == true && editorConfig.SeparateImportDirectiveGroups != true)))
        {
            transformations.Add(new UsingDirectiveOrganizer());
        }

        if (IsEnabled("Cleaning_RemoveEndOfLineWhitespace", Settings.Default.Cleaning_RemoveEndOfLineWhitespace))
        {
            transformations.Add(new RemoveTrailingWhitespaceConverter());
        }
        else if (editorConfig.TrimTrailingWhitespace == true)
        {
            transformations.Add(new RemoveTrailingWhitespaceConverter());
        }

        if (IsEnabled("Cleaning_RemoveBlankLinesAtTop", Settings.Default.Cleaning_RemoveBlankLinesAtTop))
        {
            transformations.Add(new DelegateSourceTransformation("Remove blank lines at top", RemoveBlankLinesAtTop));
        }

        if (IsEnabled("Cleaning_RemoveBlankLinesAtBottom", Settings.Default.Cleaning_RemoveBlankLinesAtBottom))
        {
            transformations.Add(new DelegateSourceTransformation("Remove blank lines at bottom", RemoveBlankLinesAtBottom));
        }

        if (IsEnabled("Cleaning_RemoveBlankLinesAfterAttributes", Settings.Default.Cleaning_RemoveBlankLinesAfterAttributes))
        {
            transformations.Add(new DelegateSourceTransformation("Remove blank lines after attributes", RemoveBlankLinesAfterAttributes));
        }

        if (IsEnabled("Cleaning_RemoveBlankLinesAfterOpeningBrace", Settings.Default.Cleaning_RemoveBlankLinesAfterOpeningBrace))
        {
            transformations.Add(new DelegateSourceTransformation("Remove blank lines after opening brace", RemoveBlankLinesAfterOpeningBrace));
        }

        if (IsEnabled("Cleaning_RemoveBlankLinesBeforeClosingBrace", Settings.Default.Cleaning_RemoveBlankLinesBeforeClosingBrace))
        {
            transformations.Add(new DelegateSourceTransformation("Remove blank lines before closing brace", RemoveBlankLinesBeforeClosingBrace));
        }

        if (IsEnabled("Cleaning_RemoveBlankLinesBetweenChainedStatements", Settings.Default.Cleaning_RemoveBlankLinesBetweenChainedStatements))
        {
            transformations.Add(new DelegateSourceTransformation("Remove blank lines between chained statements", RemoveBlankLinesBetweenChainedStatements));
        }

        if (IsEnabled("Cleaning_RemoveMultipleConsecutiveBlankLines", Settings.Default.Cleaning_RemoveMultipleConsecutiveBlankLines))
        {
            transformations.Add(new NormalizeBlankLinesConverter());
        }

        transformations.Add(new EnsureFinalNewlineConverter());

        return new SourceTransformationPipeline(transformations);
    }

    /// <summary>
    /// Transformations for a file produced by the top-level type split.
    /// </summary>
    internal static string ApplyHeadlessCSharpTransformationsForCreatedFile(string source, string filePath)
    {
        return ApplyHeadlessCSharpTransformations(source, filePath);
    }
    /// <summary>
    /// Discovers types across multiple files that should not be sealed, such as types used as
    /// base classes in <c>BaseListSyntax</c> or generic type constraints in <c>where T : Base</c>.
    /// </summary>
    internal static HashSet<string> DiscoverDisqualifiedTypes(IEnumerable<string> filePaths)
    {
        var disqualified = new HashSet<string>(StringComparer.Ordinal);
        if (filePaths is null)
        {
            return disqualified;
        }

        foreach (var file in filePaths)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file) || !file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var text = File.ReadAllText(file);
                var tree = CSharpSyntaxTree.ParseText(text);
                var root = tree.GetRoot();

                foreach (var baseType in root.DescendantNodes()
                             .OfType<BaseListSyntax>()
                             .SelectMany(b => b.Types))
                {
                    var name = SealedClassConverter.GetSimpleName(baseType.Type);
                    if (!string.IsNullOrEmpty(name))
                    {
                        disqualified.Add(name);
                    }
                }

                foreach (var constraint in root.DescendantNodes()
                             .OfType<TypeParameterConstraintClauseSyntax>()
                             .SelectMany(c => c.Constraints)
                             .OfType<TypeConstraintSyntax>())
                {
                    var name = SealedClassConverter.GetSimpleName(constraint.Type);
                    if (!string.IsNullOrEmpty(name))
                    {
                        disqualified.Add(name);
                    }
                }
            }
            catch
            {
                // Non-fatal if a file cannot be parsed during discovery
            }
        }

        return disqualified;
    }

    /// <summary>
    /// Discovers disqualified types from sibling C# files in the directory containing <paramref name="filePath"/>.
    /// </summary>
    internal static HashSet<string> DiscoverDisqualifiedTypesForFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                var csFiles = Directory.GetFiles(dir, "*.cs", SearchOption.TopDirectoryOnly);
                if (csFiles.Length > 1)
                {
                    return DiscoverDisqualifiedTypes(csFiles);
                }
            }
        }
        catch
        {
        }

        return new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Determines whether the C# cleanup settings still require the editor-backed DTE path.
    /// </summary>
    /// <returns>True if editor-backed cleanup must run, otherwise false.</returns>

    internal bool RequiresEditorCleanupForCSharp()
    {
        if (Settings.Default.Reorganizing_RunAtStartOfCleanup) return true;

        if (Settings.Default.Cleaning_RunVisualStudioFormatDocumentCommand ||
            Settings.Default.ThirdParty_UseJetBrainsReSharperCleanup ||
            Settings.Default.ThirdParty_UseTelerikJustCodeCleanup ||
            Settings.Default.ThirdParty_UseXAMLStylerCleanup ||
            _otherCleaningCommands.Value.Any())
        {
            return true;
        }

        if (Settings.Default.Cleaning_RunVisualStudioRemoveAndSortUsingStatements) return true;

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

        CleanupDocument(document, usingsLeftInPlace: false);
    }

    /// <summary>
    /// Attempts to run code cleanup on the specified document.
    /// </summary>
    /// <param name="document">The document for cleanup.</param>
    /// <param name="usingsLeftInPlace">
    /// True when the semantic using move was already attempted for the file in this cleanup and left the using
    /// directives in place: it is not retried, since that would repeat the analysis and the warning.
    /// </param>

    private void CleanupDocument(Document document, bool usingsLeftInPlace)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _cleanupExecutionStats.EditorItems++;

        if (!_codeCleanupAvailabilityLogic.CanCleanupDocument(document, true)) return;

        // Make sure the document to be cleaned up is active, required for some commands like format document.
        document.Activate();

        // Check for designer windows being active, which should not proceed with cleanup as the code isn't truly active.
        if (document.ActiveWindow.Caption.EndsWith(" [Design]"))
        {
            return;
        }

        if (_package.ActiveDocument != document)
        {
            OutputWindowHelper.WarningWriteLine($"Activation was not completed before cleaning began for '{document.Name}'");
        }

        // When types are split into their own files, the semantic using move must run before the split so the
        // created files inherit the moved directives. It is then its own undo unit, like the split, and the calls in
        // the cleanup undo transaction (RunCodeCleanupCSharp) find nothing left to move. Once a move left the
        // directives in place (including the closed-file move of this cleanup), no later step retries it.
        if (!usingsLeftInPlace && Settings.Default.Cleaning_MoveTopLevelTypesToSeparateFiles && document.GetCodeLanguage() == CodeLanguage.CSharp)
        {
            usingsLeftInPlace = _moveUsingsOutsideNamespaceLogic.MoveUsingsOutsideNamespace(document.GetTextDocument()) == UsingsMoveOutcome.LeftInPlace;
        }

        TrySplitTopLevelTypesToSeparateFiles(document);

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
                var cleanupMethod = FindCodeCleanupMethod(document, usingsLeftInPlace);
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
    }

    /// <summary>
    /// Resets execution statistics for the next cleanup batch.
    /// </summary>

    internal void ResetCleanupExecutionStats()
    {
        _cleanupExecutionStats = default(CleanupExecutionStats);
    }

    /// <summary>
    /// Sets the solution-wide disqualified class-sealing type names (base types, generic
    /// constraints) to use for the remainder of the current cleanup batch. A batch orchestrator
    /// (e.g. <c>CleanupProgressViewModel</c>) should call this once, on the UI thread, before
    /// starting a batch, and clear it (pass null) once the batch completes. Outside an active
    /// batch, cleanup falls back to the narrower per-file/per-directory heuristic so that a
    /// single-document cleanup (e.g. cleanup-on-save) never triggers a full solution rescan.
    /// </summary>
    /// <param name="disqualifiedTypes">The batch-scoped disqualified type names, or null to clear.</param>

    internal void SetCurrentBatchDisqualifiedTypes(IReadOnlyCollection<string> disqualifiedTypes)
    {
        _currentBatchDisqualifiedTypes = disqualifiedTypes;
    }

    /// <summary>
    /// Gets the solution-wide disqualified type names for the currently active cleanup batch,
    /// or null when no batch is active.
    /// </summary>

    internal IReadOnlyCollection<string> GetCurrentBatchDisqualifiedTypes()
    {
        return _currentBatchDisqualifiedTypes;
    }

    /// <summary>
    /// Discovers class-sealing disqualified type names (base types, generic constraints) across
    /// every C# file in the given solution. Must run on the UI thread since it enumerates EnvDTE
    /// project items; callers running on a background thread must call this beforehand and pass
    /// the resulting plain <see cref="HashSet{T}" /> across the thread boundary. Returns null
    /// when the solution is unavailable or enumeration fails, so callers can fall back safely.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    internal static HashSet<string> DiscoverSolutionDisqualifiedTypes(CodeJanitorPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (package?.IDE?.Solution is null)
        {
            return null;
        }

        try
        {
            var csFiles = SolutionHelper.GetAllItemsInSolution<ProjectItem>(package.IDE.Solution)
                .Select(item =>
                {
                    try
                    {
                        return item.GetFileName();
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(f => !string.IsNullOrEmpty(f) && f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));

            return DiscoverDisqualifiedTypes(csFiles);
        }
        catch
        {
            return null;
        }
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
    /// Moves the using directives of a closed C# project item outside its namespaces, when enabled for the item. Must
    /// run before the headless cleanup of the item (see <see cref="MoveUsingsOutsideNamespaceLogic" />).
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>True when the file was rewritten with the moved directives.</returns>

    internal async Task<bool> MoveUsingsOutsideNamespaceAsync(ProjectItem projectItem) =>
        await _moveUsingsOutsideNamespaceLogic.MoveUsingsOutsideNamespaceAsync(projectItem) == UsingsMoveOutcome.Moved;

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
    /// <param name="usingsLeftInPlace">
    /// True when the semantic using move already left the using directives in place in this cleanup.
    /// </param>
    /// <returns>The code cleanup method, otherwise null.</returns>

    private Action<Document> FindCodeCleanupMethod(Document document, bool usingsLeftInPlace)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        switch (document.GetCodeLanguage())
        {
            case CodeLanguage.CSharp:
                return csharpDocument => RunCodeCleanupCSharp(csharpDocument, usingsLeftInPlace);

            case CodeLanguage.VisualBasic:
                return RunCodeCleanupVB;

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
                return RunCodeCleanupC;

            case CodeLanguage.HTML:
            case CodeLanguage.XAML:
            case CodeLanguage.XML:
                return RunCodeCleanupMarkup;

            case CodeLanguage.FSharp:
            case CodeLanguage.Unknown:
                return RunCodeCleanupGeneric;

            default:
                OutputWindowHelper.WarningWriteLine($"FindCodeCleanupMethod does not recognize document language '{document.Language}'");
                return null;
        }
    }

    /// <summary>
    /// Splits top-level C# types in the active document into separate files when enabled, adding generated files to the project, updating execution stats, writing a diagnostic message, and replacing the document&apos;s source with the updated content.
    /// </summary>
    /// <param name="document">The document.</param>

    private void TrySplitTopLevelTypesToSeparateFiles(Document document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!Settings.Default.Cleaning_MoveTopLevelTypesToSeparateFiles ||
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
            using (var reader = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true))
            {
                reader.ReadToEnd();
                encoding = reader.CurrentEncoding;
            }
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

        foreach (var createdFile in splitResult.CreatedFiles)
        {
            AddGeneratedFileToProject(projectItem, createdFile);
        }

        _cleanupExecutionStats.SplitOperations++;
        _cleanupExecutionStats.SplitCreatedFiles += splitResult.CreatedFiles.Count;

        OutputWindowHelper.DiagnosticWriteLine(
            $"Top-level type split for '{filePath}' created {splitResult.CreatedFiles.Count} file(s).");

        var endPoint = textDocument.EndPoint.CreateEditPoint();
        startPoint.ReplaceText(endPoint, splitResult.UpdatedSource, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
    }

    /// <summary>
    /// Adds the generated file to the source project item&apos;s collection if on the UI thread, the file exists, and it isn&apos;t already in the solution, logging a warning if the add fails.
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
            var projectItems = sourceProjectItem.Collection ?? sourceProjectItem.ContainingProject?.ProjectItems;
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
    /// <param name="usingsLeftInPlace">
    /// True when the semantic using move already left the using directives in place in this cleanup; it is then not
    /// retried.
    /// </param>

    private void RunCodeCleanupCSharp(Document document, bool usingsLeftInPlace)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var textDocument = document.GetTextDocument();

        // Move using directives outside namespace (to top of file), when enabled. Each attempt re-analyzes the
        // document semantically, so once the move left the directives in place it is not attempted again.
        if (!usingsLeftInPlace)
        {
            usingsLeftInPlace = _moveUsingsOutsideNamespaceLogic.MoveUsingsOutsideNamespace(textDocument) == UsingsMoveOutcome.LeftInPlace;
        }

        // Convert to a file-scoped namespace first (changes file structure), when enabled.
        _fileScopedNamespaceLogic.ConvertToFileScopedNamespace(textDocument);

        // Convert local variable declarations to 'var' when the type is apparent, when enabled.
        _varWhenApparentLogic.ConvertToVarWhenApparent(textDocument);

        // Add 'readonly' to fields provably never written outside their constructor, when enabled.
        _readonlyFieldLogic.AddReadonlyWhenSafe(textDocument);

        // Add 'sealed' to classes provably safe to seal within this file, when enabled.
        _sealedClassLogic.SealWhenSafe(textDocument);

        // Insert a blank line before return/throw statements that end a block, when enabled.
        _returnThrowBlankLinePaddingLogic.InsertPaddingBeforeReturnAndThrowStatements(textDocument);

        // Convert List<T>/array initializations to collection expression syntax, when enabled.
        _collectionExpressionLogic.ConvertToCollectionExpressions(textDocument);

        // Replace direct JsonSerializerOptions allocations in JsonSerializer calls, when enabled.
        _jsonSerializerOptionsReuseLogic.ReuseJsonSerializerOptionsForCA1869(textDocument);

        // Simplify single-statement lambda blocks to expression-bodied lambdas, when enabled.
        _singleStatementLambdaLogic.SimplifySingleStatementLambdas(textDocument);

        // Perform any actions that can modify the file code model first.
        RunExternalFormatting(textDocument);
        if (!document.IsExternal())
        {
            _usingStatementCleanupLogic.RemoveAndSortUsingStatements(textDocument);

            // External cleanup (e.g. ReSharper, or Format Document running a code cleanup profile) can put using
            // directives back inside the namespace; move them out again unless the move already proved unsafe. When
            // nothing is inside a namespace this is a syntax-only check, without semantic analysis.
            if (!usingsLeftInPlace)
            {
                _moveUsingsOutsideNamespaceLogic.MoveUsingsOutsideNamespace(textDocument);
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
        _fileHeaderLogic.UpdateFileHeader(textDocument);

        // Perform removal cleanup.
        _removeByteOrderMarkLogic.RemoveByteOrderMark(textDocument);
        _removeRegionLogic.RemoveRegions(regions);
        _removeWhitespaceLogic.RemoveEOLWhitespace(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesAtTop(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesAtBottom(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesAfterAttributes(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesAfterOpeningBrace(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesBeforeClosingBrace(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesBetweenChainedStatements(textDocument);
        _removeWhitespaceLogic.RemoveMultipleConsecutiveBlankLines(textDocument);

        // Perform insertion of blank line padding cleanup.
        _insertBlankLinePaddingLogic.InsertPaddingBeforeRegionTags(regions);
        _insertBlankLinePaddingLogic.InsertPaddingAfterRegionTags(regions);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeEndRegionTags(regions);
        _insertBlankLinePaddingLogic.InsertPaddingAfterEndRegionTags(regions);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(usingStatementsThatStartBlocks);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(usingStatementsThatEndBlocks);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(namespaces);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(namespaces);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(classes);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(classes);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(delegates);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(delegates);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(enumerations);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(enumerations);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(events);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(events);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(fields);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(fields);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(interfaces);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(interfaces);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(methods);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(methods);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(properties);
        _insertBlankLinePaddingLogic.InsertPaddingBetweenMultiLinePropertyAccessors(properties);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(properties);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(structs);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(structs);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCaseStatements(textDocument);
        _insertBlankLinePaddingLogic.InsertPaddingBeforeSingleLineComments(textDocument);

        // Perform insertion of explicit access modifier cleanup.
        _insertExplicitAccessModifierLogic.InsertExplicitAccessModifiersOnClasses(classes);
        _insertExplicitAccessModifierLogic.InsertExplicitAccessModifiersOnDelegates(delegates);
        _insertExplicitAccessModifierLogic.InsertExplicitAccessModifiersOnEnumerations(enumerations);
        _insertExplicitAccessModifierLogic.InsertExplicitAccessModifiersOnEvents(events);
        _insertExplicitAccessModifierLogic.InsertExplicitAccessModifiersOnFields(fields);
        _insertExplicitAccessModifierLogic.InsertExplicitAccessModifiersOnInterfaces(interfaces);
        _insertExplicitAccessModifierLogic.InsertExplicitAccessModifiersOnMethods(methods);
        _insertExplicitAccessModifierLogic.InsertExplicitAccessModifiersOnProperties(properties);
        _insertExplicitAccessModifierLogic.InsertExplicitAccessModifiersOnStructs(structs);

        // Perform insertion of whitespace cleanup.
        _insertWhitespaceLogic.InsertEOFTrailingNewLine(textDocument);

        // Perform update cleanup.
        _updateLogic.UpdateEndRegionDirectives(textDocument);
        _updateLogic.UpdateEventAccessorsToBothBeSingleLineOrMultiLine(events);
        _updateLogic.UpdatePropertyAccessorsToBothBeSingleLineOrMultiLine(properties);
        _updateLogic.UpdateSingleLineMethods(methods);

        // Perform comment cleaning.
        _commentFormatLogic.FormatComments(textDocument);
    }

    /// <summary>
    /// Attempts to run code cleanup on the specified VB.Net document.
    /// </summary>
    /// <param name="document">The document for cleanup.</param>

    private void RunCodeCleanupVB(Document document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var textDocument = document.GetTextDocument();

        // Perform any actions that can modify the file code model first.
        RunExternalFormatting(textDocument);
        if (!document.IsExternal())
        {
            _usingStatementCleanupLogic.RemoveAndSortUsingStatements(textDocument);
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
        _fileHeaderLogic.UpdateFileHeader(textDocument);

        // Perform removal cleanup.
        _removeByteOrderMarkLogic.RemoveByteOrderMark(textDocument);
        _removeRegionLogic.RemoveRegions(regions);
        _removeWhitespaceLogic.RemoveEOLWhitespace(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesAtTop(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesAtBottom(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesAfterAttributes(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesBetweenChainedStatements(textDocument);
        _removeWhitespaceLogic.RemoveMultipleConsecutiveBlankLines(textDocument);

        // Perform insertion of blank line padding cleanup.
        _insertBlankLinePaddingLogic.InsertPaddingBeforeRegionTags(regions);
        _insertBlankLinePaddingLogic.InsertPaddingAfterRegionTags(regions);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeEndRegionTags(regions);
        _insertBlankLinePaddingLogic.InsertPaddingAfterEndRegionTags(regions);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(usingStatementsThatStartBlocks);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(usingStatementsThatEndBlocks);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(namespaces);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(namespaces);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(classes);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(classes);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(delegates);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(delegates);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(enumerations);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(enumerations);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(events);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(events);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(fields);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(fields);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(interfaces);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(interfaces);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(methods);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(methods);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(properties);
        _insertBlankLinePaddingLogic.InsertPaddingBetweenMultiLinePropertyAccessors(properties);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(properties);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCodeElements(structs);
        _insertBlankLinePaddingLogic.InsertPaddingAfterCodeElements(structs);

        _insertBlankLinePaddingLogic.InsertPaddingBeforeCaseStatements(textDocument);
        _insertBlankLinePaddingLogic.InsertPaddingBeforeSingleLineComments(textDocument);

        // Perform insertion of whitespace cleanup.
        _insertWhitespaceLogic.InsertEOFTrailingNewLine(textDocument);

        // Perform comment cleaning.
        _commentFormatLogic.FormatComments(textDocument);
    }

    /// <summary>
    /// Attempts to run code cleanup on the specified C/C++ document.
    /// </summary>
    /// <param name="document">The document for cleanup.</param>

    private void RunCodeCleanupC(Document document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var textDocument = document.GetTextDocument();

        RunExternalFormatting(textDocument);

        // Perform file header cleanup.
        _fileHeaderLogic.UpdateFileHeader(textDocument);

        // Perform removal cleanup.
        _removeByteOrderMarkLogic.RemoveByteOrderMark(textDocument);
        _removeWhitespaceLogic.RemoveEOLWhitespace(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesAtTop(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesAtBottom(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesAfterOpeningBrace(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesBeforeClosingBrace(textDocument);
        _removeWhitespaceLogic.RemoveMultipleConsecutiveBlankLines(textDocument);

        // Perform insertion of blank line padding cleanup.
        _insertBlankLinePaddingLogic.InsertPaddingBeforeSingleLineComments(textDocument);

        // Perform insertion of whitespace cleanup.
        _insertWhitespaceLogic.InsertEOFTrailingNewLine(textDocument);
    }

    /// <summary>
    /// Attempts to run code cleanup on the specified markup document.
    /// </summary>
    /// <param name="document">The document for cleanup.</param>

    private void RunCodeCleanupMarkup(Document document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var textDocument = document.GetTextDocument();

        RunExternalFormatting(textDocument);

        // Run Razor-specific formatting in a safe, scoped way for .razor files only.
        _razorFormatterLogic.FormatRazorDocument(textDocument);

        if (!document.IsExternal())
        {
            _usingStatementCleanupLogic.RemoveAndSortUsingStatements(textDocument);
        }

        // Perform file header cleanup.
        _fileHeaderLogic.UpdateFileHeader(textDocument);

        // Perform removal cleanup.
        _removeByteOrderMarkLogic.RemoveByteOrderMark(textDocument);
        _removeWhitespaceLogic.RemoveEOLWhitespace(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesAtTop(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesAtBottom(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesBeforeClosingTag(textDocument);
        _removeWhitespaceLogic.RemoveBlankSpacesBeforeClosingAngleBracket(textDocument);
        _removeWhitespaceLogic.RemoveMultipleConsecutiveBlankLines(textDocument);

        // Perform insertion cleanup.
        _insertWhitespaceLogic.InsertBlankSpaceBeforeSelfClosingAngleBracket(textDocument);
        _insertWhitespaceLogic.InsertEOFTrailingNewLine(textDocument);
    }

    /// <summary>
    /// Attempts to run code cleanup on the specified generic document.
    /// </summary>
    /// <param name="document">The document for cleanup.</param>

    private void RunCodeCleanupGeneric(Document document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var textDocument = document.GetTextDocument();

        RunExternalFormatting(textDocument);

        // Perform file header cleanup.
        _fileHeaderLogic.UpdateFileHeader(textDocument);

        // Perform removal cleanup.
        _removeByteOrderMarkLogic.RemoveByteOrderMark(textDocument);
        _removeWhitespaceLogic.RemoveEOLWhitespace(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesAtTop(textDocument);
        _removeWhitespaceLogic.RemoveBlankLinesAtBottom(textDocument);
        _removeWhitespaceLogic.RemoveMultipleConsecutiveBlankLines(textDocument);

        // Perform insertion cleanup.
        _insertWhitespaceLogic.InsertEOFTrailingNewLine(textDocument);
    }

    /// <summary>
    /// Runs external formatting tools (e.g. Visual Studio, JetBrains ReSharper, Telerik JustCode).
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>

    private void RunExternalFormatting(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        RunVisualStudioFormatDocument(textDocument);
        RunJetBrainsReSharperCleanup(textDocument);
        RunTelerikJustCodeCleanup(textDocument);
        RunXAMLStylerCleanup(textDocument);
        RunOtherCleanupCommands(textDocument);
    }

    /// <summary>
    /// Runs the Visual Studio built-in format document command.
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>

    private void RunVisualStudioFormatDocument(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!Settings.Default.Cleaning_RunVisualStudioFormatDocumentCommand) return;

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

        if (!_otherCleaningCommands.Value.Any()) return;

        foreach (var commandName in _otherCleaningCommands.Value)
        {
            _commandHelper.ExecuteCommand(textDocument, commandName);
        }
    }
}
