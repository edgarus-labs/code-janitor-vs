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

    /// <summary>
    /// Gets or sets the instance.
    /// </summary>
    public static AiExplainCommand Instance { get; private set; }

    /// <summary>
    /// Initializes the AI explain command by creating an AiExplainCommand instance assigned to the static Instance field and asynchronously watching the Feature_AiExplainMethod setting to invoke SwitchAsync on changes.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>A Task value produced by this method.</returns>
    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new AiExplainCommand(package);
        await package.SettingsMonitor.WatchAsync(s => s.Feature_AiExplainMethod, Instance.SwitchAsync);
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
    /// This override first ensures execution occurs on the UI thread, then invokes the base OnExecute implementation, and finally calls ExecuteWithItem with a null argument.
    /// </summary>
    protected override void OnExecute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnExecute();
        ExecuteWithItem(null);
    }

    /// <summary>
    /// Executes an AI-based code explanation asynchronously on the given code item (or active code context if null), displaying a modal result window with the explanation, and showing MessageBox warnings when AI configuration is missing or no code context is available.
    /// </summary>
    /// <param name="codeItem">The code item.</param>
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

        var context = codeItem is not null
            ? AiContextHelper.GetCodeItemContext(Package, codeItem)
            : AiContextHelper.GetActiveCodeContext(Package);

        if (context is null || string.IsNullOrWhiteSpace(context.CodeSnippet))
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
