using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace CodeJanitor.NativeSettings
{
    // Collapsing category, migrated from CodeJanitorShared/Properties/Settings.settings (Collapsing_*).
#pragma warning disable VSEXTPREVIEW_SETTINGS // Settings APIs are preview.
    internal static class CollapsingNativeSettings
    {
        [VisualStudioContribution]
        internal static SettingCategory CollapsingCategory { get; } = new("codeJanitorCollapsing", "Collapsing", NativeSettingCategories.RootCategory);

        [VisualStudioContribution]
        internal static Setting.Boolean CollapseSolutionWhenOpened { get; } = new(
            "codeJanitorCollapsingCollapseSolutionWhenOpened", "Collapse solution when opened", CollapsingCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean KeepSoloProjectExpanded { get; } = new(
            "codeJanitorCollapsingKeepSoloProjectExpanded", "Keep solo project expanded", CollapsingCategory, defaultValue: true);
    }
#pragma warning restore VSEXTPREVIEW_SETTINGS
}
