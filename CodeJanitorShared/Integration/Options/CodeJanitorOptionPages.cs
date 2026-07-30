using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.Options;
using CodeJanitor.UI.Dialogs.Options.Cleaning;
using CodeJanitor.UI.Dialogs.Options.Collapsing;
using CodeJanitor.UI.Dialogs.Options.Digging;
using CodeJanitor.UI.Dialogs.Options.Finding;
using CodeJanitor.UI.Dialogs.Options.Formatting;
using CodeJanitor.UI.Dialogs.Options.General;
using CodeJanitor.UI.Dialogs.Options.Progressing;
using CodeJanitor.UI.Dialogs.Options.Reorganizing;
using CodeJanitor.UI.Dialogs.Options.Switching;
using CodeJanitor.UI.Dialogs.Options.ThirdParty;

// Each class in this file is a concrete VS Options page for one Code Janitor settings section.
// Register each with [ProvideOptionPage] on CodeJanitorPackage — VS builds the tree from those.
namespace CodeJanitor.Integration.Options
{
    public sealed class CodeJanitorGeneralPage : CodeJanitorSectionDialogPage
    {
        protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new GeneralViewModel(package, settings);
    }

    public sealed class CodeJanitorFeaturesPage : CodeJanitorSectionDialogPage
    {
        protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new FeaturesViewModel(package, settings);
    }

    public sealed class CodeJanitorCleaningParentPage : CodeJanitorSectionDialogPage
    {
        protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new CleaningParentViewModel(package, settings);
    }

    public sealed class CodeJanitorCleaningGeneralPage : CodeJanitorSectionDialogPage
    {
        protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new CleaningGeneralViewModel(package, settings);
    }

    public sealed class CodeJanitorCleaningFileTypesPage : CodeJanitorSectionDialogPage
    {
        protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new CleaningFileTypesViewModel(package, settings);
    }

    public sealed class CodeJanitorCleaningVisualStudioPage : CodeJanitorSectionDialogPage
    {
        protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new CleaningVisualStudioViewModel(package, settings);
    }

    public sealed class CodeJanitorCleaningInsertPage : CodeJanitorSectionDialogPage
    {
        protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new CleaningInsertViewModel(package, settings);
    }

    public sealed class CodeJanitorCleaningRemovePage : CodeJanitorSectionDialogPage
    {
        protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new CleaningRemoveViewModel(package, settings);
    }

    public sealed class CodeJanitorCleaningUpdatePage : CodeJanitorSectionDialogPage
    {
        protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new CleaningUpdateViewModel(package, settings);
    }

    public sealed class CodeJanitorCollapsingPage : CodeJanitorSectionDialogPage
    {
        protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new CollapsingViewModel(package, settings);
    }

    public sealed class CodeJanitorDiggingPage : CodeJanitorSectionDialogPage
    {
        protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new DiggingViewModel(package, settings);
    }

    public sealed class CodeJanitorFindingPage : CodeJanitorSectionDialogPage
    {
        protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new FindingViewModel(package, settings);
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

    public sealed class CodeJanitorReorganizingGeneralPage : CodeJanitorSectionDialogPage
    {
        protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new ReorganizingGeneralViewModel(package, settings);
    }

    public sealed class CodeJanitorReorganizingTypesPage : CodeJanitorSectionDialogPage
    {
        protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new ReorganizingTypesViewModel(package, settings);
    }

    public sealed class CodeJanitorReorganizingRegionsPage : CodeJanitorSectionDialogPage
    {
        protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new ReorganizingRegionsViewModel(package, settings);
    }

    public sealed class CodeJanitorSwitchingPage : CodeJanitorSectionDialogPage
    {
        protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new SwitchingViewModel(package, settings);
    }

    public sealed class CodeJanitorThirdPartyPage : CodeJanitorSectionDialogPage
    {
        protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
            new ThirdPartyViewModel(package, settings);
    }
}
