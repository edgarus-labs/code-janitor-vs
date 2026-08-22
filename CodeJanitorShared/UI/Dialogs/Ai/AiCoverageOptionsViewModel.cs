using CodeJanitor.Properties;
using CodeJanitor.UI;
using System;

namespace CodeJanitor.UI.Dialogs.Ai;

/// <summary>
/// View model for the Target Code Coverage configuration dialog.
/// </summary>
public sealed class AiCoverageOptionsViewModel : Bindable
{
    private DelegateCommand _startCommand;
    private DelegateCommand _cancelCommand;

    public AiCoverageOptionsViewModel(string targetName)
    {
        TargetName = targetName ?? "Code Member";
        var settings = Settings.Default;

        TargetCoveragePercentage = settings.Ai_TargetCoveragePercentage > 0 ? settings.Ai_TargetCoveragePercentage : 90;
        MaxIterations = settings.Ai_MaxCoverageIterations > 0 ? settings.Ai_MaxCoverageIterations : 3;
        TestFramework = string.IsNullOrWhiteSpace(settings.Ai_TestFramework) ? "xUnit" : settings.Ai_TestFramework;
        MockingLibrary = string.IsNullOrWhiteSpace(settings.Ai_MockingLibrary) ? "Moq" : settings.Ai_MockingLibrary;
    }

    public string TargetName
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    public int TargetCoveragePercentage
    {
        get => GetPropertyValue<int>();
        set => SetPropertyValue(Math.Max(10, Math.Min(100, value)));
    }

    public int MaxIterations
    {
        get => GetPropertyValue<int>();
        set => SetPropertyValue(Math.Max(1, Math.Min(10, value)));
    }

    public string TestFramework
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    public string MockingLibrary
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    public bool IsConfirmed { get; private set; }

    public event EventHandler RequestClose;

    public DelegateCommand StartCommand => _startCommand
        ?? (_startCommand = new DelegateCommand(OnStart));

    public DelegateCommand CancelCommand => _cancelCommand
        ?? (_cancelCommand = new DelegateCommand(OnCancel));

    private void OnStart(object parameter)
    {
        // Persist settings
        var settings = Settings.Default;
        settings.Ai_TargetCoveragePercentage = TargetCoveragePercentage;
        settings.Ai_MaxCoverageIterations = MaxIterations;
        settings.Ai_TestFramework = TestFramework;
        settings.Ai_MockingLibrary = MockingLibrary;
        settings.Save();

        IsConfirmed = true;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancel(object parameter)
    {
        IsConfirmed = false;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
