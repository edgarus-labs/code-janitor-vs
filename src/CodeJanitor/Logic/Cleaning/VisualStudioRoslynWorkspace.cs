using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// Gives cleanup steps that need the Roslyn semantic model access to the C# documents of the Visual Studio
/// Roslyn workspace (<c>VisualStudioWorkspace</c>).
/// </summary>
/// <remarks>
/// Roslyn workspace types are only touched from methods marked <see cref="MethodImplOptions.NoInlining" />; callers
/// invoke them inside a try/catch, so a host whose Roslyn cannot satisfy the compile-time Microsoft.CodeAnalysis 5.9
/// reference produces an explicit, logged failure (see <see cref="IsRoslynBindingFailure" />).
/// </remarks>
internal sealed class VisualStudioRoslynWorkspace
{
    /// <summary>
    /// The MEF contract name of Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace. The
    /// type is resolved at runtime because no Microsoft.VisualStudio.LanguageServices package
    /// compatible with Microsoft.CodeAnalysis 5.9 is published.
    /// </summary>
    private const string VisualStudioWorkspaceTypeName = "Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace";

    private const string VisualStudioWorkspaceAssemblyName = "Microsoft.VisualStudio.LanguageServices";

    private readonly CodeJanitorPackage _package;

    /// <summary>
    /// Initializes a new instance of the <see cref="VisualStudioRoslynWorkspace" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    internal VisualStudioRoslynWorkspace(CodeJanitorPackage package)
    {
        _package = package;
    }

    /// <summary>
    /// Gets the Visual Studio Roslyn workspace through MEF, verifying that it shares the
    /// Microsoft.CodeAnalysis.Workspaces assembly this extension is bound to.
    /// </summary>
    /// <returns>The Visual Studio workspace.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal Workspace GetWorkspace()
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
            $"The Visual Studio Roslyn workspace uses {DescribeWorkspaceAssembly(workspace.GetType())}, which is not the Microsoft.CodeAnalysis.Workspaces {typeof(Workspace).Assembly.GetName().Version} this extension is bound to. Features that use the Roslyn workspace require a Visual Studio version whose Roslyn is 5.9 or newer.");
    }

    /// <summary>
    /// Resolves the C# document for the file in the given solution, with its text replaced by
    /// <paramref name="currentText" /> when the workspace has not yet observed it (e.g. right after the
    /// headless cleanup wrote the file, or while earlier cleanup steps edited the editor buffer).
    /// </summary>
    /// <param name="solution">The current workspace solution.</param>
    /// <param name="filePath">The file path.</param>
    /// <param name="projectFilePath">The file path of the project containing the item, if known.</param>
    /// <param name="currentText">The current text of the file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static async Task<Microsoft.CodeAnalysis.Document> GetDocumentAsync(
        Solution solution,
        string filePath,
        string projectFilePath,
        string currentText,
        CancellationToken cancellationToken)
    {
        var documentId = FindDocumentId(solution, filePath, projectFilePath)
            ?? throw new InvalidOperationException(
                $"'{filePath}' is not part of any C# project loaded in the Visual Studio Roslyn workspace (for example it is excluded from compilation), so it cannot be analyzed semantically.");

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
    /// Determines whether an exception indicates that the Roslyn assemblies this extension is compiled
    /// against could not be bound to the host's Roslyn (missing/older assemblies, mismatched types).
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>True for binding failures, otherwise false.</returns>
    internal static bool IsRoslynBindingFailure(Exception exception)
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
    /// Gets the file path of the project containing the project item, when available.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>The project file path, otherwise null.</returns>
    internal static string GetContainingProjectPath(EnvDTE.ProjectItem projectItem)
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
    internal static string ReadFileText(string filePath)
    {
        using (var reader = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true))
        {
            return reader.ReadToEnd();
        }
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
}
