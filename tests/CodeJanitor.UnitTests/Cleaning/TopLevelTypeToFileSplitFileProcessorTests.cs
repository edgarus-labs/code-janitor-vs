using System;
using System.IO;
using System.Linq;
using System.Text;
using CodeJanitor.Logic.Cleaning;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public sealed class TopLevelTypeToFileSplitFileProcessorTests
{
    private string _tempDirectory;
    private TopLevelTypeToFileSplitFileProcessor _processor;

    [TestInitialize]
    public void TestInitialize()
    {
        _processor = new TopLevelTypeToFileSplitFileProcessor();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [TestMethod]
    public void Apply_CreatesNewFiles_AndReturnsUpdatedSource()
    {
        string source =
            "namespace Demo;\r\n\r\nclass Foo { }\r\ninterface IBar { }\r\nenum Baz { A }\r\n";
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");

        TopLevelTypeToFileSplitFileProcessor.ApplyResult result = _processor.Apply(source, filePath, Encoding.UTF8, null);

        Assert.IsTrue(result.Changed);
        Assert.HasCount(2, result.CreatedFiles);
        Assert.DoesNotContain("interface IBar", result.UpdatedSource);
        Assert.IsTrue(File.Exists(Path.Combine(_tempDirectory, "IBar.cs")));
        Assert.IsTrue(File.Exists(Path.Combine(_tempDirectory, "Baz.cs")));
    }

    [TestMethod]
    public void Apply_UsesDedicatedTransformForCreatedFiles()
    {
        string source =
            "namespace Demo;\r\n\r\nclass Foo { }\r\nclass Bar { }\r\n";
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");

        TopLevelTypeToFileSplitFileProcessor.ApplyResult result = _processor.Apply(
            source,
            filePath,
            Encoding.UTF8,
            (text, path) => "// updated\r\n" + text,
            transformUpdatedSource: true,
            transformCreatedFile: (text, path) => "// created\r\n" + text);

        Assert.IsTrue(result.Changed);
        Assert.StartsWith("// updated", result.UpdatedSource);
        Assert.StartsWith("// created", File.ReadAllText(Path.Combine(_tempDirectory, "Bar.cs")));
    }

    [TestMethod]
    public void Apply_PassesGeneratedAndUpdatedSourcesThroughTransformer()
    {
        string source =
            "namespace Demo;\r\n\r\nclass Foo { }\r\nclass Bar { }\r\n";
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");

        TopLevelTypeToFileSplitFileProcessor.ApplyResult result = _processor.Apply(
            source,
            filePath,
            Encoding.UTF8,
            (text, path) => "// " + Path.GetFileName(path) + "\r\n" + text);

        Assert.IsTrue(result.Changed);
        Assert.StartsWith("// Foo.cs", result.UpdatedSource);
        string generatedFile = result.CreatedFiles.Single();
        Assert.StartsWith("// Bar.cs", File.ReadAllText(generatedFile));
    }

    [TestMethod]
    public void Apply_WhenNoSplitNeeded_ReturnsUnchangedWithoutCreatingFiles()
    {
        string source =
            "namespace Demo;\r\n\r\nclass Foo { }\r\n";
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");

        TopLevelTypeToFileSplitFileProcessor.ApplyResult result = _processor.Apply(source, filePath, Encoding.UTF8, null);

        Assert.IsFalse(result.Changed);
        Assert.AreEqual(source, result.UpdatedSource);
        Assert.IsEmpty(result.CreatedFiles);
        Assert.IsEmpty(Directory.GetFiles(_tempDirectory, "*.cs"));
    }

    [TestMethod]
    public void Apply_WithTransformUpdatedSourceDisabled_OnlyTransformsGeneratedFiles()
    {
        string source =
            "namespace Demo;\r\n\r\nclass Foo { }\r\nclass Bar { }\r\n";
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");

        TopLevelTypeToFileSplitFileProcessor.ApplyResult result = _processor.Apply(
            source,
            filePath,
            Encoding.UTF8,
            (text, path) => "// " + Path.GetFileName(path) + "\r\n" + text,
            transformUpdatedSource: false);

        Assert.IsTrue(result.Changed);
        Assert.IsFalse(result.UpdatedSource.StartsWith("// ", StringComparison.Ordinal));
        string generatedFile = result.CreatedFiles.Single();
        Assert.StartsWith("// Bar.cs", File.ReadAllText(generatedFile));
    }

    [TestMethod]
    public void Apply_WhenSplitCreatesFiles_DoesNotLeaveTemporaryFiles()
    {
        string source =
            "namespace Demo;\r\n\r\nclass Foo { }\r\nclass Bar { }\r\nclass Baz { }\r\n";
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");

        TopLevelTypeToFileSplitFileProcessor.ApplyResult result = _processor.Apply(source, filePath, Encoding.UTF8, null);

        Assert.IsTrue(result.Changed);
        string[] tempArtifacts = Directory.GetFiles(_tempDirectory, "*.codejanitor.tmp.*", SearchOption.TopDirectoryOnly);
        Assert.IsEmpty(tempArtifacts);
    }
}
