using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
        Settings.Default.Cleaning_ConvertStringFormatToInterpolation = true;
        Settings.Default.Cleaning_RemoveByteOrderMark = true;

        // The samples are files of a C# 10+ project (file-scoped namespaces).
        CSharpLanguageVersionSupport.SetLanguageVersionResolver(_ => new[] { LanguageVersion.CSharp12 });

        _tempDirectory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Settings.Default.Reset();
        CSharpLanguageVersionSupport.SetLanguageVersionResolver(null);

        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async System.Threading.Tasks.Task RunSemanticStepsAsync_LaterStepThrows_FileRewrittenByAnEarlierStepIsStillCountedAndTheErrorPropagates()
    {
        int changedCount = 0;
        var steps = new Func<System.Threading.Tasks.Task<bool>>[]
        {
            () => System.Threading.Tasks.Task.FromResult(true),
            () => throw new IOException("sealing failed"),
        };

        IOException error = await Assert.ThrowsExactlyAsync<IOException>(
            () => CodeJanitor.UI.Dialogs.CleanupProgress.CleanupProgressViewModel.RunSemanticStepsAsync(steps, () => changedCount++));

        Assert.AreEqual("sealing failed", error.Message);
        Assert.AreEqual(1, changedCount, "The file rewritten by the first step must be counted as changed.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void TryRunHeadlessPreCleanupForCSharpCore_OriginalCannotBeWrittenAfterTheSplit_RemovesTheFilesCreatedByTheSplit()
    {
        Settings.Default.Cleaning_MoveTopLevelTypesToSeparateFiles = true;
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");
        const string Original = "class Foo { }\r\nclass Bar { }\r\n";
        File.WriteAllText(filePath, Original);
        File.SetAttributes(filePath, FileAttributes.ReadOnly);
        try
        {
            CodeCleanupManager.HeadlessPreCleanupOutcome outcome = CodeCleanupManager.GetInstance(null).TryRunHeadlessPreCleanupForCSharpCore(filePath);

            // Bar is still declared by the unchanged original: a surviving Bar.cs would duplicate it (CS0101).
            Assert.AreEqual(CodeCleanupManager.HeadlessCleanupResult.NotApplicable, outcome.Result);
            Assert.IsFalse(outcome.SplitOperationOccurred);
            Assert.IsEmpty(outcome.CreatedFiles);
            Assert.IsFalse(File.Exists(Path.Combine(_tempDirectory, "Bar.cs")), "The file created for the moved type must be removed.");
            Assert.AreEqual(Original, File.ReadAllText(filePath));
        }
        finally
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyHeadlessCSharpTransformationsToFiles_CleansMultipleFilesConcurrently()
    {
        List<string> filePaths = [];
        for (int i = 0; i < 10; i++)
        {
            string filePath = Path.Combine(_tempDirectory, $"Sample_{i}.cs");
            string content = $"namespace Demo;\r\n\r\npublic class C{i} {{ public string M(string n) {{ return string.Format(\"Hello {{0}}\", n); }} }}\r\n";
            File.WriteAllText(filePath, content);
            filePaths.Add(filePath);
        }

        CodeCleanupManager.ParallelCleanupResult result = CodeCleanupManager.ApplyHeadlessCSharpTransformationsToFiles(filePaths, maxDegreeOfParallelism: 4);

        Assert.AreEqual(10, result.TotalFiles);
        Assert.AreEqual(10, result.ChangedFiles);
        Assert.AreEqual(0, result.FailedFiles);

        foreach (string filePath in filePaths)
        {
            string cleanedContent = File.ReadAllText(filePath);
            Assert.Contains("return $\"Hello {n}\";", cleanedContent, $"File {filePath} was not transformed.");
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyHeadlessCSharpTransformationsToFiles_ReportsProgress()
    {
        List<string> filePaths = [];
        for (int i = 0; i < 5; i++)
        {
            string filePath = Path.Combine(_tempDirectory, $"ProgressSample_{i}.cs");
            string content = $"namespace Demo;\r\n\r\npublic class C{i} {{ }}\r\n";
            File.WriteAllText(filePath, content);
            filePaths.Add(filePath);
        }

        int progressCount = 0;
        Progress<CodeCleanupManager.ParallelCleanupProgress> progress = new Progress<CodeCleanupManager.ParallelCleanupProgress>(p => Interlocked.Increment(ref progressCount));

        CodeCleanupManager.ParallelCleanupResult result = CodeCleanupManager.ApplyHeadlessCSharpTransformationsToFiles(filePaths, progress: progress);

        Assert.AreEqual(5, result.TotalFiles);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void BaseProgressViewModel_ProgressPercentText_CalculatesAccurately()
    {
        TestProgressViewModel vm = new TestProgressViewModel();

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

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyHeadlessCSharpTransformationsToFiles_LeavesInterlockedFieldMutable_InNestedType()
    {
        Settings.Default.Cleaning_MakeFieldsReadonlyWhenSafe = true;

        string filePath = Path.Combine(_tempDirectory, "Fleet.cs");
        string content = @"namespace Demo;

public class Fleet
{
    private int _active;

    public int Active => _active;

    private class NestedHelper
    {
        public void Increment(Fleet fleet)
        {
            System.Threading.Interlocked.Increment(ref fleet._active);
        }
    }
}
";
        File.WriteAllText(filePath, content);

        CodeCleanupManager.ParallelCleanupResult result = CodeCleanupManager.ApplyHeadlessCSharpTransformationsToFiles(new[] { filePath });

        Assert.AreEqual(0, result.FailedFiles);
        string text = File.ReadAllText(filePath);
        Assert.DoesNotContain("readonly int _active", text, "Field mutated via Interlocked.Increment in nested class must not be marked readonly.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyHeadlessCSharpTransformationsToFiles_WhenTransformedOutputHasSyntaxErrors_DoesNotCountFileAsChanged()
    {
        string filePath = Path.Combine(_tempDirectory, "Broken.cs");
        // Pre-existing (unfixable) syntax error: unterminated method body. The BOM removal
        // setting (enabled in TestInitialize) makes this "Changed" via encoding alone, even
        // though no transformation can repair the missing closing brace.
        string content = "namespace Demo;\r\n\r\npublic class Foo\r\n{\r\n    public void M()\r\n    {\r\n";
        File.WriteAllText(filePath, content, new System.Text.UTF8Encoding(true));

        CodeCleanupManager.ParallelCleanupResult result = CodeCleanupManager.ApplyHeadlessCSharpTransformationsToFiles(new[] { filePath });

        Assert.AreEqual(1, result.FailedFiles, "A file whose transformed output has syntax errors must be counted as failed.");
        Assert.AreEqual(0, result.ChangedFiles, "A file that failed syntax verification must not also be counted as changed.");
        Assert.DoesNotContain(filePath, result.ModifiedFilePaths, "A file that failed syntax verification must not be reported as a modified path.");
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
