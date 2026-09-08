using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.CleanupOptions;
using CodeJanitor.UI.Dialogs.CleanupProgress;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
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
        await Instance.SwitchAsync(true);
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
                if (optionsViewModel.PreviewRequested)
                {
                    PreviewCleanup(selectedProjectItems);

                    return;
                }

                var viewModel = new CleanupProgressViewModel(Package, selectedProjectItems);
                var window = new CleanupProgressWindow { DataContext = viewModel };

                window.ShowModal();
            }
        }
    }

    private void PreviewCleanup(IReadOnlyList<ProjectItem> projectItems)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var itemsByPreview = new Dictionary<CleanupPreviewFile, ProjectItem>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in projectItems)
        {
            var path = item.Name;
            CleanupPreviewFile preview;
            try
            {
                path = item.FileNames[1];
                if (!seenPaths.Add(path))
                {
                    continue;
                }

                if (!string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase))
                {
                    preview = new CleanupPreviewFile(path, null, null, "Skipped: C# text preview only");
                }
                else
                {
                    var document = item.Document?.Object("TextDocument") as TextDocument;
                    var source = document is null
                        ? File.ReadAllText(path)
                        : document.StartPoint.CreateEditPoint().GetText(document.EndPoint);
                    preview = new CleanupPreviewFile(path, source, CodeCleanupManager.CreateHeadlessCSharpPipeline(source, path));
                }
            }
            catch (Exception exception)
            {
                preview = new CleanupPreviewFile(path, null, null, "Skipped: " + exception.Message);
            }

            itemsByPreview.Add(preview, item);
        }

        var viewModel = new CleanupPreviewViewModel(itemsByPreview.Keys.ToList());
        new CleanupPreviewWindow { DataContext = viewModel }.ShowModal();
        if (viewModel.DialogResult != true || !CodeCleanupAvailabilityLogic.IsCleanupEnvironmentAvailable())
        {
            return;
        }

        var applied = 0;
        var errors = new List<string>();
        foreach (var entry in itemsByPreview.Where(entry => entry.Key.Include && entry.Key.CanApply))
        {
            try
            {
                var document = entry.Value.Document ?? entry.Value.Open(Constants.vsViewKindCode)?.Document;
                if (document is null || document.ReadOnly || !(document.Object("TextDocument") is TextDocument textDocument))
                {
                    errors.Add(entry.Key.Path + ": skipped (editor unavailable or read-only).");
                    continue;
                }

                var start = textDocument.StartPoint.CreateEditPoint();
                var currentSource = start.GetText(textDocument.EndPoint);
                if (!entry.Key.TryApply(currentSource, updated =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    using (new UndoTransactionHelper(Package, "Apply C# Text Cleanup Preview"))
                    {
                        start.ReplaceText(textDocument.EndPoint, updated, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
                    }
                }))
                {
                    errors.Add(entry.Key.Path + ": skipped (changed since preview; create a new preview).");
                    continue;
                }

                applied++;
            }
            catch (Exception exception)
            {
                errors.Add(entry.Key.Path + ": " + exception.Message);
            }
        }

        var summary = $"Applied preview to {applied} editor buffer(s). No files were saved.";
        Package.IDE.StatusBar.Text = summary;
        if (errors.Count > 0)
        {
            MessageBox.Show(summary + Environment.NewLine + string.Join(Environment.NewLine, errors),
                "C# Text Cleanup Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            if (temporarySettings is null)
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

        /// <summary>
        /// Restores all original settings values from _originalValues into Settings.Default, thereby reverting any changes made during the object&apos;s lifetime.
        /// </summary>

        public void Dispose()
        {
            foreach (var item in _originalValues)
            {
                Settings.Default[item.Key] = item.Value;
            }
        }
    }
}
