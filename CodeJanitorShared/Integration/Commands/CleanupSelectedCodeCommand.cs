using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.CleanupOptions;
using CodeJanitor.UI.Dialogs.CleanupProgress;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.Integration.Commands;

/// <summary>
/// A command that provides for cleaning up code in the selected documents.
/// </summary>

internal sealed class CleanupSelectedCodeCommand : BaseCommand
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CleanupSelectedCodeCommand" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    internal CleanupSelectedCodeCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorCleanupSelectedCode)
    {
        CodeCleanupAvailabilityLogic = CodeCleanupAvailabilityLogic.GetInstance(Package);
    }

    /// <summary>
    /// A singleton instance of this command.
    /// </summary>
    public static CleanupSelectedCodeCommand Instance { get; private set; }

    /// <summary>
    /// Gets or sets the code cleanup availability logic.
    /// </summary>
    private CodeCleanupAvailabilityLogic CodeCleanupAvailabilityLogic { get; }

    /// <summary>
    /// Gets the list of selected project items.
    /// </summary>

    private IEnumerable<ProjectItem> SelectedProjectItems
        => SolutionHelper.GetSelectedProjectItemsRecursively(Package).Where(x => CodeCleanupAvailabilityLogic.CanCleanupProjectItem(x));

    /// <summary>
    /// Initializes a singleton instance of this command.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>A task.</returns>

    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new CleanupSelectedCodeCommand(package);
        await package.SettingsMonitor.WatchAsync(s => s.Feature_CleanupSelectedCode, Instance.SwitchAsync);
    }

    /// <summary>
    /// Called to update the current status of the command.
    /// </summary>

    protected override void OnBeforeQueryStatus()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Enabled = Package.IDE.Solution.IsOpen;
    }

    /// <summary>
    /// Called to execute the command.
    /// </summary>

    protected override void OnExecute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnExecute();

        if (!CodeCleanupAvailabilityLogic.IsCleanupEnvironmentAvailable())
        {
            MessageBox.Show(Resources.CleanupCannotRunWhileDebugging,
                            Resources.CleanupSelectedCode,
                            MessageBoxButton.OK, MessageBoxImage.Warning);

            return;
        }

        var selectedProjectItems = SelectedProjectItems.ToList();
        if (!selectedProjectItems.Any())
        {
            MessageBox.Show("No cleanable files were found in the selected scope.",
                            Resources.CleanupSelectedCode,
                            MessageBoxButton.OK, MessageBoxImage.Information);

            return;
        }

        var optionsViewModel = new CleanupOptionsViewModel(Package, Settings.Default, selectedProjectItems.Count);
        var optionsWindow = new CleanupOptionsWindow { DataContext = optionsViewModel };
        optionsWindow.ShowModal();
        if (optionsViewModel.DialogResult != true)
        {
            return;
        }

        if (optionsViewModel.UseTemporaryCleanupSettings)
        {
            optionsViewModel.SaveTemporarySettings();
        }

        using (new ActiveDocumentRestorer(Package))
        {
            using (optionsViewModel.UseTemporaryCleanupSettings
                ? new TemporaryCleanupSettingsScope(optionsViewModel.TemporarySettings)
                : null)
            {
                var viewModel = new CleanupProgressViewModel(Package, selectedProjectItems);
                var window = new CleanupProgressWindow { DataContext = viewModel };

                window.ShowModal();
            }
        }
    }

    /// <summary>
    /// Temporarily applies cleanup settings for one cleanup run and restores original values afterwards.
    /// </summary>

    private sealed class TemporaryCleanupSettingsScope : IDisposable
    {
        private readonly Dictionary<string, object> _originalValues = new Dictionary<string, object>(StringComparer.Ordinal);

        internal TemporaryCleanupSettingsScope(Settings temporarySettings)
        {
            if (temporarySettings == null)
            {
                return;
            }

            foreach (SettingsProperty property in Settings.Default.Properties)
            {
                if (!property.Name.StartsWith("Cleaning_", StringComparison.Ordinal))
                {
                    continue;
                }

                _originalValues[property.Name] = Settings.Default[property.Name];
                Settings.Default[property.Name] = temporarySettings[property.Name];
            }
        }

        public void Dispose()
        {
            foreach (var item in _originalValues)
            {
                Settings.Default[item.Key] = item.Value;
            }
        }
    }
}