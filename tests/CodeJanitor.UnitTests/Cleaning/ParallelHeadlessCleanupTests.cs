using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CodeJanitor.UnitTests.Cleaning;

/// <summary>
/// Unit tests for parallel headless cleanup.
/// </summary>
[TestClass]
public sealed class ParallelHeadlessCleanupTests
{
    private string _tempDirectory;

    [TestInitialize]
    public void TestInitialize()
    {
        Settings.Default.Reset();
        Settings.Default.Cleaning_ConvertToPatternMatchingNullChecks = true;
        Settings.Default.Cleaning_ConvertStringFormatToInterpolation = true;
        Settings.Default.Cleaning_RemoveByteOrderMark = true;

        _tempDirectory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Settings.Default.Reset();

        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyHeadlessCSharpTransformationsToFiles_CleansMultipleFilesConcurrently()
    {
        var filePaths = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            var filePath = Path.Combine(_tempDirectory, $"Sample_{i}.cs");
            var content = $"namespace Demo;\r\n\r\npublic class C{i} {{ public void M(object x) {{ if (x != null) {{ }} }} }}\r\n";
            File.WriteAllText(filePath, content);
            filePaths.Add(filePath);
        }

        var result = CodeCleanupManager.ApplyHeadlessCSharpTransformationsToFiles(filePaths, maxDegreeOfParallelism: 4);

        Assert.AreEqual(10, result.TotalFiles);
        Assert.AreEqual(10, result.ChangedFiles);
        Assert.AreEqual(0, result.FailedFiles);

        foreach (var filePath in filePaths)
        {
            var cleanedContent = File.ReadAllText(filePath);
            Assert.IsTrue(cleanedContent.Contains("if (x is not null)"), $"File {filePath} was not transformed.");
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyHeadlessCSharpTransformationsToFiles_ReportsProgress()
    {
        var filePaths = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var filePath = Path.Combine(_tempDirectory, $"ProgressSample_{i}.cs");
            var content = $"namespace Demo;\r\n\r\npublic class C{i} {{ }}\r\n";
            File.WriteAllText(filePath, content);
            filePaths.Add(filePath);
        }

        var progressCount = 0;
        var progress = new Progress<CodeCleanupManager.ParallelCleanupProgress>(p => Interlocked.Increment(ref progressCount));

        var result = CodeCleanupManager.ApplyHeadlessCSharpTransformationsToFiles(filePaths, progress: progress);

        Assert.AreEqual(5, result.TotalFiles);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void BaseProgressViewModel_ProgressPercentText_CalculatesAccurately()
    {
        var vm = new TestProgressViewModel();

        // 0 of 0
        vm.CountTotal = 0;
        vm.ProcessedCount = 0;
        Assert.AreEqual("0%", vm.ProgressPercentText);

        // 0 of 10
        vm.CountTotal = 10;
        vm.ProcessedCount = 0;
        Assert.AreEqual("0%", vm.ProgressPercentText);

        // 5 of 10
        vm.ProcessedCount = 5;
        Assert.AreEqual("50%", vm.ProgressPercentText);

        // 10 of 10
        vm.ProcessedCount = 10;
        Assert.AreEqual("100%", vm.ProgressPercentText);

        // 1 of 3 (33%)
        vm.CountTotal = 3;
        vm.ProcessedCount = 1;
        Assert.AreEqual("33%", vm.ProgressPercentText);
    }

    /// <summary>
    /// Provides a test-specific implementation of the base progress view model for validating cleanup progress dialog behavior.
    /// </summary>
    private sealed class TestProgressViewModel : CodeJanitor.UI.Dialogs.CleanupProgress.BaseProgressViewModel
    {
        /// <summary>
        /// Handles the execution of the cancel command without performing any action, serving as a no-op override for scenarios where cancellation is explicitly ignored.
        /// </summary>
        /// <param name="parameter">The parameter.</param>
        protected override void OnCancelCommandExecuted(object parameter)
        {
        }
    }
}
