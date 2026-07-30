using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace CodeJanitor.NativeSettings
{
    // Pilot: single-setting slice used to validate the native Settings API pipeline
    // (see docs/todo/post-migration-backlog.md BL-006) before scaling to all pages.
#pragma warning disable VSEXTPREVIEW_SETTINGS // Settings APIs are preview.
    internal static class SwitchingNativeSettings
    {
        [VisualStudioContribution]
        internal static SettingCategory SwitchingCategory { get; } = new("codeJanitorSwitching", "Switching", NativeSettingCategories.RootCategory);

        [VisualStudioContribution]
        internal static Setting.String RelatedFileExtensionsExpression { get; } = new(
            "codeJanitorSwitchingRelatedFileExtensionsExpression",
            "Related file extensions expression",
            SwitchingCategory,
            defaultValue: ".cpp .h||.xaml .xaml.cs||.xml .xsd||.ascx .ascx.cs||.aspx .aspx.cs||.master .master.cs||.cshtml .cshtml.cs");
    }
#pragma warning restore VSEXTPREVIEW_SETTINGS
}

