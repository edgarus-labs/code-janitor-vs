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

    private readonly HashSet<string> _diagnosticIds;

    /// <summary>
    /// Gets the options of the Fix Namespace command: Roslyn's "Namespace does not match folder structure" analyzer
    /// and code fix (IDE0130, <c>dotnet_style_namespace_match_folder</c>) and no other diagnostic.
    /// </summary>
    public static DiagnosticCleanupOptions NamespaceMatchFolder { get; } = new DiagnosticCleanupOptions(
        (DiagnosticCleanupCategory[])Enum.GetValues(typeof(DiagnosticCleanupCategory)),
        analyzerConfigOverrides: new Dictionary<string, string>
        {
            ["dotnet_style_namespace_match_folder"] = "true:suggestion",
            ["dotnet_diagnostic.IDE0130.severity"] = "suggestion",
        },
        diagnosticIds: new[] { "IDE0130" });

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticCleanupOptions" /> class.
    /// </summary>
    /// <param name="enabledCategories">The diagnostic families whose diagnostics may be fixed.</param>
    /// <param name="maxPasses">The maximum number of fixes applied (each followed by a re-analysis).</param>
    /// <param name="analyzerConfigOverrides">
    /// .editorconfig entries (option name → value) that apply to the cleaned document on top of its analyzer
    /// configuration, or null for none.
    /// </param>
    /// <param name="diagnosticIds">The only diagnostic IDs that may be fixed, or null for every ID.</param>
    /// <param name="usingDirectiveSorting">How the using directives of the cleaned document are sorted when a fix changed it.</param>
    public DiagnosticCleanupOptions(
        IEnumerable<DiagnosticCleanupCategory> enabledCategories,
        int maxPasses = DefaultMaxPasses,
        IReadOnlyDictionary<string, string> analyzerConfigOverrides = null,
        IEnumerable<string> diagnosticIds = null,
        UsingDirectiveSorting usingDirectiveSorting = UsingDirectiveSorting.None)
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
        _diagnosticIds = diagnosticIds is null ? null : new HashSet<string>(diagnosticIds, StringComparer.OrdinalIgnoreCase);
        UsingDirectiveSorting = usingDirectiveSorting;
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
    /// Gets the only diagnostic IDs that may be fixed, or null when every diagnostic of an enabled category may be.
    /// </summary>
    public IReadOnlyCollection<string> DiagnosticIds => _diagnosticIds;

    /// <summary>
    /// Gets how the using directives of the cleaned document are sorted when a fix changed it.
    /// </summary>
    public UsingDirectiveSorting UsingDirectiveSorting { get; }

    /// <summary>
    /// Gets a value indicating whether no category is enabled, in which case the engine performs no analysis at all.
    /// </summary>
    public bool IsEmpty => EnabledCategories.Count == 0;

    /// <summary>
    /// Determines whether diagnostics of <paramref name="category" /> may be fixed.
    /// </summary>
    internal bool IsEnabled(DiagnosticCleanupCategory category) => EnabledCategories.Contains(category);

    /// <summary>
    /// Determines whether diagnostics with <paramref name="diagnosticId" /> may be fixed.
    /// </summary>
    internal bool Includes(string diagnosticId) => _diagnosticIds is null || _diagnosticIds.Contains(diagnosticId);
}
