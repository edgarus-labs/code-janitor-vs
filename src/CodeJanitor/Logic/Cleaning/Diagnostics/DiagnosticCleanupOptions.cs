using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeJanitor.Logic.Cleaning.Diagnostics;

/// <summary>
/// Options of a single <see cref="DiagnosticCleanupEngine" /> run.
/// </summary>
public sealed class DiagnosticCleanupOptions
{
    private const int DefaultMaxPasses = 50;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticCleanupOptions" /> class.
    /// </summary>
    /// <param name="enabledCategories">The diagnostic families whose diagnostics may be fixed.</param>
    /// <param name="maxPasses">The maximum number of fixes applied (each followed by a re-analysis).</param>
    /// <param name="analyzerConfigOverrides">
    /// .editorconfig entries (option name → value) that apply to the cleaned document on top of its analyzer
    /// configuration, or null for none.
    /// </param>
    public DiagnosticCleanupOptions(
        IEnumerable<DiagnosticCleanupCategory> enabledCategories,
        int maxPasses = DefaultMaxPasses,
        IReadOnlyDictionary<string, string> analyzerConfigOverrides = null)
    {
        if (enabledCategories is null)
        {
            throw new ArgumentNullException(nameof(enabledCategories));
        }

        if (maxPasses < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPasses), maxPasses, "At least one pass is required.");
        }

        EnabledCategories = enabledCategories.Distinct().OrderBy(category => category).ToList().AsReadOnly();
        MaxPasses = maxPasses;
        AnalyzerConfigOverrides = (analyzerConfigOverrides ?? new Dictionary<string, string>())
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the diagnostic families whose diagnostics may be fixed, without duplicates.
    /// </summary>
    public IReadOnlyCollection<DiagnosticCleanupCategory> EnabledCategories { get; }

    /// <summary>
    /// Gets the maximum number of fixes applied (each followed by a re-analysis).
    /// </summary>
    public int MaxPasses { get; }

    /// <summary>
    /// Gets the .editorconfig entries (option name → value) that apply to the cleaned document on top of its analyzer
    /// configuration: they win over every .editorconfig and global configuration of the project. The engine only
    /// analyzes with them; they never appear in <see cref="DiagnosticCleanupResult.ChangedSolution" />.
    /// </summary>
    public IReadOnlyDictionary<string, string> AnalyzerConfigOverrides { get; }

    /// <summary>
    /// Gets a value indicating whether no category is enabled, in which case the engine performs no analysis at all.
    /// </summary>
    public bool IsEmpty => EnabledCategories.Count == 0;

    /// <summary>
    /// Determines whether diagnostics of <paramref name="category" /> may be fixed.
    /// </summary>
    internal bool IsEnabled(DiagnosticCleanupCategory category) => EnabledCategories.Contains(category);
}
