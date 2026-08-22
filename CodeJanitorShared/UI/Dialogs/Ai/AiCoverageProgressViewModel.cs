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

    public string TargetName
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    public int TargetCoveragePercentage
    {
        get => GetPropertyValue<int>();
        set => SetPropertyValue(value);
    }

    public int CurrentCoveragePercentage
    {
        get => GetPropertyValue<int>();
        set => SetPropertyValue(value);
    }

    public string IterationText
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    public string StatusMessage
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    public bool IsCancelled { get; private set; }

    public event EventHandler RequestClose;

    public DelegateCommand CancelCommand => _cancelCommand
        ?? (_cancelCommand = new DelegateCommand(OnCancel));

    public void Report(AiCoverageProgressReport value)
    {
        if (value == null) return;

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

    private void OnCancel(object parameter)
    {
        IsCancelled = true;
        StatusMessage = "Cancelling operation...";
        _cts?.Cancel();
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
