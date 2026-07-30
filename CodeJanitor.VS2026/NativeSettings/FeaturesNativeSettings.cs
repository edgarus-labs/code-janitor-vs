using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace CodeJanitor.NativeSettings
{
    // Features category (General > Features), migrated from CodeJanitorShared/Properties/Settings.settings (Feature_*).
#pragma warning disable VSEXTPREVIEW_SETTINGS // Settings APIs are preview.
    internal static class FeaturesNativeSettings
    {
        [VisualStudioContribution]
        internal static SettingCategory FeaturesCategory { get; } = new("codeJanitorFeatures", "Features", GeneralNativeSettings.GeneralCategory);

        [VisualStudioContribution]
        internal static Setting.Boolean BuildProgressToolWindow { get; } = new(
            "codeJanitorFeaturesBuildProgressToolWindow", "Build Progress tool window", FeaturesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean CleanupActiveCode { get; } = new(
            "codeJanitorFeaturesCleanupActiveCode", "Cleanup active code", FeaturesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean CleanupAllCode { get; } = new(
            "codeJanitorFeaturesCleanupAllCode", "Cleanup all code", FeaturesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean CleanupChangedFiles { get; } = new(
            "codeJanitorFeaturesCleanupChangedFiles", "Cleanup changed files", FeaturesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean CleanupOpenCode { get; } = new(
            "codeJanitorFeaturesCleanupOpenCode", "Cleanup open code", FeaturesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean CleanupSelectedCode { get; } = new(
            "codeJanitorFeaturesCleanupSelectedCode", "Cleanup selected code", FeaturesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean CloseAllReadOnly { get; } = new(
            "codeJanitorFeaturesCloseAllReadOnly", "Close all read only", FeaturesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean CollapseAllSolutionExplorer { get; } = new(
            "codeJanitorFeaturesCollapseAllSolutionExplorer", "Collapse all Solution Explorer", FeaturesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean CollapseSelectedSolutionExplorer { get; } = new(
            "codeJanitorFeaturesCollapseSelectedSolutionExplorer", "Collapse selected Solution Explorer", FeaturesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean CommentFormat { get; } = new(
            "codeJanitorFeaturesCommentFormat", "Comment format", FeaturesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean FindInSolutionExplorer { get; } = new(
            "codeJanitorFeaturesFindInSolutionExplorer", "Find in Solution Explorer", FeaturesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean JoinLines { get; } = new(
            "codeJanitorFeaturesJoinLines", "Join lines", FeaturesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean ReadOnlyToggle { get; } = new(
            "codeJanitorFeaturesReadOnlyToggle", "Read only toggle", FeaturesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean RemoveRegion { get; } = new(
            "codeJanitorFeaturesRemoveRegion", "Remove region", FeaturesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean ReorganizeActiveCode { get; } = new(
            "codeJanitorFeaturesReorganizeActiveCode", "Reorganize active code", FeaturesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean SettingCleanupOnSave { get; } = new(
            "codeJanitorFeaturesSettingCleanupOnSave", "Cleanup on save", FeaturesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean SortLines { get; } = new(
            "codeJanitorFeaturesSortLines", "Sort lines", FeaturesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean SpadeToolWindow { get; } = new(
            "codeJanitorFeaturesSpadeToolWindow", "Spade tool window", FeaturesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean SwitchFile { get; } = new(
            "codeJanitorFeaturesSwitchFile", "Switch file", FeaturesCategory, defaultValue: true);
    }
#pragma warning restore VSEXTPREVIEW_SETTINGS
}
