using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace CodeJanitor.NativeSettings
{
    // Shared root category so every settings page nests under a single "Code Janitor" node.
#pragma warning disable VSEXTPREVIEW_SETTINGS // Settings APIs are preview.
    internal static class NativeSettingCategories
    {
        [VisualStudioContribution]
        internal static SettingCategory RootCategory { get; } = new("codeJanitor", "Code Janitor");
    }
#pragma warning restore VSEXTPREVIEW_SETTINGS
}
