using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.CompilerServices;
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
/// analyzed, nothing is sealed and the reason is written to the output pane as a warning. So is the case when the
/// solution is still loading or has a project that is not loaded: the workspace does not contain such a project, and
/// the classes it derives from, constrains or converts would be sealed unseen.
/// The converter (whose fields bind the Roslyn workspace assemblies) is created only in <see cref="SealInWorkspaceAsync" />,
/// so that creating this singleton, from the constructor of the cleanup manager, never fails on a host whose Roslyn
/// cannot be bound.
/// </remarks>
internal sealed class SealedClassLogic
{
    private readonly SemanticFileRewriter _rewriter;
    private ClassSealingConverter _converter;

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
        _rewriter = new SemanticFileRewriter(
            package,
            nameof(Settings.Cleaning_SealClassesWhenSafe),
            "No class was sealed",
            "SealedClassLogic sealed no class",
            "SealedClassLogic sealed classes",
            SealInWorkspaceAsync);
    }

    /// <summary>
    /// Seals the classes of an open document that are safe to seal, when enabled in the effective settings, replacing
    /// the editor buffer (preserving markers) only when a class was sealed.
    /// </summary>
    /// <param name="textDocument">The text document to update.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>
    internal void SealWhenSafe(TextDocument textDocument, EffectiveCleanupSettings settings) =>
        _rewriter.Rewrite(textDocument, settings);

    /// <summary>
    /// Seals the classes of a closed C# file that are safe to seal, when enabled in the effective settings, and writes
    /// the result back with the file's encoding: a byte order mark is kept when the file has one and not added when it
    /// has none.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <param name="cancellationToken">Cancels the analysis (together with the disposal of the package); the file is then left unchanged.</param>
    /// <returns>True when the file was rewritten.</returns>
    /// <exception cref="OperationCanceledException">The analysis was canceled; the file is left unchanged.</exception>
    internal Task<bool> SealWhenSafeAsync(ProjectItem projectItem, CancellationToken cancellationToken = default) =>
        _rewriter.RewriteAsync(projectItem, cancellationToken);

    /// <summary>
    /// Resolves every C# document of the file in the Visual Studio workspace with <paramref name="currentText" /> and
    /// seals the safe classes of them, unless the solution is not completely loaded. Runs on the UI thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">The solution is still loading or has an unloaded project.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task<string> SealInWorkspaceAsync(string filePath, string projectFilePath, string currentText, CancellationToken cancellationToken)
    {
        var incompleteSolutionReason = GetIncompleteSolutionReason();
        if (incompleteSolutionReason != null)
        {
            throw new InvalidOperationException(incompleteSolutionReason);
        }

        _converter ??= new ClassSealingConverter();

        return _rewriter.RewriteInWorkspaceAsync(filePath, projectFilePath, currentText, _converter.SealWhenSafeAsync, cancellationToken);
    }

    /// <summary>
    /// Gets why the Visual Studio solution does not contain all its projects in the Roslyn workspace: it is still
    /// loading (background or deferred project loading), or a project is unloaded (by the user, or because it failed
    /// to load). Null when it does.
    /// </summary>
    private static string GetIncompleteSolutionReason()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!(Package.GetGlobalService(typeof(SVsSolution)) is IVsSolution solution))
        {
            return "the Visual Studio solution service is unavailable, so it cannot be verified that all projects are loaded.";
        }

        if (ErrorHandler.Succeeded(solution.GetProperty((int)__VSPROPID4.VSPROPID_IsSolutionFullyLoaded, out var fullyLoaded)) &&
            fullyLoaded is bool loaded && !loaded)
        {
            return "the solution is still loading; classes used by projects that are not loaded yet would be sealed unseen.";
        }

        var anyProject = Guid.Empty;
        if (ErrorHandler.Succeeded(solution.GetProjectEnum((uint)__VSENUMPROJFLAGS.EPF_UNLOADEDINSOLUTION, ref anyProject, out var unloadedProjects)) &&
            unloadedProjects != null &&
            ErrorHandler.Succeeded(unloadedProjects.Next(1, new IVsHierarchy[1], out var fetched)) &&
            fetched > 0)
        {
            return "the solution has a project that is not loaded (unloaded or failed to load); classes it uses would be sealed unseen.";
        }

        return null;
    }
}
