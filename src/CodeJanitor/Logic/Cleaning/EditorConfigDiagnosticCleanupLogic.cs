using CodeJanitor.Helpers;
using CodeJanitor.Logic.Cleaning.Diagnostics;
using CodeJanitor.Properties;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
/// Microsoft.CodeAnalysis 5.9 reference (type/file load failures) produces an explicit, logged failure
/// instead of crashing cleanup or silently succeeding.
/// </remarks>

internal sealed class EditorConfigDiagnosticCleanupLogic
{
    /// <summary>
    /// The MEF contract name of Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace. The
    /// type is resolved at runtime because no Microsoft.VisualStudio.LanguageServices package
    /// compatible with Microsoft.CodeAnalysis 5.9 is published.
    /// </summary>
    private const string VisualStudioWorkspaceTypeName = "Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace";

    private const string VisualStudioWorkspaceAssemblyName = "Microsoft.VisualStudio.LanguageServices";

    private readonly CodeJanitorPackage _package;
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
    }

    /// <summary>
    /// Gets the diagnostic cleanup categories enabled for the specified file, honoring repository
    /// overrides (.codejanitor) the same way as the headless C# pipeline.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    /// <returns>The enabled categories, empty when diagnostic cleanup is disabled.</returns>

    internal static List<DiagnosticCleanupCategory> GetEnabledCategories(string filePath)
    {
        var repositoryOverrides = RepositoryCleanupSettings.LoadForFile(filePath);
        bool IsEnabled(string settingName, bool fallback) => repositoryOverrides.TryGetBoolean(settingName, fallback);
        var categories = new List<DiagnosticCleanupCategory>();

        if (IsEnabled("Cleaning_ApplyEditorConfigFormatting", Settings.Default.Cleaning_ApplyEditorConfigFormatting))
        {
            categories.Add(DiagnosticCleanupCategory.Formatting);
        }

        if (IsEnabled("Cleaning_ApplyEditorConfigNaming", Settings.Default.Cleaning_ApplyEditorConfigNaming))
        {
            categories.Add(DiagnosticCleanupCategory.Naming);
        }

        if (IsEnabled("Cleaning_ApplyEditorConfigCodeStyle", Settings.Default.Cleaning_ApplyEditorConfigCodeStyle))
        {
            categories.Add(DiagnosticCleanupCategory.CodeStyle);
        }

        if (IsEnabled("Cleaning_ApplyAnalyzerCodeFixes", Settings.Default.Cleaning_ApplyAnalyzerCodeFixes))
        {
            categories.Add(DiagnosticCleanupCategory.AnalyzerFixes);
        }

        return categories;
    }

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
            : await CleanupCoreAsync(filePath, GetContainingProjectPath(projectItem), () => ReadFileText(filePath));
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
            GetContainingProjectPath(document.ProjectItem),
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

        var categories = GetEnabledCategories(filePath);
        if (categories.Count == 0)
        {
            return default(DiagnosticCleanupOutcome);
        }

        try
        {
            return await RunInWorkspaceAsync(filePath, projectFilePath, readCurrentText, categories);
        }
        catch (Exception ex) when (IsRoslynBindingFailure(ex))
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            return new DiagnosticCleanupOutcome
            {
                Failure = new InvalidOperationException(
                    $"Diagnostic cleanup could not bind to the Roslyn workspace API of this Visual Studio instance (CodeJanitor is compiled against Microsoft.CodeAnalysis 5.9; the host Roslyn may be older). '{filePath}' was not modified by diagnostic cleanup.",
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
    /// Determines whether an exception indicates that the Roslyn assemblies this extension is compiled
    /// against could not be bound to the host's Roslyn (missing/older assemblies, mismatched types).
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>True for binding failures, otherwise false.</returns>

    private static bool IsRoslynBindingFailure(Exception exception)
    {
        switch (exception)
        {
            case TypeLoadException _:
            case MissingMemberException _:
            case FileLoadException _:
            case BadImageFormatException _:
            case InvalidCastException _:
                return true;

            case FileNotFoundException fileNotFound:
                // Assembly load failures report the assembly display name, not a source file path.
                return fileNotFound.FileName?.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) == true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Runs the engine against the Visual Studio workspace and applies its result. When the workspace
    /// rejects the changes (the solution changed while the engine ran), the result is recomputed once
    /// from a fresh solution before failing explicitly.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="projectFilePath">The file path of the project containing the item, if known.</param>
    /// <param name="readCurrentText">Reads the current cleaned text of the file; called on the UI thread.</param>
    /// <param name="categories">The enabled categories.</param>
    /// <returns>The diagnostic cleanup outcome.</returns>

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<DiagnosticCleanupOutcome> RunInWorkspaceAsync(
        string filePath,
        string projectFilePath,
        Func<string> readCurrentText,
        IReadOnlyCollection<DiagnosticCleanupCategory> categories)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var workspace = GetVisualStudioWorkspace();
        var engine = _engine ?? (_engine = new DiagnosticCleanupEngine(new CodeFixProviderCatalog(GetMefCodeFixProviders())));
        var options = new DiagnosticCleanupOptions(categories);
        var cancellationToken = _package.DisposalToken;

        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var currentText = readCurrentText();
            var solution = workspace.CurrentSolution;

            // The engine is host-agnostic and CPU bound: run it off the UI thread.
            await TaskScheduler.Default;
            var document = await GetInputDocumentAsync(solution, filePath, projectFilePath, currentText, cancellationToken);
            var result = await engine.CleanupAsync(document, options, cancellationToken);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            if (!result.HasChanges)
            {
                LogResult(filePath, result, applied: false);
                return CreateOutcome(result, changed: false);
            }

            if (workspace.TryApplyChanges(result.ChangedSolution))
            {
                LogResult(filePath, result, applied: true);
                return CreateOutcome(result, changed: true);
            }

            OutputWindowHelper.DiagnosticWriteLine(
                $"Diagnostic cleanup for '{filePath}': Visual Studio rejected the changes (attempt {attempt} of {maxAttempts}); the solution changed while diagnostics were being fixed.");
        }

        throw new InvalidOperationException(
            $"Visual Studio rejected the diagnostic fixes for '{filePath}' twice because the solution kept changing during cleanup. No diagnostic fixes were applied.");
    }

    /// <summary>
    /// Resolves the input document for the engine: the C# document for the file in the given
    /// solution, with its text replaced by the current cleaned text when the workspace has not yet
    /// observed it (e.g. right after the headless cleanup wrote the file).
    /// </summary>
    /// <param name="solution">The current workspace solution.</param>
    /// <param name="filePath">The file path.</param>
    /// <param name="projectFilePath">The file path of the project containing the item, if known.</param>
    /// <param name="currentText">The current cleaned text of the file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The input document.</returns>

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<Microsoft.CodeAnalysis.Document> GetInputDocumentAsync(
        Solution solution,
        string filePath,
        string projectFilePath,
        string currentText,
        CancellationToken cancellationToken)
    {
        var documentId = FindDocumentId(solution, filePath, projectFilePath)
            ?? throw new InvalidOperationException(
                $"'{filePath}' is not part of any C# project loaded in the Visual Studio Roslyn workspace (for example it is excluded from compilation), so its diagnostics cannot be analyzed.");

        var document = solution.GetDocument(documentId);
        var workspaceText = await document.GetTextAsync(cancellationToken);
        if (string.Equals(workspaceText.ToString(), currentText, StringComparison.Ordinal))
        {
            return document;
        }

        return solution
            .WithDocumentText(documentId, SourceText.From(currentText, workspaceText.Encoding, workspaceText.ChecksumAlgorithm))
            .GetDocument(documentId);
    }

    /// <summary>
    /// Finds the C# document for the specified file. A file can map to several documents (linked
    /// files, shared projects, multi-targeted projects); the document of the project containing the
    /// project item is preferred, then the choice is made deterministically by project file path and
    /// project name (ordinal), so the same target framework flavor is always used.
    /// </summary>
    /// <param name="solution">The solution.</param>
    /// <param name="filePath">The file path.</param>
    /// <param name="projectFilePath">The file path of the project containing the item, if known.</param>
    /// <returns>The document id, otherwise null.</returns>

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DocumentId FindDocumentId(Solution solution, string filePath, string projectFilePath)
    {
        return solution.GetDocumentIdsWithFilePath(filePath)
            .Select(id => solution.GetDocument(id))
            .Where(document => document is not null && document.Project.Language == LanguageNames.CSharp)
            .OrderBy(document => string.Equals(document.Project.FilePath, projectFilePath, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(document => document.Project.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(document => document.Project.Name, StringComparer.Ordinal)
            .Select(document => document.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// Gets the Visual Studio Roslyn workspace through MEF, verifying that it shares the
    /// Microsoft.CodeAnalysis.Workspaces assembly this extension is bound to.
    /// </summary>
    /// <returns>The Visual Studio workspace.</returns>

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Workspace GetVisualStudioWorkspace()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var componentModel = _package.ComponentModel
            ?? throw new InvalidOperationException("The Visual Studio component model (MEF) service is unavailable.");

        object workspace = null;
        var workspaceType = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => string.Equals(assembly.GetName().Name, VisualStudioWorkspaceAssemblyName, StringComparison.Ordinal))
            .Select(assembly => assembly.GetType(VisualStudioWorkspaceTypeName, throwOnError: false))
            .FirstOrDefault(type => type is not null);

        if (workspaceType is not null)
        {
            // Equivalent to componentModel.GetService<VisualStudioWorkspace>().
            workspace = typeof(IComponentModel).GetMethod(nameof(IComponentModel.GetService))
                .MakeGenericMethod(workspaceType)
                .Invoke(componentModel, null);
        }
        else
        {
            // Language services not loaded yet: resolve the export by contract name (a null required
            // type identity, i.e. object, matches the export regardless of its declared type).
            workspace = componentModel.DefaultExportProvider.GetExportedValueOrDefault<object>(VisualStudioWorkspaceTypeName);
        }

        if (workspace is null)
        {
            throw new InvalidOperationException("The Visual Studio Roslyn workspace (VisualStudioWorkspace) is unavailable.");
        }

        if (workspace is Workspace compatibleWorkspace)
        {
            return compatibleWorkspace;
        }

        throw new InvalidOperationException(
            $"The Visual Studio Roslyn workspace uses {DescribeWorkspaceAssembly(workspace.GetType())}, which is not the Microsoft.CodeAnalysis.Workspaces {typeof(Workspace).Assembly.GetName().Version} this extension is bound to. Diagnostic cleanup requires a Visual Studio version whose Roslyn is 5.9 or newer.");
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

    /// <summary>
    /// Describes the Microsoft.CodeAnalysis.Workspaces assembly a host workspace type derives from.
    /// </summary>
    /// <param name="workspaceType">The host workspace type.</param>
    /// <returns>A human-readable assembly description.</returns>

    private static string DescribeWorkspaceAssembly(Type workspaceType)
    {
        for (var type = workspaceType; type is not null; type = type.BaseType)
        {
            if (string.Equals(type.FullName, "Microsoft.CodeAnalysis.Workspace", StringComparison.Ordinal))
            {
                return type.Assembly.GetName().FullName;
            }
        }

        return workspaceType.Assembly.GetName().FullName;
    }

    /// <summary>
    /// Gets the file path of the project containing the project item, when available.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>The project file path, otherwise null.</returns>

    private static string GetContainingProjectPath(EnvDTE.ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            return projectItem?.ContainingProject?.FullName;
        }
        catch (Exception)
        {
            // Some project systems do not expose a containing project; fall back to the
            // deterministic document ordering.
            return null;
        }
    }

    /// <summary>
    /// Reads the text of a file from disk, detecting its encoding from the byte order mark.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>The file text.</returns>

    private static string ReadFileText(string filePath)
    {
        using (var reader = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true))
        {
            return reader.ReadToEnd();
        }
    }
}
