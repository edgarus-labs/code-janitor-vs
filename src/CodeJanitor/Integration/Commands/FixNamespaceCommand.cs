using CodeJanitor.Helpers;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.Integration.Commands;

/// <summary>
/// A command that makes the namespaces of the C# files in scope match their folders through Roslyn's IDE0130 analyzer
/// and code fix (see <see cref="EditorConfigDiagnosticCleanupLogic.FixNamespaceAsync" />).
/// </summary>
internal sealed class FixNamespaceCommand : BaseCommand
{
    /// <summary>
    /// The large scope warning threshold.
    /// </summary>
    private const int LargeScopeWarningThreshold = 2000;

    /// <summary>
    /// The very large scope warning threshold.
    /// </summary>
    private const int VeryLargeScopeWarningThreshold = 10000;

    private readonly EditorConfigDiagnosticCleanupLogic _diagnosticCleanupLogic;
    private readonly CodeCleanupAvailabilityLogic _codeCleanupAvailabilityLogic;

    internal FixNamespaceCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorFixNamespace)
    {
        _diagnosticCleanupLogic = EditorConfigDiagnosticCleanupLogic.GetInstance(Package);
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
        Enabled = Package.IDE.Solution.IsOpen || (Package.ActiveDocument is not null && Package.ActiveDocument.GetCodeLanguage() == CodeLanguage.CSharp);
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
        var unresolvedCount = 0;
        var failedCount = 0;

        using (new ActiveDocumentRestorer(Package))
        {
            var totalCount = projectItems.Count;
            var current = 0;

            foreach (var projectItem in projectItems)
            {
                current++;

                Package.IDE.StatusBar.Text = $"CodeJanitor fixing namespace {current}/{totalCount}: {projectItem.Name}";

                var outcome = ThreadHelper.JoinableTaskFactory.Run(() => _diagnosticCleanupLogic.FixNamespaceAsync(projectItem));
                if (outcome.Failure is not null)
                {
                    OutputWindowHelper.ExceptionWriteLine($"Fix Namespace failed for '{projectItem.GetFileName()}'", outcome.Failure);
                    failedCount++;
                    continue;
                }

                if (outcome.Changed)
                {
                    changedCount++;
                }

                unresolvedCount += outcome.UnresolvedCount;
            }
        }

        var problems = failedCount > 0 || unresolvedCount > 0;
        Package.IDE.StatusBar.Text = $"CodeJanitor Fix Namespace completed: changed {changedCount} of {projectItems.Count} file(s), {failedCount} failed, {unresolvedCount} namespace(s) left unchanged.";
        MessageBox.Show(
            $"Processed {projectItems.Count} file(s). Changed {changedCount} file(s)."
                + (unresolvedCount == 0 ? string.Empty : $" {unresolvedCount} namespace(s) could not be fixed; see the CodeJanitor output pane.")
                + (failedCount == 0 ? string.Empty : $" {failedCount} file(s) failed; see the CodeJanitor output pane."),
            "CodeJanitor Fix Namespace",
            MessageBoxButton.OK,
            problems ? MessageBoxImage.Warning : MessageBoxImage.Information);
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
    /// Returns distinct C# project items (see <see cref="CanFixNamespace" />) from selected UI hierarchy roots, falling back to the active document&apos;s project item if selection empty, and otherwise returns an empty sequence while requiring the UI thread and accessing Package state.
    /// </summary>
    /// <returns>A IEnumerable&lt;ProjectItem&gt; value produced by this method.</returns>
    private IEnumerable<ProjectItem> GetScopeProjectItems()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // 1. If the active window is a document editor, prioritize the active document
        var activeWindow = Package.IDE.ActiveWindow;
        if (activeWindow is not null && activeWindow.Type == vsWindowType.vsWindowTypeDocument)
        {
            var activeDoc = Package.ActiveDocument;
            if (activeDoc?.ProjectItem is not null && CanFixNamespace(activeDoc.ProjectItem))
            {
                return new[] { activeDoc.ProjectItem };
            }
        }

        // 2. Otherwise, check selection in Solution Explorer
        var selectedScopeRoots = UIHierarchyHelper.GetSelectedUIHierarchyItems(Package)
            .Select(item => item.Object)
            .Where(item => item is not null)
            .ToList();

        // If a single ProjectItem (file) is selected in Solution Explorer
        if (selectedScopeRoots.Count == 1 && selectedScopeRoots[0] is ProjectItem singleProjectItem)
        {
            if (CanFixNamespace(singleProjectItem))
            {
                return new[] { singleProjectItem };
            }
        }

        var selectedProjectItems = selectedScopeRoots
            .SelectMany(SolutionHelper.GetItemsRecursively<ProjectItem>)
            .Where(CanFixNamespace);

        var selectedScopedDistinct = DistinctByFilePath(selectedProjectItems).ToList();
        if (selectedScopedDistinct.Count > 0)
        {
            return selectedScopedDistinct;
        }

        // 3. Fallback to active document if any
        var fallbackDoc = Package.ActiveDocument;
        if (fallbackDoc?.ProjectItem is not null && CanFixNamespace(fallbackDoc.ProjectItem))
        {
            return new[] { fallbackDoc.ProjectItem };
        }

        return Enumerable.Empty<ProjectItem>();
    }

    /// <summary>
    /// Determines whether the namespace of a project item can be fixed: a physical .cs file outside the bin, obj and
    /// generated folders.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>True when the namespace can be fixed.</returns>
    private static bool CanFixNamespace(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (projectItem is null || !projectItem.IsPhysicalFile() || !string.Equals(Path.GetExtension(projectItem.Name), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var filePath = projectItem.GetFileName();

        return !string.IsNullOrWhiteSpace(filePath) && !NamespacePathHelper.IsInExcludedDirectory(filePath);
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
