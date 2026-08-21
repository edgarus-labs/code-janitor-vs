using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.Options;
using CodeJanitor.UI.Dialogs.Options.Cleaning;
using CodeJanitor.UI.Dialogs.Options.Digging;
using CodeJanitor.UI.Dialogs.Options.Formatting;
using CodeJanitor.UI.Dialogs.Options.General;
using CodeJanitor.UI.Dialogs.Options.Navigation;
using CodeJanitor.UI.Dialogs.Options.Progressing;
using CodeJanitor.UI.Dialogs.Options.Reorganizing;
using CodeJanitor.UI.Dialogs.Options.ThirdParty;
using System.Runtime.InteropServices;

// Each class in this file is a concrete VS Options page for one Code Janitor settings section.
// Register each with [ProvideOptionPage] on CodeJanitorPackage — VS builds the tree from those.
// The explicit [Guid] values must never change: without them the page identity is derived from
// the assembly version, so every release would strand the previously registered pages.

namespace CodeJanitor.Integration.Options;

[Guid("707636e3-b3b9-498f-97e6-923023377553")]
public sealed class CodeJanitorGeneralPage : CodeJanitorSectionDialogPage
{
    /// <summary>
    /// Creates a new GeneralParentViewModel with the provided package and settings and returns it, with no side effects or thrown exceptions.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="settings">The settings.</param>
    /// <returns>A OptionsPageViewModel value produced by this method.</returns>

    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new GeneralParentViewModel(package, settings);
}
