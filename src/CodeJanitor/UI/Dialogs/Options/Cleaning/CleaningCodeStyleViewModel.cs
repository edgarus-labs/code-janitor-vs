using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeJanitor.UI.Dialogs.Options.Cleaning;

/// <summary>
/// The view model for the Code Style cleaning options: the Roslyn code-style rules Code Janitor applies through the
/// diagnostic cleanup when .editorconfig does not enforce them (<see cref="CodeStyleRules" />).
/// </summary>
public sealed class CleaningCodeStyleViewModel : OptionsPageViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CleaningCodeStyleViewModel" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <param name="activeSettings">The active settings.</param>
    public CleaningCodeStyleViewModel(CodeJanitorPackage package, Settings activeSettings)
        : base(package, activeSettings)
    {
        Groups = CodeStyleRules.All
            .GroupBy(rule => rule.Group)
            .Select(group => new CodeStyleRuleGroupViewModel(group.Key, group.Select(rule => new CodeStyleRuleOptionViewModel(rule)).ToList()))
            .ToList();
    }

    /// <summary>
    /// Gets the header.
    /// </summary>
    public override string Header => "Code Style";

    /// <summary>
    /// Gets the rules, grouped as in <see cref="CodeStyleRules.All" />.
    /// </summary>
    public IReadOnlyList<CodeStyleRuleGroupViewModel> Groups { get; }

    private IEnumerable<CodeStyleRuleOptionViewModel> Rules => Groups.SelectMany(group => group.Rules);

    /// <summary>
    /// Loads the enabled rules and their values, and the notes of the rules the open solution's .editorconfig enforces.
    /// </summary>
    public override void LoadSettings()
    {
        base.LoadSettings();

        var enabled = CodeStyleRules.ParseSetting(ActiveSettings.Cleaning_CodeStyleRules);
        foreach (var rule in Rules)
        {
            rule.IsEnabled = enabled.TryGetValue(rule.Key, out var value);
            rule.Value = rule.IsEnabled ? value : rule.DefaultValue;
            rule.HasSavedValue = rule.IsEnabled;
            rule.EditorConfigOverride = EditorConfigOverrides[rule.Key];
        }
    }

    /// <summary>
    /// Saves the enabled rules and their values. An enabled rule whose value is invalid keeps its saved value.
    /// </summary>
    public override void SaveSettings()
    {
        base.SaveSettings();

        var saved = CodeStyleRules.ParseSetting(ActiveSettings.Cleaning_CodeStyleRules);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rule in Rules.Where(rule => rule.IsEnabled))
        {
            if (rule.HasValidValue)
            {
                values[rule.Key] = rule.Normalize();
            }
            else if (saved.TryGetValue(rule.Key, out var savedValue))
            {
                values[rule.Key] = savedValue;
            }
        }

        ActiveSettings.Cleaning_CodeStyleRules = CodeStyleRules.FormatSetting(values);
        foreach (var rule in Rules)
        {
            rule.HasSavedValue = values.ContainsKey(rule.Key);
        }
    }
}
