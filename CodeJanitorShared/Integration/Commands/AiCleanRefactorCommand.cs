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
/// A command that performs Clean Code refactoring and Guard Clause adoption using AI.
/// </summary>
internal sealed class AiCleanRefactorCommand : BaseCommand
{
    private readonly AiCleanRefactorLogic _logic;

    internal AiCleanRefactorCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorAiCleanRefactor)
    {
        _logic = AiCleanRefactorLogic.GetInstance(Package);
    }

    public static AiCleanRefactorCommand Instance { get; private set; }

    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new AiCleanRefactorCommand(package);
        await package.SettingsMonitor.WatchAsync(s => s.Feature_AiCleanRefactor, Instance.SwitchAsync);
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

        if (!AiCleanRefactorLogic.IsConfigurationPresent())
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
            MessageBox.Show("Could not find code to refactor.", "CodeJanitor AI Assistant", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Package.JoinableTaskFactory.RunAsync(async delegate
        {
            var result = await _logic.RefactorCodeAsync(context.TargetName, context.CodeSnippet);

            await Package.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (!result.Success)
            {
                MessageBox.Show(result.ErrorMessage ?? "Refactoring failed.", "CodeJanitor AI Assistant", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var viewModel = new AiResultViewModel(
                title: "✨ AI Clean Code & Guard Clauses Refactor",
                header: $"Target: {context.TargetName}",
                contentText: result.Explanation,
                codeSnippet: result.RefactoredCode,
                onApply: context.ReplaceAction,
                applyButtonText: "⚡ Apply Refactored Code");

            var window = new AiResultWindow(viewModel)
            {
                Owner = Application.Current?.MainWindow
            };
            window.ShowDialog();
        });
    }
}
