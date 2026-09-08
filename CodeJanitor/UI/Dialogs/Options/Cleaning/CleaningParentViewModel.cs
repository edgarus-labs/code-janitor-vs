using CodeJanitor.Properties;

namespace CodeJanitor.UI.Dialogs.Options.Cleaning;

/// <summary>
/// The view model for cleaning options - hosts the more specific cleaning view models as tabs.
/// </summary>

public sealed class CleaningParentViewModel : CompositeOptionsPageViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CleaningParentViewModel" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <param name="activeSettings">The active settings.</param>

    public CleaningParentViewModel(CodeJanitorPackage package, Settings activeSettings)
        : base(package, activeSettings, new OptionsPageViewModel[]
        {
            new CleaningGeneralViewModel(package, activeSettings),
            new CleaningFileTypesViewModel(package, activeSettings),
            new CleaningVisualStudioViewModel(package, activeSettings),
            new CleaningInsertViewModel(package, activeSettings),
            new CleaningRemoveViewModel(package, activeSettings),
            new CleaningUpdateViewModel(package, activeSettings),
        })
    {
    }

    /// <summary>
    /// Gets the header.
    /// </summary>
    public override string Header => Resources.Cleaning_n;
}
