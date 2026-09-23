using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace CodeJanitor.Logic.Cleaning.Diagnostics;

/// <summary>
/// Outcome of a <see cref="DiagnosticCleanupEngine" /> run.
/// </summary>
public sealed class DiagnosticCleanupResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticCleanupResult" /> class.
    /// </summary>
    /// <param name="originalSolution">The solution the cleanup started from.</param>
    /// <param name="changedSolution">The solution with all accepted fixes; only document texts differ.</param>
    /// <param name="appliedFixes">The applied fixes, in order of first application.</param>
    /// <param name="unresolved">The actionable diagnostics left in the cleaned document.</param>
    public DiagnosticCleanupResult(
        Solution originalSolution,
        Solution changedSolution,
        IEnumerable<AppliedDiagnosticFix> appliedFixes,
        IEnumerable<UnresolvedDiagnostic> unresolved)
    {
        OriginalSolution = originalSolution ?? throw new ArgumentNullException(nameof(originalSolution));
        ChangedSolution = changedSolution ?? throw new ArgumentNullException(nameof(changedSolution));
        AppliedFixes = (appliedFixes ?? throw new ArgumentNullException(nameof(appliedFixes))).ToList().AsReadOnly();
        Unresolved = (unresolved ?? throw new ArgumentNullException(nameof(unresolved))).ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets the solution the cleanup started from.
    /// </summary>
    public Solution OriginalSolution { get; }

    /// <summary>
    /// Gets the solution with all accepted fixes; compared to <see cref="OriginalSolution" /> only document texts differ.
    /// </summary>
    public Solution ChangedSolution { get; }

    /// <summary>
    /// Gets a value indicating whether at least one fix was accepted, i.e. <see cref="ChangedSolution" /> is not
    /// <see cref="OriginalSolution" />.
    /// </summary>
    public bool HasChanges => !ReferenceEquals(OriginalSolution, ChangedSolution);

    /// <summary>
    /// Gets the applied fixes, in order of first application.
    /// </summary>
    public IReadOnlyList<AppliedDiagnosticFix> AppliedFixes { get; }

    /// <summary>
    /// Gets the actionable diagnostics left in the cleaned document.
    /// </summary>
    public IReadOnlyList<UnresolvedDiagnostic> Unresolved { get; }

    /// <summary>
    /// Gets a value indicating whether the cleanup finished its work: <c>false</c> when a fix was rejected or the
    /// cleanup did not converge. Diagnostics that simply have no usable fix are reported but do not make it incomplete.
    /// </summary>
    public bool IsComplete => Unresolved.All(diagnostic => !IsBlocking(diagnostic.Reason));

    private static bool IsBlocking(UnresolvedDiagnosticReason reason) =>
        reason == UnresolvedDiagnosticReason.FixRejectedIntroducesCompilerErrors
        || reason == UnresolvedDiagnosticReason.FixRejectedUnsupportedChanges
        || reason == UnresolvedDiagnosticReason.NotConverged;
}
