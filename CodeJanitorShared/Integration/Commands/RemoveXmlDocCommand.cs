using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.Integration.Commands;

/// <summary>
/// A command that removes XML documentation comments from C# files in the selected scope or active document.
/// </summary>
internal sealed class RemoveXmlDocCommand : BaseCommand
{
    /// <summary>
    /// The large scope warning threshold.
    /// </summary>
    private const int LargeScopeWarningThreshold = 50;
    /// <summary>
    /// The very large scope warning threshold.
    /// </summary>
    private const int VeryLargeScopeWarningThreshold = 200;

    private readonly RemoveXmlDocumentationLogic _removeXmlDocumentationLogic;
    private readonly CodeCleanupAvailabilityLogic _codeCleanupAvailabilityLogic;

    internal RemoveXmlDocCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorRemoveXmlDoc)
    {
        _removeXmlDocumentationLogic = RemoveXmlDocumentationLogic.GetInstance(Package);
        _codeCleanupAvailabilityLogic = CodeCleanupAvailabilityLogic.GetInstance(Package);
    }

    /// <summary>
    /// Gets the singleton instance of this command.
    /// </summary>
    public static RemoveXmlDocCommand Instance { get; private set; }

    /// <summary>
    /// Initializes a singleton instance of this command and monitors the Feature_RemoveXmlDoc setting.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>A task.</returns>
    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new RemoveXmlDocCommand(package);
        await package.SettingsMonitor.WatchAsync(s => s.Feature_RemoveXmlDoc, Instance.SwitchAsync);
    }

    /// <summary>
    /// Called to update the current status of the command.
    /// </summary>
    protected override void OnBeforeQueryStatus()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Enabled = Package.IDE.Solution.IsOpen || (Package.ActiveDocument is not null && Package.ActiveDocument.GetCodeLanguage() == CodeLanguage.CSharp);
    }

    /// <summary>
    /// Called to execute the command.
    /// </summary>
    protected override void OnExecute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnExecute();

        if (!_codeCleanupAvailabilityLogic.IsCleanupEnvironmentAvailable())
        {
            MessageBox.Show(Resources.CleanupCannotRunWhileDebugging,
                            "CodeJanitor Remove XMLDoc",
                            MessageBoxButton.OK, MessageBoxImage.Warning);

            return;
        }

        var projectItems = GetScopeProjectItems().ToList();
        if (projectItems.Count == 0)
        {
            MessageBox.Show("No C# files found in current scope.",
                            "CodeJanitor Remove XMLDoc",
                            MessageBoxButton.OK, MessageBoxImage.Information);

            return;
        }

        if (!ConfirmScope(projectItems.Count))
        {
            return;
        }

        var changedCount = 0;

        using (new ActiveDocumentRestorer(Package))
        {
            var totalCount = projectItems.Count;
            var current = 0;

            foreach (var projectItem in projectItems)
            {
                current++;

                if (projectItem is not null)
                {
                    Package.IDE.StatusBar.Text = $"CodeJanitor removing XML documentation {current}/{totalCount}: {projectItem.Name}";
                }

                if (_removeXmlDocumentationLogic.RemoveXmlDoc(projectItem))
                {
                    changedCount++;
                }
            }
        }

        Package.IDE.StatusBar.Text = $"CodeJanitor Remove XMLDoc completed: removed XML documentation from {changedCount} of {projectItems.Count} file(s).";
        MessageBox.Show($"Processed {projectItems.Count} file(s). Removed XML documentation from {changedCount} file(s).",
                        "CodeJanitor Remove XMLDoc",
                        MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Shows a modal Yes/No confirmation dialog with wording scaled by file count thresholds.
    /// </summary>
    /// <param name="fileCount">The file count.</param>
    /// <returns>True if confirmed, otherwise false.</returns>
    private bool ConfirmScope(int fileCount)
    {
        if (fileCount <= 1)
        {
            return true;
        }

        var message = fileCount > VeryLargeScopeWarningThreshold
            ? $"You are about to run Remove XMLDoc on {fileCount:N0} files. This will modify many files across the solution. Continue?"
            : fileCount > LargeScopeWarningThreshold
                ? $"You are about to run Remove XMLDoc on {fileCount:N0} files. Continue?"
                : $"Remove XMLDoc from {fileCount:N0} files?";

        return MessageBox.Show(message,
                               "CodeJanitor Remove XMLDoc Confirmation",
                               MessageBoxButton.YesNo,
                               MessageBoxImage.Question,
                               MessageBoxResult.No)
               == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Returns distinct project items from selected UI hierarchy roots that pass checks,
    /// prioritizing Solution Explorer selection, or falling back to the active document.
    /// </summary>
    /// <returns>Sequence of project items.</returns>
    private IEnumerable<ProjectItem> GetScopeProjectItems()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // 1. Check selection in Solution Explorer first
        var selectedScopeRoots = UIHierarchyHelper.GetSelectedUIHierarchyItems(Package)
            .Select(item => item.Object)
            .Where(item => item is not null)
            .ToList();

        // If a single ProjectItem (file) is selected in Solution Explorer
        if (selectedScopeRoots.Count == 1 && selectedScopeRoots[0] is ProjectItem singleProjectItem)
        {
            if (_removeXmlDocumentationLogic.CanRemoveXmlDocProjectItem(singleProjectItem))
            {
                return new[] { singleProjectItem };
            }
        }

        var selectedProjectItems = selectedScopeRoots
            .SelectMany(SolutionHelper.GetItemsRecursively<ProjectItem>)
            .Where(projectItem => _removeXmlDocumentationLogic.CanRemoveXmlDocProjectItem(projectItem));

        var selectedScopedDistinct = DistinctByFilePath(selectedProjectItems).ToList();
        if (selectedScopedDistinct.Count > 0)
        {
            return selectedScopedDistinct;
        }

        // 2. Fallback to active document if editing
        var activeDoc = Package.ActiveDocument;
        if (activeDoc?.ProjectItem is not null && _removeXmlDocumentationLogic.CanRemoveXmlDocProjectItem(activeDoc.ProjectItem))
        {
            return new[] { activeDoc.ProjectItem };
        }

        return Enumerable.Empty<ProjectItem>();
    }

    /// <summary>
    /// project items from the specified sequence, filtering out duplicates by comparing file paths case-insensitively.
    /// </summary>
    /// <param name="items">The collection of items.</param>
    /// <returns>A collection of ienumerable items.</returns>
    private static IEnumerable<ProjectItem> DistinctByFilePath(IEnumerable<ProjectItem> items)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var path = item.GetFileName();
            if (string.IsNullOrWhiteSpace(path) || seen.Add(path))
            {
                yield return item;
            }
        }
    }
}
