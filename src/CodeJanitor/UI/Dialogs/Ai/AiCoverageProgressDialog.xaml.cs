using System.Reflection;
using System.Windows;

namespace CodeJanitor.UI.Dialogs.Ai;

/// <summary>
/// Interaction logic for AiCoverageProgressDialog.xaml
/// </summary>
public partial class AiCoverageProgressDialog : Window
{
    public AiCoverageProgressDialog()
    {
        Application.ResourceAssembly = Assembly.GetExecutingAssembly();
        InitializeComponent();
    }

    public AiCoverageProgressDialog(AiCoverageProgressViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += (s, e) => Close();
    }
}
