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

    /// <summary>
    /// Gets or sets the instance.
    /// </summary>
    public static AiCleanRefactorCommand Instance { get; private set; }

    /// <summary>
    /// This static async initializer creates an AiCleanRefactorCommand from the supplied package, stores it in the Instance field, and registers a settings watcher that asynchronously invokes SwitchAsync whenever Feature_AiCleanRefactor changes.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>A Task value produced by this method.</returns>
    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new AiCleanRefactorCommand(package);
        await package.SettingsMonitor.WatchAsync(s => s.Feature_AiCleanRefactor, Instance.SwitchAsync);
    }

    /// <summary>
    /// This override verifies UI-thread execution then sets Enabled true only when an active C# document exists.
    /// </summary>
    protected override void OnBeforeQueryStatus()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Enabled = Package.ActiveDocument is not null && Package.ActiveDocument.GetCodeLanguage() == CodeLanguage.CSharp;
    }

    /// <summary>
    /// Overrides OnExecute to enforce UI-thread execution, invoke the base implementation, and then call ExecuteWithItem with a null item.
    /// </summary>
    protected override void OnExecute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnExecute();
        ExecuteWithItem(null);
    }

    /// <summary>
    /// ExecuteWithItem requires the UI thread, aborts with MessageBox warnings if AI is unconfigured or no code snippet is found, then asynchronously refactors the item or active context and shows a modal result dialog whose apply callback can replace the original code.
    /// </summary>
    /// <param name="codeItem">The code item.</param>
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

        var context = codeItem is not null
            ? AiContextHelper.GetCodeItemContext(Package, codeItem)
            : AiContextHelper.GetActiveCodeContext(Package);

        if (context is null || string.IsNullOrWhiteSpace(context.CodeSnippet))
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
