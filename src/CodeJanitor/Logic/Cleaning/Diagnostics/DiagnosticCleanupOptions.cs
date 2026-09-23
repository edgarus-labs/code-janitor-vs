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
    public DiagnosticCleanupOptions(IEnumerable<DiagnosticCleanupCategory> enabledCategories, int maxPasses = DefaultMaxPasses)
    {
        if (enabledCategories == null)
        {
            throw new ArgumentNullException(nameof(enabledCategories));
        }

        if (maxPasses < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPasses), maxPasses, "At least one pass is required.");
        }

        EnabledCategories = enabledCategories.Distinct().OrderBy(category => category).ToList().AsReadOnly();
        MaxPasses = maxPasses;
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
    /// Gets a value indicating whether no category is enabled, in which case the engine performs no analysis at all.
    /// </summary>
    public bool IsEmpty => EnabledCategories.Count == 0;

    /// <summary>
    /// Determines whether diagnostics of <paramref name="category" /> may be fixed.
    /// </summary>
    internal bool IsEnabled(DiagnosticCleanupCategory category) => EnabledCategories.Contains(category);
}
