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
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A class for encapsulating the logic of adding the <c>sealed</c> modifier, during cleanup, to the classes that
/// <see cref="ClassSealingConverter" /> proves safe to seal.
/// </summary>
/// <remarks>
/// The analysis needs the whole solution (derived classes and generic constraints in other files and projects), which
/// comes from the Visual Studio Roslyn workspace, with the current text of the cleaned file. When the file cannot be
/// analyzed, nothing is sealed and the reason is written to the output pane as a warning.
/// </remarks>
internal sealed class SealedClassLogic
{
    private readonly CodeJanitorPackage _package;
    private readonly VisualStudioRoslynWorkspace _workspace;
    private readonly ClassSealingConverter _converter;

    /// <summary>
    /// The singleton instance of the <see cref="SealedClassLogic" /> class.
    /// </summary>
    private static SealedClassLogic _instance;

    /// <summary>
    /// Gets an instance of the <see cref="SealedClassLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>An instance of the <see cref="SealedClassLogic" /> class.</returns>
    internal static SealedClassLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new SealedClassLogic(package));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SealedClassLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    private SealedClassLogic(CodeJanitorPackage package)
    {
        _package = package;
        _workspace = new VisualStudioRoslynWorkspace(package);
        _converter = new ClassSealingConverter();
    }

    /// <summary>
    /// Seals the classes of an open document that are safe to seal, when enabled in the effective settings, replacing
    /// the editor buffer (preserving markers) only when a class was sealed.
    /// </summary>
    /// <param name="textDocument">The text document to update.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>
    internal void SealWhenSafe(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_SealClassesWhenSafe)))
        {
            return;
        }

        var filePath = textDocument.Parent?.FullName;
        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var originalText = startPoint.GetText(textDocument.EndPoint);
        var projectFilePath = VisualStudioRoslynWorkspace.GetContainingProjectPath(textDocument.Parent?.ProjectItem);

        var sealedText = ThreadHelper.JoinableTaskFactory.Run(() => TrySealAsync(filePath, projectFilePath, originalText, CancellationToken.None));
        if (sealedText is null)
        {
            return;
        }

        var endPoint = textDocument.EndPoint.CreateEditPoint();
        startPoint.ReplaceText(endPoint, sealedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
    }

    /// <summary>
    /// Seals the classes of a closed C# file that are safe to seal, when enabled in the effective settings, and writes
    /// the result back with the file's encoding: a byte order mark is kept when the file has one and not added when it
    /// has none.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <param name="cancellationToken">Cancels the analysis (together with the disposal of the package); the file is then left unchanged.</param>
    /// <returns>True when the file was rewritten.</returns>
    /// <exception cref="OperationCanceledException">The analysis was canceled; the file is left unchanged.</exception>
    internal async Task<bool> SealWhenSafeAsync(ProjectItem projectItem, CancellationToken cancellationToken = default)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var filePath = projectItem.GetFileName();
        if (string.IsNullOrEmpty(filePath) || !filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !File.Exists(filePath) ||
            !EffectiveCleanupSettings.For(filePath).GetBoolean(nameof(Settings.Cleaning_SealClassesWhenSafe)))
        {
            return false;
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
            WriteNotSealedWarning(filePath, ex.Message);

            return false;
        }

        // The file is written only after the whole analysis completed, so a canceled analysis leaves it unchanged.
        var sealedText = await TrySealAsync(filePath, projectFilePath, originalText, cancellationToken);
        if (sealedText is null)
        {
            return false;
        }

        try
        {
            File.WriteAllText(filePath, sealedText, encoding);

            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            WriteNotSealedWarning(filePath, ex.Message);

            return false;
        }
    }

    private static void WriteNotSealedWarning(string filePath, string reason) =>
        OutputWindowHelper.WarningWriteLine($"No class was sealed in '{filePath}': {reason}");

    /// <summary>
    /// Runs the analysis on <paramref name="currentText" /> and returns the sealed text, or null when no class was
    /// sealed or the file could not be analyzed (reported as a warning). Cancellation is not reported as a failure: the
    /// <see cref="OperationCanceledException" /> propagates.
    /// </summary>
    private async Task<string> TrySealAsync(string filePath, string projectFilePath, string currentText, CancellationToken cancellationToken)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        string sealedText;
        try
        {
            sealedText = await SealInWorkspaceAsync(filePath, projectFilePath, currentText, cancellationToken);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            WriteNotSealedWarning(
                filePath,
                VisualStudioRoslynWorkspace.IsRoslynBindingFailure(ex)
                    ? "the Roslyn workspace API of this Visual Studio instance could not be used (CodeJanitor is compiled against Microsoft.CodeAnalysis 5.0; the host Roslyn may be older)."
                    : ex.Message);

            return null;
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (string.Equals(sealedText, currentText, StringComparison.Ordinal))
        {
            OutputWindowHelper.DiagnosticWriteLine($"SealedClassLogic sealed no class in '{filePath}'.");

            return null;
        }

        OutputWindowHelper.InfoWriteLine($"SealedClassLogic sealed classes in '{filePath}'.");

        return sealedText;
    }

    /// <summary>
    /// Resolves every C# document of the file in the Visual Studio workspace with <paramref name="currentText" /> and
    /// runs the converter on them off the UI thread, until <paramref name="cancellationToken" /> is canceled or the
    /// package is disposed.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<string> SealInWorkspaceAsync(string filePath, string projectFilePath, string currentText, CancellationToken cancellationToken)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var solution = _workspace.GetWorkspace().CurrentSolution;

        using (var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(_package.DisposalToken, cancellationToken))
        {
            await TaskScheduler.Default;
            var documents = await VisualStudioRoslynWorkspace.GetDocumentsAsync(solution, filePath, projectFilePath, currentText, linkedSource.Token);

            return await _converter.SealWhenSafeAsync(documents, linkedSource.Token);
        }
    }
}
