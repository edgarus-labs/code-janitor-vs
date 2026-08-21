using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.Options;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

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

            return (UIElement)_host ?? new TextBlock
            {
                Text = "Code Janitor could not load its package, so these settings are unavailable. "
                    + "Restart Visual Studio, and check ActivityLog.xml if the problem persists.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(11)
            };
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

        var package = CodeJanitorPackage.Instance ?? ForceLoadPackage();
        if (package == null) return;

        _viewModel = CreateViewModel(package, Settings.Default);
        _viewModel.LoadSettings();
        _host = new SectionPageHost { DataContext = _viewModel };
    }
    /// <summary>
    /// The package only auto-loads once a solution is fully loaded, so Tools &gt; Options opened
    /// without a solution has to request the load explicitly.
    /// </summary>
    private static CodeJanitorPackage ForceLoadPackage()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var shell = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsShell)) as IVsShell;
        if (shell == null) return null;

        var packageGuid = new Guid(PackageGuids.GuidCodeJanitorPackageString);
        shell.LoadPackage(ref packageGuid, out _);

        return CodeJanitorPackage.Instance;
    }
}
