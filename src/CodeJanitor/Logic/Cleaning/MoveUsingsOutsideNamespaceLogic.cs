using CodeJanitor.Helpers;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// The outcome of one attempt of the semantic using move during cleanup.
/// </summary>
internal enum UsingsMoveOutcome
{
    /// <summary>
    /// Nothing was attempted: the step is disabled, the file is not a C# file, or no namespace contains using
    /// directives.
    /// </summary>
    NotApplicable,

    /// <summary>
    /// The using directives were moved outside the namespace.
    /// </summary>
    Moved,

    /// <summary>
    /// The using directives were left in place because the move was not safe or could not be performed; the reason
    /// was written to the output pane. Later steps of the same cleanup must not retry the move.
    /// </summary>
    LeftInPlace,
}

/// <summary>
/// A class for encapsulating the logic of moving using directives outside namespace declarations during cleanup.
/// </summary>
/// <remarks>
/// The move needs the semantic model of the document (see <see cref="MoveUsingsOutsideNamespaceConverter" />), which
/// comes from the Visual Studio Roslyn workspace. A file compiled by several projects or target frameworks is moved
/// only when the move is safe, with the same result, in every one of them. When the move is not safe or the document
/// cannot be analyzed, the using directives are left in place and the reason is written to the output pane.
/// </remarks>
internal sealed class MoveUsingsOutsideNamespaceLogic
{
    private const string SettingName = nameof(Settings.Cleaning_MoveUsingsOutsideNamespace);

    private static readonly RegionDirectiveRemover RegionRemover = new RegionDirectiveRemover();

    private readonly CodeJanitorPackage _package;
    private readonly VisualStudioRoslynWorkspace _workspace;
    private readonly MoveUsingsOutsideNamespaceConverter _converter;

    private static MoveUsingsOutsideNamespaceLogic _instance;

    /// <summary>
    /// Returns the singleton MoveUsingsOutsideNamespaceLogic instance, lazily creating it with the supplied package on first access.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>A MoveUsingsOutsideNamespaceLogic value produced by this method.</returns>
    internal static MoveUsingsOutsideNamespaceLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new MoveUsingsOutsideNamespaceLogic(package));
    }

    private MoveUsingsOutsideNamespaceLogic(CodeJanitorPackage package)
    {
        _package = package;
        _workspace = new VisualStudioRoslynWorkspace(package);
        _converter = new MoveUsingsOutsideNamespaceConverter();
    }

    /// <summary>
    /// Determines whether moving using directives outside namespaces is enabled for the file: the repository policy
    /// (<c>.codejanitor</c>) overrides the user setting, as for the other cleanup steps.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>True when enabled.</returns>
    internal static bool IsEnabledFor(string filePath) =>
        RepositoryCleanupSettings.LoadForFile(filePath).TryGetBoolean(SettingName, Settings.Default.Cleaning_MoveUsingsOutsideNamespace);

    /// <summary>
    /// Moves the using directives of an open document outside its namespaces, when enabled, replacing the editor
    /// buffer (preserving markers) only when the move succeeded. Must be called only by the editor cleanup, which
    /// removes all regions anyway.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <returns>The outcome of the move.</returns>
    internal UsingsMoveOutcome MoveUsingsOutsideNamespace(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var filePath = textDocument.Parent?.FullName;
        if (!IsEnabledFor(filePath))
        {
            OutputWindowHelper.InfoWriteLine(
                $"MoveUsingsOutsideNamespaceLogic.MoveUsingsOutsideNamespace skipped for '{filePath}' because {SettingName} is false.");

            return UsingsMoveOutcome.NotApplicable;
        }

        // The editor cleanup removes every region later (RemoveRegionLogic), so region directives are removed before
        // moving, as the headless cleanup does, so "#region Usings" blocks inside a namespace do not block the move.
        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var originalText = startPoint.GetText(textDocument.EndPoint);
        var projectFilePath = VisualStudioRoslynWorkspace.GetContainingProjectPath(textDocument.Parent?.ProjectItem);

        var (outcome, movedText) = ThreadHelper.JoinableTaskFactory.Run(() => TryMoveAsync(filePath, projectFilePath, RegionRemover.Apply(originalText)));
        if (outcome != UsingsMoveOutcome.Moved)
        {
            return outcome;
        }

        var endPoint = textDocument.EndPoint.CreateEditPoint();
        startPoint.ReplaceText(endPoint, movedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);

        return UsingsMoveOutcome.Moved;
    }

    /// <summary>
    /// Moves the using directives of a closed C# file outside its namespaces, when enabled for the file, and writes the
    /// result back with the file's encoding. Runs before the headless text cleanup, so later steps (file header, using
    /// organization, type splitting) see the moved directives, as they did when the move was a text transformation.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>The outcome of the move; <see cref="UsingsMoveOutcome.Moved" /> when the file was rewritten.</returns>
    internal async Task<UsingsMoveOutcome> MoveUsingsOutsideNamespaceAsync(ProjectItem projectItem)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var filePath = projectItem.GetFileName();
        if (string.IsNullOrEmpty(filePath) || !filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !File.Exists(filePath))
        {
            return UsingsMoveOutcome.NotApplicable;
        }

        if (!IsEnabledFor(filePath))
        {
            OutputWindowHelper.InfoWriteLine(
                $"MoveUsingsOutsideNamespaceLogic.MoveUsingsOutsideNamespaceAsync skipped for '{filePath}' because {SettingName} is false.");

            return UsingsMoveOutcome.NotApplicable;
        }

        var projectFilePath = VisualStudioRoslynWorkspace.GetContainingProjectPath(projectItem);
        string originalText;
        Encoding encoding;
        try
        {
            // Without a byte order mark the file is written back without one.
            using (var reader = new StreamReader(filePath, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
            {
                originalText = await reader.ReadToEndAsync();
                encoding = reader.CurrentEncoding;
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            WriteNotMovedWarning(filePath, ex.Message);

            return UsingsMoveOutcome.LeftInPlace;
        }

        // The headless cleanup removes region directives first unless the repository policy keeps them; do the same
        // before moving, so "#region Usings" blocks inside a namespace do not block the move.
        var sourceText = RepositoryCleanupSettings.LoadForFile(filePath).RemovesRegions
            ? RegionRemover.Apply(originalText)
            : originalText;

        var (outcome, movedText) = await TryMoveAsync(filePath, projectFilePath, sourceText);
        if (outcome != UsingsMoveOutcome.Moved)
        {
            return outcome;
        }

        try
        {
            File.WriteAllText(filePath, movedText, encoding);

            return UsingsMoveOutcome.Moved;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            WriteNotMovedWarning(filePath, ex.Message);

            return UsingsMoveOutcome.LeftInPlace;
        }
    }

    private static void WriteNotMovedWarning(string filePath, string reason) =>
        OutputWindowHelper.WarningWriteLine(
            $"Using directives were not moved outside the namespace in '{filePath}': {reason} They were left in place.");

    /// <summary>
    /// Runs the semantic move and reports its outcome, with the moved text when the outcome is
    /// <see cref="UsingsMoveOutcome.Moved" />.
    /// </summary>
    private async Task<(UsingsMoveOutcome Outcome, string MovedText)> TryMoveAsync(string filePath, string projectFilePath, string currentText)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (!MoveUsingsOutsideNamespaceConverter.HasUsingsInsideNamespace(currentText))
        {
            OutputWindowHelper.InfoWriteLine(
                $"MoveUsingsOutsideNamespaceLogic made no change for '{filePath}' (no using directives inside a namespace).");

            return (UsingsMoveOutcome.NotApplicable, null);
        }

        MoveUsingsOutsideNamespaceResult result;
        try
        {
            result = await MoveInWorkspaceAsync(filePath, projectFilePath, currentText);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            WriteNotMovedWarning(
                filePath,
                VisualStudioRoslynWorkspace.IsRoslynBindingFailure(ex)
                    ? "the Roslyn workspace API of this Visual Studio instance could not be used (CodeJanitor is compiled against Microsoft.CodeAnalysis 5.9; the host Roslyn may be older)."
                    : ex.Message);

            return (UsingsMoveOutcome.LeftInPlace, null);
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        switch (result.Status)
        {
            case MoveUsingsOutsideNamespaceStatus.Moved:
                OutputWindowHelper.InfoWriteLine(
                    $"MoveUsingsOutsideNamespaceLogic moved using directives outside namespace for '{filePath}'.");

                return (UsingsMoveOutcome.Moved, result.Text);

            case MoveUsingsOutsideNamespaceStatus.Skipped:
                OutputWindowHelper.WarningWriteLine(
                    $"Using directives were not moved outside the namespace in '{filePath}' because {result.Reason}. They were left in place.");

                return (UsingsMoveOutcome.LeftInPlace, null);

            default:
                return (UsingsMoveOutcome.NotApplicable, null);
        }
    }

    /// <summary>
    /// Resolves every C# document of the file in the Visual Studio workspace with <paramref name="currentText" /> and
    /// runs the converter on them off the UI thread.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<MoveUsingsOutsideNamespaceResult> MoveInWorkspaceAsync(string filePath, string projectFilePath, string currentText)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var solution = _workspace.GetWorkspace().CurrentSolution;
        var cancellationToken = _package.DisposalToken;

        await TaskScheduler.Default;
        var documents = await VisualStudioRoslynWorkspace.GetDocumentsAsync(solution, filePath, projectFilePath, currentText, cancellationToken);

        return await MoveInEveryFlavorAsync(_converter, documents, cancellationToken);
    }

    /// <summary>
    /// Runs the converter on every document of one file (one per project and target framework flavor compiling it).
    /// Each flavor binds names against its own references and preprocessor symbols, so a move that is safe for one
    /// flavor can break another: the result is <see cref="MoveUsingsOutsideNamespaceStatus.Moved" /> only when every
    /// flavor moved the directives to the same text; otherwise it is the outcome shared by every flavor, or
    /// <see cref="MoveUsingsOutsideNamespaceStatus.Skipped" /> naming the project that does not agree.
    /// </summary>
    /// <param name="converter">The converter.</param>
    /// <param name="documents">The documents of the file, at least one, all with the same text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The combined result.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static async Task<MoveUsingsOutsideNamespaceResult> MoveInEveryFlavorAsync(
        MoveUsingsOutsideNamespaceConverter converter,
        IReadOnlyList<Microsoft.CodeAnalysis.Document> documents,
        CancellationToken cancellationToken)
    {
        if (documents.Count == 1)
        {
            return await converter.MoveUsingsOutsideAsync(documents[0], cancellationToken).ConfigureAwait(false);
        }

        MoveUsingsOutsideNamespaceResult firstResult = null;
        Microsoft.CodeAnalysis.Document firstDocument = null;
        foreach (var document in documents)
        {
            var result = await converter.MoveUsingsOutsideAsync(document, cancellationToken).ConfigureAwait(false);

            if (result.Status == MoveUsingsOutsideNamespaceStatus.Skipped)
            {
                return MoveUsingsOutsideNamespaceResult.Skipped($"{result.Reason} in project '{document.Project.Name}'");
            }

            if (firstResult is null)
            {
                firstResult = result;
                firstDocument = document;
            }
            else if (result.Status != firstResult.Status || !string.Equals(result.Text, firstResult.Text, StringComparison.Ordinal))
            {
                return MoveUsingsOutsideNamespaceResult.Skipped(
                    $"project '{document.Project.Name}' and project '{firstDocument.Project.Name}' both compile the file but the move gives a different result in each of them");
            }
        }

        return firstResult;
    }
}
