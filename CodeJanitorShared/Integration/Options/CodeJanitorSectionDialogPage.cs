using Microsoft.VisualStudio.Shell;
using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.Options;
using System.ComponentModel;
using System.Windows;

namespace CodeJanitor.Integration.Options;

/// <summary>
/// Base class for all native VS Options pages hosted by Code Janitor.
/// Each concrete subclass provides a single settings section ViewModel.
/// VS handles tree navigation, OK/Cancel, and page lifecycle.
/// </summary>

public abstract class CodeJanitorSectionDialogPage : UIElementDialogPage
{
    private SectionPageHost _host;
    private OptionsPageViewModel _viewModel;

    /// <summary>
    /// Creates the ViewModel for this settings section.
    /// Called lazily when the page is first shown.
    /// </summary>

    protected abstract OptionsPageViewModel CreateViewModel(CodeJanitorPackage package, Settings settings);

    /// <inheritdoc />

    protected override UIElement Child
    {
        get
        {
            EnsureInitialized();

            return _host;
        }
    }

    /// <inheritdoc />

    protected override void OnActivate(CancelEventArgs e)
    {
        base.OnActivate(e);
        EnsureInitialized();
        _viewModel?.LoadSettings();
    }

    /// <inheritdoc />

    public override void SaveSettingsToStorage()
    {
        _viewModel?.SaveSettings();
        Settings.Default.Save();
        base.SaveSettingsToStorage();
    }

    /// <summary>
    /// Initializes the view model and host only if not already initialized, loading settings from the default configuration and assigning both to the backing fields.
    /// </summary>

    private void EnsureInitialized()
    {
        if (_host != null) return;

        var package = CodeJanitorPackage.Instance;
        if (package == null) return;

        _viewModel = CreateViewModel(package, Settings.Default);
        _viewModel.LoadSettings();
        _host = new SectionPageHost { DataContext = _viewModel };
    }
}
