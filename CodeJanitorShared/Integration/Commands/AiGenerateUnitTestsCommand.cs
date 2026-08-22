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
/// A command that generates unit tests for the current class or method using AI.
/// </summary>
internal sealed class AiGenerateUnitTestsCommand : BaseCommand
{
    private readonly AiTestGeneratorLogic _logic;

    internal AiGenerateUnitTestsCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorAiGenerateUnitTests)
    {
        _logic = AiTestGeneratorLogic.GetInstance(Package);
    }

    public static AiGenerateUnitTestsCommand Instance { get; private set; }

    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new AiGenerateUnitTestsCommand(package);
        await package.SettingsMonitor.WatchAsync(s => s.Feature_AiGenerateUnitTests, Instance.SwitchAsync);
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

        if (!AiTestGeneratorLogic.IsConfigurationPresent())
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
            MessageBox.Show("Could not find code to generate unit tests for.", "CodeJanitor AI Assistant", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Package.JoinableTaskFactory.RunAsync(async delegate
        {
            var generatedTests = await _logic.GenerateUnitTestsAsync(context.TargetName, context.CodeSnippet);

            await Package.JoinableTaskFactory.SwitchToMainThreadAsync();

            var viewModel = new AiResultViewModel(
                title: "🧪 AI Unit Test Generator",
                header: $"Target: {context.TargetName}",
                contentText: generatedTests,
                codeSnippet: generatedTests,
                onApply: context.InsertAction,
                applyButtonText: "➕ Insert at Cursor");

            var window = new AiResultWindow(viewModel)
            {
                Owner = Application.Current?.MainWindow
            };
            window.ShowDialog();
        });
    }
}
