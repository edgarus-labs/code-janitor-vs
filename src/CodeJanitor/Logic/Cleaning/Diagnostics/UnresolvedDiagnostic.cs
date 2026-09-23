using System;
using Microsoft.CodeAnalysis;

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
    public UnresolvedDiagnostic(
        string diagnosticId,
        DiagnosticCleanupCategory category,
        DiagnosticSeverity severity,
        string filePath,
        int line,
        string message,
        UnresolvedDiagnosticReason reason)
    {
        DiagnosticId = diagnosticId ?? throw new ArgumentNullException(nameof(diagnosticId));
        Category = category;
        Severity = severity;
        FilePath = filePath ?? string.Empty;
        Line = line;
        Message = message ?? string.Empty;
        Reason = reason;
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

    /// <inheritdoc />
    public override string ToString() => $"{FilePath}({Line}): {Severity} {DiagnosticId} [{Category}] {Reason}: {Message}";
}
