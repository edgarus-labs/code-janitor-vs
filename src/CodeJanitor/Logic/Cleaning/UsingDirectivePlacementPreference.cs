namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// The using directive placement the cleanup enforces.
/// </summary>
internal enum UsingDirectivePlacementPreference
{
    /// <summary>
    /// Using directives are left where they are.
    /// </summary>
    Unchanged,

    /// <summary>
    /// Using directives are moved outside the namespace.
    /// </summary>
    OutsideNamespace,

    /// <summary>
    /// Using directives are moved inside the namespace.
    /// </summary>
    InsideNamespace,
}
