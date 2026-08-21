using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace CodeJanitor.UI.Dialogs.About;

/// <summary>
/// Interaction logic for AboutWindow.xaml
/// </summary>

public partial class AboutWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AboutWindow" /> class.
    /// </summary>

    public AboutWindow()
    {
        Application.ResourceAssembly = Assembly.GetExecutingAssembly();

        InitializeComponent();

        VersionTextBlock.Text = $"Version {Vsix.Version}";
    }

    /// <summary>
    /// Called when a key is pressed.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The event arguments.</param>

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        DialogResult = false;
    }

    /// <summary>
    /// Called when an uncaptured left mouse button down event is received on the background.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">
    /// The <see cref="System.Windows.Input.MouseButtonEventArgs" /> instance containing the
    /// event data.
    /// </param>

    private void OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DialogResult = false;
    }

    /// <summary>
    /// Called when the Website link is clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">
    /// The <see cref="System.Windows.RoutedEventArgs" /> instance containing the event data.
    /// </param>

    private void OnWebsiteLinkClick(object sender, RoutedEventArgs e)
    {
        LaunchLink(@"http://www.CodeJanitor.net/");
    }

    /// <summary>
    /// Called when the Visual Studio Marketplace link is clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">
    /// The <see cref="System.Windows.RoutedEventArgs" /> instance containing the event data.
    /// </param>

    private void OnVisualStudioMarketplaceLinkClick(object sender, RoutedEventArgs e)
    {
        LaunchLink(@"https://marketplace.visualstudio.com/items?itemName=CodeJanitor");
    }

    /// <summary>
    /// Called when the GitHub link is clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">
    /// The <see cref="System.Windows.RoutedEventArgs" /> instance containing the event data.
    /// </param>

    private void OnGitHubLinkClick(object sender, RoutedEventArgs e)
    {
        LaunchLink(@"https://github.com/codecadwallader/CodeJanitor");
    }

    /// <summary>
    /// Called when the Twitter link is clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">
    /// The <see cref="System.Windows.RoutedEventArgs" /> instance containing the event data.
    /// </param>

    private void OnTwitterLinkClick(object sender, RoutedEventArgs e)
    {
        LaunchLink(@"https://twitter.com/CodeJanitor/");
    }

    /// <summary>
    /// Attempts to launch the specified link.
    /// </summary>
    /// <param name="link">The link.</param>

    private static void LaunchLink(string link)
    {
        try
        {
            Process.Start(link);
        }
        catch (Exception)
        {
            // Do nothing if default application handler is not associated.
        }
    }
}
