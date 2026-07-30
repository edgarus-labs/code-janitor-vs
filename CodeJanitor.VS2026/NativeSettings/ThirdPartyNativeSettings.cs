using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace CodeJanitor.NativeSettings
{
    // ThirdParty category, migrated from CodeJanitorShared/Properties/Settings.settings (ThirdParty_*).
#pragma warning disable VSEXTPREVIEW_SETTINGS // Settings APIs are preview.
    internal static class ThirdPartyNativeSettings
    {
        [VisualStudioContribution]
        internal static SettingCategory ThirdPartyCategory { get; } = new("codeJanitorThirdParty", "Third Party", NativeSettingCategories.RootCategory);

        [VisualStudioContribution]
        internal static Setting.Boolean UseJetBrainsReSharperCleanup { get; } = new(
            "codeJanitorThirdPartyUseJetBrainsReSharperCleanup", "Use JetBrains ReSharper cleanup", ThirdPartyCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean UseTelerikJustCodeCleanup { get; } = new(
            "codeJanitorThirdPartyUseTelerikJustCodeCleanup", "Use Telerik JustCode cleanup", ThirdPartyCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean UseXAMLStylerCleanup { get; } = new(
            "codeJanitorThirdPartyUseXAMLStylerCleanup", "Use XAML Styler cleanup", ThirdPartyCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.String OtherCleaningCommandsExpression { get; } = new(
            "codeJanitorThirdPartyOtherCleaningCommandsExpression", "Other cleaning commands expression", ThirdPartyCategory, defaultValue: string.Empty);
    }
#pragma warning restore VSEXTPREVIEW_SETTINGS
}
