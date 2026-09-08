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
/// A command that performs an AI-assisted Code Review & Smell Detection for the active document or member.
/// </summary>
internal sealed class AiCodeReviewCommand : BaseCommand
{
    private readonly AiCodeReviewLogic _logic;

    internal AiCodeReviewCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorAiCodeReview)
    {
        _logic = AiCodeReviewLogic.GetInstance(Package);
    }

    /// <summary>
    /// Gets or sets the instance.
    /// </summary>
    public static AiCodeReviewCommand Instance { get; private set; }

    /// <summary>
    /// izes the AI code review command by creating a new instance assigned to the static Instance field and subscribes it to settings changes for the Feature_AiCodeReview option to invoke SwitchAsync when toggled.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>A Task value produced by this method.</returns>
    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new AiCodeReviewCommand(package);
        await package.SettingsMonitor.WatchAsync(s => s.Feature_AiCodeReview, Instance.SwitchAsync);
    }

    /// <summary>
    /// Sets the command&apos;s Enabled state to true only when an active document exists and its code language is C#, ensuring UI thread execution.
    /// </summary>
    protected override void OnBeforeQueryStatus()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Enabled = Package.ActiveDocument is not null && Package.ActiveDocument.GetCodeLanguage() == CodeLanguage.CSharp;
    }

    /// <summary>
    /// This override of OnExecute first enforces UI-thread execution (throwing otherwise), invokes the base implementation, and then calls ExecuteWithItem with a null item.
    /// </summary>
    protected override void OnExecute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnExecute();
        ExecuteWithItem(null);
    }

    /// <summary>
    /// ates AI configuration, gathers the code context for the given or active item, and asynchronously runs an AI code review on the UI thread, displaying the resulting report in a modal `AiResultWindow` (showing warning or info MessageBoxes and aborting if configuration is missing or no code snippet is found).
    /// </summary>
    /// <param name="codeItem">The code item.</param>
    internal void ExecuteWithItem(ICodeItem codeItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!AiCodeReviewLogic.IsConfigurationPresent())
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
            MessageBox.Show("Could not find code to review.", "CodeJanitor AI Assistant", MessageBoxButton.OK, MessageBoxImage.Information);

            return;
        }

        Package.JoinableTaskFactory.RunAsync(async delegate
        {
            var reviewReport = await _logic.ReviewCodeAsync(context.TargetName, context.CodeSnippet);

            await Package.JoinableTaskFactory.SwitchToMainThreadAsync();

            var viewModel = new AiResultViewModel(
                title: "🔍 AI Code Review & Smell Detector",
                header: $"Target: {context.TargetName}",
                contentText: reviewReport,
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
