using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using System.IO;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.Integration.Commands;

/// <summary>
/// A command that imports cleanup settings from a repository-level .codejanitor policy file shared
/// with the VS Code extension into the current user settings.
/// </summary>

internal sealed class ImportRepositorySettingsCommand : BaseCommand
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ImportRepositorySettingsCommand" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    internal ImportRepositorySettingsCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorImportRepositorySettings)
    {
    }

    /// <summary>
    /// A singleton instance of this command.
    /// </summary>
    public static ImportRepositorySettingsCommand Instance { get; private set; }

    /// <summary>
    /// Initializes a singleton instance of this command.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>A task.</returns>

    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new ImportRepositorySettingsCommand(package);
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
            Package.IDE.StatusBar.Text = "CodeJanitor: open a solution to import repository settings.";
            return;
        }

        var solutionDirectory = Path.GetDirectoryName(solutionFile);
        if (!RepositoryCleanupSettings.TryFindConfigFile(solutionDirectory, out var configPath))
        {
            Package.IDE.StatusBar.Text = $"CodeJanitor: {RepositoryCleanupSettings.PrimaryConfigFileName} was not found.";
            return;
        }

        var overrides = RepositoryCleanupSettings.LoadFromDirectory(solutionDirectory);
        var applied = RepositoryCleanupSettings.ApplyToSettings(overrides, Settings.Default);
        Settings.Default.Save();

        OutputWindowHelper.InfoWriteLine($"CodeJanitor: imported {applied} repository setting(s) from {configPath}");
        Package.IDE.StatusBar.Text = $"CodeJanitor: imported {applied} repository setting(s) from {Path.GetFileName(configPath)}.";
    }
}
