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

    public static AiCodeReviewCommand Instance { get; private set; }

    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new AiCodeReviewCommand(package);
        await package.SettingsMonitor.WatchAsync(s => s.Feature_AiCodeReview, Instance.SwitchAsync);
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

        if (!AiCodeReviewLogic.IsConfigurationPresent())
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
