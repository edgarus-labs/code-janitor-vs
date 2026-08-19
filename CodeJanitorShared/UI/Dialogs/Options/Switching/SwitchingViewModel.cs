using CodeJanitor.Properties;

namespace CodeJanitor.UI.Dialogs.Options.Switching;

/// <summary>
/// The view model for switching options.
/// </summary>

public class SwitchingViewModel : OptionsPageViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SwitchingViewModel" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <param name="activeSettings">The active settings.</param>

    public SwitchingViewModel(CodeJanitorPackage package, Settings activeSettings)
        : base(package, activeSettings)
    {
        Mappings = new SettingsToOptionsList(ActiveSettings, this)
        {
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Switching_RelatedFileExtensionsExpression, x => RelatedFileExtensionsExpression)
        };
    }

    /// <summary>
    /// Gets the header.
    /// </summary>
    public override string Header => Resources.SwitchingViewModel_Switching;

    /// <summary>
    /// Gets or sets the expression for related file extensions.
    /// </summary>

    public string RelatedFileExtensionsExpression
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }
}