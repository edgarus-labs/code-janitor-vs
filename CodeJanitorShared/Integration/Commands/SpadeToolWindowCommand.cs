using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.Integration.Commands;

/// <summary>
/// A command that provides for launching the Spade tool window.
/// </summary>

internal sealed class SpadeToolWindowCommand : BaseCommand
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpadeToolWindowCommand" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    internal SpadeToolWindowCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorSpadeToolWindow)
    {
    }

    /// <summary>
    /// A singleton instance of this command.
    /// </summary>
    public static SpadeToolWindowCommand Instance { get; private set; }

    /// <summary>
    /// Initializes a singleton instance of this command.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>A task.</returns>

    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new SpadeToolWindowCommand(package);
        await package.SettingsMonitor.WatchAsync(s => s.Feature_SpadeToolWindow, Instance.SwitchAsync);
    }

    /// <summary>
    /// 1. **Analyze the Request**: * Target: C# method `public override async Task SwitchAsync(bool on)`. * Body: awaits base, then if `on` is false, calls `Package.Spade?.Close()`. * Constraint: exactly one concise summary sentence, plain text only, no XML, no quotes. * Mention key behavior and side effects. * Detected thrown exceptions: none detected (so I don&apos;t need to mention exceptions). 2. **Analyze the Code**: * `await base.SwitchAsync(on)` -&gt; calls base implementation, awaits it. * `if (!on)` -&gt; only when turning off. * `Package.Spade?.Close()` -&gt; null-conditional call to close the spade, if it exists. 3. **Draft the Summary**: * Behavior: Overrides SwitchAsync, delegates to base, and conditionally closes the spade when turning off. * Side effect: Closing `Package.Spade` (if not null) when `on` is false. * Combine into one sentence. 4. **Check constraints**: * Exactly.
    /// </summary>
    /// <param name="on">The on.</param>
    /// <returns>A Task value produced by this method.</returns>

    public override async Task SwitchAsync(bool on)
    {
        await base.SwitchAsync(on);

        if (!on)
        {
            Package.Spade?.Close();
        }
    }

    /// <summary>
    /// Called when a document has been saved.
    /// </summary>
    /// <param name="document">The document that was saved.</param>

    internal void OnAfterDocumentSave(Document document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var spade = Package.Spade;
        if (spade != null)
        {
            spade.NotifyDocumentSave(document);
        }
    }

    /// <summary>
    /// Called when a window change has occurred, potentially to be used by the Spade tool window.
    /// </summary>
    /// <param name="document">The document that got focus, may be null.</param>

    internal void OnWindowChange(Document document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var spade = Package.Spade;
        if (spade != null)
        {
            spade.NotifyActiveDocument(document);
        }
    }

    /// <summary>
    /// Called to execute the command.
    /// </summary>

    protected override void OnExecute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnExecute();

        var spade = Package.SpadeForceLoad;
        if (spade?.Frame is IVsWindowFrame spadeFrame)
        {
            spadeFrame.Show();
        }
    }
}
