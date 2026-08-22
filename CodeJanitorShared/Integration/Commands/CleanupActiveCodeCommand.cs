using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.Integration.Commands;

/// <summary>
/// A command that provides for cleaning up code in the active document.
/// </summary>

internal sealed class CleanupActiveCodeCommand : BaseCommand
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CleanupActiveCodeCommand" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    internal CleanupActiveCodeCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorCleanupActiveCode)
    {
        CodeCleanupAvailabilityLogic = CodeCleanupAvailabilityLogic.GetInstance(Package);
        CodeCleanupManager = CodeCleanupManager.GetInstance(Package);
    }

    /// <summary>
    /// A singleton instance of this command.
    /// </summary>
    public static CleanupActiveCodeCommand Instance { get; private set; }

    /// <summary>
    /// Gets the code cleanup availability logic.
    /// </summary>
    private CodeCleanupAvailabilityLogic CodeCleanupAvailabilityLogic { get; }

    /// <summary>
    /// Gets the code cleanup manager.
    /// </summary>
    private CodeCleanupManager CodeCleanupManager { get; }

    /// <summary>
    /// Initializes a singleton instance of this command.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>A task.</returns>

    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new CleanupActiveCodeCommand(package);
        await package.SettingsMonitor.WatchAsync(s => s.Feature_CleanupActiveCode, Instance.SwitchAsync);
    }

    /// <summary>
    /// Called before a document is saved in order to potentially run code cleanup.
    /// </summary>
    /// <param name="document">The document about to be saved.</param>

    internal void OnBeforeDocumentSave(Document document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!Settings.Default.Cleaning_AutoCleanupOnFileSave) return;
        if (!CodeCleanupAvailabilityLogic.CanCleanupDocument(document)) return;

        try
        {
            Package.IsAutoSaveContext = true;

            using (new ActiveDocumentRestorer(Package))
            {
                CodeCleanupManager.Cleanup(document);
            }
        }
        finally
        {
            Package.IsAutoSaveContext = false;
        }
    }

    /// <summary>
    /// Called to update the current status of the command.
    /// </summary>

    protected override void OnBeforeQueryStatus()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Enabled = Package.ActiveDocument is not null;
    }

    /// <summary>
    /// Called to execute the command.
    /// </summary>

    protected override void OnExecute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnExecute();

        CodeCleanupManager.Cleanup(Package.ActiveDocument);
    }
}
