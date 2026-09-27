using System;
using System.Collections.Generic;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// Represents cleanup overrides loaded from a repository-level policy file (.codejanitor), mapping
/// the shared CodeJanitor VS Code configuration schema onto Visual Studio setting property names.
/// </summary>
internal sealed class RepositoryCleanupOverrides
{
    /// <summary>
    /// An empty overrides instance used when no repository policy file is present.
    /// </summary>
    internal static readonly RepositoryCleanupOverrides Empty = new RepositoryCleanupOverrides(
        new Dictionary<string, object>(StringComparer.Ordinal), removeRegions: null, organizeUsings: null);

    private readonly IReadOnlyDictionary<string, object> _values;

    /// <summary>
    /// Initializes a new instance of the <see cref="RepositoryCleanupOverrides" /> class.
    /// </summary>
    /// <param name="values">The overridden setting values keyed by Visual Studio setting property name.</param>
    /// <param name="removeRegions">The repository policy for region removal, if specified.</param>
    /// <param name="organizeUsings">The repository policy for forcing using directive organization, if specified.</param>
    internal RepositoryCleanupOverrides(IReadOnlyDictionary<string, object> values, bool? removeRegions, bool? organizeUsings)
    {
        _values = values;
        RemoveRegions = removeRegions;
        OrganizeUsings = organizeUsings;
    }

    /// <summary>
    /// Gets the overridden setting values keyed by Visual Studio setting property name.
    /// </summary>
    internal IReadOnlyDictionary<string, object> Values => _values;

    /// <summary>
    /// Gets the number of overridden settings.
    /// </summary>
    internal int Count => _values.Count;

    /// <summary>
    /// Gets the repository policy for region removal, if specified. Visual Studio has no user setting
    /// for this; the policy only takes effect when the repository file defines it.
    /// </summary>
    internal bool? RemoveRegions { get; }

    /// <summary>
    /// Gets a value indicating whether the headless cleanup removes region directives: always, unless the
    /// repository policy opts out with <c>removeRegions: false</c>.
    /// </summary>
    internal bool RemovesRegions => RemoveRegions ?? true;

    /// <summary>
    /// Gets the repository policy for forcing using directive organization independent of .editorconfig,
    /// if specified.
    /// </summary>
    internal bool? OrganizeUsings { get; }

    /// <summary>
    /// Gets an overridden boolean setting value, falling back when the repository policy does not define it.
    /// </summary>
    /// <param name="settingName">The Visual Studio setting property name.</param>
    /// <param name="fallback">The fallback value.</param>
    /// <returns>The effective value.</returns>
    internal bool TryGetBoolean(string settingName, bool fallback)
    {
        return _values.TryGetValue(settingName, out var value) && value is bool boolean ? boolean : fallback;
    }

    /// <summary>
    /// Gets an overridden string setting value, falling back when the repository policy does not define it.
    /// </summary>
    /// <param name="settingName">The Visual Studio setting property name.</param>
    /// <param name="fallback">The fallback value.</param>
    /// <returns>The effective value.</returns>
    internal string TryGetString(string settingName, string fallback)
    {
        return _values.TryGetValue(settingName, out var value) && value is string text ? text : fallback;
    }

    /// <summary>
    /// Gets an overridden integer setting value, falling back when the repository policy does not define it.
    /// </summary>
    /// <param name="settingName">The Visual Studio setting property name.</param>
    /// <param name="fallback">The fallback value.</param>
    /// <returns>The effective value.</returns>
    internal int TryGetInt32(string settingName, int fallback)
    {
        return _values.TryGetValue(settingName, out var value) && value is int number ? number : fallback;
    }
}
