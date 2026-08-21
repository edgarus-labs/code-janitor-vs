using Microsoft.VisualStudio.Shell;
using CodeJanitor.Integration.Options;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.Integration.Commands;

/// <summary>
/// A command that provides for launching the CodeJanitor Options to the Spade page.
/// </summary>

internal sealed class SpadeOptionsCommand : BaseCommand
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpadeOptionsCommand" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    internal SpadeOptionsCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorSpadeOptions)
    {
    }

    /// <summary>
    /// A singleton instance of this command.
    /// </summary>
    public static SpadeOptionsCommand Instance { get; private set; }

    /// <summary>
    /// Initializes a singleton instance of this command.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>A task.</returns>

    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new SpadeOptionsCommand(package);
        await Instance.SwitchAsync(on: true);
    }

    /// <summary>
    /// Called to execute the command.
    /// </summary>

    protected override void OnExecute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnExecute();
        Package.ShowOptionPage(typeof(CodeJanitorDiggingPage));
    }
}
