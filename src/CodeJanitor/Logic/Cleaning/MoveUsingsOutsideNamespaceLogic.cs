using CodeJanitor.Helpers;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A class for encapsulating the logic of moving using directives outside namespace declarations during cleanup.
/// </summary>
/// <remarks>
/// The move needs the semantic model of the document (see <see cref="MoveUsingsOutsideNamespaceConverter" />), which
/// comes from the Visual Studio Roslyn workspace. When the move is not safe or the document cannot be analyzed, the
/// document is left unchanged and the reason is written to the output pane.
/// </remarks>
internal sealed class MoveUsingsOutsideNamespaceLogic
{
    private const string SettingName = nameof(Settings.Cleaning_MoveUsingsOutsideNamespace);

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
    /// Moves the using directives of an open document outside its namespaces, when enabled, replacing the editor
    /// buffer (preserving markers) only when the move succeeded.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    internal void MoveUsingsOutsideNamespace(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var filePath = textDocument.Parent?.FullName;
        if (!Settings.Default.Cleaning_MoveUsingsOutsideNamespace)
        {
            OutputWindowHelper.InfoWriteLine(
                $"MoveUsingsOutsideNamespaceLogic.MoveUsingsOutsideNamespace skipped for '{filePath}' because {SettingName} is false.");

            return;
        }

        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var originalText = startPoint.GetText(textDocument.EndPoint);
        var projectFilePath = VisualStudioRoslynWorkspace.GetContainingProjectPath(textDocument.Parent?.ProjectItem);

        var movedText = ThreadHelper.JoinableTaskFactory.Run(() => TryMoveAsync(filePath, projectFilePath, originalText));
        if (movedText is null)
        {
            return;
        }

        var endPoint = textDocument.EndPoint.CreateEditPoint();
        startPoint.ReplaceText(endPoint, movedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
    }

    /// <summary>
    /// Moves the using directives of a closed C# file (typically just written by the headless cleanup) outside its
    /// namespaces, when enabled for the file, and writes the result back with the file's encoding.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>A task.</returns>
    internal async Task MoveUsingsOutsideNamespaceAsync(ProjectItem projectItem)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var filePath = projectItem.GetFileName();
        if (string.IsNullOrEmpty(filePath) || !filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !File.Exists(filePath))
        {
            return;
        }

        if (!RepositoryCleanupSettings.LoadForFile(filePath).TryGetBoolean(SettingName, Settings.Default.Cleaning_MoveUsingsOutsideNamespace))
        {
            OutputWindowHelper.InfoWriteLine(
                $"MoveUsingsOutsideNamespaceLogic.MoveUsingsOutsideNamespaceAsync skipped for '{filePath}' because {SettingName} is false.");

            return;
        }

        var projectFilePath = VisualStudioRoslynWorkspace.GetContainingProjectPath(projectItem);
        string originalText;
        Encoding encoding;

        // Without a byte order mark the file is written back without one.
        using (var reader = new StreamReader(filePath, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
        {
            originalText = await reader.ReadToEndAsync();
            encoding = reader.CurrentEncoding;
        }

        var movedText = await TryMoveAsync(filePath, projectFilePath, originalText);
        if (movedText is null)
        {
            return;
        }

        File.WriteAllText(filePath, movedText, encoding);
    }

    /// <summary>
    /// Runs the semantic move and reports its outcome; returns the moved text, or null when the document must stay
    /// unchanged.
    /// </summary>
    private async Task<string> TryMoveAsync(string filePath, string projectFilePath, string currentText)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (!MoveUsingsOutsideNamespaceConverter.HasUsingsInsideNamespace(currentText))
        {
            OutputWindowHelper.InfoWriteLine(
                $"MoveUsingsOutsideNamespaceLogic made no change for '{filePath}' (no using directives inside a namespace).");

            return null;
        }

        MoveUsingsOutsideNamespaceResult result;
        try
        {
            result = await MoveInWorkspaceAsync(filePath, projectFilePath, currentText);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var reason = VisualStudioRoslynWorkspace.IsRoslynBindingFailure(ex)
                ? "the Roslyn workspace API of this Visual Studio instance could not be used (CodeJanitor is compiled against Microsoft.CodeAnalysis 5.9; the host Roslyn may be older)"
                : ex.Message;
            OutputWindowHelper.WarningWriteLine(
                $"Using directives were not moved outside the namespace in '{filePath}': {reason} The file was left unchanged.");

            return null;
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        switch (result.Status)
        {
            case MoveUsingsOutsideNamespaceStatus.Moved:
                OutputWindowHelper.InfoWriteLine(
                    $"MoveUsingsOutsideNamespaceLogic moved using directives outside namespace for '{filePath}'.");

                return result.Text;

            case MoveUsingsOutsideNamespaceStatus.Skipped:
                OutputWindowHelper.WarningWriteLine(
                    $"Using directives were not moved outside the namespace in '{filePath}' because {result.Reason}. The file was left unchanged.");

                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Resolves the document in the Visual Studio workspace with <paramref name="currentText" /> and runs the
    /// converter off the UI thread.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<MoveUsingsOutsideNamespaceResult> MoveInWorkspaceAsync(string filePath, string projectFilePath, string currentText)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var solution = _workspace.GetWorkspace().CurrentSolution;
        var cancellationToken = _package.DisposalToken;

        await TaskScheduler.Default;
        var document = await VisualStudioRoslynWorkspace.GetDocumentAsync(solution, filePath, projectFilePath, currentText, cancellationToken);

        return await _converter.MoveUsingsOutsideAsync(document, cancellationToken);
    }
}
