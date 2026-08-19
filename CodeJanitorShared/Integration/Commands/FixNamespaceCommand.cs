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

    public static FixNamespaceCommand Instance { get; private set; }

    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new FixNamespaceCommand(package);
        await Task.CompletedTask;
    }

    protected override void OnBeforeQueryStatus()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Enabled = Package.IDE.Solution.IsOpen || (Package.ActiveDocument != null && Package.ActiveDocument.GetCodeLanguage() == CodeLanguage.CSharp);
    }

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