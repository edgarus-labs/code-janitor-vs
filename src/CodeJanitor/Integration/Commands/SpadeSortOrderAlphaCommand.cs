using Microsoft.VisualStudio.Shell;
using CodeJanitor.Model.CodeTree;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.Integration.Commands;

/// <summary>
/// A command that provides for setting Spade to alphabetical sort order.
/// </summary>

internal sealed class SpadeSortOrderAlphaCommand : BaseCommand
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpadeSortOrderAlphaCommand" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    internal SpadeSortOrderAlphaCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorSpadeSortOrderAlpha)
    {
    }

    /// <summary>
    /// A singleton instance of this command.
    /// </summary>
    public static SpadeSortOrderAlphaCommand Instance { get; private set; }

    /// <summary>
    /// Initializes a singleton instance of this command.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>A task.</returns>

    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new SpadeSortOrderAlphaCommand(package);
        await Instance.SwitchAsync(on: true);
    }

    /// <summary>
    /// Called to update the current status of the command.
    /// </summary>

    protected override void OnBeforeQueryStatus()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var spade = Package.Spade;
        if (spade is not null)
        {
            Checked = spade.SortOrder == CodeSortOrder.Alpha;
        }
    }

    /// <summary>
    /// Called to execute the command.
    /// </summary>

    protected override void OnExecute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnExecute();

        var spade = Package.Spade;
        if (spade is not null)
        {
            spade.SortOrder = CodeSortOrder.Alpha;
        }
    }
}
