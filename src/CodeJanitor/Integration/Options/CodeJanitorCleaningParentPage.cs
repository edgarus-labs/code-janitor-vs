using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.Options;
using CodeJanitor.UI.Dialogs.Options.Cleaning;

// Each class in this file is a concrete VS Options page for one Code Janitor settings section.
// Register each with [ProvideOptionPage] on CodeJanitorPackage — VS builds the tree from those.

namespace CodeJanitor.Integration.Options;

/// <summary>
/// page class for the CodeJanitor cleaning feature that provides view model creation functionality for derived cleaning pages.
/// </summary>
public sealed class CodeJanitorCleaningParentPage : CodeJanitorSectionDialogPage
{
    /// <summary>
    /// Creates and returns a new CleaningParentViewModel using the given package and settings, with no additional side effects.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="settings">The settings.</param>
    /// <returns>A OptionsPageViewModel value produced by this method.</returns>
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new CleaningParentViewModel(package, settings);
}
