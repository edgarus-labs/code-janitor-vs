using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Logic.SourceControl;
using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.CleanupProgress;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.Integration.Commands
{
    /// <summary>
    /// A command that provides for cleaning up code only in files reported as changed by git
    /// (see ADR-0007 / BL-017).
    /// </summary>
    internal sealed class CleanupChangedFilesCommand : BaseCommand
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CleanupChangedFilesCommand" /> class.
        /// </summary>
        /// <param name="package">The hosting package.</param>
        internal CleanupChangedFilesCommand(CodeJanitorPackage package)
            : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorCleanupChangedFiles)
        {
            CodeCleanupAvailabilityLogic = CodeCleanupAvailabilityLogic.GetInstance(Package);
            ChangedFilesProvider = new GitChangedFilesProvider(new ProcessRunner(), new GitStatusParser());
        }

        /// <summary>
        /// A singleton instance of this command.
        /// </summary>
        public static CleanupChangedFilesCommand Instance { get; private set; }

        /// <summary>
        /// Gets the code cleanup availability logic.
        /// </summary>
        private CodeCleanupAvailabilityLogic CodeCleanupAvailabilityLogic { get; }

        /// <summary>
        /// Gets the provider used to determine which files have changed according to git.
        /// </summary>
        private IChangedFilesProvider ChangedFilesProvider { get; }

        /// <summary>
        /// Gets the list of all project items in the solution eligible for cleanup.
        /// </summary>
        private IEnumerable<ProjectItem> AllProjectItems
            => SolutionHelper.GetAllItemsInSolution<ProjectItem>(Package.IDE.Solution).Where(x => CodeCleanupAvailabilityLogic.CanCleanupProjectItem(x));

        /// <summary>
        /// Initializes a singleton instance of this command.
        /// </summary>
        /// <param name="package">The hosting package.</param>
        /// <returns>A task.</returns>
        public static async Task InitializeAsync(CodeJanitorPackage package)
        {
            Instance = new CleanupChangedFilesCommand(package);
            await package.SettingsMonitor.WatchAsync(s => s.Feature_CleanupChangedFiles, Instance.SwitchAsync);
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
                                Resources.CodeJanitorCleanupChangedFiles,
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var solutionDirectory = GetSolutionDirectory();
            var changedFiles = solutionDirectory != null
                ? new HashSet<string>(ChangedFilesProvider.GetChangedFiles(solutionDirectory), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var projectItemsToClean = changedFiles.Count == 0
                ? new List<ProjectItem>()
                : AllProjectItems.Where(x => IsChangedFile(x, changedFiles)).ToList();

            if (projectItemsToClean.Count == 0)
            {
                MessageBox.Show(Resources.NoChangedFilesFoundAccordingToGitStatus,
                                Resources.CodeJanitorCleanupChangedFiles,
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(string.Format(Resources.AreYouReadyForCodeJanitorToCleanNChangedFiles, projectItemsToClean.Count),
                                Resources.CodeJanitorConfirmationForCleanupChangedFiles,
                                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
                    == MessageBoxResult.Yes)
            {
                using (new ActiveDocumentRestorer(Package))
                {
                    var viewModel = new CleanupProgressViewModel(Package, projectItemsToClean);
                    var window = new CleanupProgressWindow { DataContext = viewModel };

                    window.ShowModal();
                }
            }
        }

        /// <summary>
        /// Gets the directory of the currently open solution, or null when no solution is open.
        /// </summary>
        private string GetSolutionDirectory()
        {
            var solutionFullName = Package.IDE.Solution?.FullName;
            return string.IsNullOrEmpty(solutionFullName) ? null : Path.GetDirectoryName(solutionFullName);
        }

        /// <summary>
        /// Determines if the specified project item corresponds to one of the changed file paths.
        /// </summary>
        /// <param name="projectItem">The project item.</param>
        /// <param name="changedFiles">The set of changed absolute file paths.</param>
        /// <returns>True if the project item is a changed file, otherwise false.</returns>
        private static bool IsChangedFile(ProjectItem projectItem, HashSet<string> changedFiles)
        {
            for (short i = 1; i <= projectItem.FileCount; i++)
            {
                var fileName = projectItem.FileNames[i];
                if (!string.IsNullOrEmpty(fileName) && changedFiles.Contains(fileName))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
