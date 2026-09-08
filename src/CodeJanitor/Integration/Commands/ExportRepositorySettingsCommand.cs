using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using System.IO;
using System.Windows;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.Integration.Commands;

/// <summary>
/// A command that exports the current cleanup settings to a repository-level .codejanitor policy
/// file shared with the VS Code extension.
/// </summary>

internal sealed class ExportRepositorySettingsCommand : BaseCommand
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExportRepositorySettingsCommand" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    internal ExportRepositorySettingsCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorExportRepositorySettings)
    {
    }

    /// <summary>
    /// A singleton instance of this command.
    /// </summary>
    public static ExportRepositorySettingsCommand Instance { get; private set; }

    /// <summary>
    /// Initializes a singleton instance of this command.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>A task.</returns>

    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new ExportRepositorySettingsCommand(package);
        await Instance.SwitchAsync(on: true);
    }

    /// <summary>
    /// Called to execute the command.
    /// </summary>

    protected override void OnExecute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnExecute();

        var solutionFile = Package.IDE.Solution?.FullName;
        if (string.IsNullOrEmpty(solutionFile))
        {
            Package.IDE.StatusBar.Text = "CodeJanitor: open a solution to export repository settings.";
            return;
        }

        var filePath = Path.Combine(Path.GetDirectoryName(solutionFile), RepositoryCleanupSettings.PrimaryConfigFileName);
        if (File.Exists(filePath))
        {
            var choice = MessageBox.Show(
                $"Overwrite {RepositoryCleanupSettings.PrimaryConfigFileName}?",
                "CodeJanitor",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (choice != MessageBoxResult.Yes)
            {
                return;
            }
        }

        File.WriteAllText(filePath, RepositoryCleanupSettings.BuildJson(Settings.Default) + "\n");
        OutputWindowHelper.InfoWriteLine($"CodeJanitor: exported repository settings to {filePath}");
        Package.IDE.StatusBar.Text = $"CodeJanitor: exported repository settings to {RepositoryCleanupSettings.PrimaryConfigFileName}.";
    }
}
