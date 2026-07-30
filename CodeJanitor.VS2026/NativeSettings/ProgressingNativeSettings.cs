using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace CodeJanitor.NativeSettings
{
    // Progressing category, migrated from CodeJanitorShared/Properties/Settings.settings (Progressing_*).
#pragma warning disable VSEXTPREVIEW_SETTINGS // Settings APIs are preview.
    internal static class ProgressingNativeSettings
    {
        [VisualStudioContribution]
        internal static SettingCategory ProgressingCategory { get; } = new("codeJanitorProgressing", "Progressing", NativeSettingCategories.RootCategory);

        [VisualStudioContribution]
        internal static Setting.Boolean HideBuildProgressOnBuildStop { get; } = new(
            "codeJanitorProgressingHideBuildProgressOnBuildStop", "Hide build progress on build stop", ProgressingCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean ShowBuildProgressOnBuildStart { get; } = new(
            "codeJanitorProgressingShowBuildProgressOnBuildStart", "Show build progress on build start", ProgressingCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean ShowProgressOnWindowsTaskbar { get; } = new(
            "codeJanitorProgressingShowProgressOnWindowsTaskbar", "Show progress on Windows taskbar", ProgressingCategory, defaultValue: true);
    }
#pragma warning restore VSEXTPREVIEW_SETTINGS
}
