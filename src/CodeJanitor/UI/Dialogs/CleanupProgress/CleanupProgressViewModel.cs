using Microsoft.VisualStudio.Shell;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Helpers;
using CodeJanitor.Properties;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System;

namespace CodeJanitor.UI.Dialogs.CleanupProgress;

/// <summary>
/// The view model representing the state and commands available for cleanup progress.
/// </summary>
public sealed class CleanupProgressViewModel : BaseProgressViewModel
{
    private readonly CodeJanitorPackage _package;
    private readonly BackgroundWorker _backgroundWorker;
    private readonly Stopwatch _batchStopwatch;

    /// <summary>
    /// the progress state for an ongoing operation, tracking the target file name along with the number of completed and total work items.
    /// </summary>
    private sealed class ProgressReportState
    {
        /// <summary>
        /// Gets or sets the file name.
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Gets or sets the completed.
        /// </summary>
        public int Completed { get; set; }

        /// <summary>
        /// Gets or sets the total.
        /// </summary>
        public int Total { get; set; }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CleanupProgressViewModel" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <param name="items">The items to cleanup.</param>
    public CleanupProgressViewModel(CodeJanitorPackage package, IEnumerable<object> items)
    {
        _package = package;
        CodeCleanupManager = CodeCleanupManager.GetInstance(package);
        CodeCleanupManager.ResetCleanupExecutionStats();
        CodeCleanupManager.SetCurrentBatchDisqualifiedTypes(
            CodeCleanupManager.DiscoverSolutionDisqualifiedTypes(package));
        _batchStopwatch = Stopwatch.StartNew();

        var cleanupItems = items.ToList();

        // Initialize UI elements.
        CountTotal = cleanupItems.Count;
        ProcessedCount = 0;
        UpdateExecutionSummary();

        // Initialize background worker.
        _backgroundWorker = new BackgroundWorker
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true
        };

        _backgroundWorker.DoWork += backgroundWorker_DoWork;
        _backgroundWorker.ProgressChanged += backgroundWorker_ProgressChanged;
        _backgroundWorker.RunWorkerCompleted += backgroundWorker_RunWorkerCompleted;

        _backgroundWorker.RunWorkerAsync(cleanupItems);
    }

    /// <summary>
    /// Gets or sets the code cleanup manager.
    /// </summary>
    private CodeCleanupManager CodeCleanupManager { get; set; }

    /// <summary>
    /// Called when the <see cref="CancelCommand" /> is executed.
    /// </summary>
    /// <param name="parameter">The command parameter.</param>
    protected override void OnCancelCommandExecuted(object parameter)
    {
        IsCanceling = true;
        CancelCommand.RaiseCanExecuteChanged();

        _backgroundWorker.CancelAsync();
    }

    /// <summary>
    /// Handles the DoWork event of the backgroundWorker control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">
    /// The <see cref="System.ComponentModel.DoWorkEventArgs" /> instance containing the event data.
    /// </param>
    private void backgroundWorker_DoWork(object sender, DoWorkEventArgs e)
    {
        var bw = (BackgroundWorker)sender;
        var items = ((IEnumerable<object>)e.Argument).ToList();

        int totalCount = items.Count;
        int completedCount = 0;

        bool enableParallel = Settings.Default.Cleaning_EnableParallelCleanup;
        int maxDegree = Settings.Default.Cleaning_MaxDegreeOfParallelism > 0
            ? Settings.Default.Cleaning_MaxDegreeOfParallelism
            : Math.Max(1, Environment.ProcessorCount);

        // Check if all items are ProjectItems and editor cleanup is not required, enabling full parallel mode
        if (enableParallel && items.All(item => item is EnvDTE.ProjectItem) && !CodeCleanupManager.RequiresEditorCleanupForCSharp())
        {
            var projectItems = items.Cast<EnvDTE.ProjectItem>().ToList();
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxDegree
            };

            var workItems = ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                return projectItems.Select(CreateWorkItem).ToList();
            });

            var (parallelItems, sequentialItems) = CleanupBatchPartitioner.Partition(workItems, workItem => workItem.FilePath, workItem => workItem.IsOpen);
            totalCount = parallelItems.Count + sequentialItems.Count;

            try
            {
                Parallel.ForEach(parallelItems, parallelOptions, (workItem, loopState) =>
                {
                    if (bw.CancellationPending)
                    {
                        loopState.Stop();

                        return;
                    }

                    var fileName = workItem.FileName;

                    bw.ReportProgress(0, new ProgressReportState { FileName = fileName, Completed = completedCount, Total = totalCount });

                    try
                    {
                        var outcome = CodeCleanupManager.TryRunHeadlessPreCleanupForCSharpCore(workItem.FilePath, CodeCleanupManager.GetCurrentBatchDisqualifiedTypes());
                        if (outcome.Result == CodeCleanupManager.HeadlessCleanupResult.Changed)
                        {
                            CodeCleanupManager.IncrementHeadlessChanged();
                        }
                        else
                        {
                            CodeCleanupManager.IncrementHeadlessNoOp();
                        }

                        if (outcome.SplitOperationOccurred && outcome.CreatedFiles.Count > 0)
                        {
                            CodeCleanupManager.RecordSplitOperation(outcome.CreatedFiles.Count);
                            ThreadHelper.JoinableTaskFactory.Run(async () =>
                            {
                                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                                foreach (var createdFile in outcome.CreatedFiles)
                                {
                                    CodeCleanupManager.AddGeneratedFileToProject(workItem.ProjectItem, createdFile);
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        CodeCleanupManager.RecordCleanupFailure(workItem.FilePath, ex);
                    }

                    var currentCompleted = Interlocked.Increment(ref completedCount);
                    bw.ReportProgress(0, new ProgressReportState { FileName = fileName, Completed = currentCompleted, Total = totalCount });
                });
            }
            catch (OperationCanceledException)
            {
                e.Cancel = true;

                return;
            }

            if (bw.CancellationPending)
            {
                e.Cancel = true;

                return;
            }

            foreach (var workItem in parallelItems)
            {
                if (bw.CancellationPending)
                {
                    e.Cancel = true;

                    return;
                }

                ThreadHelper.JoinableTaskFactory.Run(async delegate
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    try
                    {
                        await CodeCleanupManager.RunDiagnosticCleanupAsync(workItem.ProjectItem);
                    }
                    catch (Exception ex)
                    {
                        CodeCleanupManager.RecordCleanupFailure(workItem.FilePath, ex);
                    }
                });
            }

            foreach (var workItem in sequentialItems)
            {
                if (bw.CancellationPending)
                {
                    e.Cancel = true;

                    return;
                }

                bw.ReportProgress(0, new ProgressReportState { FileName = workItem.FileName, Completed = completedCount, Total = totalCount });

                ThreadHelper.JoinableTaskFactory.Run(async delegate
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    try
                    {
                        await CodeCleanupManager.CleanupAsync(workItem.ProjectItem);
                    }
                    catch (Exception ex)
                    {
                        CodeCleanupManager.RecordCleanupFailure(workItem.FilePath ?? workItem.FileName ?? "Unknown", ex);
                    }
                });

                completedCount++;
                bw.ReportProgress(0, new ProgressReportState { FileName = workItem.FileName, Completed = completedCount, Total = totalCount });
            }

            return;
        }

        // Sequential / Hybrid fallback path (for mixed items or when editor-bound cleanup like ReSharper / format doc is active)
        foreach (dynamic item in items)
        {
            if (bw.CancellationPending)
            {
                e.Cancel = true;
                break;
            }

            string itemName = null;
            try
            {
                itemName = item.Name;
            }
            catch
            {
            }

            bw.ReportProgress(0, new ProgressReportState { FileName = itemName, Completed = completedCount, Total = totalCount });

            ThreadHelper.JoinableTaskFactory.Run(async delegate
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                try
                {
                    if (item is EnvDTE.ProjectItem projectItem)
                    {
                        await CodeCleanupManager.CleanupAsync(projectItem);
                    }
                    else
                    {
                        CodeCleanupManager.Cleanup(item);
                    }
                }
                catch (Exception ex)
                {
                    CodeCleanupManager.RecordCleanupFailure(itemName ?? "Unknown", ex);
                }

                var currentCompleted = Interlocked.Increment(ref completedCount);
                bw.ReportProgress(0, new ProgressReportState { FileName = itemName, Completed = currentCompleted, Total = totalCount });
            });
        }
    }

    /// <summary>
    /// Handles the ProgressChanged event of the backgroundWorker control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">
    /// The <see cref="System.ComponentModel.ProgressChangedEventArgs" /> instance containing
    /// the event data.
    /// </param>
    private void backgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
    {
        if (e.UserState is ProgressReportState state)
        {
            if (!string.IsNullOrEmpty(state.FileName))
            {
                CurrentFileName = state.FileName;
            }

            CountTotal = state.Total;
            ProcessedCount = Math.Min(state.Total, state.Completed);
            CountProgress = ProcessedCount;
        }

        UpdateExecutionSummary();
    }

    /// <summary>
    /// Handles the RunWorkerCompleted event of the backgroundWorker control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">
    /// The <see cref="System.ComponentModel.RunWorkerCompletedEventArgs" /> instance containing
    /// the event data.
    /// </param>
    private void backgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
    {
        // Clear the batch-scoped solution-wide disqualified types first, before any other
        // logic that could throw, so a later standalone single-document cleanup (e.g.
        // cleanup-on-save) never reuses a stale set from this completed batch.
        CodeCleanupManager.SetCurrentBatchDisqualifiedTypes(null);

        _batchStopwatch.Stop();
        ProcessedCount = CountTotal;
        UpdateExecutionSummary();

        var stats = CodeCleanupManager.GetCleanupExecutionStats();
        var counts = $"headlessChanged={stats.HeadlessChangedItems}, headlessNoOp={stats.HeadlessNoOpItems}, editor={stats.EditorItems}, failed={stats.FailedItems}, splitOps={stats.SplitOperations}, splitFiles={stats.SplitCreatedFiles}, diagnosticChanged={stats.DiagnosticChangedItems}, diagnosticUnresolved={stats.DiagnosticUnresolvedItems}, elapsedMs={_batchStopwatch.ElapsedMilliseconds}";

        if (e.Error is not null)
        {
            OutputWindowHelper.WarningWriteLine($"Cleanup batch failed after {counts}.");
            MessageBox.Show(e.Error.Message, "CodeJanitor Cleanup Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else if (e.Cancelled)
        {
            OutputWindowHelper.InfoWriteLine($"Cleanup batch canceled. Processed: {counts}.");
        }
        else if (stats.FailedItems > 0)
        {
            OutputWindowHelper.WarningWriteLine($"Cleanup batch completed with failures. Processed: {counts}.");
            MessageBox.Show($"Cleanup completed with {stats.FailedItems} failed item(s). Please check the CodeJanitor output window for details.", "CodeJanitor Cleanup Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else if (stats.DiagnosticUnresolvedItems > 0)
        {
            OutputWindowHelper.WarningWriteLine($"Cleanup batch completed with unresolved diagnostics. Processed: {counts}.");
            MessageBox.Show($"Cleanup completed, but {stats.DiagnosticUnresolvedItems} file(s) still have diagnostics that could not be fixed automatically. Please check the CodeJanitor output window for details.", "CodeJanitor Cleanup Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            OutputWindowHelper.InfoWriteLine($"Cleanup batch completed. Processed: {counts}.");
        }

        // Run post-cleanup build verification only when the batch completed cleanly
        // (not canceled, no worker error, no per-file failures) and Visual Studio's
        // build context is available.
        if (!e.Cancelled && e.Error is null && stats.FailedItems == 0 &&
            _package?.IDE?.Solution?.SolutionBuild != null &&
            (stats.HeadlessChangedItems > 0 || stats.DiagnosticChangedItems > 0))
        {
            try
            {
                OutputWindowHelper.InfoWriteLine("Running post-cleanup build verification...");
                _package.IDE.Solution.SolutionBuild.Build(true);
                if (_package.IDE.Solution.SolutionBuild.LastBuildInfo > 0)
                {
                    OutputWindowHelper.WarningWriteLine(
                        $"Post-cleanup build verification reported {_package.IDE.Solution.SolutionBuild.LastBuildInfo} failed project(s).");
                }
                else
                {
                    OutputWindowHelper.InfoWriteLine("Post-cleanup build verification passed: solution compiled successfully.");
                }
            }
            catch (Exception ex)
            {
                OutputWindowHelper.WarningWriteLine($"Post-cleanup build verification could not be executed: {ex.Message}");
            }
        }
        DialogResult = true;
    }

    private static WorkItem CreateWorkItem(EnvDTE.ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            return new WorkItem(
                projectItem,
                projectItem.Name,
                projectItem.GetFileName(),
                projectItem.IsOpen[EnvDTE.Constants.vsViewKindTextView] || projectItem.IsOpen[EnvDTE.Constants.vsViewKindCode]);
        }
        catch (Exception ex)
        {
            OutputWindowHelper.ExceptionWriteLine("Unable to read a project item for cleanup", ex);

            return new WorkItem(projectItem, null, null, false);
        }
    }

    private sealed class WorkItem
    {
        public WorkItem(EnvDTE.ProjectItem projectItem, string fileName, string filePath, bool isOpen)
        {
            ProjectItem = projectItem;
            FileName = fileName;
            FilePath = filePath;
            IsOpen = isOpen;
        }

        public EnvDTE.ProjectItem ProjectItem { get; }

        public string FileName { get; }

        public string FilePath { get; }

        public bool IsOpen { get; }
    }

    /// <summary>
    /// Updates the execution summary displayed in the progress dialog.
    /// </summary>
    private void UpdateExecutionSummary()
    {
        var stats = CodeCleanupManager.GetCleanupExecutionStats();
        ExecutionSummary = $"Changed: {stats.HeadlessChangedItems} | No-op: {stats.HeadlessNoOpItems} | Editor: {stats.EditorItems} | Failed: {stats.FailedItems} | Split: {stats.SplitOperations} ops / {stats.SplitCreatedFiles} files | Diagnostics: {stats.DiagnosticChangedItems} fixed / {stats.DiagnosticUnresolvedItems} unresolved";

        ElapsedSummary = $"Processed: {ProcessedCount}/{CountTotal} | Elapsed: {_batchStopwatch.Elapsed:mm\\:ss}";
    }
}
