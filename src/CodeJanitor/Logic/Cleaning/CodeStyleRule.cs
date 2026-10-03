using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A Roslyn code-style option that Code Janitor can apply when .editorconfig does not enforce it. The option is
/// applied by the diagnostic cleanup through the existing Roslyn analyzers and code fixes of its diagnostic IDs.
/// </summary>
internal sealed class CodeStyleRule
{
    private readonly Func<string, bool> _isValidValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeStyleRule" /> class.
    /// </summary>
    /// <param name="group">The group shown in Options.</param>
    /// <param name="key">The .editorconfig option name.</param>
    /// <param name="description">The description shown in Options.</param>
    /// <param name="diagnosticIds">The IDs of the diagnostics reported for the option.</param>
    /// <param name="values">The accepted values, or empty when the value is free text.</param>
    /// <param name="defaultValue">The value proposed when the rule is enabled.</param>
    /// <param name="isValidValue">Validates a free-text value; null when <paramref name="values" /> lists every value.</param>
    internal CodeStyleRule(string group, string key, string description, string[] diagnosticIds, string[] values, string defaultValue, Func<string, bool> isValidValue = null)
    {
        Group = group;
        Key = key;
        Description = description;
        DiagnosticIds = diagnosticIds;
        Values = values;
        DefaultValue = defaultValue;
        _isValidValue = isValidValue ?? (value => values.Contains(value, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the group shown in Options.
    /// </summary>
    internal string Group { get; }

    /// <summary>
    /// Gets the .editorconfig option name.
    /// </summary>
    internal string Key { get; }

    /// <summary>
    /// Gets the description shown in Options.
    /// </summary>
    internal string Description { get; }

    /// <summary>
    /// Gets the IDs of the diagnostics reported for the option.
    /// </summary>
    internal IReadOnlyList<string> DiagnosticIds { get; }

    /// <summary>
    /// Gets the accepted values, or an empty list when the value is free text.
    /// </summary>
    internal IReadOnlyList<string> Values { get; }

    /// <summary>
    /// Gets the value proposed when the rule is enabled.
    /// </summary>
    internal string DefaultValue { get; }

    /// <summary>
    /// Determines whether the specified value is a valid value of the option.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>True when the value is valid.</returns>
    internal bool IsValidValue(string value) => value is not null && _isValidValue(value);

    /// <summary>
    /// Returns the canonical form of a value: the matching entry of <see cref="Values" /> ignoring case, or the
    /// trimmed value when the option is free text or no entry matches.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The canonical value.</returns>
    internal string Normalize(string value)
    {
        var trimmed = value?.Trim();

        return Values.FirstOrDefault(entry => string.Equals(entry, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }
}
