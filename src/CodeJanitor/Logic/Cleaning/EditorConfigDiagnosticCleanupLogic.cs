using CodeJanitor.Helpers;
using CodeJanitor.Logic.Cleaning.Diagnostics;
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
/// The outcome of running .editorconfig/Roslyn diagnostic cleanup on one C# file. The default value
/// means nothing changed and nothing is unresolved (including when no category is enabled).
/// </summary>

internal struct DiagnosticCleanupOutcome
{
    /// <summary>
    /// Gets or sets a value indicating whether fixes were applied to the Visual Studio workspace.
    /// </summary>
    internal bool Changed { get; set; }

    /// <summary>
    /// Gets or sets the number of actionable diagnostics that were left unresolved.
    /// </summary>
    internal int UnresolvedCount { get; set; }

    /// <summary>
    /// Gets or sets the failure that prevented diagnostic cleanup, otherwise null.
    /// </summary>
    internal Exception Failure { get; set; }
}

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

    internal static EditorConfigDiagnosticCleanupLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new EditorConfigDiagnosticCleanupLogic(package));
    }

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
    /// and the file on disk otherwise (e.g. after the headless cleanup wrote it).
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>The diagnostic cleanup outcome.</returns>

    internal async Task<DiagnosticCleanupOutcome> CleanupAsync(EnvDTE.ProjectItem projectItem)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var filePath = projectItem.GetFileName();
        var isOpen = projectItem.IsOpen[EnvDTE.Constants.vsViewKindTextView] || projectItem.IsOpen[EnvDTE.Constants.vsViewKindCode];
        var document = isOpen ? projectItem.Document : null;

        return document is not null
            ? await CleanupAsync(document)
            : await CleanupCoreAsync(filePath, VisualStudioRoslynWorkspace.GetContainingProjectPath(projectItem), () => VisualStudioRoslynWorkspace.ReadFileText(filePath));
    }

    /// <summary>
    /// Runs diagnostic cleanup for an open C# document, using its editor buffer text as input.
    /// </summary>
    /// <param name="document">The open document.</param>
    /// <returns>The diagnostic cleanup outcome.</returns>

    internal async Task<DiagnosticCleanupOutcome> CleanupAsync(EnvDTE.Document document)
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
            });
    }

    /// <summary>
    /// Runs diagnostic cleanup for a C# file, converting every failure into an explicit failure outcome.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="projectFilePath">The file path of the project containing the item, if known.</param>
    /// <param name="readCurrentText">Reads the current cleaned text of the file; called on the UI thread.</param>
    /// <returns>The diagnostic cleanup outcome.</returns>

    private async Task<DiagnosticCleanupOutcome> CleanupCoreAsync(string filePath, string projectFilePath, Func<string> readCurrentText)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (string.IsNullOrEmpty(filePath) || !filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return default(DiagnosticCleanupOutcome);
        }

        try
        {
            return await RunInWorkspaceAsync(filePath, projectFilePath, readCurrentText);
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
    /// <returns>The diagnostic cleanup outcome.</returns>

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<DiagnosticCleanupOutcome> RunInWorkspaceAsync(
        string filePath,
        string projectFilePath,
        Func<string> readCurrentText)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var workspace = _workspace.GetWorkspace();
        var engine = _engine ?? (_engine = new DiagnosticCleanupEngine(new CodeFixProviderCatalog(GetMefCodeFixProviders())));
        var options = new DiagnosticCleanupOptions(AllCategories);
        var cancellationToken = _package.DisposalToken;

        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var currentText = readCurrentText();
            var solution = workspace.CurrentSolution;

            // The engine is host-agnostic and CPU bound: run it off the UI thread.
            await TaskScheduler.Default;
            var document = await VisualStudioRoslynWorkspace.GetDocumentAsync(solution, filePath, projectFilePath, currentText, cancellationToken);
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
            // not gain compiler errors in any other project that compiles it.
            if (result.HasChanges)
            {
                var otherFlavorError = await FindNewErrorInOtherFlavorsAsync(result.OriginalSolution, result.ChangedSolution, cancellationToken);
                if (otherFlavorError is not null)
                {
                    throw new InvalidOperationException(
                        $"Diagnostic fixes for '{filePath}' were computed in project '{document.Project.Name}', but {otherFlavorError}. No diagnostic fixes were applied.");
                }
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            if (!result.HasChanges)
            {
                LogResult(filePath, result, applied: false);
                return CreateOutcome(result, changed: false);
            }

            if (workspace.TryApplyChanges(result.ChangedSolution))
            {
                LogResult(filePath, result, applied: true);
                ApplyPostApplyOperations(workspace, filePath, result, cancellationToken);

                return CreateOutcome(result, changed: true);
            }

            OutputWindowHelper.DiagnosticWriteLine(
                $"Diagnostic cleanup for '{filePath}': Visual Studio rejected the changes (attempt {attempt} of {maxAttempts}); the solution changed while diagnostics were being fixed.");
        }

        throw new InvalidOperationException(
            $"Visual Studio rejected the diagnostic fixes for '{filePath}' twice because the solution kept changing during cleanup. No diagnostic fixes were applied.");
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
    /// Writes applied fixes and unresolved diagnostics to the CodeJanitor output pane.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="result">The engine result.</param>
    /// <param name="applied">Whether the fixes were applied to the workspace.</param>

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LogResult(string filePath, DiagnosticCleanupResult result, bool applied)
    {
        if (applied)
        {
            foreach (var fix in result.AppliedFixes)
            {
                OutputWindowHelper.InfoWriteLine(
                    $"Diagnostic cleanup fixed {fix.DiagnosticId} ({fix.Category}) x{fix.Count} using {fix.ProviderName} in '{filePath}'.");
            }
        }

        foreach (var unresolved in result.Unresolved)
        {
            OutputWindowHelper.WarningWriteLine(
                $"Diagnostic cleanup left {unresolved.DiagnosticId} ({unresolved.Category}, {unresolved.Severity}) unresolved at '{unresolved.FilePath}' line {unresolved.Line}: {unresolved.Reason}. {unresolved.Message}");
        }

        if (!result.IsComplete)
        {
            OutputWindowHelper.WarningWriteLine(
                $"Diagnostic cleanup for '{filePath}' is incomplete: some fixes were rejected as unsafe or did not converge.");
        }
    }

    /// <summary>
    /// Creates the outcome for an engine result.
    /// </summary>
    /// <param name="result">The engine result.</param>
    /// <param name="changed">Whether fixes were applied.</param>
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
