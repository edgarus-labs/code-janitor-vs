using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace CodeJanitor.NativeSettings
{
    // Finding category, migrated from CodeJanitorShared/Properties/Settings.settings (Finding_*).
#pragma warning disable VSEXTPREVIEW_SETTINGS // Settings APIs are preview.
    internal static class FindingNativeSettings
    {
        [VisualStudioContribution]
        internal static SettingCategory FindingCategory { get; } = new("codeJanitorFinding", "Finding", NativeSettingCategories.RootCategory);

        [VisualStudioContribution]
        internal static Setting.Boolean ClearSolutionExplorerSearch { get; } = new(
            "codeJanitorFindingClearSolutionExplorerSearch", "Clear Solution Explorer search", FindingCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean TemporarilyOpenSolutionFolders { get; } = new(
            "codeJanitorFindingTemporarilyOpenSolutionFolders", "Temporarily open solution folders", FindingCategory, defaultValue: true);
    }
#pragma warning restore VSEXTPREVIEW_SETTINGS
}
