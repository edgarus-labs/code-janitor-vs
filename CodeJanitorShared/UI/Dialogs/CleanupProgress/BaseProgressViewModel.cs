using System;
using CodeJanitor.Helpers;
using CodeJanitor.Properties;

namespace CodeJanitor.UI.Dialogs.CleanupProgress;

/// <summary>
/// Base class for progress dialog view models.
/// </summary>
public abstract class BaseProgressViewModel : Bindable
{
    /// <summary>
    /// Gets the window title.
    /// </summary>
    public virtual string WindowTitle => Resources.CodeJanitorCleanupProgress;

    /// <summary>
    /// Gets the header title displayed above the current file name.
    /// </summary>
    public virtual string HeaderTitle => Resources.Cleaning;

    /// <summary>
    /// Gets or sets the name of the current file being processed.
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
    /// Gets or sets the number of items that have finished processing.
    /// </summary>
    public int ProcessedCount
    {
        get
        {
            return GetPropertyValue<int>();
        }
        set
        {
            if (SetPropertyValue(value))
            {
                RaisePropertyChanged(nameof(ProgressPercentText));
            }
        }
    }

    /// <summary>
    /// Gets or sets the total count.
    /// </summary>
    public int CountTotal
    {
        get
        {
            return GetPropertyValue<int>();
        }
        set
        {
            if (SetPropertyValue(value))
            {
                RaisePropertyChanged(nameof(ProgressPercentText));
            }
        }
    }

    /// <summary>
    /// Gets the formatted percentage text (e.g. "45%").
    /// </summary>
    public string ProgressPercentText
    {
        get
        {
            if (CountTotal <= 0)
            {
                return "0%";
            }

            var pct = Math.Min(100, Math.Max(0, (int)Math.Round((double)ProcessedCount / CountTotal * 100)));
            return $"{pct}%";
        }
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

    private DelegateCommand _cancelCommand;

    /// <summary>
    /// Gets the cancel command.
    /// </summary>
    public DelegateCommand CancelCommand => _cancelCommand ?? (_cancelCommand = new DelegateCommand(OnCancelCommandExecuted, OnCancelCommandCanExecute));

    /// <summary>
    /// Determines whether the cancel command can execute.
    /// </summary>
    protected virtual bool OnCancelCommandCanExecute(object parameter)
    {
        return !IsCanceling;
    }

    /// <summary>
    /// Executes when the cancel command is triggered.
    /// </summary>
    protected abstract void OnCancelCommandExecuted(object parameter);
}
