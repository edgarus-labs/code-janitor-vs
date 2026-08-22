using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Ai;
using CodeJanitor.Model.CodeItems;
using CodeJanitor.UI.Dialogs.Ai;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.Integration.Commands;

/// <summary>
/// A command that opens the Target Code Coverage test generator, shows real-time progress with cancel support, and outputs the resulting test suite.
/// </summary>
internal sealed class AiTargetCoverageCommand : BaseCommand
{
    internal AiTargetCoverageCommand(CodeJanitorPackage package)
        : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorAiTargetCoverage)
    {
    }

    public static AiTargetCoverageCommand Instance { get; private set; }

    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new AiTargetCoverageCommand(package);
        await package.SettingsMonitor.WatchAsync(s => s.Feature_AiTargetCoverage, Instance.SwitchAsync);
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

        if (!AiCoverageTargetLogic.IsConfigurationPresent())
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
            MessageBox.Show("Could not find code to generate tests for.", "CodeJanitor AI Assistant", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Step 1: Options Dialog
        var optionsVm = new AiCoverageOptionsViewModel(context.TargetName);
        var optionsDialog = new AiCoverageOptionsDialog(optionsVm)
        {
            Owner = Application.Current?.MainWindow
        };

        var dialogResult = optionsDialog.ShowDialog();
        if (!optionsVm.IsConfirmed)
        {
            return;
        }

        // Step 2: Progress Dialog + Engine Execution
        var cts = new CancellationTokenSource();
        var progressVm = new AiCoverageProgressViewModel(context.TargetName, optionsVm.TargetCoveragePercentage, cts);
        var progressDialog = new AiCoverageProgressDialog(progressVm)
        {
            Owner = Application.Current?.MainWindow
        };

        var logic = AiCoverageTargetLogic.CreateFromSettings();

        Package.JoinableTaskFactory.RunAsync(async delegate
        {
            AiCoverageResult coverageResult = null;
            try
            {
                coverageResult = await logic.GenerateTestsToTargetCoverageAsync(
                    targetName: context.TargetName,
                    codeSnippet: context.CodeSnippet,
                    targetCoveragePct: optionsVm.TargetCoveragePercentage,
                    maxIterations: optionsVm.MaxIterations,
                    testFramework: optionsVm.TestFramework,
                    mockingLib: optionsVm.MockingLibrary,
                    progress: progressVm,
                    cancellationToken: cts.Token);
            }
            catch (OperationCanceledException)
            {
                // User cancelled
            }
            finally
            {
                await Package.JoinableTaskFactory.SwitchToMainThreadAsync();
                progressDialog.Close();
            }

            if (coverageResult == null)
            {
                return;
            }

            if (!coverageResult.Success)
            {
                MessageBox.Show(coverageResult.ErrorMessage ?? "Generation failed.", "CodeJanitor AI Assistant", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Step 3: Show Result in AiResultWindow
            var resultVm = new AiResultViewModel(
                title: $"🎯 Target Coverage Suite: {coverageResult.AchievedCoveragePercentage}% (Goal: {coverageResult.TargetCoveragePercentage}%)",
                header: $"Target: {context.TargetName} | Iterations: {coverageResult.IterationsUsed}",
                contentText: coverageResult.CoverageSummaryReport,
                codeSnippet: coverageResult.GeneratedTestCode,
                onApply: context.InsertAction,
                applyButtonText: "➕ Insert at Cursor");

            var resultWindow = new AiResultWindow(resultVm)
            {
                Owner = Application.Current?.MainWindow
            };
            resultWindow.ShowDialog();
        });

        progressDialog.ShowDialog();
    }
}
