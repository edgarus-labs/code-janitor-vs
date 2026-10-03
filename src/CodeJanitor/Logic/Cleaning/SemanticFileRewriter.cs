using CodeJanitor.Helpers;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Document = Microsoft.CodeAnalysis.Document;
using TextDocument = EnvDTE.TextDocument;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// Runs a cleanup step that rewrites a C# file with the semantic model of the Visual Studio Roslyn workspace, on an
/// open document (replacing the editor buffer) or a closed file (writing it back with its encoding).
/// </summary>
/// <remarks>
/// The step gets every document of the file (one per project flavor compiling it) with the current text of the
/// cleaned file. When the file cannot be analyzed it is left unchanged and the reason is written to the output pane as
/// a warning; a canceled analysis leaves it unchanged too.
/// </remarks>
internal sealed class SemanticFileRewriter
{
    private readonly CodeJanitorPackage _package;
    private readonly VisualStudioRoslynWorkspace _workspace;
    private readonly string _settingName;
    private readonly string _notRewrittenWarning;
    private readonly string _unchangedDiagnostic;
    private readonly string _rewrittenInfo;
    private readonly Func<string, string, string, CancellationToken, Task<string>> _rewriteInWorkspaceAsync;

    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticFileRewriter" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <param name="settingName">The name of the boolean cleanup setting that enables the step.</param>
    /// <param name="notRewrittenWarning">The start of the warning written when the file cannot be analyzed, e.g. "No class was sealed".</param>
    /// <param name="unchangedDiagnostic">The start of the diagnostic written when the step changed nothing.</param>
    /// <param name="rewrittenInfo">The start of the message written when the step changed the file.</param>
    /// <param name="rewriteInWorkspaceAsync">
    /// Rewrites the file (path, containing project path, current text) in the workspace, usually by calling
    /// <see cref="RewriteInWorkspaceAsync" />; returns the new text, or the current text when nothing changes. It must
    /// be a method that the JIT compiles separately (<c>MethodImplOptions.NoInlining</c>), so that a Roslyn assembly the
    /// host cannot bind is reported as a warning instead of failing the cleanup.
    /// </param>
    internal SemanticFileRewriter(
        CodeJanitorPackage package,
        string settingName,
        string notRewrittenWarning,
        string unchangedDiagnostic,
        string rewrittenInfo,
        Func<string, string, string, CancellationToken, Task<string>> rewriteInWorkspaceAsync)
    {
        _package = package;
        _workspace = new VisualStudioRoslynWorkspace(package);
        _settingName = settingName;
        _notRewrittenWarning = notRewrittenWarning;
        _unchangedDiagnostic = unchangedDiagnostic;
        _rewrittenInfo = rewrittenInfo;
        _rewriteInWorkspaceAsync = rewriteInWorkspaceAsync;
    }

    /// <summary>
    /// Rewrites an open document, when enabled in the effective settings, replacing the editor buffer (preserving
    /// markers) only when the step changed its text.
    /// </summary>
    /// <param name="textDocument">The text document to update.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>
    internal void Rewrite(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(_settingName))
        {
            return;
        }

        var filePath = textDocument.Parent?.FullName;
        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var originalText = startPoint.GetText(textDocument.EndPoint);
        var projectFilePath = VisualStudioRoslynWorkspace.GetContainingProjectPath(textDocument.Parent?.ProjectItem);

        var rewrittenText = ThreadHelper.JoinableTaskFactory.Run(() => TryRewriteAsync(filePath, projectFilePath, originalText, CancellationToken.None));
        if (rewrittenText is null)
        {
            return;
        }

        var endPoint = textDocument.EndPoint.CreateEditPoint();
        startPoint.ReplaceText(endPoint, rewrittenText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
        WriteRewrittenInfo(filePath);
    }

    /// <summary>
    /// Rewrites a closed C# file, when enabled in the effective settings, and writes the result back with the file's
    /// encoding: a byte order mark is kept when the file has one and not added when it has none.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <param name="cancellationToken">Cancels the analysis (together with the disposal of the package); the file is then left unchanged.</param>
    /// <returns>True when the file was rewritten.</returns>
    /// <exception cref="OperationCanceledException">The analysis was canceled; the file is left unchanged.</exception>
    internal async Task<bool> RewriteAsync(ProjectItem projectItem, CancellationToken cancellationToken)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var filePath = projectItem.GetFileName();
        if (string.IsNullOrEmpty(filePath) || !filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !File.Exists(filePath) ||
            !EffectiveCleanupSettings.For(filePath).GetBoolean(_settingName))
        {
            return false;
        }

        var projectFilePath = VisualStudioRoslynWorkspace.GetContainingProjectPath(projectItem);

        return await RewriteClosedFileAsync(
            filePath,
            projectFilePath,
            () => VsShellUtilities.IsDocumentOpen(_package, filePath, Guid.Empty, out _, out _, out _),
            cancellationToken);
    }

    /// <summary>
    /// Rewrites the closed file at <paramref name="filePath" /> and writes the result back with the file's encoding,
    /// unless it was opened or changed on disk while it was being analyzed. The file is written only after the whole
    /// analysis completed, so a canceled or failed analysis leaves it unchanged.
    /// </summary>
    /// <param name="filePath">The path of the file.</param>
    /// <param name="projectFilePath">The path of the project containing the file, or null.</param>
    /// <param name="isDocumentOpen">Tells, on the UI thread, whether the file is open in an editor.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when the file was rewritten.</returns>
    /// <exception cref="OperationCanceledException">The analysis was canceled; the file is left unchanged.</exception>
    internal async Task<bool> RewriteClosedFileAsync(string filePath, string projectFilePath, Func<bool> isDocumentOpen, CancellationToken cancellationToken)
    {
        string originalText;
        try
        {
            using (var reader = new StreamReader(filePath, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
            {
                originalText = await reader.ReadToEndAsync();
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            WriteNotRewrittenWarning(filePath, ex.Message);

            return false;
        }

        var rewrittenText = await TryRewriteAsync(filePath, projectFilePath, originalText, cancellationToken);
        if (rewrittenText is null)
        {
            return false;
        }

        // The analysis took a while: a file the user opened meanwhile is left alone, its editor buffer is the truth.
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (isDocumentOpen())
        {
            WriteNotRewrittenWarning(filePath, "the file was opened while it was being analyzed; it was left unchanged.");

            return false;
        }

        if (!TryWriteRewrittenText(filePath, originalText, rewrittenText, reason => WriteNotRewrittenWarning(filePath, reason)))
        {
            return false;
        }

        WriteRewrittenInfo(filePath);

        return true;
    }

    /// <summary>
    /// Writes the rewritten text of a closed file with the file's encoding, unless the file changed on disk since
    /// <paramref name="originalText" /> was read: an edit made during the analysis is never overwritten.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="originalText">The text the rewrite was computed from.</param>
    /// <param name="rewrittenText">The rewritten text.</param>
    /// <param name="reportNotWritten">Reports why the file was left unchanged.</param>
    /// <returns>True when the file was written.</returns>
    internal static bool TryWriteRewrittenText(string filePath, string originalText, string rewrittenText, Action<string> reportNotWritten)
    {
        ClosedFileWriteResult result;
        try
        {
            result = VisualStudioRoslynWorkspace.TryWriteClosedFileText(filePath, originalText, rewrittenText);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            reportNotWritten(ex.Message);

            return false;
        }

        switch (result)
        {
            case ClosedFileWriteResult.Written:
                return true;

            case ClosedFileWriteResult.ChangedOnDisk:
                reportNotWritten("the file changed while it was being analyzed; it was left unchanged.");

                return false;

            default:
                reportNotWritten("the file is read-only or cannot be written directly; it was left unchanged.");

                return false;
        }
    }

    /// <summary>
    /// Resolves every C# document of the file in the Visual Studio workspace with <paramref name="currentText" /> and
    /// runs <paramref name="rewriteAsync" /> on them off the UI thread, until <paramref name="cancellationToken" /> is
    /// canceled or the package is disposed.
    /// </summary>
    /// <param name="filePath">The path of the file.</param>
    /// <param name="projectFilePath">The path of the project containing the file, or null.</param>
    /// <param name="currentText">The current text of the file.</param>
    /// <param name="rewriteAsync">Rewrites the documents of the file; returns the new text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The text returned by <paramref name="rewriteAsync" />.</returns>
    internal async Task<string> RewriteInWorkspaceAsync(
        string filePath,
        string projectFilePath,
        string currentText,
        Func<IReadOnlyList<Document>, CancellationToken, Task<string>> rewriteAsync,
        CancellationToken cancellationToken)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var solution = _workspace.GetWorkspace().CurrentSolution;

        using (var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(_package.DisposalToken, cancellationToken))
        {
            await TaskScheduler.Default;
            var documents = await VisualStudioRoslynWorkspace.GetDocumentsAsync(solution, filePath, projectFilePath, currentText, linkedSource.Token);

            return await rewriteAsync(documents, linkedSource.Token);
        }
    }

    private void WriteNotRewrittenWarning(string filePath, string reason) =>
        OutputWindowHelper.WarningWriteLine($"{_notRewrittenWarning} in '{filePath}': {reason}");

    private void WriteRewrittenInfo(string filePath) =>
        OutputWindowHelper.InfoWriteLine($"{_rewrittenInfo} in '{filePath}'.");

    /// <summary>
    /// Runs the step on <paramref name="currentText" /> and returns the rewritten text, or null when the step changed
    /// nothing or the file could not be analyzed (reported as a warning). Cancellation is not reported as a failure:
    /// the <see cref="OperationCanceledException" /> propagates.
    /// </summary>
    private async Task<string> TryRewriteAsync(string filePath, string projectFilePath, string currentText, CancellationToken cancellationToken)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        string rewrittenText;
        try
        {
            rewrittenText = await _rewriteInWorkspaceAsync(filePath, projectFilePath, currentText, cancellationToken);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            WriteNotRewrittenWarning(
                filePath,
                VisualStudioRoslynWorkspace.IsRoslynBindingFailure(ex)
                    ? "the Roslyn workspace API of this Visual Studio instance could not be used (CodeJanitor is compiled against Microsoft.CodeAnalysis 5.0; the host Roslyn may be older)."
                    : ex.Message);

            return null;
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (string.Equals(rewrittenText, currentText, StringComparison.Ordinal))
        {
            OutputWindowHelper.DiagnosticWriteLine($"{_unchangedDiagnostic} in '{filePath}'.");

            return null;
        }

        return rewrittenText;
    }
}
