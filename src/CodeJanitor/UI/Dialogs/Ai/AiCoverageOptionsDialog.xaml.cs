using System.Reflection;
using System.Windows;

namespace CodeJanitor.UI.Dialogs.Ai;

/// <summary>
/// Interaction logic for AiCoverageOptionsDialog.xaml
/// </summary>
public partial class AiCoverageOptionsDialog : Window
{
    public AiCoverageOptionsDialog()
    {
        Application.ResourceAssembly = Assembly.GetExecutingAssembly();
        InitializeComponent();
    }

    public AiCoverageOptionsDialog(AiCoverageOptionsViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += (s, e) => Close();
    }
}
