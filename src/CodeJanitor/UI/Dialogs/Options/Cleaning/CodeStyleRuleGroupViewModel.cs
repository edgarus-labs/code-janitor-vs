using System.Collections.Generic;

namespace CodeJanitor.UI.Dialogs.Options.Cleaning;

/// <summary>
/// A group of Code Janitor code-style rules on the Code Style options page.
/// </summary>
public sealed class CodeStyleRuleGroupViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CodeStyleRuleGroupViewModel" /> class.
    /// </summary>
    /// <param name="header">The group header.</param>
    /// <param name="rules">The rules of the group.</param>
    internal CodeStyleRuleGroupViewModel(string header, IReadOnlyList<CodeStyleRuleOptionViewModel> rules)
    {
        Header = header;
        Rules = rules;
    }

    /// <summary>
    /// Gets the group header.
    /// </summary>
    public string Header { get; }

    /// <summary>
    /// Gets the rules of the group.
    /// </summary>
    public IReadOnlyList<CodeStyleRuleOptionViewModel> Rules { get; }
}
