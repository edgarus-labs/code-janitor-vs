using System.Reflection;
using System.Windows;

namespace CodeJanitor.UI.Dialogs.CleanupOptions;

/// <summary>
/// Interaction logic for CleanupOptionsWindow.xaml
/// </summary>

public partial class CleanupOptionsWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CleanupOptionsWindow" /> class.
    /// </summary>

    public CleanupOptionsWindow()
    {
        Application.ResourceAssembly = Assembly.GetExecutingAssembly();
        InitializeComponent();
    }
}