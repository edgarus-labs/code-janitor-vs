using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

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
    internal static SealedClassLogic GetInstance(CodeJanitorPackage package) => _instance ?? (_instance = new SealedClassLogic(package));

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
    internal void SealWhenSafe(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        _rewriter.Rewrite(textDocument, settings);
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
    internal Task<bool> SealWhenSafeAsync(ProjectItem projectItem, CancellationToken cancellationToken = default)
    {
        return _rewriter.RewriteAsync(projectItem, cancellationToken);
    }

    /// <summary>
    /// Resolves every C# document of the file in the Visual Studio workspace with <paramref name="currentText" /> and
    /// seals the safe classes of them, unless the solution is not completely loaded. Runs on the UI thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">The solution is still loading or has an unloaded project.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task<string> SealInWorkspaceAsync(string filePath, string projectFilePath, string currentText, CancellationToken cancellationToken)
    {
        var referencedOutside = GetProjectsReferencedOutsideCompleteWorkspace();
        _converter ??= new ClassSealingConverter();

        return _rewriter.RewriteInWorkspaceAsync(
            filePath,
            projectFilePath,
            currentText,
            (documents, token) => _converter.SealWhenSafeAsync(documents, referencedOutside, token),
            cancellationToken);
    }

    /// <summary>
    /// Gets the project files that projects outside the Roslyn workspace reference, after verifying that the workspace
    /// contains every project of the Visual Studio solution.
    /// </summary>
    /// <returns>The referenced project files (see <see cref="GetProjectsReferencedOutsideWorkspace" />).</returns>
    /// <exception cref="InvalidOperationException">The solution is still loading or has an unloaded project.</exception>
    private static IReadOnlyCollection<string> GetProjectsReferencedOutsideCompleteWorkspace()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var solution = Package.GetGlobalService(typeof(SVsSolution)) as IVsSolution;
        var incompleteSolutionReason = GetIncompleteSolutionReason(solution);
        if (incompleteSolutionReason is not null)
        {
            throw new InvalidOperationException(incompleteSolutionReason);
        }

        return GetProjectsReferencedOutsideWorkspace(solution);
    }

    /// <summary>
    /// Gets the full paths of the project files that the loaded projects outside the Roslyn workspace (projects that
    /// are neither C# nor Visual Basic, such as C++/CLI projects) reference.
    /// </summary>
    /// <param name="solution">The Visual Studio solution.</param>
    /// <returns>The referenced project files.</returns>
    /// <exception cref="InvalidOperationException">A project reference cannot be resolved to a file path.</exception>
    /// <exception cref="COMException">The loaded projects cannot be enumerated.</exception>
    internal static IReadOnlyCollection<string> GetProjectsReferencedOutsideWorkspace(IVsSolution solution)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anyProject = Guid.Empty;
        ErrorHandler.ThrowOnFailure(solution.GetProjectEnum((uint)__VSENUMPROJFLAGS.EPF_LOADEDINSOLUTION, ref anyProject, out var loadedProjects));

        var hierarchy = new IVsHierarchy[1];
        int result;
        while ((result = loadedProjects.Next(1, hierarchy, out var fetched)) == VSConstants.S_OK && fetched == 1)
        {
            if (ErrorHandler.Succeeded(hierarchy[0].GetCanonicalName((uint)VSConstants.VSITEMID.Root, out var projectFilePath)) &&
                IsProjectFileOutsideWorkspace(projectFilePath))
            {
                referenced.UnionWith(ReadProjectReferences(projectFilePath));
            }
        }

        ErrorHandler.ThrowOnFailure(result);

        return referenced;
    }

    /// <summary>
    /// Determines whether <paramref name="projectFilePath" /> is the file of a project that the Roslyn workspace does
    /// not contain: any project file other than a C# or Visual Basic one. Solution folders and other hierarchies
    /// without a project file are not projects.
    /// </summary>
    private static bool IsProjectFileOutsideWorkspace(string projectFilePath)
    {
        if (string.IsNullOrEmpty(projectFilePath) || !Path.IsPathRooted(projectFilePath) || !File.Exists(projectFilePath))
        {
            return false;
        }

        var extension = Path.GetExtension(projectFilePath);

        return !string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".vbproj", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the full paths of the projects that the project file references with <c>ProjectReference</c> items,
    /// whatever their conditions.
    /// </summary>
    /// <exception cref="InvalidOperationException">An item names its project with an MSBuild property or a wildcard.</exception>
    private static IEnumerable<string> ReadProjectReferences(string projectFilePath)
    {
        var directory = Path.GetDirectoryName(projectFilePath);

        foreach (var include in XDocument.Load(projectFilePath).Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value?.Trim())
            .Where(include => !string.IsNullOrEmpty(include)))
        {
            if (include.Contains("$(") || include.Contains("*") || include.Contains("?") || include.Contains(";"))
            {
                throw new InvalidOperationException(
                    $"the project reference '{include}' of '{projectFilePath}' cannot be resolved; classes that project uses could be sealed unseen.");
            }

            yield return Path.GetFullPath(Path.Combine(directory, include));
        }
    }

    /// <summary>
    /// Gets why the Visual Studio solution does not contain all its projects in the Roslyn workspace: it is still
    /// loading (background or deferred project loading), or a project is unloaded (by the user, or because it failed
    /// to load). Null when it does.
    /// </summary>
    /// <param name="solution">The Visual Studio solution, or null when the service is unavailable.</param>
    /// <returns>The reason, or null.</returns>
    internal static string GetIncompleteSolutionReason(IVsSolution solution)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (solution is null)
        {
            return "the Visual Studio solution service is unavailable, so it cannot be verified that all projects are loaded.";
        }

        if (ErrorHandler.Failed(solution.GetProperty((int)__VSPROPID4.VSPROPID_IsSolutionFullyLoaded, out var fullyLoaded)) ||
            !(fullyLoaded is bool loaded))
        {
            return "it cannot be verified that the solution is fully loaded; classes used by projects that are not loaded yet would be sealed unseen.";
        }

        if (!loaded)
        {
            return "the solution is still loading; classes used by projects that are not loaded yet would be sealed unseen.";
        }

        var anyProject = Guid.Empty;
        if (ErrorHandler.Failed(solution.GetProjectEnum((uint)__VSENUMPROJFLAGS.EPF_UNLOADEDINSOLUTION, ref anyProject, out var unloadedProjects)) ||
            unloadedProjects is null ||
            ErrorHandler.Failed(unloadedProjects.Next(1, new IVsHierarchy[1], out var fetched)))
        {
            return "it cannot be verified that all projects are loaded; classes used by a project that is not loaded would be sealed unseen.";
        }

        if (fetched > 0)
        {
            return "the solution has a project that is not loaded (unloaded or failed to load); classes it uses would be sealed unseen.";
        }

        return null;
    }
}
