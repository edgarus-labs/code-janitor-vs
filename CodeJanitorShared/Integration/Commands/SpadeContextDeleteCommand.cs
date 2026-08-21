using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Model.CodeItems;
using CodeJanitor.Properties;
using System;
using System.Linq;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.Integration.Commands;

/// <summary>
/// A command that provides for deleting a member within Spade.
/// </summary>

internal sealed class SpadeContextDeleteCommand : BaseCommand
{
    private readonly UndoTransactionHelper _undoTransactionHelper;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpadeContextDeleteCommand" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    internal SpadeContextDeleteCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorSpadeContextDelete)
    {
        _undoTransactionHelper = new UndoTransactionHelper(package, Resources.CodeJanitorDeleteItems);
    }

    /// <summary>
    /// A singleton instance of this command.
    /// </summary>
    public static SpadeContextDeleteCommand Instance { get; private set; }

    /// <summary>
    /// Initializes a singleton instance of this command.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>A task.</returns>

    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new SpadeContextDeleteCommand(package);
        await Instance.SwitchAsync(on: true);
    }

    /// <summary>
    /// Called to update the current status of the command.
    /// </summary>

    protected override void OnBeforeQueryStatus()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        bool visible = false;

        var spade = Package.Spade;
        if (spade != null)
        {
            visible = spade.SelectedItems.Any(IsDeletable);
        }

        Visible = visible;
    }

    /// <summary>
    /// Called to execute the command.
    /// </summary>

    protected override void OnExecute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnExecute();

        var spade = Package.Spade;
        if (spade != null)
        {
            // Delay the check of start/end points until execution time, to avoid an intermediate state issue.
            var items = spade.SelectedItems.Where(IsDeletable).Where(x => x.StartPoint != null && x.EndPoint != null);

            _undoTransactionHelper.Run(() =>
            {
                // Iterate through items in reverse order (reduces line number updates during removal).
                foreach (var item in items.OrderByDescending(x => x.StartLine))
                {
                    var start = item.StartPoint.CreateEditPoint();

                    start.Delete(item.EndPoint);
                    start.DeleteWhitespace(vsWhitespaceOptions.vsWhitespaceOptionsVertical);
                    start.Insert(Environment.NewLine);
                }
            });

            spade.Refresh();
        }
    }

    /// <summary>
    /// Determines if the specified item is a candidate for deletion.
    /// </summary>
    /// <param name="codeItem">The code item.</param>
    /// <returns>True if the code item can be deleted, otherwise false.</returns>

    private static bool IsDeletable(BaseCodeItem codeItem)
    {
        return !(codeItem is CodeItemRegion) || !((CodeItemRegion)codeItem).IsPseudoGroup;
    }
}
