using Microsoft.VisualStudio.Extensibility;

namespace CodeJanitor.NativeSettings;

#pragma warning disable VSEXTPREVIEW_SETTINGS // Settings APIs are preview.
// VisualStudio.Extensibility projects must contribute exactly one class extending Extension
// (VSEXT0004). The real CodeJanitorNativeSettingsExtension is compiled out by default (see
// EnableNativeSettings in CodeJanitor.VS2026.csproj - parked, not deleted, due to
// CodeJanitorNativeSettingsExtension.InitializeAsync never reliably activating; the classic
// WPF Options UI is the real settings UI again). This inert placeholder satisfies that
// requirement in the meantime and is compiled out once native settings are re-enabled.
#if !CODEJANITOR_NATIVE_SETTINGS

[VisualStudioContribution]
internal sealed class PlaceholderExtension : Extension
{
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        RequiresInProcessHosting = true,
    };
}

#endif