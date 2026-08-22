using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Ai;
using CodeJanitor.Model.CodeItems;
using CodeJanitor.UI.Dialogs.Ai;
using System.Threading.Tasks;
using System.Windows;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.Integration.Commands;

/// <summary>
/// A command that invokes AI explanation and complexity decomposition for the current method or selection.
/// </summary>
internal sealed class AiExplainCommand : BaseCommand
{
    private readonly AiExplainLogic _logic;

    internal AiExplainCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorAiExplainMethod)
    {
        _logic = AiExplainLogic.GetInstance(Package);
    }

    public static AiExplainCommand Instance { get; private set; }

    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new AiExplainCommand(package);
        await package.SettingsMonitor.WatchAsync(s => s.Feature_AiExplainMethod, Instance.SwitchAsync);
    }

    protected override void OnBeforeQueryStatus()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Enabled = Package.ActiveDocument != null && Package.ActiveDocument.GetCodeLanguage() == CodeLanguage.CSharp;
    }

    protected override void OnExecute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnExecute();
        ExecuteWithItem(null);
    }

    internal void ExecuteWithItem(ICodeItem codeItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!AiExplainLogic.IsConfigurationPresent())
        {
            MessageBox.Show(
                "AI endpoint is not configured. Please configure your OpenAI-compatible endpoint in Tools > Options > Code Janitor > XML Documentation.",
                "CodeJanitor AI Assistant",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var context = codeItem != null
            ? AiContextHelper.GetCodeItemContext(Package, codeItem)
            : AiContextHelper.GetActiveCodeContext(Package);

        if (context == null || string.IsNullOrWhiteSpace(context.CodeSnippet))
        {
            MessageBox.Show("Could not find code to explain.", "CodeJanitor AI Assistant", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Package.JoinableTaskFactory.RunAsync(async delegate
        {
            var explanation = await _logic.ExplainCodeAsync(context.TargetName, context.CodeSnippet);

            await Package.JoinableTaskFactory.SwitchToMainThreadAsync();

            var viewModel = new AiResultViewModel(
                title: "💡 AI Explanation & Complexity Analysis",
                header: $"Target: {context.TargetName}",
                contentText: explanation,
                codeSnippet: null,
                onApply: null);

            var window = new AiResultWindow(viewModel)
            {
                Owner = Application.Current?.MainWindow
            };
            window.ShowDialog();
        });
    }
}
