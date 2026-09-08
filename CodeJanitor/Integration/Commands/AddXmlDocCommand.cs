using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Ai;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.CleanupProgress;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.Integration.Commands;

/// <summary>
/// A command that generates XML documentation comments for C# methods in the selected scope or active document.
/// </summary>
internal sealed class AddXmlDocCommand : BaseCommand
{
    /// <summary>
    /// The large scope warning threshold.
    /// </summary>
    private const int LargeScopeWarningThreshold = 50;

    /// <summary>
    /// The very large scope warning threshold.
    /// </summary>
    private const int VeryLargeScopeWarningThreshold = 200;

    private readonly AiXmlDocumentationLogic _aiXmlDocumentationLogic;
    private readonly CodeCleanupAvailabilityLogic _codeCleanupAvailabilityLogic;

    internal AddXmlDocCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorAddXmlDoc)
    {
        _aiXmlDocumentationLogic = AiXmlDocumentationLogic.GetInstance(Package);
        _codeCleanupAvailabilityLogic = CodeCleanupAvailabilityLogic.GetInstance(Package);
    }

    /// <summary>
    /// Gets or sets the instance.
    /// </summary>
    public static AddXmlDocCommand Instance { get; private set; }

    /// <summary>
    /// Initializes a singleton instance of this command and monitors the Feature_AddXmlDoc setting.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>A task.</returns>
    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new AddXmlDocCommand(package);
        await package.SettingsMonitor.WatchAsync(s => s.Feature_AddXmlDoc, Instance.SwitchAsync);
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
                            "CodeJanitor Add XMLDoc",
                            MessageBoxButton.OK, MessageBoxImage.Warning);

            return;
        }

        if (!Settings.Default.Cleaning_AiXmlDocumentationEnabled)
        {
            MessageBox.Show("AI-assisted XML documentation is disabled. You can enable and configure it in CodeJanitor Options -> Cleaning -> Update.",
                            "CodeJanitor Add XMLDoc",
                            MessageBoxButton.OK, MessageBoxImage.Warning);

            return;
        }

        if (!AiXmlDocumentationLogic.IsConfigurationPresent())
        {
            MessageBox.Show("AI XML documentation endpoint URL or API key is not configured. Please configure them in CodeJanitor Options -> Cleaning -> Update.",
                            "CodeJanitor Add XMLDoc",
                            MessageBoxButton.OK, MessageBoxImage.Warning);

            return;
        }

        var projectItems = GetScopeProjectItems().ToList();
        if (projectItems.Count == 0)
        {
            MessageBox.Show("No C# files found in current scope.",
                            "CodeJanitor Add XMLDoc",
                            MessageBoxButton.OK, MessageBoxImage.Information);

            return;
        }

        if (!ConfirmScope(projectItems.Count))
        {
            return;
        }

        using (new ActiveDocumentRestorer(Package))
        {
            var viewModel = new XmlDocProgressViewModel(Package, projectItems);
            var window = new CleanupProgressWindow { DataContext = viewModel };

            window.ShowModal();
        }
    }

    /// <summary>
    /// Shows a modal Yes/No confirmation dialog with wording scaled by file count thresholds and returns true only if the user clicks Yes.
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
            ? $"You are about to run Add XMLDoc on {fileCount:N0} files. This may make many AI API requests and take significant time. Continue?"
            : fileCount > LargeScopeWarningThreshold
                ? $"You are about to run Add XMLDoc on {fileCount:N0} files. Continue?"
                : $"Run Add XMLDoc on {fileCount:N0} files?";

        return MessageBox.Show(message,
                               "CodeJanitor Add XMLDoc Confirmation",
                               MessageBoxButton.YesNo,
                               MessageBoxImage.Question,
                               MessageBoxResult.No)
               == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Returns distinct project items from selected UI hierarchy roots that pass XML documentation checks,
    /// prioritizing Solution Explorer selection (Solution, Solution Folder, Project, Folder, File),
    /// or falling back to the active document when editing.
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
            if (_aiXmlDocumentationLogic.CanDocumentProjectItem(singleProjectItem))
            {
                return new[] { singleProjectItem };
            }
        }

        var selectedProjectItems = selectedScopeRoots
            .SelectMany(SolutionHelper.GetItemsRecursively<ProjectItem>)
            .Where(projectItem => _aiXmlDocumentationLogic.CanDocumentProjectItem(projectItem));

        var selectedScopedDistinct = DistinctByFilePath(selectedProjectItems).ToList();
        if (selectedScopedDistinct.Count > 0)
        {
            return selectedScopedDistinct;
        }

        // 2. Fallback to active document if editing
        var activeDoc = Package.ActiveDocument;
        if (activeDoc?.ProjectItem is not null && _aiXmlDocumentationLogic.CanDocumentProjectItem(activeDoc.ProjectItem))
        {
            return new[] { activeDoc.ProjectItem };
        }

        return Enumerable.Empty<ProjectItem>();
    }

    /// <summary>
    /// Returns ProjectItems in encounter order, skipping duplicates by file path.
    /// </summary>
    /// <param name="projectItems">The project items.</param>
    /// <returns>Distinct project items by file path.</returns>
    private static IEnumerable<ProjectItem> DistinctByFilePath(IEnumerable<ProjectItem> projectItems)
    {
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var projectItem in projectItems)
        {
            var filePath = projectItem.GetFileName();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            if (seenPaths.Add(filePath))
            {
                yield return projectItem;
            }
        }
    }
}
