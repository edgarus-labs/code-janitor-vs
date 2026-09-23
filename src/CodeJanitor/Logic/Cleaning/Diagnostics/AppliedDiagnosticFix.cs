using System;

namespace CodeJanitor.Logic.Cleaning.Diagnostics;

/// <summary>
/// Diagnostics of one ID fixed by one code fix provider during a diagnostic-driven cleanup.
/// </summary>
public sealed class AppliedDiagnosticFix
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppliedDiagnosticFix" /> class.
    /// </summary>
    /// <param name="diagnosticId">The fixed diagnostic ID.</param>
    /// <param name="category">The category of the fixed diagnostics.</param>
    /// <param name="providerName">The name of the code fix provider that fixed them.</param>
    /// <param name="count">The number of diagnostics targeted by the applied fixes.</param>
    public AppliedDiagnosticFix(string diagnosticId, DiagnosticCleanupCategory category, string providerName, int count)
    {
        DiagnosticId = diagnosticId ?? throw new ArgumentNullException(nameof(diagnosticId));
        Category = category;
        ProviderName = providerName ?? throw new ArgumentNullException(nameof(providerName));
        Count = count;
    }

    /// <summary>
    /// Gets the fixed diagnostic ID.
    /// </summary>
    public string DiagnosticId { get; }

    /// <summary>
    /// Gets the category of the fixed diagnostics.
    /// </summary>
    public DiagnosticCleanupCategory Category { get; }

    /// <summary>
    /// Gets the name of the code fix provider that fixed them.
    /// </summary>
    public string ProviderName { get; }

    /// <summary>
    /// Gets the number of diagnostics targeted by the applied fixes.
    /// </summary>
    public int Count { get; }

    /// <inheritdoc />
    public override string ToString() => $"{DiagnosticId} ({Category}) fixed {Count}x by {ProviderName}";
}
