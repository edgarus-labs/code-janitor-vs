using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace CodeJanitor.NativeSettings
{
    // Migrated from CodeJanitorShared/Properties/Settings.settings (Digging_* / Spade tool window).
#pragma warning disable VSEXTPREVIEW_SETTINGS // Settings APIs are preview.
    internal static class DiggingNativeSettings
    {
        [VisualStudioContribution]
        internal static SettingCategory DiggingCategory { get; } = new("codeJanitorDigging", "Digging", NativeSettingCategories.RootCategory);

        [VisualStudioContribution]
        internal static Setting.Boolean CenterOnWhole { get; } = new(
            "codeJanitorDiggingCenterOnWhole", "Center on whole", DiggingCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Integer ComplexityAlertThreshold { get; } = new(
            "codeJanitorDiggingComplexityAlertThreshold", "Complexity alert threshold", DiggingCategory, defaultValue: 15);

        [VisualStudioContribution]
        internal static Setting.Integer ComplexityWarningThreshold { get; } = new(
            "codeJanitorDiggingComplexityWarningThreshold", "Complexity warning threshold", DiggingCategory, defaultValue: 10);

        [VisualStudioContribution]
        internal static Setting.Integer IndentationMargin { get; } = new(
            "codeJanitorDiggingIndentationMargin", "Indentation margin", DiggingCategory, defaultValue: 17);

        [VisualStudioContribution]
        internal static Setting.Integer PrimarySortOrder { get; } = new(
            "codeJanitorDiggingPrimarySortOrder", "Primary sort order", DiggingCategory, defaultValue: 0);

        [VisualStudioContribution]
        internal static Setting.Boolean SecondarySortTypeByName { get; } = new(
            "codeJanitorDiggingSecondarySortTypeByName", "Secondary sort type by name", DiggingCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean ShowItemComplexity { get; } = new(
            "codeJanitorDiggingShowItemComplexity", "Show item complexity", DiggingCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean ShowItemMetadata { get; } = new(
            "codeJanitorDiggingShowItemMetadata", "Show item metadata", DiggingCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean ShowItemTypes { get; } = new(
            "codeJanitorDiggingShowItemTypes", "Show item types", DiggingCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean ShowMethodParameters { get; } = new(
            "codeJanitorDiggingShowMethodParameters", "Show method parameters", DiggingCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean SynchronizeOutlining { get; } = new(
            "codeJanitorDiggingSynchronizeOutlining", "Synchronize outlining", DiggingCategory, defaultValue: true);
    }
#pragma warning restore VSEXTPREVIEW_SETTINGS
}
