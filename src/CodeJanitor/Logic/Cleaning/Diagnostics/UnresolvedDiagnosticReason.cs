namespace CodeJanitor.Logic.Cleaning.Diagnostics;

/// <summary>
/// Why an actionable diagnostic is still present after the diagnostic-driven cleanup.
/// </summary>
public enum UnresolvedDiagnosticReason
{
    /// <summary>
    /// No code fix provider handles the diagnostic. It is reported and the code is never modified for it.
    /// </summary>
    NoCodeFixProvider,

    /// <summary>
    /// The providers offered nothing usable: no action, only actions with nested choices, or an action without
    /// any effect.
    /// </summary>
    NoApplicableCodeAction,

    /// <summary>
    /// The fix was rejected because it increased the number of compiler errors of a changed project.
    /// </summary>
    FixRejectedIntroducesCompilerErrors,

    /// <summary>
    /// The fix was rejected because it did more than change document texts (e.g. added or removed documents,
    /// projects or references) or did not consist of exactly one solution change. Other operations a fix returns
    /// next to its solution change, such as host notifications, are ignored and never executed.
    /// </summary>
    FixRejectedUnsupportedChanges,

    /// <summary>
    /// The diagnostic was still present when the maximum number of passes was reached.
    /// </summary>
    NotConverged,
}
