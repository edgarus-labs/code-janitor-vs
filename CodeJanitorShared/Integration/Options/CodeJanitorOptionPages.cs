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
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
        new GeneralParentViewModel(package, settings);
}

[Guid("cade8401-696c-4fa7-87fd-eb9dcb13acb3")]
public sealed class CodeJanitorCleaningParentPage : CodeJanitorSectionDialogPage
{
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
        new CleaningParentViewModel(package, settings);
}

[Guid("38be1f43-2d2a-4f05-8384-79cffe22d3e7")]
public sealed class CodeJanitorNavigationPage : CodeJanitorSectionDialogPage
{
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
        new NavigationParentViewModel(package, settings);
}

[Guid("78b0af6a-39c1-46f9-8920-c711b15b51d0")]
public sealed class CodeJanitorDiggingPage : CodeJanitorSectionDialogPage
{
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
        new DiggingViewModel(package, settings);
}

[Guid("8a3d9d78-ba9e-4a2f-9865-bcb66934c9af")]
public sealed class CodeJanitorFormattingPage : CodeJanitorSectionDialogPage
{
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
        new FormattingViewModel(package, settings);
}

[Guid("0faa5b03-0b19-4de3-b303-eea64511e2ac")]
public sealed class CodeJanitorProgressingPage : CodeJanitorSectionDialogPage
{
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
        new ProgressingViewModel(package, settings);
}

[Guid("5fe71a9e-5946-4a7c-ab61-73e5141506b9")]
public sealed class CodeJanitorReorganizingParentPage : CodeJanitorSectionDialogPage
{
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
        new ReorganizingParentViewModel(package, settings);
}

[Guid("a8ca8916-aeb0-450e-84d3-edf8a85f84cf")]
public sealed class CodeJanitorThirdPartyPage : CodeJanitorSectionDialogPage
{
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
        new ThirdPartyViewModel(package, settings);
}