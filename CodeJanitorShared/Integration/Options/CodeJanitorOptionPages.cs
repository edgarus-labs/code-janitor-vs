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

// Each class in this file is a concrete VS Options page for one Code Janitor settings section.
// Register each with [ProvideOptionPage] on CodeJanitorPackage — VS builds the tree from those.

namespace CodeJanitor.Integration.Options;

public sealed class CodeJanitorGeneralPage : CodeJanitorSectionDialogPage
{
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
        new GeneralParentViewModel(package, settings);
}

public sealed class CodeJanitorCleaningParentPage : CodeJanitorSectionDialogPage
{
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
        new CleaningParentViewModel(package, settings);
}

public sealed class CodeJanitorNavigationPage : CodeJanitorSectionDialogPage
{
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
        new NavigationParentViewModel(package, settings);
}

public sealed class CodeJanitorDiggingPage : CodeJanitorSectionDialogPage
{
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
        new DiggingViewModel(package, settings);
}

public sealed class CodeJanitorFormattingPage : CodeJanitorSectionDialogPage
{
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
        new FormattingViewModel(package, settings);
}

public sealed class CodeJanitorProgressingPage : CodeJanitorSectionDialogPage
{
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
        new ProgressingViewModel(package, settings);
}

public sealed class CodeJanitorReorganizingParentPage : CodeJanitorSectionDialogPage
{
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
        new ReorganizingParentViewModel(package, settings);
}

public sealed class CodeJanitorThirdPartyPage : CodeJanitorSectionDialogPage
{
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
        new ThirdPartyViewModel(package, settings);
}