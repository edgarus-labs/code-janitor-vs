using System.Reflection;
using System.Windows;

namespace CodeJanitor.UI.Dialogs.Ai;

/// <summary>
/// Interaction logic for AiResultWindow.xaml
/// </summary>
public partial class AiResultWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AiResultWindow"/> class.
    /// </summary>
    public AiResultWindow()
    {
        Application.ResourceAssembly = Assembly.GetExecutingAssembly();
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AiResultWindow"/> class with a ViewModel.
    /// </summary>
    public AiResultWindow(AiResultViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += (s, e) => Close();
    }
}
