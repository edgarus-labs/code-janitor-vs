using System;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// The outcome of running .editorconfig/Roslyn diagnostic cleanup on one C# file. The default value
/// means nothing changed and nothing is unresolved (including when no category is enabled).
/// </summary>
internal struct DiagnosticCleanupOutcome
{
    /// <summary>
    /// Gets or sets a value indicating whether fixes were applied to the Visual Studio workspace.
    /// </summary>
    internal bool Changed { get; set; }

    /// <summary>
    /// Gets or sets the number of actionable diagnostics that were left unresolved.
    /// </summary>
    internal int UnresolvedCount { get; set; }

    /// <summary>
    /// Gets or sets the failure that prevented diagnostic cleanup, otherwise null.
    /// </summary>
    internal Exception Failure { get; set; }
}
