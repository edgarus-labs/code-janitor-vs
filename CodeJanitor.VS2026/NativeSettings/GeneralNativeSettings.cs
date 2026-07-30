using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace CodeJanitor.NativeSettings
{
    // General category, migrated from CodeJanitorShared/Properties/Settings.settings.
    // General_Theme, General_IconSet and General_Font are intentionally NOT migrated -
    // they're being dropped in favor of the existing always-on theme/icon auto-detection.
#pragma warning disable VSEXTPREVIEW_SETTINGS // Settings APIs are preview.
    internal static class GeneralNativeSettings
    {
        [VisualStudioContribution]
        internal static SettingCategory GeneralCategory { get; } = new("codeJanitorGeneral", "General", NativeSettingCategories.RootCategory);

        [VisualStudioContribution]
        internal static Setting.Boolean CacheFiles { get; } = new(
            "codeJanitorGeneralCacheFiles", "Cache file code models", GeneralCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean DiagnosticsMode { get; } = new(
            "codeJanitorGeneralDiagnosticsMode", "Diagnostics mode", GeneralCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean LoadModelsAsynchronously { get; } = new(
            "codeJanitorGeneralLoadModelsAsynchronously", "Load models asynchronously", GeneralCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean ShowStartPageOnSolutionClose { get; } = new(
            "codeJanitorGeneralShowStartPageOnSolutionClose", "Show start page when a solution is closed", GeneralCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean SkipUndoTransactionsDuringAutoCleanupOnSave { get; } = new(
            "codeJanitorGeneralSkipUndoTransactionsDuringAutoCleanupOnSave", "Skip during automatic cleanup on save", GeneralCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean UseUndoTransactions { get; } = new(
            "codeJanitorGeneralUseUndoTransactions", "Use undo transactions", GeneralCategory, defaultValue: true);
    }
#pragma warning restore VSEXTPREVIEW_SETTINGS
}
