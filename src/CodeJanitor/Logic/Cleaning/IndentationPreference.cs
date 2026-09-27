namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// The leading indentation style the cleanup enforces.
/// </summary>
internal enum IndentationPreference
{
    /// <summary>
    /// Indentation is left as it is.
    /// </summary>
    Unchanged,

    /// <summary>
    /// Tab indentation is converted to spaces.
    /// </summary>
    Spaces,

    /// <summary>
    /// Space indentation is converted to tabs.
    /// </summary>
    Tabs,
}
