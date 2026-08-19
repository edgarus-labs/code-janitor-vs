using CodeJanitor.Properties;

namespace CodeJanitor.UI.Dialogs.Options.General;

/// <summary>
/// The view model for the General category - hosts the general and features view models as tabs.
/// </summary>

public class GeneralParentViewModel : CompositeOptionsPageViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralParentViewModel" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <param name="activeSettings">The active settings.</param>

    public GeneralParentViewModel(CodeJanitorPackage package, Settings activeSettings)
        : base(package, activeSettings, new OptionsPageViewModel[]
        {
            new GeneralViewModel(package, activeSettings),
            new FeaturesViewModel(package, activeSettings),
        })
    {
    }

    /// <summary>
    /// Gets the header.
    /// </summary>
    public override string Header => Resources.General;
}