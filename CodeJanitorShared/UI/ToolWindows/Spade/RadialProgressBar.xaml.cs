using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace CodeJanitor.UI.ToolWindows.Spade;

/// <summary>
/// A circular UI control that visually represents the completion progress of an operation along a radial arc.
/// </summary>
public partial class RadialProgressBar : UserControl
{
    public RadialProgressBar()
    {
        Application.ResourceAssembly = Assembly.GetExecutingAssembly();

        InitializeComponent();
    }
}
