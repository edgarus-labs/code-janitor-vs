using Microsoft.CodeAnalysis;
using System;

namespace CodeJanitor.Logic.Cleaning.Diagnostics;

/// <summary>
/// An actionable diagnostic that is still present after a diagnostic-driven cleanup.
/// </summary>
public sealed class UnresolvedDiagnostic
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnresolvedDiagnostic" /> class.
    /// </summary>
    /// <param name="diagnosticId">The diagnostic ID.</param>
    /// <param name="category">The category of the diagnostic.</param>
    /// <param name="severity">The reported (effective) severity.</param>
    /// <param name="filePath">The path of the file containing the diagnostic.</param>
    /// <param name="line">The 1-based line of the diagnostic.</param>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="reason">Why the diagnostic was not fixed.</param>
    /// <param name="detail">
    /// Optional details of the reason, e.g. the failing provider and its exception message for
    /// <see cref="UnresolvedDiagnosticReason.FixProviderFailed" />.
    /// </param>
    public UnresolvedDiagnostic(
        string diagnosticId,
        DiagnosticCleanupCategory category,
        DiagnosticSeverity severity,
        string filePath,
        int line,
        string message,
        UnresolvedDiagnosticReason reason,
        string detail = null)
    {
        DiagnosticId = diagnosticId ?? throw new ArgumentNullException(nameof(diagnosticId));
        Category = category;
        Severity = severity;
        FilePath = filePath ?? string.Empty;
        Line = line;
        Message = message ?? string.Empty;
        Reason = reason;
        Detail = detail ?? string.Empty;
    }

    /// <summary>
    /// Gets the diagnostic ID.
    /// </summary>
    public string DiagnosticId { get; }

    /// <summary>
    /// Gets the category of the diagnostic.
    /// </summary>
    public DiagnosticCleanupCategory Category { get; }

    /// <summary>
    /// Gets the reported (effective) severity.
    /// </summary>
    public DiagnosticSeverity Severity { get; }

    /// <summary>
    /// Gets the path of the file containing the diagnostic.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Gets the 1-based line of the diagnostic.
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// Gets the diagnostic message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets why the diagnostic was not fixed.
    /// </summary>
    public UnresolvedDiagnosticReason Reason { get; }

    /// <summary>
    /// Gets the details of <see cref="Reason" />, e.g. the failing provider and its exception message; empty when
    /// there are none.
    /// </summary>
    public string Detail { get; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{FilePath}({Line}): {Severity} {DiagnosticId} [{Category}] {Reason}: {Message}" + (Detail.Length == 0 ? string.Empty : $" ({Detail})");
}
