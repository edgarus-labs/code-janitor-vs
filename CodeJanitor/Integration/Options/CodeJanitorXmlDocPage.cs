using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.Options;
using CodeJanitor.UI.Dialogs.Options.XmlDoc;

namespace CodeJanitor.Integration.Options;

/// <summary>
/// Presents a Visual Studio Tools &gt; Options page for XML documentation settings.
/// </summary>
public sealed class CodeJanitorXmlDocPage : CodeJanitorSectionDialogPage
{
    /// <summary>
    /// Creates and returns a new XmlDocViewModel using the provided package and settings.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="settings">The settings.</param>
    /// <returns>An OptionsPageViewModel value produced by this method.</returns>
    protected override OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings) =>
        new XmlDocViewModel(package, settings);
}
