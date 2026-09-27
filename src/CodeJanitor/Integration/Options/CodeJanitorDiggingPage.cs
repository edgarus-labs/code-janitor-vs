using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.Options;
using CodeJanitor.UI.Dialogs.Options.Digging;

// Each class in this file is a concrete VS Options page for one Code Janitor settings section.
// Register each with [ProvideOptionPage] on CodeJanitorPackage — VS builds the tree from those.

namespace CodeJanitor.Integration.Options;

/// <summary>
/// CodeJanitorDiggingPage represents a page view in a Code Janitor digging feature that provides functionality to create its associated view model.
/// </summary>
public sealed class CodeJanitorDiggingPage : CodeJanitorSectionDialogPage
{
    /// <summary>
    /// Creates and returns a new DiggingViewModel initialized with the provided package and settings, having no side effects beyond constructing the ViewModel instance.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="settings">The settings.</param>
    /// <returns>A OptionsPageViewModel value produced by this method.</returns>
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new DiggingViewModel(package, settings);
}
