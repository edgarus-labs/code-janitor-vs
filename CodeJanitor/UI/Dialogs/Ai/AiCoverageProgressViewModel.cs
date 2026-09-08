using CodeJanitor.Logic.Ai;
using CodeJanitor.UI;
using System;
using System.Threading;

namespace CodeJanitor.UI.Dialogs.Ai;

/// <summary>
/// View model for the real-time, cancellable AI Code Coverage test generation progress dialog.
/// </summary>
public sealed class AiCoverageProgressViewModel : Bindable, IProgress<AiCoverageProgressReport>
{
    private readonly CancellationTokenSource _cts;
    private DelegateCommand _cancelCommand;

    public AiCoverageProgressViewModel(string targetName, int targetCoverage, CancellationTokenSource cts)
    {
        TargetName = targetName ?? "Code Member";
        TargetCoveragePercentage = targetCoverage;
        _cts = cts;
        StatusMessage = "Starting AI test generation engine...";
        IterationText = "Preparing...";
        CurrentCoveragePercentage = 0;
    }

    /// <summary>
    /// Gets or sets the target name.
    /// </summary>
    public string TargetName
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the target coverage percentage.
    /// </summary>
    public int TargetCoveragePercentage
    {
        get => GetPropertyValue<int>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the current coverage percentage.
    /// </summary>
    public int CurrentCoveragePercentage
    {
        get => GetPropertyValue<int>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the iteration text.
    /// </summary>
    public string IterationText
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    public string StatusMessage
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the is cancelled.
    /// </summary>
    public bool IsCancelled { get; private set; }

    /// <summary>
    /// Occurs when request close.
    /// </summary>
    public event EventHandler RequestClose;

    /// <summary>
    /// Gets the cancel command.
    /// </summary>
    public DelegateCommand CancelCommand => _cancelCommand
        ?? (_cancelCommand = new DelegateCommand(OnCancel));

    /// <summary>
    /// Updates CurrentCoveragePercentage, StatusMessage, and IterationText from a non-null AiCoverageProgressReport (no-op on null), formatting iteration and branch-coverage details when CurrentIteration is positive or else using a static analyzing message.
    /// </summary>
    /// <param name="value">The value.</param>
    public void Report(AiCoverageProgressReport value)
    {
        if (value is null) return;

        CurrentCoveragePercentage = value.CurrentCoveragePercentage;
        StatusMessage = value.StatusMessage;
        if (value.CurrentIteration > 0)
        {
            IterationText = $"Iteration {value.CurrentIteration} of {value.MaxIterations} ({value.CoveredBranchesCount}/{value.TotalBranchesCount} branches covered)";
        }
        else
        {
            IterationText = "Analyzing code branches...";
        }
    }

    /// <summary>
    /// OnCancel sets IsCancelled to true, updates StatusMessage, cancels the CancellationTokenSource, and raises RequestClose to close the associated view.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
    private void OnCancel(object parameter)
    {
        IsCancelled = true;
        StatusMessage = "Cancelling operation...";
        _cts?.Cancel();
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
