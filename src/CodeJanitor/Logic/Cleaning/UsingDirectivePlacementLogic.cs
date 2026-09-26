using CodeJanitor.Helpers;
using CodeJanitor.Logic.Transformations;
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
    /// Nothing was attempted: no using directive placement is enforced for the file, the file is not a C# file, or no
    /// using directive is on the side it would move from.
    /// </summary>
    NotApplicable,

    /// <summary>
    /// The using directives were moved to the enforced placement (outside or inside the namespace).
    /// </summary>
    Moved,

    /// <summary>
    /// The using directives were left in place because the move was not safe or could not be performed; the reason
    /// was written to the output pane. Later steps of the same cleanup must not retry the move.
    /// </summary>
    LeftInPlace,
}

/// <summary>
/// A class for encapsulating the logic of placing using directives outside or inside the namespace during cleanup, as
/// <see cref="EffectiveCleanupSettings.UsingDirectivePlacement" /> requires for the file (<c>.editorconfig</c>
/// <c>csharp_using_directive_placement</c>, then the <c>.codejanitor</c> repository policy, then the user setting).
/// </summary>
/// <remarks>
/// The move needs the semantic model of the document (see <see cref="UsingDirectivePlacementConverter" />), which
/// comes from the Visual Studio Roslyn workspace. A file compiled by several projects or target frameworks is moved
/// only when the move is safe, with the same result, in every one of them. When the move is not safe or the document
/// cannot be analyzed, the using directives are left in place and the reason is written to the output pane.
/// </remarks>
internal sealed class UsingDirectivePlacementLogic
{
    private static readonly RegionDirectiveRemover RegionRemover = new RegionDirectiveRemover();

    private readonly CodeJanitorPackage _package;
    private readonly VisualStudioRoslynWorkspace _workspace;
    private readonly UsingDirectivePlacementConverter _converter;

    private static UsingDirectivePlacementLogic _instance;

    /// <summary>
    /// Returns the singleton UsingDirectivePlacementLogic instance, lazily creating it with the supplied package on first access.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>A UsingDirectivePlacementLogic value produced by this method.</returns>
    internal static UsingDirectivePlacementLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new UsingDirectivePlacementLogic(package));
    }

    private UsingDirectivePlacementLogic(CodeJanitorPackage package)
    {
        _package = package;
        _workspace = new VisualStudioRoslynWorkspace(package);
        _converter = new UsingDirectivePlacementConverter();
    }

    /// <summary>
    /// Moves the using directives of an open document outside or inside its namespace, as enforced for the file,
    /// replacing the editor buffer (preserving markers) only when the move succeeded. Must be called only by the editor
    /// cleanup, which removes the region directives later unless the repository policy keeps them.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <returns>The outcome of the move.</returns>
    internal UsingsMoveOutcome PlaceUsingDirectives(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var filePath = textDocument.Parent?.FullName;
        var settings = EffectiveCleanupSettings.For(filePath);
        var placement = settings.UsingDirectivePlacement;
        if (placement == UsingDirectivePlacementPreference.Unchanged)
        {
            WriteNoPlacementEnforced(nameof(PlaceUsingDirectives), filePath);

            return UsingsMoveOutcome.NotApplicable;
        }

        // The editor cleanup removes the region directives later (RemoveRegionLogic) unless the repository policy keeps
        // them. When they are removed anyway, they are removed before moving, as the headless cleanup does, so
        // "#region Usings" blocks do not block the move.
        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var originalText = startPoint.GetText(textDocument.EndPoint);
        var sourceText = settings.RemovesRegions
            ? RegionRemover.Apply(originalText)
            : originalText;
        var projectFilePath = VisualStudioRoslynWorkspace.GetContainingProjectPath(textDocument.Parent?.ProjectItem);

        var (outcome, movedText) = ThreadHelper.JoinableTaskFactory.Run(() => TryPlaceAsync(filePath, projectFilePath, sourceText, placement, CancellationToken.None));
        if (outcome != UsingsMoveOutcome.Moved)
        {
            return outcome;
        }

        var endPoint = textDocument.EndPoint.CreateEditPoint();
        startPoint.ReplaceText(endPoint, movedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);

        return UsingsMoveOutcome.Moved;
    }

    /// <summary>
    /// Moves the using directives of a closed C# file outside or inside its namespace, as enforced for the file, and
    /// writes the result back with the file's encoding. Runs before the headless text cleanup, so later steps (file
    /// header, using organization, type splitting) see the moved directives.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <param name="cancellationToken">
    /// Cancels the semantic analysis of the move (together with the disposal of the package); the file is then left
    /// unchanged.
    /// </param>
    /// <returns>The outcome of the move; <see cref="UsingsMoveOutcome.Moved" /> when the file was rewritten.</returns>
    /// <exception cref="OperationCanceledException">The move was canceled.</exception>
    internal async Task<UsingsMoveOutcome> PlaceUsingDirectivesAsync(ProjectItem projectItem, CancellationToken cancellationToken = default)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var filePath = projectItem.GetFileName();
        if (string.IsNullOrEmpty(filePath) || !filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !File.Exists(filePath))
        {
            return UsingsMoveOutcome.NotApplicable;
        }

        var projectFilePath = VisualStudioRoslynWorkspace.GetContainingProjectPath(projectItem);

        return await PlaceUsingDirectivesInFileAsync(
            filePath,
            (sourceText, placement) => TryPlaceAsync(filePath, projectFilePath, sourceText, placement, cancellationToken));
    }

    /// <summary>
    /// Places the using directives of the closed C# file <paramref name="filePath" /> as enforced for it: reads the
    /// file, removes its region directives first unless the repository policy keeps them (as the headless cleanup
    /// does, so "#region Usings" blocks do not block the move), runs <paramref name="placeAsync" /> on that text and,
    /// only when it moved the directives, writes the moved text back with the file's encoding: a byte order mark is
    /// kept when the file has one and not added when it has none.
    /// </summary>
    /// <param name="filePath">The path of the C# file.</param>
    /// <param name="placeAsync">
    /// Runs the semantic move of the given text in the given direction and returns its outcome, with the moved text
    /// when the outcome is <see cref="UsingsMoveOutcome.Moved" />.
    /// </param>
    /// <returns>The outcome of the move; <see cref="UsingsMoveOutcome.Moved" /> when the file was rewritten.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="placeAsync" /> was canceled; the file is left unchanged.</exception>
    internal static async Task<UsingsMoveOutcome> PlaceUsingDirectivesInFileAsync(
        string filePath,
        Func<string, UsingDirectivePlacementPreference, Task<(UsingsMoveOutcome Outcome, string MovedText)>> placeAsync)
    {
        var settings = EffectiveCleanupSettings.For(filePath);
        var placement = settings.UsingDirectivePlacement;
        if (placement == UsingDirectivePlacementPreference.Unchanged)
        {
            WriteNoPlacementEnforced(nameof(PlaceUsingDirectivesAsync), filePath);

            return UsingsMoveOutcome.NotApplicable;
        }

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
            WriteNotMovedWarning(filePath, placement, ex.Message);

            return UsingsMoveOutcome.LeftInPlace;
        }

        var sourceText = settings.RemovesRegions
            ? RegionRemover.Apply(originalText)
            : originalText;

        // The file is written only after the whole semantic move completed, so a canceled move leaves it unchanged.
        var (outcome, movedText) = await placeAsync(sourceText, placement);
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
            WriteNotMovedWarning(filePath, placement, ex.Message);

            return UsingsMoveOutcome.LeftInPlace;
        }
    }

    /// <summary>
    /// Runs the converter for <paramref name="placement" /> on every document of one file (one per project and target
    /// framework flavor compiling it). Each flavor binds names against its own references and preprocessor symbols, so
    /// a move that is safe for one flavor can break another: the result is <see cref="UsingDirectivePlacementStatus.Moved" />
    /// only when every flavor moved the directives to the same text; otherwise it is the outcome shared by every flavor,
    /// or <see cref="UsingDirectivePlacementStatus.Skipped" /> naming the project that does not agree. Nothing moves
    /// when <paramref name="placement" /> is <see cref="UsingDirectivePlacementPreference.Unchanged" />.
    /// </summary>
    /// <param name="converter">The converter.</param>
    /// <param name="placement">The using directive placement to enforce.</param>
    /// <param name="documents">The documents of the file, at least one, all with the same text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The combined result.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static async Task<UsingDirectivePlacementResult> PlaceInEveryFlavorAsync(
        UsingDirectivePlacementConverter converter,
        UsingDirectivePlacementPreference placement,
        IReadOnlyList<Microsoft.CodeAnalysis.Document> documents,
        CancellationToken cancellationToken)
    {
        Func<Microsoft.CodeAnalysis.Document, Task<UsingDirectivePlacementResult>> move;
        switch (placement)
        {
            case UsingDirectivePlacementPreference.OutsideNamespace:
                move = document => converter.MoveUsingsOutsideAsync(document, cancellationToken);
                break;

            case UsingDirectivePlacementPreference.InsideNamespace:
                move = document => converter.MoveUsingsInsideAsync(document, cancellationToken);
                break;

            default:
                return UsingDirectivePlacementResult.NothingToMove;
        }

        if (documents.Count == 1)
        {
            return await move(documents[0]).ConfigureAwait(false);
        }

        UsingDirectivePlacementResult firstResult = null;
        Microsoft.CodeAnalysis.Document firstDocument = null;
        foreach (var document in documents)
        {
            var result = await move(document).ConfigureAwait(false);

            if (result.Status == UsingDirectivePlacementStatus.Skipped)
            {
                return UsingDirectivePlacementResult.Skipped($"{result.Reason} in project '{document.Project.Name}'");
            }

            if (firstResult is null)
            {
                firstResult = result;
                firstDocument = document;
            }
            else if (result.Status != firstResult.Status || !string.Equals(result.Text, firstResult.Text, StringComparison.Ordinal))
            {
                return UsingDirectivePlacementResult.Skipped(
                    $"project '{document.Project.Name}' and project '{firstDocument.Project.Name}' both compile the file but the move gives a different result in each of them");
            }
        }

        return firstResult;
    }

    /// <summary>
    /// Describes where <paramref name="placement" /> moves the using directives, for messages.
    /// </summary>
    private static string DescribeDirection(UsingDirectivePlacementPreference placement) =>
        placement == UsingDirectivePlacementPreference.InsideNamespace ? "inside the namespace" : "outside the namespace";

    private static void WriteNoPlacementEnforced(string methodName, string filePath) =>
        OutputWindowHelper.DiagnosticWriteLine(
            $"UsingDirectivePlacementLogic.{methodName} skipped for '{filePath}' because no using directive placement is enforced for it " +
            "(.editorconfig csharp_using_directive_placement, .codejanitor moveUsingsOutsideNamespace, or the Cleaning_MoveUsingsOutsideNamespace setting).");

    private static void WriteNotMovedWarning(string filePath, UsingDirectivePlacementPreference placement, string reason) =>
        OutputWindowHelper.WarningWriteLine(
            $"Using directives were not moved {DescribeDirection(placement)} in '{filePath}': {reason} They were left in place.");

    /// <summary>
    /// Runs the semantic move in the direction of <paramref name="placement" /> and reports its outcome, with the moved
    /// text when the outcome is <see cref="UsingsMoveOutcome.Moved" />. Cancellation is not reported as a failure to
    /// move: the <see cref="OperationCanceledException" /> propagates.
    /// </summary>
    private async Task<(UsingsMoveOutcome Outcome, string MovedText)> TryPlaceAsync(
        string filePath,
        string projectFilePath,
        string currentText,
        UsingDirectivePlacementPreference placement,
        CancellationToken cancellationToken)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var direction = DescribeDirection(placement);
        var hasUsingsToMove = placement == UsingDirectivePlacementPreference.InsideNamespace
            ? UsingDirectivePlacementConverter.HasUsingsOutsideNamespace(currentText)
            : UsingDirectivePlacementConverter.HasUsingsInsideNamespace(currentText);
        if (!hasUsingsToMove)
        {
            OutputWindowHelper.DiagnosticWriteLine(
                $"UsingDirectivePlacementLogic made no change for '{filePath}' (no using directives it can move {direction}).");

            return (UsingsMoveOutcome.NotApplicable, null);
        }

        UsingDirectivePlacementResult result;
        try
        {
            result = await PlaceInWorkspaceAsync(filePath, projectFilePath, currentText, placement, cancellationToken);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            WriteNotMovedWarning(
                filePath,
                placement,
                VisualStudioRoslynWorkspace.IsRoslynBindingFailure(ex)
                    ? "the Roslyn workspace API of this Visual Studio instance could not be used (CodeJanitor is compiled against Microsoft.CodeAnalysis 5.0; the host Roslyn may be older)."
                    : ex.Message);

            return (UsingsMoveOutcome.LeftInPlace, null);
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        switch (result.Status)
        {
            case UsingDirectivePlacementStatus.Moved:
                OutputWindowHelper.InfoWriteLine(
                    $"UsingDirectivePlacementLogic moved using directives {direction} for '{filePath}'.");

                return (UsingsMoveOutcome.Moved, result.Text);

            case UsingDirectivePlacementStatus.Skipped:
                OutputWindowHelper.WarningWriteLine(
                    $"Using directives were not moved {direction} in '{filePath}' because {result.Reason}. They were left in place.");

                return (UsingsMoveOutcome.LeftInPlace, null);

            default:
                return (UsingsMoveOutcome.NotApplicable, null);
        }
    }

    /// <summary>
    /// Resolves every C# document of the file in the Visual Studio workspace with <paramref name="currentText" /> and
    /// runs the converter on them off the UI thread, until <paramref name="cancellationToken" /> is canceled or the
    /// package is disposed.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<UsingDirectivePlacementResult> PlaceInWorkspaceAsync(
        string filePath,
        string projectFilePath,
        string currentText,
        UsingDirectivePlacementPreference placement,
        CancellationToken cancellationToken)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var solution = _workspace.GetWorkspace().CurrentSolution;

        using (var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(_package.DisposalToken, cancellationToken))
        {
            await TaskScheduler.Default;
            var documents = await VisualStudioRoslynWorkspace.GetDocumentsAsync(solution, filePath, projectFilePath, currentText, linkedSource.Token);

            return await PlaceInEveryFlavorAsync(_converter, placement, documents, linkedSource.Token);
        }
    }
}
