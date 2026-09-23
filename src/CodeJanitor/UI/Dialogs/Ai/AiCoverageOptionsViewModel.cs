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
        set => SetPropertyValue(Math.Max(10, Math.Min(100, value)));
    }

    /// <summary>
    /// Gets or sets the max iterations.
    /// </summary>
    public int MaxIterations
    {
        get => GetPropertyValue<int>();
        set => SetPropertyValue(Math.Max(1, Math.Min(10, value)));
    }

    /// <summary>
    /// Gets or sets the test framework.
    /// </summary>
    public string TestFramework
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the mocking library.
    /// </summary>
    public string MockingLibrary
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the is confirmed.
    /// </summary>
    public bool IsConfirmed { get; private set; }

    /// <summary>
    /// Occurs when request close.
    /// </summary>
    public event EventHandler RequestClose;

    /// <summary>
    /// Gets the start command.
    /// </summary>
    public DelegateCommand StartCommand => _startCommand
        ?? (_startCommand = new DelegateCommand(OnStart));

    /// <summary>
    /// Gets the cancel command.
    /// </summary>
    public DelegateCommand CancelCommand => _cancelCommand
        ?? (_cancelCommand = new DelegateCommand(OnCancel));

    /// <summary>
    /// Persists AI-related configuration settings to user settings, sets IsConfirmed to true, and raises the RequestClose event to signal completion.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
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

    /// <summary>
    /// a cancel action by setting the IsConfirmed property to false and invoking the RequestClose event.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
    private void OnCancel(object parameter)
    {
        IsConfirmed = false;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
