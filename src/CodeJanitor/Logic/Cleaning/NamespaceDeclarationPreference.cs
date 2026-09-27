
namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// The namespace declaration style the cleanup enforces.
/// </summary>
internal enum NamespaceDeclarationPreference
{
    /// <summary>
    /// Namespace declarations are left as they are.
    /// </summary>
    Unchanged,

    /// <summary>
    /// Block-scoped namespaces are converted to file-scoped namespaces.
    /// </summary>
    FileScoped,

    /// <summary>
    /// File-scoped namespaces are converted to block-scoped namespaces.
    /// </summary>
    BlockScoped,
}
