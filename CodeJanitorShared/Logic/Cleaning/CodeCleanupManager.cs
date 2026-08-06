using EnvDTE;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Formatting;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Logic.Reorganizing;
using CodeJanitor.Model;
using CodeJanitor.Model.CodeItems;
using CodeJanitor.Properties;
using CodeJanitor.UI.Enumerations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeJanitor.Logic.Cleaning
{
    /// <summary>
    /// A manager class for cleaning up code.
    /// </summary>
    /// <remarks>
    ///
    /// Note: All POSIXRegEx text replacements search against '\n' but insert/replace with
    ///       Environment.NewLine. This handles line endings correctly.
    /// </remarks>
    internal class CodeCleanupManager
    {
        private sealed class DelegateSourceTransformation : ISourceTransformation
        {
            private readonly Func<string, string> _apply;

            internal DelegateSourceTransformation(string name, Func<string, string> apply)
            {
                Name = name;
                _apply = apply;
            }

            public string Name { get; }

            public string Apply(string source)
            {
                return _apply(source) ?? source;
            }
        }

        internal enum HeadlessCleanupResult
        {
            NotApplicable,
            NoChanges,
            Changed
        }

        internal struct CleanupExecutionStats
        {
            internal int HeadlessChangedItems { get; set; }

            internal int HeadlessNoOpItems { get; set; }

            internal int EditorItems { get; set; }

            internal int SplitOperations { get; set; }

            internal int SplitCreatedFiles { get; set; }

            internal int TotalProcessedItems => HeadlessChangedItems + HeadlessNoOpItems + EditorItems;
        }

        #region Fields

        private readonly CodeJanitorPackage _package;

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
        private readonly VarWhenApparentLogic _varWhenApparentLogic;
        private readonly ReadonlyFieldLogic _readonlyFieldLogic;
        private readonly AiXmlDocumentationLogic _aiXmlDocumentationLogic;
        private readonly RazorFormatterLogic _razorFormatterLogic;
        private readonly ReturnThrowBlankLinePaddingLogic _returnThrowBlankLinePaddingLogic;
        private readonly SealedClassLogic _sealedClassLogic;
        private readonly SingleStatementLambdaLogic _singleStatementLambdaLogic;
        private readonly RemoveRegionLogic _removeRegionLogic;
        private readonly RemoveWhitespaceLogic _removeWhitespaceLogic;
        private readonly UpdateLogic _updateLogic;
        private readonly UsingStatementCleanupLogic _usingStatementCleanupLogic;

        private readonly CachedSettingSet<string> _otherCleaningCommands =
            new CachedSettingSet<string>(() => Settings.Default.ThirdParty_OtherCleaningCommandsExpression,
                                         expression =>
                                         expression.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries)
                                                   .Select(x => x.Trim())
                                                   .Where(y => !string.IsNullOrEmpty(y))
                                                   .ToList());

        private CleanupExecutionStats _cleanupExecutionStats;

        #endregion Fields

        #region Constructors

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
            _fileHeaderLogic = FileHeaderLogic.GetInstance(_package);
            _fileScopedNamespaceLogic = FileScopedNamespaceLogic.GetInstance(_package);
            _varWhenApparentLogic = VarWhenApparentLogic.GetInstance(_package);
            _readonlyFieldLogic = ReadonlyFieldLogic.GetInstance(_package);
            _aiXmlDocumentationLogic = AiXmlDocumentationLogic.GetInstance(_package);
            _razorFormatterLogic = RazorFormatterLogic.GetInstance(_package);
            _returnThrowBlankLinePaddingLogic = ReturnThrowBlankLinePaddingLogic.GetInstance(_package);
            _sealedClassLogic = SealedClassLogic.GetInstance(_package);
            _singleStatementLambdaLogic = SingleStatementLambdaLogic.GetInstance(_package);
            _removeRegionLogic = RemoveRegionLogic.GetInstance(_package);
            _removeWhitespaceLogic = RemoveWhitespaceLogic.GetInstance(_package);
            _updateLogic = UpdateLogic.GetInstance(_package);
            _usingStatementCleanupLogic = UsingStatementCleanupLogic.GetInstance(_package);
        }

        #endregion Constructors

        #region Internal Methods

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
            var headlessResult = wasOpen ? HeadlessCleanupResult.NotApplicable : TryRunHeadlessPreCleanupForCSharp(projectItem);

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
                catch (Exception)
                {
                    // OK if file cannot be opened (ex: deleted from disk, non-text based type.)
                }
            }

            if (projectItem.Document != null)
            {
                Cleanup(projectItem.Document);

                // Close the document if it was opened for cleanup.
                if (Settings.Default.Cleaning_AutoSaveAndCloseIfOpenedByCleanup && !wasOpen)
                {
                    projectItem.Document.Close(vsSaveChanges.vsSaveChangesYes);
                }
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
            if (string.IsNullOrEmpty(projectItemFileName) ||
                !projectItemFileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(projectItemFileName))
            {
                return HeadlessCleanupResult.NotApplicable;
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
                if (Settings.Default.Cleaning_MoveTopLevelTypesToSeparateFiles)
                {
                    var splitResult = _topLevelTypeToFileSplitFileProcessor.Apply(
                        originalSource,
                        projectItemFileName,
                        encoding,
                        ApplyHeadlessCSharpTransformations,
                        transformUpdatedSource: false);
                    if (splitResult.Changed)
                    {
                        splitChanged = true;
                        originalSource = splitResult.UpdatedSource;

                        foreach (var createdFile in splitResult.CreatedFiles)
                        {
                            AddGeneratedFileToProject(projectItem, createdFile);
                        }

                        _cleanupExecutionStats.SplitOperations++;
                        _cleanupExecutionStats.SplitCreatedFiles += splitResult.CreatedFiles.Count;

                        OutputWindowHelper.DiagnosticWriteLine(
                            $"Headless top-level type split for '{projectItemFileName}' created {splitResult.CreatedFiles.Count} file(s).");
                    }
                }

                var transformedSource = ApplyHeadlessCSharpTransformations(originalSource, projectItemFileName);
                if (splitChanged || !string.Equals(originalSource, transformedSource, StringComparison.Ordinal))
                {
                    File.WriteAllText(projectItemFileName, transformedSource, encoding);
                    return HeadlessCleanupResult.Changed;
                }

                return HeadlessCleanupResult.NoChanges;
            }
            catch (Exception ex)
            {
                OutputWindowHelper.WarningWriteLine(
                    $"Headless C# pre-cleanup skipped for '{projectItemFileName}' due to an error: {ex.Message}");
                return HeadlessCleanupResult.NotApplicable;
            }
        }

        /// <summary>
        /// Applies enabled, Roslyn-based C# source transformations in the same order used by the
        /// in-editor cleanup path.
        /// </summary>
        /// <param name="source">The source text.</param>
        /// <returns>Transformed source text.</returns>
        internal static string ApplyHeadlessCSharpTransformations(string source, string filePath)
        {
            var editorConfig = EditorConfigHelper.LoadCSharpOptions(filePath);
            var transformations = new List<ISourceTransformation>();

            // Region directives are policy-only structure and should always be removed.
            transformations.Add(new RegionDirectiveRemover());

            if (Settings.Default.Cleaning_ConvertToFileScopedNamespace)
            {
                var fileScopedConverter = new FileScopedNamespaceConverter();
                if (!fileScopedConverter.HasMultipleNamespaces(source))
                {
                    transformations.Add(fileScopedConverter);
                }
            }

            if (Settings.Default.Cleaning_ConvertToVarWhenApparent)
            {
                transformations.Add(new VarWhenApparentConverter());
            }

            if (Settings.Default.Cleaning_MakeFieldsReadonlyWhenSafe)
            {
                transformations.Add(new ReadonlyFieldConverter());
            }

            if (Settings.Default.Cleaning_SealClassesWhenSafe)
            {
                transformations.Add(new SealedClassConverter());
            }

            if (Settings.Default.Cleaning_InsertBlankLineBeforeReturnAndThrowStatements)
            {
                transformations.Add(new ReturnThrowBlankLinePaddingConverter());
            }

            if (Settings.Default.Cleaning_ConvertToCollectionExpressions)
            {
                transformations.Add(new CollectionExpressionConverter());
            }

            if (Settings.Default.Cleaning_ReuseJsonSerializerOptionsForCA1869)
            {
                transformations.Add(new JsonSerializerOptionsReuseConverter());
            }

            if (Settings.Default.Cleaning_SimplifySingleStatementLambdas)
            {
                transformations.Add(new SingleStatementLambdaConverter());
            }

            if (Settings.Default.Cleaning_InsertExplicitAccessModifiersOnClasses ||
                Settings.Default.Cleaning_InsertExplicitAccessModifiersOnDelegates ||
                Settings.Default.Cleaning_InsertExplicitAccessModifiersOnEnumerations ||
                Settings.Default.Cleaning_InsertExplicitAccessModifiersOnEvents ||
                Settings.Default.Cleaning_InsertExplicitAccessModifiersOnFields ||
                Settings.Default.Cleaning_InsertExplicitAccessModifiersOnInterfaces ||
                Settings.Default.Cleaning_InsertExplicitAccessModifiersOnMethods ||
                Settings.Default.Cleaning_InsertExplicitAccessModifiersOnProperties ||
                Settings.Default.Cleaning_InsertExplicitAccessModifiersOnStructs)
            {
                transformations.Add(new ExplicitAccessModifierConverter());
            }

            if (Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses ||
                Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses ||
                Settings.Default.Cleaning_InsertBlankLinePaddingBeforeDelegates ||
                Settings.Default.Cleaning_InsertBlankLinePaddingAfterDelegates ||
                Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEnumerations ||
                Settings.Default.Cleaning_InsertBlankLinePaddingAfterEnumerations ||
                Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEvents ||
                Settings.Default.Cleaning_InsertBlankLinePaddingAfterEvents ||
                Settings.Default.Cleaning_InsertBlankLinePaddingBeforeFieldsMultiLine ||
                Settings.Default.Cleaning_InsertBlankLinePaddingAfterFieldsMultiLine ||
                Settings.Default.Cleaning_InsertBlankLinePaddingBeforeFieldsSingleLine ||
                Settings.Default.Cleaning_InsertBlankLinePaddingAfterFieldsSingleLine ||
                Settings.Default.Cleaning_InsertBlankLinePaddingBeforeInterfaces ||
                Settings.Default.Cleaning_InsertBlankLinePaddingAfterInterfaces ||
                Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods ||
                Settings.Default.Cleaning_InsertBlankLinePaddingAfterMethods ||
                Settings.Default.Cleaning_InsertBlankLinePaddingBeforeNamespaces ||
                Settings.Default.Cleaning_InsertBlankLinePaddingAfterNamespaces ||
                Settings.Default.Cleaning_InsertBlankLinePaddingBeforePropertiesMultiLine ||
                Settings.Default.Cleaning_InsertBlankLinePaddingAfterPropertiesMultiLine ||
                Settings.Default.Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine ||
                Settings.Default.Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine ||
                Settings.Default.Cleaning_InsertBlankLinePaddingBeforeStructs ||
                Settings.Default.Cleaning_InsertBlankLinePaddingAfterStructs ||
                Settings.Default.Cleaning_InsertBlankLinePaddingBeforeUsingStatementBlocks ||
                Settings.Default.Cleaning_InsertBlankLinePaddingAfterUsingStatementBlocks)
            {
                // TODO: BlankLinePaddingConverter disabled (not yet implemented).
                // The conversion logic would move blank line padding from editor-dependent DTE path to headless Roslyn pipeline.
                // transformations.Add(new BlankLinePaddingConverter());
            }

            if (Settings.Default.Cleaning_UpdateEndRegionDirectives)
            {
                transformations.Add(new UpdateEndRegionDirectivesConverter());
            }

            if (Settings.Default.Cleaning_UpdateSingleLineMethods)
            {
                transformations.Add(new UpdateSingleLineMethodsConverter());
            }

            if (Settings.Default.Cleaning_UpdateAccessorsToBothBeSingleLineOrMultiLine)
            {
                transformations.Add(new UpdateAccessorsToBothBeSingleLineOrMultiLineConverter());
            }

            if (Settings.Default.Formatting_CommentRunDuringCleanup)
            {
                transformations.Add(new CommentFormatConverter());
            }

            if (!string.IsNullOrWhiteSpace(Settings.Default.Cleaning_UpdateFileHeaderCSharp))
            {
                transformations.Add(new DelegateSourceTransformation("Update C# file header", ApplyConfiguredCSharpFileHeader));
            }

            if (Settings.Default.Cleaning_AiXmlDocumentationEnabled &&
                !Settings.Default.Cleaning_AiXmlDocumentationPreviewChanges &&
                AiXmlDocumentationLogic.IsConfigurationPresent())
            {
                var aiXmlDocumentationLogic = AiXmlDocumentationLogic.GetInstance(_instance._package);
                transformations.Add(new DelegateSourceTransformation("Apply AI XML documentation", aiXmlDocumentationLogic.ApplyXmlDocumentationToSource));
            }

            if (string.Equals(editorConfig.IndentStyle, "space", StringComparison.OrdinalIgnoreCase))
            {
                var tabSize = editorConfig.TabWidth ?? editorConfig.IndentSize ?? 4;
                transformations.Add(new TabToSpaceConverter(tabSize));
            }

            if (!Settings.Default.Cleaning_RunVisualStudioRemoveAndSortUsingStatements &&
                editorConfig.SortSystemDirectivesFirst == true &&
                editorConfig.SeparateImportDirectiveGroups != true)
            {
                transformations.Add(new UsingDirectiveOrganizer());
            }

            if (Settings.Default.Cleaning_RemoveEndOfLineWhitespace)
            {
                transformations.Add(new RemoveTrailingWhitespaceConverter());
            }
            else if (editorConfig.TrimTrailingWhitespace == true)
            {
                transformations.Add(new RemoveTrailingWhitespaceConverter());
            }

            if (Settings.Default.Cleaning_RemoveBlankLinesAtTop)
            {
                transformations.Add(new DelegateSourceTransformation("Remove blank lines at top", RemoveBlankLinesAtTop));
            }

            if (Settings.Default.Cleaning_RemoveBlankLinesAtBottom)
            {
                transformations.Add(new DelegateSourceTransformation("Remove blank lines at bottom", RemoveBlankLinesAtBottom));
            }

            if (Settings.Default.Cleaning_RemoveEndOfFileTrailingNewLine)
            {
                transformations.Add(new DelegateSourceTransformation("Remove final newline", RemoveFinalNewline));
            }

            if (Settings.Default.Cleaning_RemoveBlankLinesAfterAttributes)
            {
                transformations.Add(new DelegateSourceTransformation("Remove blank lines after attributes", RemoveBlankLinesAfterAttributes));
            }

            if (Settings.Default.Cleaning_RemoveBlankLinesAfterOpeningBrace)
            {
                transformations.Add(new DelegateSourceTransformation("Remove blank lines after opening brace", RemoveBlankLinesAfterOpeningBrace));
            }

            if (Settings.Default.Cleaning_RemoveBlankLinesBeforeClosingBrace)
            {
                transformations.Add(new DelegateSourceTransformation("Remove blank lines before closing brace", RemoveBlankLinesBeforeClosingBrace));
            }

            if (Settings.Default.Cleaning_RemoveBlankLinesBetweenChainedStatements)
            {
                transformations.Add(new DelegateSourceTransformation("Remove blank lines between chained statements", RemoveBlankLinesBetweenChainedStatements));
            }

            if (Settings.Default.Cleaning_RemoveMultipleConsecutiveBlankLines)
            {
                transformations.Add(new NormalizeBlankLinesConverter());
            }

            if (Settings.Default.Cleaning_InsertEndOfFileTrailingNewLine)
            {
                transformations.Add(new EnsureFinalNewlineConverter());
            }
            else if (editorConfig.InsertFinalNewline == true)
            {
                transformations.Add(new EnsureFinalNewlineConverter());
            }

            if (transformations.Count == 0)
            {
                return source;
            }

            var pipeline = new SourceTransformationPipeline(transformations);
            return pipeline.Run(source);
        }

        /// <summary>
        /// Determines whether the C# cleanup settings still require the editor-backed DTE path.
        /// </summary>
        /// <returns>True if editor-backed cleanup must run, otherwise false.</returns>
        private bool RequiresEditorCleanupForCSharp()
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

            if (Settings.Default.Cleaning_AiXmlDocumentationEnabled)
            {
                return Settings.Default.Cleaning_AiXmlDocumentationPreviewChanges;
            }

            return false;
        }

        private static string ApplyConfiguredCSharpFileHeader(string source)
        {
            var settingsFileHeader = Settings.Default.Cleaning_UpdateFileHeaderCSharp;
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

            var headerPosition = (HeaderPosition)Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition;
            var headerUpdateMode = (HeaderUpdateMode)Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode;

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

        private static string InsertHeaderAtDocumentStart(string source, string settingsFileHeader)
        {
            return source.StartsWith(settingsFileHeader.Trim(), StringComparison.Ordinal)
                ? source
                : settingsFileHeader + source;
        }

        private static string ReplaceHeaderAtDocumentStart(string source, string settingsFileHeader)
        {
            TryExtractLeadingHeaderSegment(source, out var currentHeaderLength, out var currentHeader);
            return string.Equals(currentHeader, settingsFileHeader.Trim(), StringComparison.Ordinal)
                ? source
                : settingsFileHeader + source.Substring(currentHeaderLength);
        }

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

        private static string EnsureHeaderStartsOnNewLine(string header)
        {
            var newline = header.Contains("\r\n") ? "\r\n" : Environment.NewLine;
            return header.StartsWith(newline, StringComparison.Ordinal) ? header : newline + header;
        }

        private static int GetTopLevelUsingInsertionIndex(string source)
        {
            var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
            return root.Usings.Count == 0 ? 0 : root.Usings.Last().FullSpan.End;
        }

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

        private static string NormalizeLineEndings(string value, string newline)
        {
            return value.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", newline);
        }

        private static string RemoveBlankLinesAtTop(string source)
        {
            return Regex.Replace(source, @"\A(?:[ \t]*\r?\n)+", string.Empty);
        }

        private static string RemoveBlankLinesAtBottom(string source)
        {
            return Regex.Replace(source, @"(?:\r?\n[ \t]*)+\z", string.Empty);
        }

        private static string RemoveFinalNewline(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source;
            }

            if (source.EndsWith("\r\n", StringComparison.Ordinal))
            {
                return source.Substring(0, source.Length - 2);
            }

            if (source.EndsWith("\n", StringComparison.Ordinal))
            {
                return source.Substring(0, source.Length - 1);
            }

            return source;
        }

        private static string RemoveBlankLinesAfterAttributes(string source)
        {
            return ReplaceUsingFileLineEnding(source, @"(^[ \t]*\[[^\]]+\][ \t]*(//[^\r\n]*)*)(\r?\n){2}(?![ \t]*//)", "$1{NL}");
        }

        private static string RemoveBlankLinesAfterOpeningBrace(string source)
        {
            return ReplaceUsingFileLineEnding(source, @"\{([ \t]*(//[^\r\n]*)*)(\r?\n){2,}", "{$1{NL}");
        }

        private static string RemoveBlankLinesBeforeClosingBrace(string source)
        {
            return ReplaceUsingFileLineEnding(source, @"(\r?\n){2,}([ \t]*)\}", "{NL}$2}");
        }

        private static string RemoveBlankLinesBetweenChainedStatements(string source)
        {
            return ReplaceUsingFileLineEnding(source, @"(\r?\n){2,}([ \t]*)(else|catch|finally)( |\t|\r?\n)", "{NL}$2$3$4");
        }

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
                    var cleanupMethod = FindCodeCleanupMethod(document);
                    if (cleanupMethod != null)
                    {
                        OutputWindowHelper.InfoWriteLine($"Cleanup started for '{document.FullName}'");
                        _package.IDE.StatusBar.Text = string.Format(Resources.CodeJanitorIsCleaning0, document.Name);

                        // Perform the set of configured cleanups based on the language.
                        cleanupMethod(document);

                        _package.IDE.StatusBar.Text = string.Format(Resources.CodeJanitorCleaned0, document.Name);
                        OutputWindowHelper.InfoWriteLine($"Cleanup completed for '{document.FullName}'");
                    }
                });
        }

        /// <summary>
        /// Resets execution statistics for the next cleanup batch.
        /// </summary>
        internal void ResetCleanupExecutionStats()
        {
            _cleanupExecutionStats = default(CleanupExecutionStats);
        }

        /// <summary>
        /// Returns the current execution statistics for the ongoing cleanup batch.
        /// </summary>
        /// <returns>The current cleanup execution statistics.</returns>
        internal CleanupExecutionStats GetCleanupExecutionStats()
        {
            return _cleanupExecutionStats;
        }

        #endregion Internal Methods

        #region Private Language Methods

        /// <summary>
        /// Finds a code cleanup method appropriate for the specified document, otherwise null.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <returns>The code cleanup method, otherwise null.</returns>
        private Action<Document> FindCodeCleanupMethod(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            switch (document.GetCodeLanguage())
            {
                case CodeLanguage.CSharp:
                    return RunCodeCleanupCSharp;

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

        private void TrySplitTopLevelTypesToSeparateFiles(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!Settings.Default.Cleaning_MoveTopLevelTypesToSeparateFiles ||
                document == null ||
                document.GetCodeLanguage() != CodeLanguage.CSharp)
            {
                return;
            }

            var projectItem = document.ProjectItem;
            var filePath = projectItem?.GetFileName();
            if (projectItem == null || string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            var textDocument = document.GetTextDocument();
            if (textDocument == null)
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
                ApplyHeadlessCSharpTransformations);
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

        private void AddGeneratedFileToProject(ProjectItem sourceProjectItem, string filePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sourceProjectItem == null || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return;
            }

            if (_package.IDE.Solution.FindProjectItem(filePath) != null)
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
        /// Attempts to run code cleanup on the specified CSharp document.
        /// </summary>
        /// <param name="document">The document for cleanup.</param>
        private void RunCodeCleanupCSharp(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var textDocument = document.GetTextDocument();

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
            _removeRegionLogic.RemoveRegions(regions);
            _removeWhitespaceLogic.RemoveEOLWhitespace(textDocument);
            _removeWhitespaceLogic.RemoveBlankLinesAtTop(textDocument);
            _removeWhitespaceLogic.RemoveBlankLinesAtBottom(textDocument);
            _removeWhitespaceLogic.RemoveEOFTrailingNewLine(textDocument);
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

            // Add AI-assisted XML documentation before comment formatting so normal formatter can
            // align and wrap newly inserted tags consistently.
            _aiXmlDocumentationLogic.ApplyXmlDocumentation(textDocument);

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
            _removeRegionLogic.RemoveRegions(regions);
            _removeWhitespaceLogic.RemoveEOLWhitespace(textDocument);
            _removeWhitespaceLogic.RemoveBlankLinesAtTop(textDocument);
            _removeWhitespaceLogic.RemoveBlankLinesAtBottom(textDocument);
            _removeWhitespaceLogic.RemoveEOFTrailingNewLine(textDocument);
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
            _removeWhitespaceLogic.RemoveEOLWhitespace(textDocument);
            _removeWhitespaceLogic.RemoveBlankLinesAtTop(textDocument);
            _removeWhitespaceLogic.RemoveBlankLinesAtBottom(textDocument);
            _removeWhitespaceLogic.RemoveEOFTrailingNewLine(textDocument);
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
            _removeWhitespaceLogic.RemoveEOLWhitespace(textDocument);
            _removeWhitespaceLogic.RemoveBlankLinesAtTop(textDocument);
            _removeWhitespaceLogic.RemoveBlankLinesAtBottom(textDocument);
            _removeWhitespaceLogic.RemoveEOFTrailingNewLine(textDocument);
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
            _removeWhitespaceLogic.RemoveEOLWhitespace(textDocument);
            _removeWhitespaceLogic.RemoveBlankLinesAtTop(textDocument);
            _removeWhitespaceLogic.RemoveBlankLinesAtBottom(textDocument);
            _removeWhitespaceLogic.RemoveEOFTrailingNewLine(textDocument);
            _removeWhitespaceLogic.RemoveMultipleConsecutiveBlankLines(textDocument);

            // Perform insertion cleanup.
            _insertWhitespaceLogic.InsertEOFTrailingNewLine(textDocument);
        }

        #endregion Private Language Methods

        #region Private Cleanup Methods

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

        #endregion Private Cleanup Methods
    }
}