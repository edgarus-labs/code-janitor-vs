using Microsoft.VisualStudio.Shell;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Helpers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System;

namespace CodeJanitor.UI.Dialogs.CleanupProgress
{
    /// <summary>
    /// The view model representing the state and commands available for cleanup progress.
    /// </summary>
    public class CleanupProgressViewModel : Bindable
    {
        #region Fields

        private readonly BackgroundWorker _backgroundWorker;
        private readonly Stopwatch _batchStopwatch;

        #endregion Fields

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="CleanupProgressViewModel" /> class.
        /// </summary>
        /// <param name="package">The hosting package.</param>
        /// <param name="items">The items to cleanup.</param>
        public CleanupProgressViewModel(CodeJanitorPackage package, IEnumerable<object> items)
        {
            CodeCleanupManager = CodeCleanupManager.GetInstance(package);
            CodeCleanupManager.ResetCleanupExecutionStats();
            _batchStopwatch = Stopwatch.StartNew();

            var cleanupItems = items.ToList();

            // Initialize UI elements.
            CountTotal = cleanupItems.Count;
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

        #endregion Constructors

        #region Properties

        /// <summary>
        /// Gets or sets the name of the current file being cleaned.
        /// </summary>
        public string CurrentFileName
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the current execution stats summary.
        /// </summary>
        public string ExecutionSummary
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the current elapsed time summary.
        /// </summary>
        public string ElapsedSummary
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the progress count.
        /// </summary>
        public int CountProgress
        {
            get { return GetPropertyValue<int>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the total count.
        /// </summary>
        public int CountTotal
        {
            get { return GetPropertyValue<int>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the dialog result.
        /// </summary>
        public bool? DialogResult
        {
            get { return GetPropertyValue<bool?>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets a flag indicating if the operation is being canceled.
        /// </summary>
        public bool IsCanceling
        {
            get { return GetPropertyValue<bool>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the code cleanup manager.
        /// </summary>
        private CodeCleanupManager CodeCleanupManager { get; set; }

        #endregion Properties

        #region Cancel Command

        private DelegateCommand _cancelCommand;

        /// <summary>
        /// Gets the cancel command.
        /// </summary>
        public DelegateCommand CancelCommand => _cancelCommand ?? (_cancelCommand = new DelegateCommand(OnCancelCommandExecuted, OnCancelCommandCanExecute));

        /// <summary>
        /// Called when the <see cref="CancelCommand" /> needs to determine if it can execute.
        /// </summary>
        /// <param name="parameter">The command parameter.</param>
        /// <returns>True if the command can execute, otherwise false.</returns>
        private bool OnCancelCommandCanExecute(object parameter)
        {
            return !IsCanceling;
        }

        /// <summary>
        /// Called when the <see cref="CancelCommand" /> is executed.
        /// </summary>
        /// <param name="parameter">The command parameter.</param>
        private void OnCancelCommandExecuted(object parameter)
        {
            IsCanceling = true;
            CancelCommand.RaiseCanExecuteChanged();

            _backgroundWorker.CancelAsync();
        }

        #endregion Cancel Command

        #region Methods

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
            var items = (IEnumerable<object>)e.Argument;
            int i = 0;

            foreach (dynamic item in items)
            {
                if (bw.CancellationPending)
                {
                    e.Cancel = true;
                    break;
                }

                bw.ReportProgress(++i, item);

                ThreadHelper.JoinableTaskFactory.Run(async delegate
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    try
                    {
                        CodeCleanupManager.Cleanup(item);
                    }
                    catch (Exception ex)
                    {
                        CodeCleanupManager.RecordCleanupFailure(item.Name, ex);
                    }

                    UpdateExecutionSummary();
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
            int currentCount = e.ProgressPercentage;
            dynamic currentItem = e.UserState;

            CountProgress = currentCount;
            CurrentFileName = currentItem.Name;
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
            _batchStopwatch.Stop();
            UpdateExecutionSummary();

            var stats = CodeCleanupManager.GetCleanupExecutionStats();

            if (e.Error != null)
            {
                OutputWindowHelper.WarningWriteLine(
                    $"Cleanup batch failed after headlessChanged={stats.HeadlessChangedItems}, headlessNoOp={stats.HeadlessNoOpItems}, editor={stats.EditorItems}, failed={stats.FailedItems}, splitOps={stats.SplitOperations}, splitFiles={stats.SplitCreatedFiles}, elapsedMs={_batchStopwatch.ElapsedMilliseconds}.");
                MessageBox.Show(e.Error.Message, "CodeJanitor Cleanup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else if (e.Cancelled)
            {
                OutputWindowHelper.InfoWriteLine(
                    $"Cleanup batch canceled. Processed: headlessChanged={stats.HeadlessChangedItems}, headlessNoOp={stats.HeadlessNoOpItems}, editor={stats.EditorItems}, failed={stats.FailedItems}, splitOps={stats.SplitOperations}, splitFiles={stats.SplitCreatedFiles}, elapsedMs={_batchStopwatch.ElapsedMilliseconds}.");
            }
            else
            {
                OutputWindowHelper.InfoWriteLine(
                    $"Cleanup batch completed. Processed: headlessChanged={stats.HeadlessChangedItems}, headlessNoOp={stats.HeadlessNoOpItems}, editor={stats.EditorItems}, failed={stats.FailedItems}, splitOps={stats.SplitOperations}, splitFiles={stats.SplitCreatedFiles}, elapsedMs={_batchStopwatch.ElapsedMilliseconds}.");
            }

            // Close the dialog.
            DialogResult = true;
        }

        /// <summary>
        /// Updates the execution summary displayed in the progress dialog.
        /// </summary>
        private void UpdateExecutionSummary()
        {
            var stats = CodeCleanupManager.GetCleanupExecutionStats();
            ExecutionSummary = string.Format(
                "Headless changed: {0} | Headless no-op: {1} | Editor: {2} | Failed: {3} | Split ops: {4} | Split files: {5}",
                stats.HeadlessChangedItems,
                stats.HeadlessNoOpItems,
                stats.EditorItems,
                stats.FailedItems,
                stats.SplitOperations,
                stats.SplitCreatedFiles);

            ElapsedSummary = string.Format(
                "Processed: {0}/{1} | Elapsed: {2:mm\\:ss}",
                stats.TotalProcessedItems,
                CountTotal,
                _batchStopwatch.Elapsed);
        }

        #endregion Methods
    }
}