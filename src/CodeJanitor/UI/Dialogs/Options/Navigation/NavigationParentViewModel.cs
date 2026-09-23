using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.Options.Collapsing;
using CodeJanitor.UI.Dialogs.Options.Finding;
using CodeJanitor.UI.Dialogs.Options.Switching;

namespace CodeJanitor.UI.Dialogs.Options.Navigation;

/// <summary>
/// The view model for the Navigation category - hosts the Collapsing, Finding, and Switching
/// view models stacked on one panel. Digging (Spade) intentionally stays on its own page for
/// now (undecided future).
/// </summary>

public sealed class NavigationParentViewModel : CompositeOptionsPageViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationParentViewModel" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <param name="activeSettings">The active settings.</param>

    public NavigationParentViewModel(CodeJanitorPackage package, Settings activeSettings)
        : base(package, activeSettings, new OptionsPageViewModel[]
        {
            new CollapsingViewModel(package, activeSettings),
            new FindingViewModel(package, activeSettings),
            new SwitchingViewModel(package, activeSettings),
        })
    {
    }

    /// <summary>
    /// Gets the header.
    /// </summary>
    public override string Header => "Navigation";
}
