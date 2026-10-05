using CodeJanitor.Helpers;
using CodeJanitor.Logic.Cleaning.Diagnostics;
using CodeJanitor.Properties;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// Hosts the host-agnostic <see cref="DiagnosticCleanupEngine" /> inside Visual Studio: resolves the
/// C# document in the <c>VisualStudioWorkspace</c>, supplies the code fix providers exported through
/// Visual Studio MEF, runs the engine off the UI thread and applies the result with
/// <see cref="Workspace.TryApplyChanges(Solution)" /> on the UI thread.
/// </summary>
/// <remarks>
/// Roslyn workspace types are only touched from methods marked <see cref="MethodImplOptions.NoInlining" />
/// that are called inside a try/catch, so a host whose Roslyn cannot satisfy the compile-time
/// Microsoft.CodeAnalysis 5.0 reference (type/file load failures) produces an explicit, logged failure
/// instead of crashing cleanup or silently succeeding.
/// </remarks>
internal sealed class EditorConfigDiagnosticCleanupLogic
{
    private readonly CodeJanitorPackage _package;
    private readonly VisualStudioRoslynWorkspace _workspace;
    private DiagnosticCleanupEngine _engine;

    /// <summary>
    /// The singleton instance of the <see cref="EditorConfigDiagnosticCleanupLogic" /> class.
    /// </summary>
    private static EditorConfigDiagnosticCleanupLogic _instance;

    /// <summary>
    /// Gets an instance of the <see cref="EditorConfigDiagnosticCleanupLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>An instance of the <see cref="EditorConfigDiagnosticCleanupLogic" /> class.</returns>
    internal static EditorConfigDiagnosticCleanupLogic GetInstance(CodeJanitorPackage package) => _instance ?? (_instance = new EditorConfigDiagnosticCleanupLogic(package));

    /// <summary>
    /// Initializes a new instance of the <see cref="EditorConfigDiagnosticCleanupLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    private EditorConfigDiagnosticCleanupLogic(CodeJanitorPackage package)
    {
        _package = package;
        _workspace = new VisualStudioRoslynWorkspace(package);
    }

    private static readonly DiagnosticCleanupCategory[] AllCategories =
        (DiagnosticCleanupCategory[])Enum.GetValues(typeof(DiagnosticCleanupCategory));

    /// <summary>
    /// Runs diagnostic cleanup for a C# project item, using the editor buffer when the item is open
    /// and the file on disk otherwise (e.g. after the headless cleanup wrote it). A closed file also gets the Roslyn
    /// equivalents of the Visual Studio "Remove and Sort Usings" and "Format Document" commands, which the editor
    /// cleanup runs for open documents.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>The diagnostic cleanup outcome.</returns>
    internal Task<DiagnosticCleanupOutcome> CleanupAsync(EnvDTE.ProjectItem projectItem) => RunAsync(projectItem, fixNamespaceOnly: false);

    /// <summary>
    /// Runs diagnostic cleanup for an open C# document, using its editor buffer text as input.
    /// </summary>
    /// <param name="document">The open document.</param>
    /// <returns>The diagnostic cleanup outcome.</returns>
    internal Task<DiagnosticCleanupOutcome> CleanupAsync(EnvDTE.Document document) => RunAsync(document, fixNamespaceOnly: false);

    /// <summary>
    /// Makes the namespace of a C# project item match its folder through Roslyn's "Namespace does not match folder
    /// structure" analyzer and code fix (IDE0130, see <see cref="DiagnosticCleanupOptions.NamespaceMatchFolder" />),
    /// which also updates the references to the moved types. No other diagnostic is fixed. The editor buffer is used
    /// when the item is open, the file on disk otherwise.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>The outcome.</returns>
    internal Task<DiagnosticCleanupOutcome> FixNamespaceAsync(EnvDTE.ProjectItem projectItem) => RunAsync(projectItem, fixNamespaceOnly: true);

    /// <summary>
    /// Runs the engine for a C# project item, using the editor buffer when the item is open and the file on disk
    /// otherwise.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <param name="fixNamespaceOnly">True to fix only IDE0130, false for the diagnostic cleanup.</param>
    /// <returns>The outcome.</returns>
    private async Task<DiagnosticCleanupOutcome> RunAsync(EnvDTE.ProjectItem projectItem, bool fixNamespaceOnly)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var filePath = projectItem.GetFileName();
        var isOpen = projectItem.IsOpen[EnvDTE.Constants.vsViewKindTextView] || projectItem.IsOpen[EnvDTE.Constants.vsViewKindCode];
        var document = isOpen ? projectItem.Document : null;

        return document is not null
            ? await RunAsync(document, fixNamespaceOnly)
            : await CleanupCoreAsync(filePath, VisualStudioRoslynWorkspace.GetContainingProjectPath(projectItem), () => VisualStudioRoslynWorkspace.ReadFileText(filePath), isClosedFile: true, fixNamespaceOnly);
    }

    /// <summary>
    /// Runs the engine for an open C# document, using its editor buffer text as input.
    /// </summary>
    /// <param name="document">The open document.</param>
    /// <param name="fixNamespaceOnly">True to fix only IDE0130, false for the diagnostic cleanup.</param>
    /// <returns>The outcome.</returns>
    private async Task<DiagnosticCleanupOutcome> RunAsync(EnvDTE.Document document, bool fixNamespaceOnly)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        return await CleanupCoreAsync(
            document.FullName,
            VisualStudioRoslynWorkspace.GetContainingProjectPath(document.ProjectItem),
            () =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var textDocument = document.GetTextDocument();

                return textDocument.StartPoint.CreateEditPoint().GetText(textDocument.EndPoint);
            },
            isClosedFile: false,
            fixNamespaceOnly);
    }

    /// <summary>
    /// Runs diagnostic cleanup for a C# file, converting every failure into an explicit failure outcome.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="projectFilePath">The file path of the project containing the item, if known.</param>
    /// <param name="readCurrentText">Reads the current cleaned text of the file; called on the UI thread.</param>
    /// <param name="isClosedFile">True for a closed file: a change limited to the file is written to disk in the background.</param>
    /// <param name="fixNamespaceOnly">True to fix only IDE0130, false for the diagnostic cleanup.</param>
    /// <returns>The diagnostic cleanup outcome.</returns>
    private async Task<DiagnosticCleanupOutcome> CleanupCoreAsync(string filePath, string projectFilePath, Func<string> readCurrentText, bool isClosedFile, bool fixNamespaceOnly)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (string.IsNullOrEmpty(filePath) || !filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return default(DiagnosticCleanupOutcome);
        }

        try
        {
            return await RunInWorkspaceAsync(filePath, projectFilePath, readCurrentText, isClosedFile, fixNamespaceOnly);
        }
        catch (Exception ex) when (VisualStudioRoslynWorkspace.IsRoslynBindingFailure(ex))
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            return new DiagnosticCleanupOutcome
            {
                Failure = new InvalidOperationException(
                    $"Diagnostic cleanup could not bind to the Roslyn workspace API of this Visual Studio instance (CodeJanitor is compiled against Microsoft.CodeAnalysis 5.0; the host Roslyn may be older). '{filePath}' was not modified by diagnostic cleanup.",
                    ex)
            };
        }
        catch (Exception ex)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            return new DiagnosticCleanupOutcome
            {
                Failure = new InvalidOperationException($"Diagnostic cleanup failed for '{filePath}': {ex.Message}", ex)
            };
        }
    }

    /// <summary>
    /// Runs the engine against the Visual Studio workspace and applies its result. When the workspace
    /// rejects the changes (the solution changed while the engine ran), the result is recomputed once
    /// from a fresh solution before failing explicitly. Nothing is applied, and cleanup fails explicitly, when the
    /// fixes would add a compiler error in another project flavor of a changed file
    /// (see <see cref="FindNewErrorInOtherFlavorsAsync" />).
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="projectFilePath">The file path of the project containing the item, if known.</param>
    /// <param name="readCurrentText">Reads the current cleaned text of the file; called on the UI thread.</param>
    /// <param name="isClosedFile">True for a closed file: a change limited to the file is written to disk in the background.</param>
    /// <param name="fixNamespaceOnly">
    /// True to fix only IDE0130 (<see cref="DiagnosticCleanupOptions.NamespaceMatchFolder" />); false for the diagnostic
    /// cleanup with the file's effective settings, where a closed file first gets the Roslyn equivalents of the Visual
    /// Studio "Remove and Sort Usings" and "Format Document" commands.
    /// </param>
    /// <returns>The diagnostic cleanup outcome.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<DiagnosticCleanupOutcome> RunInWorkspaceAsync(
        string filePath,
        string projectFilePath,
        Func<string> readCurrentText,
        bool isClosedFile,
        bool fixNamespaceOnly)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var workspace = _workspace.GetWorkspace();
        var engine = _engine ?? (_engine = new DiagnosticCleanupEngine(new CodeFixProviderCatalog(GetMefCodeFixProviders())));
        var cancellationToken = _package.DisposalToken;

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(filePath);
        var options = fixNamespaceOnly
            ? DiagnosticCleanupOptions.NamespaceMatchFolder
            : new DiagnosticCleanupOptions(AllCategories, analyzerConfigOverrides: settings.AnalyzerConfigOverrides, usingDirectiveSorting: settings.GetUsingDirectiveSortingAfterFixes(_package.IsAutoSaveContext, isClosedFile));
        bool removeAndSortUsings = isClosedFile && !fixNamespaceOnly && settings.RunsRemoveAndSortUsings(_package.IsAutoSaveContext);
        bool format = isClosedFile && !fixNamespaceOnly && settings.GetBoolean(nameof(Settings.Cleaning_RunVisualStudioFormatDocumentCommand));
        List<string> usingsToKeep = (settings.GetString(nameof(Settings.Cleaning_UsingStatementsToReinsertWhenRemovedExpression)) ?? string.Empty)
            .Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(usingStatement => usingStatement.Trim())
            .Where(usingStatement => usingStatement.Length > 0)
            .ToList();

        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var currentText = readCurrentText();
            var solution = workspace.CurrentSolution;

            // The engine is host-agnostic and CPU bound: run it off the UI thread.
            await TaskScheduler.Default;
            Document originalDocument = await VisualStudioRoslynWorkspace.GetDocumentAsync(solution, filePath, projectFilePath, currentText, cancellationToken);
            Document document = await RoslynDocumentCleanup.ApplyAsync(originalDocument, removeAndSortUsings, format, usingsToKeep, cancellationToken);
            bool editorCommandsChanged = !(await document.GetTextAsync(cancellationToken)).ContentEquals(await originalDocument.GetTextAsync(cancellationToken));
            var result = await engine.CleanupAsync(document, options, cancellationToken);

            // Headless cleanup writes closed files to disk, and the workspace may not have observed those
            // writes yet. A fix that also edits another closed document (e.g. a rename updating references)
            // would then be computed on stale workspace text, and applying it would silently discard
            // Janitor's earlier on-disk edits. Re-run once with the disk text injected; if other closed
            // documents are still stale, fail without applying anything.
            var staleDocuments = await FindStaleClosedDocumentsAsync(workspace, result, document.Id, cancellationToken);
            if (staleDocuments.Count > 0)
            {
                var refreshedSolution = document.Project.Solution;
                foreach (var stale in staleDocuments)
                {
                    refreshedSolution = refreshedSolution.WithDocumentText(stale.Key, stale.Value);
                }

                result = await engine.CleanupAsync(refreshedSolution.GetDocument(document.Id), options, cancellationToken);

                staleDocuments = await FindStaleClosedDocumentsAsync(workspace, result, document.Id, cancellationToken);
                if (staleDocuments.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Diagnostic fixes for '{filePath}' would also change closed files whose Visual Studio workspace text differs from the file on disk ({string.Join(", ", staleDocuments.Keys.Select(id => result.OriginalSolution.GetDocument(id)?.FilePath))}). No diagnostic fixes were applied.");
                }
            }

            // The engine validated only the project flavors it changed; a linked, shared or multi-targeted file must
            // not gain compiler errors in any other project that compiles it. This covers the Roslyn "Remove and Sort
            // Usings" and "Format Document" steps as well.
            Solution changedSolution = result.HasChanges ? result.ChangedSolution : document.Project.Solution;
            if (result.HasChanges || editorCommandsChanged)
            {
                var otherFlavorError = await FindNewErrorInOtherFlavorsAsync(originalDocument.Project.Solution, changedSolution, cancellationToken);
                if (otherFlavorError is not null)
                {
                    throw new InvalidOperationException(
                        $"Cleanup of '{filePath}' was computed in project '{document.Project.Name}', but {otherFlavorError}. No changes were applied.");
                }
            }

            // A closed file whose only change is its own text is written straight to disk, so Visual Studio does not
            // open it in an invisible editor and save it on the UI thread. The file is checked on the UI thread right
            // before the write: a file the user opened meanwhile, or one that cannot be written directly (read-only,
            // source control checkout), goes through the workspace like changes to several files and follow-up host
            // operations do.
            if (isClosedFile && (result.HasChanges || editorCommandsChanged) && result.PostApplyOperations.Count == 0)
            {
                string closedFileText = await GetTextWhenOnlyTheFileChangedAsync(originalDocument.Project.Solution, changedSolution, document.Id, filePath, cancellationToken);
                if (closedFileText is not null)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                    if (!VsShellUtilities.IsDocumentOpen(_package, filePath, Guid.Empty, out _, out _, out _))
                    {
                        ClosedFileWriteResult writeResult = VisualStudioRoslynWorkspace.TryWriteClosedFileText(filePath, currentText, closedFileText);
                        if (writeResult == ClosedFileWriteResult.Written)
                        {
                            LogResult(filePath, result, applied: true, editorCommandsChanged);

                            return CreateOutcome(result, changed: result.HasChanges);
                        }

                        if (writeResult == ClosedFileWriteResult.ChangedOnDisk)
                        {
                            OutputWindowHelper.DiagnosticWriteLine(
                                $"Diagnostic cleanup for '{filePath}': the file changed on disk while it was being cleaned (attempt {attempt} of {maxAttempts}).");

                            continue;
                        }
                    }
                }
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            if (!result.HasChanges && !editorCommandsChanged)
            {
                LogResult(filePath, result, applied: false, editorCommandsChanged: false);

                return CreateOutcome(result, changed: false);
            }

            if (workspace.TryApplyChanges(changedSolution))
            {
                LogResult(filePath, result, applied: true, editorCommandsChanged);
                ApplyPostApplyOperations(workspace, filePath, result, cancellationToken);

                return CreateOutcome(result, changed: result.HasChanges);
            }

            OutputWindowHelper.DiagnosticWriteLine(
                $"Diagnostic cleanup for '{filePath}': Visual Studio rejected the changes (attempt {attempt} of {maxAttempts}); the solution changed while diagnostics were being fixed.");
        }

        throw new InvalidOperationException(
            $"Visual Studio rejected the diagnostic fixes for '{filePath}' twice because the solution kept changing during cleanup. No diagnostic fixes were applied.");
    }

    /// <summary>
    /// Gets the new text of a file when <paramref name="changedSolution" /> changes nothing but the documents of that
    /// file (one per project flavor): no added or removed documents and no other file.
    /// </summary>
    /// <param name="originalSolution">The solution the changes were computed from.</param>
    /// <param name="changedSolution">The solution with the changes.</param>
    /// <param name="documentId">The document of the file in <paramref name="changedSolution" />.</param>
    /// <param name="filePath">The file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new text of the file, or null when other files or documents are affected.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static async Task<string> GetTextWhenOnlyTheFileChangedAsync(
        Solution originalSolution,
        Solution changedSolution,
        DocumentId documentId,
        string filePath,
        CancellationToken cancellationToken)
    {
        SolutionChanges solutionChanges = changedSolution.GetChanges(originalSolution);
        if (solutionChanges.GetAddedProjects().Any() || solutionChanges.GetRemovedProjects().Any())
        {
            return null;
        }

        foreach (ProjectChanges projectChanges in solutionChanges.GetProjectChanges())
        {
            if (projectChanges.GetAddedDocuments().Any() ||
                projectChanges.GetRemovedDocuments().Any() ||
                projectChanges.GetChangedAdditionalDocuments().Any() ||
                projectChanges.GetChangedAnalyzerConfigDocuments().Any() ||
                projectChanges.GetChangedDocuments().Any(id => !string.Equals(changedSolution.GetDocument(id)?.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }
        }

        SourceText text = await changedSolution.GetDocument(documentId).GetTextAsync(cancellationToken);

        return text.ToString();
    }

    /// <summary>
    /// Checks that diagnostic fixes computed in one project flavor add no compiler error in the other flavors of the
    /// changed files: a file compiled by several projects (linked files, shared projects) or target frameworks
    /// (multi-targeted projects) has one document per flavor, bound against its own references and preprocessor
    /// symbols, and the engine only validated the flavors it changed. Every other flavor gets the changed text in a
    /// fork of <paramref name="originalSolution" />, and the Error-severity compiler diagnostics of its project are
    /// compared with the same flavor holding the original text. Files compiled by a single project need no extra work.
    /// </summary>
    /// <param name="originalSolution">The solution the fixes were computed from.</param>
    /// <param name="changedSolution">The solution with the fixes; compared to <paramref name="originalSolution" /> only document texts differ.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Null when no other flavor gets a new compiler error, otherwise the reason naming the first project that does and its first new error.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static async Task<string> FindNewErrorInOtherFlavorsAsync(Solution originalSolution, Solution changedSolution, CancellationToken cancellationToken)
    {
        var changedDocumentIds = new HashSet<DocumentId>(changedSolution.GetChanges(originalSolution)
            .GetProjectChanges()
            .SelectMany(projectChanges => projectChanges.GetChangedDocuments()));

        var baseline = originalSolution;
        var candidate = changedSolution;
        var otherFlavors = new List<(ProjectId ProjectId, string FilePath)>();
        var checkedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var documentId in changedDocumentIds)
        {
            var original = originalSolution.GetDocument(documentId);
            if (original?.FilePath is null || !checkedFilePaths.Add(original.FilePath))
            {
                continue;
            }

            var flavorIds = VisualStudioRoslynWorkspace.FindDocumentIds(originalSolution, original.FilePath, projectFilePath: null)
                .Where(id => !changedDocumentIds.Contains(id))
                .ToList();
            if (flavorIds.Count == 0)
            {
                continue;
            }

            var oldText = await original.GetTextAsync(cancellationToken);
            var newText = await changedSolution.GetDocument(documentId).GetTextAsync(cancellationToken);
            foreach (var flavorId in flavorIds)
            {
                // The workspace text of another flavor can lag behind the text the engine started from (e.g. the
                // current editor buffer), so both sides of the comparison get the engine's texts.
                var flavorText = await originalSolution.GetDocument(flavorId).GetTextAsync(cancellationToken);
                if (!flavorText.ContentEquals(oldText))
                {
                    baseline = baseline.WithDocumentText(flavorId, oldText);
                }

                candidate = candidate.WithDocumentText(flavorId, newText);
                otherFlavors.Add((flavorId.ProjectId, original.FilePath));
            }
        }

        foreach (var flavor in otherFlavors.GroupBy(flavor => flavor.ProjectId).Select(group => group.First()))
        {
            var baselineProject = baseline.GetProject(flavor.ProjectId);
            var candidateProject = candidate.GetProject(flavor.ProjectId);
            var newError = await CompilerErrors.FindFirstNewAsync(
                baselineProject,
                await CompilerErrors.GetAsync(baselineProject, cancellationToken),
                candidateProject,
                await CompilerErrors.GetAsync(candidateProject, cancellationToken),
                cancellationToken);
            if (newError is not null)
            {
                return $"they would add compiler errors in project '{originalSolution.GetProject(flavor.ProjectId).Name}', which also compiles '{flavor.FilePath}': {newError}";
            }
        }

        return null;
    }

    /// <summary>
    /// Executes, in order, the non-text operations of the accepted fixes (for example the rename
    /// notification that lets Visual Studio update XAML and designer references). The engine never
    /// executes them because they need the live host workspace; they only run once the text changes
    /// were actually applied. The first failure stops the remaining operations and fails the file,
    /// because the solution may now be only partially updated.
    /// </summary>
    /// <param name="workspace">The Visual Studio workspace the changes were applied to.</param>
    /// <param name="filePath">The file path.</param>
    /// <param name="result">The applied engine result.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyPostApplyOperations(
        Workspace workspace,
        string filePath,
        DiagnosticCleanupResult result,
        CancellationToken cancellationToken)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var operations = result.PostApplyOperations;
        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            try
            {
                operation.Apply(workspace, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Diagnostic fixes were applied to '{filePath}', but the follow-up operation '{operation.Title ?? operation.GetType().Name}' ({index + 1} of {operations.Count}) failed and the remaining {operations.Count - index - 1} operation(s) were skipped. Related references outside C# code (for example XAML or designer files) may not have been updated.",
                    ex);
            }
        }
    }

    /// <summary>
    /// Finds documents other than the target that the result changes, that are not open in an editor
    /// and whose text in <see cref="DiagnosticCleanupResult.OriginalSolution" /> differs from the file
    /// on disk (read with the same encoding detection as the headless cleanup).
    /// </summary>
    /// <param name="workspace">The Visual Studio workspace.</param>
    /// <param name="result">The engine result.</param>
    /// <param name="targetDocumentId">The target document id, whose text is already injected.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stale documents mapped to their disk text.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<Dictionary<DocumentId, SourceText>> FindStaleClosedDocumentsAsync(
        Workspace workspace,
        DiagnosticCleanupResult result,
        DocumentId targetDocumentId,
        CancellationToken cancellationToken)
    {
        var staleDocuments = new Dictionary<DocumentId, SourceText>();
        if (!result.HasChanges)
        {
            return staleDocuments;
        }

        var changedDocumentIds = result.ChangedSolution.GetChanges(result.OriginalSolution)
            .GetProjectChanges()
            .SelectMany(projectChanges => projectChanges.GetChangedDocuments())
            .Where(id => id != targetDocumentId && !workspace.IsDocumentOpen(id));

        foreach (var documentId in changedDocumentIds)
        {
            var original = result.OriginalSolution.GetDocument(documentId);
            if (original?.FilePath is null)
            {
                // No file backs the document, so there are no on-disk edits to lose.
                continue;
            }

            var originalText = await original.GetTextAsync(cancellationToken);
            var diskText = VisualStudioRoslynWorkspace.ReadFileText(original.FilePath);
            if (!string.Equals(originalText.ToString(), diskText, StringComparison.Ordinal))
            {
                staleDocuments[documentId] = SourceText.From(diskText, originalText.Encoding, originalText.ChecksumAlgorithm);
            }
        }

        return staleDocuments;
    }

    /// <summary>
    /// Gets the C# code fix providers exported through Visual Studio MEF (the IDE's built-in fixers
    /// such as naming and formatting, plus fixers from installed extensions).
    /// </summary>
    /// <returns>The code fix providers.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private List<CodeFixProvider> GetMefCodeFixProviders()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var providers = new List<CodeFixProvider>();
        var exports = _package.ComponentModel.DefaultExportProvider.GetExports<CodeFixProvider, IDictionary<string, object>>();

        foreach (var export in exports)
        {
            if (!SupportsCSharp(export.Metadata))
            {
                continue;
            }

            try
            {
                if (export.Value is not null)
                {
                    providers.Add(export.Value);
                }
            }
            catch (Exception ex)
            {
                OutputWindowHelper.DiagnosticWriteLine("Diagnostic cleanup skipped a code fix provider that failed to load.", ex);
            }
        }

        return providers;
    }

    /// <summary>
    /// Determines whether code fix provider export metadata declares the C# language.
    /// </summary>
    /// <param name="metadata">The export metadata.</param>
    /// <returns>True if the provider supports C#, otherwise false.</returns>
    private static bool SupportsCSharp(IDictionary<string, object> metadata)
    {
        if (metadata is null || !metadata.TryGetValue("Languages", out var languages))
        {
            return false;
        }

        switch (languages)
        {
            case string language:
                return string.Equals(language, "C#", StringComparison.Ordinal);

            case IEnumerable values:
                return values.OfType<string>().Any(language => string.Equals(language, "C#", StringComparison.Ordinal));

            default:
                return false;
        }
    }

    /// <summary>
    /// Writes applied fixes, applied Roslyn editor command equivalents and unresolved diagnostics to the CodeJanitor
    /// output pane.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="result">The engine result.</param>
    /// <param name="applied">Whether the changes were applied.</param>
    /// <param name="editorCommandsChanged">Whether the Roslyn "Remove and Sort Usings" / "Format Document" equivalents changed the file.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LogResult(string filePath, DiagnosticCleanupResult result, bool applied, bool editorCommandsChanged)
    {
        if (applied)
        {
            if (editorCommandsChanged)
            {
                OutputWindowHelper.InfoWriteLine(
                    $"Diagnostic cleanup applied Remove and Sort Usings / Format Document in '{filePath}'.");
            }

            foreach (var fix in result.AppliedFixes)
            {
                OutputWindowHelper.InfoWriteLine(
                    $"Diagnostic cleanup fixed {fix.DiagnosticId} ({fix.Category}) x{fix.Count} using {fix.ProviderName} in '{filePath}'.");
            }
        }

        foreach (var unresolved in result.Unresolved)
        {
            OutputWindowHelper.WarningWriteLine(
                $"Diagnostic cleanup left {unresolved.DiagnosticId} ({unresolved.Category}, {unresolved.Severity}) unresolved at '{unresolved.FilePath}' line {unresolved.Line}: {unresolved.Reason}. {unresolved.Message}"
                + (unresolved.Detail.Length == 0 ? string.Empty : $" ({unresolved.Detail})"));
        }

        if (!result.IsComplete)
        {
            OutputWindowHelper.WarningWriteLine(
                $"Diagnostic cleanup for '{filePath}' is incomplete: some fixes were rejected as unsafe, failed in their code fix provider, or did not converge.");
        }
    }

    /// <summary>
    /// Creates the outcome for an engine result.
    /// </summary>
    /// <param name="result">The engine result.</param>
    /// <param name="changed">Whether diagnostic fixes were applied; changes made only by the editor command equivalents do not count.</param>
    /// <returns>The outcome.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DiagnosticCleanupOutcome CreateOutcome(DiagnosticCleanupResult result, bool changed)
    {
        return new DiagnosticCleanupOutcome
        {
            Changed = changed,
            UnresolvedCount = result.Unresolved.Count,
        };
    }
}
