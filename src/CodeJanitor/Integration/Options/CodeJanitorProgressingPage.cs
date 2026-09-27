using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.Options;
using CodeJanitor.UI.Dialogs.Options.Progressing;

// Each class in this file is a concrete VS Options page for one Code Janitor settings section.
// Register each with [ProvideOptionPage] on CodeJanitorPackage — VS builds the tree from those.

namespace CodeJanitor.Integration.Options;

/// <summary>
/// Represents a page that displays the progress of a CodeJanitor operation and initializes its associated view model.
/// </summary>
public sealed class CodeJanitorProgressingPage : CodeJanitorSectionDialogPage
{
    /// <summary>
    /// Creates and returns a new ProgressingViewModel constructed from the given package and settings, with no side effects or exceptions.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="settings">The settings.</param>
    /// <returns>A OptionsPageViewModel value produced by this method.</returns>
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new ProgressingViewModel(package, settings);
}
