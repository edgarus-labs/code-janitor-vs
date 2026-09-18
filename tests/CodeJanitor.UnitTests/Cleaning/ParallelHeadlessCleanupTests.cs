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

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyHeadlessCSharpTransformationsToFiles_LeavesVirtualMemberClassUnsealed()
    {
        Settings.Default.Cleaning_SealClassesWhenSafe = true;

        var filePath = Path.Combine(_tempDirectory, "VirtualClass.cs");
        var content = "namespace Demo;\r\n\r\npublic class Foo\r\n{\r\n    public virtual string Name { get; set; }\r\n}\r\n";
        File.WriteAllText(filePath, content);

        var result = CodeCleanupManager.ApplyHeadlessCSharpTransformationsToFiles(new[] { filePath });

        Assert.AreEqual(0, result.FailedFiles);
        var text = File.ReadAllText(filePath);
        Assert.IsFalse(text.Contains("sealed class Foo"), "Class with virtual property must not be sealed.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyHeadlessCSharpTransformationsToFiles_LeavesBaseClassUnsealed_WhenGenericConstraintOrDerivedTypeInAnotherFile()
    {
        Settings.Default.Cleaning_SealClassesWhenSafe = true;

        var baseFile = Path.Combine(_tempDirectory, "Result.cs");
        var derivedFile = Path.Combine(_tempDirectory, "ResultOfT.cs");
        var handlerFile = Path.Combine(_tempDirectory, "Handler.cs");

        File.WriteAllText(baseFile, "namespace Demo;\r\n\r\npublic class Result\r\n{\r\n    public bool Success { get; set; }\r\n}\r\n");
        File.WriteAllText(derivedFile, "namespace Demo;\r\n\r\npublic class Result<T> : Result\r\n{\r\n    public T Value { get; set; }\r\n}\r\n");
        File.WriteAllText(handlerFile, "namespace Demo;\r\n\r\npublic class Handler<T> where T : Result\r\n{\r\n}\r\n");

        var result = CodeCleanupManager.ApplyHeadlessCSharpTransformationsToFiles(new[] { baseFile, derivedFile, handlerFile });

        Assert.AreEqual(0, result.FailedFiles);
        var baseText = File.ReadAllText(baseFile);
        Assert.IsFalse(baseText.Contains("sealed class Result\r\n") || baseText.Contains("sealed class Result\n"), "Base class used in generic constraint or derived type in another file must not be sealed.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyHeadlessCSharpTransformationsToFiles_LeavesBaseClassUnsealed_WhenNullableGenericConstraintInAnotherFile()
    {
        Settings.Default.Cleaning_SealClassesWhenSafe = true;

        var baseFile = Path.Combine(_tempDirectory, "Result.cs");
        var handlerFile = Path.Combine(_tempDirectory, "Handler.cs");

        File.WriteAllText(baseFile, "namespace Demo;\r\n\r\npublic class Result\r\n{\r\n    public bool Success { get; set; }\r\n}\r\n");
        File.WriteAllText(handlerFile, "namespace Demo;\r\n\r\npublic class Handler<T> where T : Result?\r\n{\r\n}\r\n");

        var result = CodeCleanupManager.ApplyHeadlessCSharpTransformationsToFiles(new[] { baseFile, handlerFile });

        Assert.AreEqual(0, result.FailedFiles);
        var baseText = File.ReadAllText(baseFile);
        Assert.IsFalse(baseText.Contains("sealed class Result\r\n") || baseText.Contains("sealed class Result\n"), "Base class used in a nullable generic constraint in another file must not be sealed.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyHeadlessCSharpTransformationsToFiles_LeavesInterlockedFieldMutable_InNestedType()
    {
        Settings.Default.Cleaning_MakeFieldsReadonlyWhenSafe = true;

        var filePath = Path.Combine(_tempDirectory, "Fleet.cs");
        var content = @"namespace Demo;

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

        var result = CodeCleanupManager.ApplyHeadlessCSharpTransformationsToFiles(new[] { filePath });

        Assert.AreEqual(0, result.FailedFiles);
        var text = File.ReadAllText(filePath);
        Assert.IsFalse(text.Contains("readonly int _active"), "Field mutated via Interlocked.Increment in nested class must not be marked readonly.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyHeadlessCSharpTransformationsToFiles_WhenTransformedOutputHasSyntaxErrors_DoesNotCountFileAsChanged()
    {
        var filePath = Path.Combine(_tempDirectory, "Broken.cs");
        // Pre-existing (unfixable) syntax error: unterminated method body. The BOM removal
        // setting (enabled in TestInitialize) makes this "Changed" via encoding alone, even
        // though no transformation can repair the missing closing brace.
        var content = "namespace Demo;\r\n\r\npublic class Foo\r\n{\r\n    public void M()\r\n    {\r\n";
        File.WriteAllText(filePath, content, new System.Text.UTF8Encoding(true));

        var result = CodeCleanupManager.ApplyHeadlessCSharpTransformationsToFiles(new[] { filePath });

        Assert.AreEqual(1, result.FailedFiles, "A file whose transformed output has syntax errors must be counted as failed.");
        Assert.AreEqual(0, result.ChangedFiles, "A file that failed syntax verification must not also be counted as changed.");
        CollectionAssert.DoesNotContain(result.ModifiedFilePaths, filePath, "A file that failed syntax verification must not be reported as a modified path.");
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
