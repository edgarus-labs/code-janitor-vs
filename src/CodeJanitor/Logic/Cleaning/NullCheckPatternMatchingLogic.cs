using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;
using EnvDTE;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A class for encapsulating the logic of converting, during cleanup, the null checks (<c>== null</c>,
/// <c>!= null</c>) that <see cref="NullCheckPatternMatchingConverter" /> proves safe to pattern matching
/// (<c>is null</c>, <c>is not null</c>).
/// </summary>
/// <remarks>
/// The analysis needs the semantic model of every project compiling the file (a user-defined <c>==</c> or <c>!=</c>
/// may be declared in another file, project or referenced assembly), which comes from the Visual Studio Roslyn
/// workspace, with the current text of the cleaned file. When the file cannot be analyzed, nothing is converted and the
/// reason is written to the output pane as a warning.
/// </remarks>
internal sealed class NullCheckPatternMatchingLogic
{
    private readonly SemanticFileRewriter _rewriter;
    private readonly NullCheckPatternMatchingConverter _converter;

    /// <summary>
    /// The singleton instance of the <see cref="NullCheckPatternMatchingLogic" /> class.
    /// </summary>
    private static NullCheckPatternMatchingLogic _instance;

    /// <summary>
    /// Gets an instance of the <see cref="NullCheckPatternMatchingLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>An instance of the <see cref="NullCheckPatternMatchingLogic" /> class.</returns>
    internal static NullCheckPatternMatchingLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new NullCheckPatternMatchingLogic(package));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NullCheckPatternMatchingLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    private NullCheckPatternMatchingLogic(CodeJanitorPackage package)
    {
        _converter = new NullCheckPatternMatchingConverter();
        _rewriter = new SemanticFileRewriter(
            package,
            nameof(Settings.Cleaning_ConvertToPatternMatchingNullChecks),
            "No null check was converted to pattern matching",
            "NullCheckPatternMatchingLogic converted no null check",
            "NullCheckPatternMatchingLogic converted null checks to pattern matching",
            ConvertInWorkspaceAsync);
    }

    /// <summary>
    /// Converts the null checks of an open document that are safe to convert, when enabled in the effective settings,
    /// replacing the editor buffer (preserving markers) only when a null check was converted.
    /// </summary>
    /// <param name="textDocument">The text document to update.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>
    internal void ConvertWhenSafe(TextDocument textDocument, EffectiveCleanupSettings settings) =>
        _rewriter.Rewrite(textDocument, settings);

    /// <summary>
    /// Converts the null checks of a closed C# file that are safe to convert, when enabled in the effective settings,
    /// and writes the result back with the file's encoding: a byte order mark is kept when the file has one and not
    /// added when it has none.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <param name="cancellationToken">Cancels the analysis (together with the disposal of the package); the file is then left unchanged.</param>
    /// <returns>True when the file was rewritten.</returns>
    /// <exception cref="OperationCanceledException">The analysis was canceled; the file is left unchanged.</exception>
    internal Task<bool> ConvertWhenSafeAsync(ProjectItem projectItem, CancellationToken cancellationToken = default) =>
        _rewriter.RewriteAsync(projectItem, cancellationToken);

    /// <summary>
    /// Resolves every C# document of the file in the Visual Studio workspace with <paramref name="currentText" /> and
    /// converts the null checks that are safe in all of them.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task<string> ConvertInWorkspaceAsync(string filePath, string projectFilePath, string currentText, CancellationToken cancellationToken) =>
        _rewriter.RewriteInWorkspaceAsync(filePath, projectFilePath, currentText, _converter.ConvertAsync, cancellationToken);
}
