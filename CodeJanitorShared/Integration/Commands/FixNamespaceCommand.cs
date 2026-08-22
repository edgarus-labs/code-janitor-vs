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
/// A command that fixes the namespace of the active C# document.
/// </summary>

internal sealed class FixNamespaceCommand : BaseCommand
{
    private const int LargeScopeWarningThreshold = 2000;
    private const int VeryLargeScopeWarningThreshold = 10000;

    private readonly NamespaceFixerLogic _namespaceFixerLogic;
    private readonly CodeCleanupAvailabilityLogic _codeCleanupAvailabilityLogic;

    internal FixNamespaceCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorFixNamespace)
    {
        _namespaceFixerLogic = NamespaceFixerLogic.GetInstance(Package);
        _codeCleanupAvailabilityLogic = CodeCleanupAvailabilityLogic.GetInstance(Package);
    }

    /// <summary>
    /// Gets or sets the instance.
    /// </summary>
    public static FixNamespaceCommand Instance { get; private set; }

    /// <summary>
    /// Initializes the static Instance with a new FixNamespaceCommand for the given package and completes synchronously, mutating global state without performing any actual asynchronous work.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>A Task value produced by this method.</returns>

    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new FixNamespaceCommand(package);
        await Instance.SwitchAsync(true);
    }

    /// <summary>
    /// This method updates the command&apos;s Enabled state to true when the solution is open or the active document is C# code, and it enforces execution on the UI thread via ThreadHelper.ThrowIfNotOnUIThread().
    /// </summary>

    protected override void OnBeforeQueryStatus()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Enabled = Package.IDE.Solution.IsOpen || (Package.ActiveDocument != null && Package.ActiveDocument.GetCodeLanguage() == CodeLanguage.CSharp);
    }

    /// <summary>
    /// This method ensures it runs on the UI thread, validates that cleanup is available and that C# files exist in scope, prompts the user for confirmation, then iterates through project items fixing namespaces while updating the status bar, and finally shows a summary message with the count of changed files.
    /// </summary>

    protected override void OnExecute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnExecute();

        if (!_codeCleanupAvailabilityLogic.IsCleanupEnvironmentAvailable())
        {
            MessageBox.Show(Resources.CleanupCannotRunWhileDebugging,
                            "CodeJanitor Fix Namespace",
                            MessageBoxButton.OK, MessageBoxImage.Warning);

            return;
        }

        var projectItems = GetScopeProjectItems().ToList();
        if (projectItems.Count == 0)
        {
            MessageBox.Show("No C# files found in current scope.",
                            "CodeJanitor Fix Namespace",
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

                if (projectItem != null)
                {
                    Package.IDE.StatusBar.Text = $"CodeJanitor fixing namespace {current}/{totalCount}: {projectItem.Name}";
                }

                if (_namespaceFixerLogic.FixNamespace(projectItem))
                {
                    changedCount++;
                }
            }
        }

        Package.IDE.StatusBar.Text = $"CodeJanitor Fix Namespace completed: changed {changedCount} of {projectItems.Count} file(s).";
        MessageBox.Show($"Processed {projectItems.Count} file(s). Changed {changedCount} file(s).",
                        "CodeJanitor Fix Namespace",
                        MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Shows a modal Yes/No confirmation dialog with wording scaled by file count thresholds and returns true only if the user clicks Yes.
    /// </summary>
    /// <param name="fileCount">The file count.</param>
    /// <returns>A bool value produced by this method.</returns>

    private bool ConfirmScope(int fileCount)
    {
        var message = fileCount > VeryLargeScopeWarningThreshold
            ? $"You are about to run Fix Namespace on {fileCount:N0} files. This may take a long time and impact Visual Studio responsiveness. Continue?"
            : fileCount > LargeScopeWarningThreshold
                ? $"You are about to run Fix Namespace on {fileCount:N0} files. Continue?"
                : fileCount > 1
                    ? $"Run Fix Namespace on {fileCount:N0} files?"
                    : "Run Fix Namespace on the selected file?";

        return MessageBox.Show(message,
                               "CodeJanitor Fix Namespace Confirmation",
                               MessageBoxButton.YesNo,
                               MessageBoxImage.Question,
                               MessageBoxResult.No)
               == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Returns distinct project items from selected UI hierarchy roots that pass namespace fixer logic, falling back to the active document&apos;s project item if selection empty, and otherwise returns an empty sequence while requiring the UI thread and accessing Package state.
    /// </summary>
    /// <returns>A IEnumerable&lt;ProjectItem&gt; value produced by this method.</returns>

    private IEnumerable<ProjectItem> GetScopeProjectItems()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var selectedScopeRoots = UIHierarchyHelper.GetSelectedUIHierarchyItems(Package)
            .Select(item => item.Object)
            .Where(item => item != null)
            .ToList();

        var selectedProjectItems = selectedScopeRoots
            .SelectMany(SolutionHelper.GetItemsRecursively<ProjectItem>)
            .Where(projectItem => _namespaceFixerLogic.CanFixNamespaceProjectItem(projectItem));

        var selectedScopedDistinct = DistinctByFilePath(selectedProjectItems).ToList();
        if (selectedScopedDistinct.Count > 0)
        {
            return selectedScopedDistinct;
        }

        var activeDocument = Package.ActiveDocument;
        if (activeDocument?.ProjectItem != null && _namespaceFixerLogic.CanFixNamespaceProjectItem(activeDocument.ProjectItem))
        {
            return new[] { activeDocument.ProjectItem };
        }

        return Enumerable.Empty<ProjectItem>();
    }

    /// <summary>
    /// Returns ProjectItems in encounter order, skipping those with null or whitespace file paths and yielding only the first item for each case-insensitively unique file path while maintaining lazy enumeration with no detected side effects or thrown exceptions.
    /// </summary>
    /// <param name="projectItems">The project items.</param>
    /// <returns>A IEnumerable&lt;ProjectItem&gt; value produced by this method.</returns>

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
