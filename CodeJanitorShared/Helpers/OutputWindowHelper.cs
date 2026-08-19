using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using CodeJanitor.Properties;
using System;

namespace CodeJanitor.Helpers;

/// <summary>
/// A helper class for writing messages to a CodeJanitor output window pane.
/// </summary>

internal static class OutputWindowHelper
{
    private static IVsOutputWindowPane _CodeJanitorOutputWindowPane;

    private static IVsOutputWindowPane CodeJanitorOutputWindowPane =>
        _CodeJanitorOutputWindowPane ?? (_CodeJanitorOutputWindowPane = GetCodeJanitorOutputWindowPane());

    /// <summary>
    /// Ensures the CodeJanitor output pane exists and is visible in the Output window's
    /// "Show output from" list, without writing any message to it. Safe to call multiple
    /// times/early (e.g. during package load) so the pane isn't only lazily created on the
    /// first logged message.
    /// </summary>

    internal static void EnsurePaneCreated()
    {
        _ = CodeJanitorOutputWindowPane;
    }

    /// <summary>
    /// Writes the specified informational line to the CodeJanitor output pane. Always shown,
    /// regardless of Diagnostics Mode.
    /// </summary>
    /// <param name="message">The message.</param>

    internal static void InfoWriteLine(string message)
    {
        WriteLine(Resources.Info, message);
    }

    /// <summary>
    /// Writes the specified diagnostic line to the CodeJanitor output pane, but only if diagnostics are enabled.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="ex">An optional exception that was handled.</param>

    internal static void DiagnosticWriteLine(string message, Exception ex = null)
    {
        if (!Settings.Default.General_DiagnosticsMode) return;

        if (ex != null)
        {
            message += $": {ex}";
        }

        WriteLine(Resources.Diagnostic, message);
    }

    /// <summary>
    /// Writes the specified exception line to the CodeJanitor output pane.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="ex">The exception that was handled.</param>

    internal static void ExceptionWriteLine(string message, Exception ex)
    {
        var exceptionMessage = $"{message}: {ex}";

        WriteLine(Resources.HandledException, exceptionMessage);
    }

    /// <summary>
    /// Writes the specified warning line to the CodeJanitor output pane.
    /// </summary>
    /// <param name="message">The message.</param>

    internal static void WarningWriteLine(string message)
    {
        WriteLine(Resources.Warning, message);
    }

    /// <summary>
    /// Attempts to create and retrieve the CodeJanitor output window pane.
    /// </summary>
    /// <returns>The CodeJanitor output window pane, otherwise null.</returns>

    private static IVsOutputWindowPane GetCodeJanitorOutputWindowPane()
    {
        if (!(Package.GetGlobalService(typeof(SVsOutputWindow)) is IVsOutputWindow outputWindow))
        {
            return null;
        }

        var outputPaneGuid = new Guid(PackageGuids.GuidCodeJanitorOutputPane.ToByteArray());

        outputWindow.CreatePane(ref outputPaneGuid, "CodeJanitor", 1, 1);
        outputWindow.GetPane(ref outputPaneGuid, out IVsOutputWindowPane windowPane);

        return windowPane;
    }

    /// <summary>
    /// Writes the specified line to the CodeJanitor output pane.
    /// </summary>
    /// <param name="category">The category.</param>
    /// <param name="message">The message.</param>

    private static void WriteLine(string category, string message)
    {
        var outputWindowPane = CodeJanitorOutputWindowPane;
        if (outputWindowPane != null)
        {
            string outputMessage = $"[CodeJanitor {category} {DateTime.Now.ToString("hh:mm:ss tt")}] {message}{Environment.NewLine}";

            outputWindowPane.OutputString(outputMessage);
        }
    }
}