using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.Options;
using CodeJanitor.UI.Dialogs.Options.Reorganizing;

// Each class in this file is a concrete VS Options page for one Code Janitor settings section.
// Register each with [ProvideOptionPage] on CodeJanitorPackage — VS builds the tree from those.

namespace CodeJanitor.Integration.Options;

/// <summary>
/// presents a page for reorganizing a parent page within a Code Janitor feature, responsible for creating the corresponding view model.
/// </summary>
public sealed class CodeJanitorReorganizingParentPage : CodeJanitorSectionDialogPage
{
    /// <summary>
    /// Creates and returns a new ReorganizingParentViewModel using the provided package and settings, with no additional side effects or exceptions.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="settings">The settings.</param>
    /// <returns>A OptionsPageViewModel value produced by this method.</returns>
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new ReorganizingParentViewModel(package, settings);
}
