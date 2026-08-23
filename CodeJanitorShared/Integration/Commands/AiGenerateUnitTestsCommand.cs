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

    /// <summary>
    /// This static method creates an AiGenerateUnitTestsCommand instance, assigns it to the static Instance field, and asynchronously registers a settings watcher that invokes SwitchAsync whenever the Feature_AiGenerateUnitTests flag changes.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>A Task value produced by this method.</returns>
    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new AiGenerateUnitTestsCommand(package);
        await package.SettingsMonitor.WatchAsync(s => s.Feature_AiGenerateUnitTests, Instance.SwitchAsync);
    }

    /// <summary>
    /// Sets the command&apos;s Enabled state to true when an active document exists and its detected code language is CSharp, ensuring the command only activates for C# files in the UI thread.
    /// </summary>
    protected override void OnBeforeQueryStatus()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Enabled = Package.ActiveDocument is not null && Package.ActiveDocument.GetCodeLanguage() == CodeLanguage.CSharp;
    }

    /// <summary>
    /// Overrides OnExecute to require the UI thread, invoke the base implementation, and then call ExecuteWithItem with a null item.
    /// </summary>
    protected override void OnExecute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnExecute();
        ExecuteWithItem(null);
    }

    /// <summary>
    /// On the UI thread this method validates AI configuration and code context (from the given item or active document), showing MessageBoxes and returning early on failure, then asynchronously generates unit tests and displays them in a modal dialog that can insert the result at the cursor.
    /// </summary>
    /// <param name="codeItem">The code item.</param>
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

        var context = codeItem is not null
            ? AiContextHelper.GetCodeItemContext(Package, codeItem)
            : AiContextHelper.GetActiveCodeContext(Package);

        if (context is null || string.IsNullOrWhiteSpace(context.CodeSnippet))
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
