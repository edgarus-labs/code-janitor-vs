using CodeJanitor.Logic.Cleaning;
using System.Collections.Generic;
using System.ComponentModel;

namespace CodeJanitor.UI.Dialogs.Options.Cleaning;

/// <summary>
/// One Code Janitor code-style rule on the Code Style options page: its on/off switch, its value and, when the open
/// solution's .editorconfig enforces the rule, the note that locks it.
/// </summary>
public sealed class CodeStyleRuleOptionViewModel : Bindable, IDataErrorInfo
{
    private readonly CodeStyleRule _rule;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeStyleRuleOptionViewModel" /> class.
    /// </summary>
    /// <param name="rule">The rule.</param>
    internal CodeStyleRuleOptionViewModel(CodeStyleRule rule)
    {
        _rule = rule;
        Value = rule.DefaultValue;
    }

    /// <summary>
    /// Gets the .editorconfig option name.
    /// </summary>
    public string Key => _rule.Key;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public string Description => _rule.Description;

    /// <summary>
    /// Gets the accepted values, or an empty list when the value is free text.
    /// </summary>
    public IReadOnlyList<string> Values => _rule.Values;

    /// <summary>
    /// Gets a value indicating whether the value is free text rather than one of <see cref="Values" />.
    /// </summary>
    public bool IsFreeText => _rule.Values.Count == 0;

    /// <summary>
    /// Gets or sets a value indicating whether Code Janitor applies the rule.
    /// </summary>
    public bool IsEnabled
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the value Code Janitor applies.
    /// </summary>
    public string Value
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the note naming the .editorconfig option that enforces the rule, or null when it does not.
    /// </summary>
    public string EditorConfigOverride
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets a value indicating whether the value is valid, so the rule can be saved.
    /// </summary>
    internal bool HasValidValue => _rule.IsValidValue(Value?.Trim());

    /// <summary>
    /// Gets or sets a value indicating whether the settings hold a saved value of the rule.
    /// </summary>
    internal bool HasSavedValue { get; set; }

    /// <summary>
    /// Gets the value proposed when the rule is enabled.
    /// </summary>
    internal string DefaultValue => _rule.DefaultValue;

    /// <inheritdoc />
    string IDataErrorInfo.Error => null;

    /// <inheritdoc />
    string IDataErrorInfo.this[string columnName] =>
        columnName == nameof(Value) && !HasValidValue
            ? HasSavedValue
                ? $"'{Value}' is not a valid value of {Key}; the previously saved value is kept."
                : $"'{Value}' is not a valid value of {Key}; the rule will not be saved until its value is valid."
            : null;

    /// <summary>
    /// Returns the value in its canonical form.
    /// </summary>
    /// <returns>The canonical value.</returns>
    internal string Normalize() => _rule.Normalize(Value);
}
