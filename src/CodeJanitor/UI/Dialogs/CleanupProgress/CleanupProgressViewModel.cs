using CodeJanitor.Helpers;
using CodeJanitor.Logic.Ai;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace CodeJanitor.UI.Dialogs.CleanupProgress;

/// <summary>
/// The view model representing the state and commands available for cleanup progress.
/// </summary>
public sealed class CleanupProgressViewModel : BaseProgressViewModel
{
    private readonly CodeJanitorPackage _package;
    private readonly Action<string, string, MessageBoxImage> _showMessage;

    /// <summary>
    /// Projects whose files are analyzed at the same time; each one holds a project compilation in memory.
    /// </summary>
    private const int MaxParallelProjects = 4;

    private readonly Stopwatch _batchStopwatch;

    /// <summary>
    /// Refreshes the elapsed time and the counts every second, independently of the cleanup progress reports.
    /// </summary>
    private readonly DispatcherTimer _refreshTimer;

    /// <summary>
    /// Canceled by the Cancel button, to stop the batch and the semantic steps of the current file.
    /// </summary>
    private readonly CancellationTokenSource _cancellationSource = new CancellationTokenSource();

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
        : this(package, items, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CleanupProgressViewModel" /> class that reports the outcome of the
    /// batch through <paramref name="showMessage" /> instead of a modal message box.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <param name="items">The items to cleanup.</param>
    /// <param name="showMessage">Shows the message of the batch outcome, or null to show a message box.</param>
    internal CleanupProgressViewModel(CodeJanitorPackage package, IEnumerable<object> items, Action<string> showMessage)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _package = package;
        _showMessage = showMessage is null ? ShowMessage : (message, _, _) => showMessage(message);
        CodeCleanupManager = CodeCleanupManager.GetInstance(package);
        CodeCleanupManager.ResetCleanupExecutionStats();
        _batchStopwatch = Stopwatch.StartNew();

        var cleanupItems = items.ToList();

        // Initialize UI elements.
        CountTotal = cleanupItems.Count;
        ProcessedCount = 0;
        UpdateExecutionSummary();
        _refreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => UpdateExecutionSummary(), Dispatcher.CurrentDispatcher);

        // The batch runs on the thread pool; UI work goes through the joinable task factory.
        _ = RunBatchAsync(cleanupItems);
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

        _cancellationSource.Cancel();
        AiXmlDocumentationLogic.CancelRun();
    }

    /// <summary>
    /// Runs the batch on the thread pool and completes it on the UI thread.
    /// </summary>
    /// <param name="items">The items to cleanup.</param>
    private async Task RunBatchAsync(List<object> items)
    {
        // AI XML documentation run during cleanup is canceled with the batch.
        AiXmlDocumentationLogic.BeginRun();
        Exception error = null;
        var canceled = false;
        try
        {
            canceled = !await Task.Run(() => CleanupItemsAsync(items));
        }
        catch (Exception ex)
        {
            error = ex;
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        OnBatchCompleted(error, canceled);
    }

    /// <summary>
    /// Posts a progress update to the UI thread without waiting for it.
    /// </summary>
    /// <param name="state">The progress state.</param>
    private void ReportProgress(ProgressReportState state) => _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                                                                   {
                                                                       await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                                                                       OnProgressChanged(state);
                                                                   });

    /// <summary>
    /// Runs <paramref name="action" /> on the UI thread through the joinable task factory, so it is also serviced while
    /// the UI thread waits in a joinable task, and returns to the thread pool afterwards.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="action">The UI-thread work.</param>
    /// <returns>The result of <paramref name="action" />.</returns>
    private static async Task<T> RunOnMainThreadAsync<T>(Func<T> action)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        try
        {
            return action();
        }
        finally
        {
            await TaskScheduler.Default;
        }
    }

    /// <summary>
    /// Cleans up the items on a thread pool thread.
    /// </summary>
    /// <param name="items">The items to cleanup.</param>
    /// <returns>False when the batch was canceled.</returns>
    private async Task<bool> CleanupItemsAsync(List<object> items)
    {
        int totalCount = items.Count;
        int completedCount = 0;
        var cancellationToken = _cancellationSource.Token;

        bool enableParallel = Settings.Default.Cleaning_EnableParallelCleanup;
        int maxDegree = Settings.Default.Cleaning_MaxDegreeOfParallelism > 0
            ? Settings.Default.Cleaning_MaxDegreeOfParallelism
            : Math.Max(1, Environment.ProcessorCount);

        // When all items are ProjectItems, files that need no editor cleanup run headless in parallel.
        if (enableParallel && items.All(item => item is EnvDTE.ProjectItem))
        {
            var projectItems = items.Cast<EnvDTE.ProjectItem>().ToList();
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxDegree
            };

            var workItems = await RunOnMainThreadAsync(() => projectItems.Select(CreateWorkItem).ToList());

            // Open documents, files in other languages than C#, and C# files that need editor-backed steps
            // (reorganizing, third-party cleanup) are cleaned one at a time in the editor.
            var editorItems = new HashSet<WorkItem>(workItems.Where(workItem =>
                workItem.IsOpen ||
                !string.Equals(Path.GetExtension(workItem.FilePath), ".cs", StringComparison.OrdinalIgnoreCase) ||
                CodeCleanupManager.RequiresEditorCleanupForCSharp()));
            var (parallelItems, sequentialItems) = CleanupBatchPartitioner.Partition(workItems, workItem => workItem.FilePath, editorItems.Contains);
            totalCount = parallelItems.Count + sequentialItems.Count;

            // The semantic steps (class sealing, null check conversion) run before the
            // parallel headless pass, so the headless steps (header, using organization, type splitting) see their
            // result. Projects run in parallel, the files of one project one at a time. A file is counted as changed as
            // soon as it is rewritten, so it is counted even when the batch is canceled before its headless cleanup. The
            // set is only read during the parallel pass, which does not count these files again.
            var filesChangedBySemanticSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Files counted as no-op by the parallel pass, counted as changed instead when the XML documentation changes them.
            var noOpItems = new ConcurrentDictionary<WorkItem, bool>();

            // A file whose semantic step or headless cleanup failed is recorded as failed once: the later passes leave it
            // alone, as they would record their own failures for the same file and inflate the failure count.
            var failedItems = new ConcurrentDictionary<WorkItem, bool>();
            await CleanupBatchPartitioner.RunPerGroupAsync(parallelItems, workItem => workItem.ProjectKey, MaxParallelProjects, cancellationToken, async workItem =>
            {
                ReportProgress(new ProgressReportState { FileName = workItem.FileName, Completed = Volatile.Read(ref completedCount), Total = totalCount });

                try
                {
                    await CodeCleanupManager.RunSemanticStepsAsync(
                        new Func<Task<bool>>[]
                        {
                            () => CodeCleanupManager.SealClassesWhenSafeAsync(workItem.ProjectItem, cancellationToken),
                            () => CodeCleanupManager.ConvertNullChecksWhenSafeAsync(workItem.ProjectItem, cancellationToken),
                        },
                        () =>
                        {
                            bool added;
                            lock (filesChangedBySemanticSteps)
                            {
                                added = filesChangedBySemanticSteps.Add(workItem.FilePath);
                            }

                            if (added)
                            {
                                CodeCleanupManager.IncrementHeadlessChanged();
                            }
                        });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Canceled by the user: the file was left unchanged, and the batch ends below.
                }
                catch (Exception ex)
                {
                    failedItems.TryAdd(workItem, true);
                    CodeCleanupManager.RecordCleanupFailure(workItem.FilePath, ex);
                }
            });

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            try
            {
                Parallel.ForEach(parallelItems, parallelOptions, (workItem, loopState) =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        loopState.Stop();

                        return;
                    }

                    if (failedItems.ContainsKey(workItem))
                    {
                        return;
                    }

                    var fileName = workItem.FileName;

                    ReportProgress(new ProgressReportState { FileName = fileName, Completed = completedCount, Total = totalCount });

                    try
                    {
                        var outcome = CodeCleanupManager.TryRunHeadlessPreCleanupForCSharpCore(workItem.FilePath);

                        // Files rewritten by the semantic steps were already counted as changed.
                        if (!filesChangedBySemanticSteps.Contains(workItem.FilePath))
                        {
                            if (outcome.Result == CodeCleanupManager.HeadlessCleanupResult.Changed)
                            {
                                CodeCleanupManager.IncrementHeadlessChanged();
                            }
                            else
                            {
                                CodeCleanupManager.IncrementHeadlessNoOp();
                                noOpItems.TryAdd(workItem, true);
                            }
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
                        failedItems.TryAdd(workItem, true);
                        CodeCleanupManager.RecordCleanupFailure(workItem.FilePath, ex);
                    }

                    // The file is counted as completed after its diagnostic cleanup below.
                    ReportProgress(new ProgressReportState { FileName = fileName, Completed = completedCount, Total = totalCount });
                });
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            // The diagnostic pass runs one project at a time: the diagnostic cleanup of a closed file applies the fixes
            // through the workspace, whose stale text of a file written by another project's cleanup would overwrite it.
            await CleanupBatchPartitioner.RunPerGroupAsync(parallelItems, workItem => workItem.ProjectKey, maxParallelGroups: 1, cancellationToken, async workItem =>
            {
                ReportProgress(new ProgressReportState { FileName = workItem.FileName, Completed = Volatile.Read(ref completedCount), Total = totalCount });

                if (!failedItems.ContainsKey(workItem))
                {
                    try
                    {
                        await CodeCleanupManager.RunDiagnosticCleanupAsync(workItem.ProjectItem);
                        if (await CodeCleanupManager.RunXmlDocumentationDuringCleanupAsync(workItem.ProjectItem) && noOpItems.ContainsKey(workItem))
                        {
                            CodeCleanupManager.RecountNoOpAsChanged();
                        }
                    }
                    catch (Exception ex)
                    {
                        CodeCleanupManager.RecordCleanupFailure(workItem.FilePath, ex);
                    }
                }

                var completed = Interlocked.Increment(ref completedCount);
                ReportProgress(new ProgressReportState { FileName = workItem.FileName, Completed = completed, Total = totalCount });
            });

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            foreach (var workItem in sequentialItems)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                ReportProgress(new ProgressReportState { FileName = workItem.FileName, Completed = completedCount, Total = totalCount });

                try
                {
                    await CodeCleanupManager.CleanupAsync(workItem.ProjectItem, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Canceled by the user: the file was left unchanged, and the next iteration ends the batch.
                }
                catch (Exception ex)
                {
                    CodeCleanupManager.RecordCleanupFailure(workItem.FilePath ?? workItem.FileName ?? "Unknown", ex);
                }

                completedCount++;
                ReportProgress(new ProgressReportState { FileName = workItem.FileName, Completed = completedCount, Total = totalCount });
            }

            return true;
        }

        // Sequential fallback path (parallel cleanup disabled, or items that are not all project items)
        foreach (dynamic item in items)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            string itemName = null;
            try
            {
                itemName = item.Name;
            }
            catch
            {
            }

            ReportProgress(new ProgressReportState { FileName = itemName, Completed = completedCount, Total = totalCount });

            try
            {
                object batchItem = item;
                var projectItem = await RunOnMainThreadAsync(() =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();

                    return batchItem as EnvDTE.ProjectItem;
                });
                if (projectItem is not null)
                {
                    await CodeCleanupManager.CleanupAsync(projectItem, cancellationToken);
                }
                else
                {
                    // The document is converted to its static type on the UI thread, which owns the COM object, so the
                    // cleanup call is bound at compile time rather than at run time on the dynamic item. The editor
                    // cleanup runs on the UI thread; the AI XML documentation step then runs from this thread, so its
                    // requests neither block Visual Studio nor the Cancel button.
                    var xmlDocumentationItem = await RunOnMainThreadAsync(() =>
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();

                        return CodeCleanupManager.CleanupWithoutXmlDocumentation((EnvDTE.Document)batchItem);
                    });
                    if (xmlDocumentationItem is not null)
                    {
                        await CodeCleanupManager.RunXmlDocumentationDuringCleanupAsync(xmlDocumentationItem);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Canceled by the user: the file was left unchanged, and the next iteration ends the batch.
            }
            catch (Exception ex)
            {
                CodeCleanupManager.RecordCleanupFailure(itemName ?? "Unknown", ex);
            }

            completedCount++;
            ReportProgress(new ProgressReportState { FileName = itemName, Completed = completedCount, Total = totalCount });
        }

        return true;
    }

    /// <summary>
    /// Shows the progress of the batch; runs on the UI thread.
    /// </summary>
    /// <param name="state">The progress state.</param>
    private void OnProgressChanged(ProgressReportState state)
    {
        if (!string.IsNullOrEmpty(state.FileName))
        {
            CurrentFileName = state.FileName;
        }

        CountTotal = state.Total;
        ProcessedCount = Math.Min(state.Total, state.Completed);
        CountProgress = ProcessedCount;

        UpdateExecutionSummary();
    }

    /// <summary>
    /// Completes the batch; runs on the UI thread.
    /// </summary>
    /// <param name="error">The exception that ended the batch, if any.</param>
    /// <param name="canceled">Whether the batch was canceled.</param>
    private void OnBatchCompleted(Exception error, bool canceled)
    {
        // The batch runs fire-and-forget: an exception escaping here would land in a task nobody observes and leave
        // the modal dialog open, because closing it is refused until the dialog result is set.
        try
        {
            CompleteBatch(error, canceled);
        }
        catch (Exception ex)
        {
            OutputWindowHelper.ExceptionWriteLine("Completing the cleanup batch failed", ex);
        }
        finally
        {
            DialogResult = true;
            _cancellationSource.Dispose();
        }
    }

    /// <summary>
    /// Reports the outcome of the batch and starts the build verification when it applies; runs on the UI thread.
    /// </summary>
    /// <param name="error">The exception that ended the batch, if any.</param>
    /// <param name="canceled">Whether the batch was canceled.</param>
    private void CompleteBatch(Exception error, bool canceled)
    {
        _refreshTimer.Stop();
        _batchStopwatch.Stop();
        ProcessedCount = CountTotal;
        UpdateExecutionSummary();

        // Cancel also cancels the AI XML documentation run, which only BeginRun resets: without a fresh run the
        // single-document cleanups after this batch would skip their XML documentation.
        AiXmlDocumentationLogic.BeginRun();

        var stats = CodeCleanupManager.GetCleanupExecutionStats();
        var counts = $"headlessChanged={stats.HeadlessChangedItems}, headlessNoOp={stats.HeadlessNoOpItems}, editor={stats.EditorItems}, failed={stats.FailedItems}, splitOps={stats.SplitOperations}, splitFiles={stats.SplitCreatedFiles}, diagnosticChanged={stats.DiagnosticChangedItems}, diagnosticUnresolved={stats.DiagnosticUnresolvedItems}, elapsedMs={_batchStopwatch.ElapsedMilliseconds}";

        if (error is not null)
        {
            OutputWindowHelper.WarningWriteLine($"Cleanup batch failed after {counts}.");
            _showMessage(error.Message, "CodeJanitor Cleanup Error", MessageBoxImage.Error);
        }
        else if (canceled)
        {
            OutputWindowHelper.InfoWriteLine($"Cleanup batch canceled. Processed: {counts}.");
        }
        else if (stats.FailedItems > 0)
        {
            OutputWindowHelper.WarningWriteLine($"Cleanup batch completed with failures. Processed: {counts}.");
            _showMessage($"Cleanup completed with {stats.FailedItems} failed item(s). Please check the CodeJanitor output window for details.", "CodeJanitor Cleanup Warning", MessageBoxImage.Warning);
        }
        else if (stats.DiagnosticUnresolvedItems > 0)
        {
            OutputWindowHelper.WarningWriteLine($"Cleanup batch completed with unresolved diagnostics. Processed: {counts}.");
            _showMessage($"Cleanup completed, but {stats.DiagnosticUnresolvedItems} file(s) still have diagnostics that could not be fixed automatically. Please check the CodeJanitor output window for details.", "CodeJanitor Cleanup Warning", MessageBoxImage.Warning);
        }
        else
        {
            OutputWindowHelper.InfoWriteLine($"Cleanup batch completed. Processed: {counts}.");
        }

        // Run post-cleanup build verification only when the batch completed cleanly
        // (not canceled, no worker error, no per-file failures) and Visual Studio's
        // build context is available.
        if (!canceled && error is null && stats.FailedItems == 0 &&
            _package?.IDE?.Solution?.SolutionBuild is not null &&
            (stats.HeadlessChangedItems > 0 || stats.DiagnosticChangedItems > 0))
        {
            StartBuildVerification();
        }
    }

    /// <summary>
    /// Starts the post-cleanup build without waiting for it, so Visual Studio stays responsive while it runs, and
    /// reports its result when the build is done.
    /// </summary>
    private void StartBuildVerification()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        EnvDTE.BuildEvents subscribedEvents = null;
        EnvDTE._dispBuildEvents_OnBuildDoneEventHandler onBuildDone = null;
        try
        {
            // The handler references the events object, which keeps it (and the subscription) alive until the build ends.
            var buildEvents = _package.IDE.Events.BuildEvents;
            onBuildDone = (scope, action) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                buildEvents.OnBuildDone -= onBuildDone;

                var failedProjects = _package.IDE.Solution.SolutionBuild.LastBuildInfo;
                if (failedProjects > 0)
                {
                    OutputWindowHelper.WarningWriteLine($"Post-cleanup build verification reported {failedProjects} failed project(s).");
                }
                else
                {
                    OutputWindowHelper.InfoWriteLine("Post-cleanup build verification passed: solution compiled successfully.");
                }
            };

            buildEvents.OnBuildDone += onBuildDone;
            subscribedEvents = buildEvents;

            OutputWindowHelper.InfoWriteLine("Running post-cleanup build verification...");
            _package.IDE.Solution.SolutionBuild.Build(WaitForBuildToFinish: false);
        }
        catch (Exception ex)
        {
            if (subscribedEvents is not null)
            {
                subscribedEvents.OnBuildDone -= onBuildDone;
            }

            OutputWindowHelper.WarningWriteLine($"Post-cleanup build verification could not be executed: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows a message box owned by the progress window (the window whose data context is this view model) and above
    /// every other window, so it cannot end up hidden behind the progress window or Visual Studio while the batch waits
    /// for it. Without such a window the message box is shown by the system defaults.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="caption">The caption.</param>
    /// <param name="image">The icon.</param>
    private void ShowMessage(string message, string caption, MessageBoxImage image)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => ReferenceEquals(window.DataContext, this));
        if (owner is null)
        {
            MessageBox.Show(message, caption, MessageBoxButton.OK, image);

            return;
        }

        var wasTopmost = owner.Topmost;
        owner.Topmost = true;
        owner.Activate();
        try
        {
            MessageBox.Show(owner, message, caption, MessageBoxButton.OK, image);
        }
        finally
        {
            owner.Topmost = wasTopmost;
        }
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
                projectItem.IsOpen[EnvDTE.Constants.vsViewKindTextView] || projectItem.IsOpen[EnvDTE.Constants.vsViewKindCode],
                projectItem.ContainingProject?.UniqueName);
        }
        catch (Exception ex)
        {
            OutputWindowHelper.ExceptionWriteLine("Unable to read a project item for cleanup", ex);

            return new WorkItem(projectItem, null, null, false, null);
        }
    }

    private sealed class WorkItem
    {
        public WorkItem(EnvDTE.ProjectItem projectItem, string fileName, string filePath, bool isOpen, string projectKey)
        {
            ProjectItem = projectItem;
            FileName = fileName;
            FilePath = filePath;
            IsOpen = isOpen;
            ProjectKey = projectKey;
        }

        public EnvDTE.ProjectItem ProjectItem { get; }

        public string FileName { get; }

        public string FilePath { get; }

        public bool IsOpen { get; }

        /// <summary>
        /// Gets the unique name of the project that contains the file, or null when unknown.
        /// </summary>
        public string ProjectKey { get; }
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
