using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Ai;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;

namespace CodeJanitor.UI.Dialogs.CleanupProgress;

/// <summary>
/// The view model representing the state and commands for AI XML documentation progress.
/// </summary>
public sealed class XmlDocProgressViewModel : BaseProgressViewModel
{
    private readonly BackgroundWorker _backgroundWorker;
    private readonly Stopwatch _batchStopwatch;
    private readonly CodeJanitorPackage _package;
    private readonly AiXmlDocumentationLogic _aiXmlDocumentationLogic;

    /// <summary>
    /// Gets the window title.
    /// </summary>
    public override string WindowTitle => "CodeJanitor Add XMLDoc";

    /// <summary>
    /// Gets the header title displayed above the current file name.
    /// </summary>
    public override string HeaderTitle => "Adding XML Documentation...";

    /// <summary>
    /// Gets or sets the count of files changed.
    /// </summary>
    public int ChangedCount
    {
        get { return GetPropertyValue<int>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the count of files unchanged.
    /// </summary>
    public int UnchangedCount
    {
        get { return GetPropertyValue<int>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the count of files that failed.
    /// </summary>
    public int FailedCount
    {
        get { return GetPropertyValue<int>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="XmlDocProgressViewModel"/> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <param name="items">The project items to document.</param>
    public XmlDocProgressViewModel(CodeJanitorPackage package, IEnumerable<ProjectItem> items)
    {
        _package = package;
        _aiXmlDocumentationLogic = AiXmlDocumentationLogic.GetInstance(package);
        AiXmlDocumentationLogic.BeginRun();
        _batchStopwatch = Stopwatch.StartNew();

        var projectItems = items.ToList();
        CountTotal = projectItems.Count;
        UpdateExecutionSummary();

        _backgroundWorker = new BackgroundWorker
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true
        };

        _backgroundWorker.DoWork += backgroundWorker_DoWork;
        _backgroundWorker.ProgressChanged += backgroundWorker_ProgressChanged;
        _backgroundWorker.RunWorkerCompleted += backgroundWorker_RunWorkerCompleted;

        _backgroundWorker.RunWorkerAsync(projectItems);
    }

    /// <summary>
    /// Executes when the cancel command is triggered, immediately cancelling AI and background worker tasks.
    /// </summary>
    protected override void OnCancelCommandExecuted(object parameter)
    {
        IsCanceling = true;
        CancelCommand.RaiseCanExecuteChanged();

        // Cancel AI HTTP calls and Roslyn loop instantly
        AiXmlDocumentationLogic.CancelRun();
        _backgroundWorker.CancelAsync();
    }

    /// <summary>
    /// Executes a parallel batch operation that applies AI-generated XML documentation to a list of project items, limiting concurrency with a semaphore, supporting cancellation, and incrementing per-item status counters (changed, unchanged, failed) with warnings logged on failure.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The e.</param>
    private void backgroundWorker_DoWork(object sender, DoWorkEventArgs e)
    {
        var bw = (BackgroundWorker)sender;
        var items = (List<ProjectItem>)e.Argument;
        var maxParallel = Math.Max(1, CodeJanitor.Properties.Settings.Default.Cleaning_AiXmlDocumentationMaxParallelFiles);
        var semaphore = new System.Threading.SemaphoreSlim(maxParallel, maxParallel);
        int processed = 0;
        int changed = 0;
        int unchanged = 0;
        int failed = 0;

        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            var tasks = new List<System.Threading.Tasks.Task>();

            foreach (var item in items)
            {
                if (bw.CancellationPending || AiXmlDocumentationLogic.RunToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await semaphore.WaitAsync(AiXmlDocumentationLogic.RunToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (bw.CancellationPending || AiXmlDocumentationLogic.RunToken.IsCancellationRequested)
                {
                    semaphore.Release();
                    break;
                }

                var task = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        if (AiXmlDocumentationLogic.RunToken.IsCancellationRequested)
                        {
                            return;
                        }

                        var isChanged = await _aiXmlDocumentationLogic.ApplyXmlDocumentationAsync(item);

                        if (isChanged)
                        {
                            System.Threading.Interlocked.Increment(ref changed);
                        }
                        else
                        {
                            System.Threading.Interlocked.Increment(ref unchanged);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Canceled
                    }
                    catch (Exception ex)
                    {
                        System.Threading.Interlocked.Increment(ref failed);
                        OutputWindowHelper.WarningWriteLine($"Failed to add XML documentation for '{item?.Name}': {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                        var count = System.Threading.Interlocked.Increment(ref processed);
                        bw.ReportProgress(count, (item, changed, unchanged, failed));
                    }
                });

                tasks.Add(task);
            }

            try
            {
                await System.Threading.Tasks.Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Canceled or ignored
            }
        });

        ChangedCount = changed;
        UnchangedCount = unchanged;
        FailedCount = failed;
        ProcessedCount = processed;

        if (bw.CancellationPending || AiXmlDocumentationLogic.RunToken.IsCancellationRequested)
        {
            e.Cancel = true;
        }
    }

    /// <summary>
    /// Handles background worker progress changes by updating the progress percentage, current file name, and processing counters (changed, unchanged, failed) from the event arguments, then refreshing the execution summary.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The e.</param>

    private void backgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
    {
        CountProgress = e.ProgressPercentage;
        ProcessedCount = e.ProgressPercentage;
        if (e.UserState is ValueTuple<ProjectItem, int, int, int> state)
        {
            if (state.Item1 is not null)
            {
                CurrentFileName = state.Item1.Name;
            }
            ChangedCount = state.Item2;
            UnchangedCount = state.Item3;
            FailedCount = state.Item4;
        }
        else if (e.UserState is ProjectItem item)
        {
            CurrentFileName = item.Name;
        }

        UpdateExecutionSummary();
    }

    /// <param name="e">The e.</param>
    private void backgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
    {
        _batchStopwatch.Stop();
        UpdateExecutionSummary();

        // Close the progress dialog immediately so the UI window is never stuck open
        DialogResult = true;

        if (e.Error is not null)
        {
            OutputWindowHelper.WarningWriteLine(
                $"Add XMLDoc batch failed after changed={ChangedCount}, unchanged={UnchangedCount}, failed={FailedCount}, elapsedMs={_batchStopwatch.ElapsedMilliseconds}. Error: {e.Error.Message}");
            MessageBox.Show(e.Error.Message, "CodeJanitor Add XMLDoc Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else if (e.Cancelled || AiXmlDocumentationLogic.RunToken.IsCancellationRequested)
        {
            OutputWindowHelper.InfoWriteLine(
                $"Add XMLDoc batch canceled. Processed {ProcessedCount} of {CountTotal} file(s). Changed={ChangedCount}, unchanged={UnchangedCount}, failed={FailedCount}, elapsedMs={_batchStopwatch.ElapsedMilliseconds}.");
            _package.IDE.StatusBar.Text = $"CodeJanitor Add XMLDoc canceled: changed {ChangedCount} of {CountTotal} file(s).";
        }
        else
        {
            OutputWindowHelper.InfoWriteLine(
                $"Add XMLDoc batch completed. Processed {CountTotal} file(s). Changed={ChangedCount}, unchanged={UnchangedCount}, failed={FailedCount}, elapsedMs={_batchStopwatch.ElapsedMilliseconds}.");
            _package.IDE.StatusBar.Text = $"CodeJanitor Add XMLDoc completed: changed {ChangedCount} of {CountTotal} file(s).";
        }
    }

    /// <summary>

    /// ExecutionSummary` and `ElapsedSummary` strings with formatted counts of changed, unchanged, failed, and processed items along with the elapsed batch time in mm:ss format.
    /// </summary>

    private void UpdateExecutionSummary()
    {
        ExecutionSummary = $"Changed: {ChangedCount} | Unchanged: {UnchangedCount} | Failed: {FailedCount}";

        ElapsedSummary = $"Processed: {ProcessedCount}/{CountTotal} | Elapsed: {_batchStopwatch.Elapsed:mm\\:ss}";
    }
}
