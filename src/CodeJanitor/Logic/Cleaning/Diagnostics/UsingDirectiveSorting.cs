namespace CodeJanitor.Logic.Cleaning.Diagnostics;

/// <summary>
/// How the diagnostic cleanup sorts the using directives of the cleaned document after a fix changed it, matching the
/// cleanup step that sorts them before the fixes.
/// </summary>
public enum UsingDirectiveSorting
{
    /// <summary>
    /// The using directives are left in the order the fixes wrote them.
    /// </summary>
    None,

    /// <summary>
    /// Sorted as the Visual Studio "Remove and Sort Usings" command sorts them (Roslyn's organize imports, with the
    /// project's options).
    /// </summary>
    RemoveAndSortUsings,

    /// <summary>
    /// Sorted by Code Janitor's using directive organizer (the <c>organizeUsings</c> policy).
    /// </summary>
    OrganizeUsings,
}
