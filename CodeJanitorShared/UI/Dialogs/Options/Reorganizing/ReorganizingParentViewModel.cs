using CodeJanitor.Properties;

namespace CodeJanitor.UI.Dialogs.Options.Reorganizing;

/// <summary>
/// The view model for reorganizing options - hosts the more specific reorganizing view models as tabs.
/// </summary>

public class ReorganizingParentViewModel : CompositeOptionsPageViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReorganizingParentViewModel" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <param name="activeSettings">The active settings.</param>

    public ReorganizingParentViewModel(CodeJanitorPackage package, Settings activeSettings)
        : base(package, activeSettings, new OptionsPageViewModel[]
        {
            new ReorganizingGeneralViewModel(package, activeSettings),
            new ReorganizingTypesViewModel(package, activeSettings),
        })
    {
    }

    /// <summary>
    /// Gets the header.
    /// </summary>
    public override string Header => Resources.Reorganizing;
}
