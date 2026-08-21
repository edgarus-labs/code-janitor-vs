using Microsoft.VisualStudio.Shell;
using CodeJanitor.Integration.Options;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.Integration.Commands;

/// <summary>
/// A command that provides for launching the CodeJanitor Options to the general cleanup page.
/// </summary>

internal sealed class OptionsCommand : BaseCommand
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OptionsCommand" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    internal OptionsCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorOptions)
    {
    }

    /// <summary>
    /// A singleton instance of this command.
    /// </summary>
    public static OptionsCommand Instance { get; private set; }

    /// <summary>
    /// Initializes a singleton instance of this command.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>A task.</returns>

    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new OptionsCommand(package);
        await Instance.SwitchAsync(on: true);
    }

    /// <summary>
    /// Called to execute the command.
    /// </summary>

    protected override void OnExecute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnExecute();
        Package.ShowOptionPage(typeof(CodeJanitorGeneralPage));
    }
}
