using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System.Linq;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.Integration.Commands;

/// <summary>
/// A command that provides for closing all read-only files.
/// </summary>

internal sealed class CloseAllReadOnlyCommand : BaseCommand
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CloseAllReadOnlyCommand" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    internal CloseAllReadOnlyCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorCloseAllReadOnly)
    {
    }

    /// <summary>
    /// A singleton instance of this command.
    /// </summary>
    public static CloseAllReadOnlyCommand Instance { get; private set; }

    /// <summary>
    /// Initializes a singleton instance of this command.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>A task.</returns>

    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new CloseAllReadOnlyCommand(package);
        await package.SettingsMonitor.WatchAsync(s => s.Feature_CloseAllReadOnly, Instance.SwitchAsync);
    }

    /// <summary>
    /// Called to update the current status of the command.
    /// </summary>

    protected override void OnBeforeQueryStatus()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Enabled = Package.IDE.Documents.Cast<Document>().Any(x => x.ReadOnly && x.Saved);
    }

    /// <summary>
    /// Called to execute the command.
    /// </summary>

    protected override void OnExecute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnExecute();

        var docs = Package.IDE.Documents;

        foreach (Document doc in docs)
        {
            if (doc.ReadOnly && doc.Saved)
            {
                doc.Close(vsSaveChanges.vsSaveChangesNo);
            }
        }
    }
}
