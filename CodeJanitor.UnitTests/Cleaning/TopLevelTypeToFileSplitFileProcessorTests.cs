using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Cleaning;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public class TopLevelTypeToFileSplitFileProcessorTests
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
        var source =
            "namespace Demo;\r\n\r\nclass Foo { }\r\ninterface IBar { }\r\nenum Baz { A }\r\n";
        var filePath = Path.Combine(_tempDirectory, "Foo.cs");

        var result = _processor.Apply(source, filePath, Encoding.UTF8, null);

        Assert.IsTrue(result.Changed);
        Assert.AreEqual(2, result.CreatedFiles.Count);
        Assert.IsFalse(result.UpdatedSource.Contains("interface IBar"));
        Assert.IsTrue(File.Exists(Path.Combine(_tempDirectory, "IBar.cs")));
        Assert.IsTrue(File.Exists(Path.Combine(_tempDirectory, "Baz.cs")));
    }

    [TestMethod]
    public void Apply_UsesDedicatedTransformForCreatedFiles()
    {
        var source =
            "namespace Demo;\r\n\r\nclass Foo { }\r\nclass Bar { }\r\n";
        var filePath = Path.Combine(_tempDirectory, "Foo.cs");

        var result = _processor.Apply(
            source,
            filePath,
            Encoding.UTF8,
            (text, path) => "// updated\r\n" + text,
            transformUpdatedSource: true,
            transformCreatedFile: (text, path) => "// created\r\n" + text);

        Assert.IsTrue(result.Changed);
        StringAssert.StartsWith(result.UpdatedSource, "// updated");
        StringAssert.StartsWith(File.ReadAllText(Path.Combine(_tempDirectory, "Bar.cs")), "// created");
    }

    [TestMethod]
    public void Apply_PassesGeneratedAndUpdatedSourcesThroughTransformer()
    {
        var source =
            "namespace Demo;\r\n\r\nclass Foo { }\r\nclass Bar { }\r\n";
        var filePath = Path.Combine(_tempDirectory, "Foo.cs");

        var result = _processor.Apply(
            source,
            filePath,
            Encoding.UTF8,
            (text, path) => "// " + Path.GetFileName(path) + "\r\n" + text);

        Assert.IsTrue(result.Changed);
        StringAssert.StartsWith(result.UpdatedSource, "// Foo.cs");
        var generatedFile = result.CreatedFiles.Single();
        StringAssert.StartsWith(File.ReadAllText(generatedFile), "// Bar.cs");
    }

    [TestMethod]
    public void Apply_WhenNoSplitNeeded_ReturnsUnchangedWithoutCreatingFiles()
    {
        var source =
            "namespace Demo;\r\n\r\nclass Foo { }\r\n";
        var filePath = Path.Combine(_tempDirectory, "Foo.cs");

        var result = _processor.Apply(source, filePath, Encoding.UTF8, null);

        Assert.IsFalse(result.Changed);
        Assert.AreEqual(source, result.UpdatedSource);
        Assert.AreEqual(0, result.CreatedFiles.Count);
        Assert.AreEqual(0, Directory.GetFiles(_tempDirectory, "*.cs").Length);
    }

    [TestMethod]
    public void Apply_WithTransformUpdatedSourceDisabled_OnlyTransformsGeneratedFiles()
    {
        var source =
            "namespace Demo;\r\n\r\nclass Foo { }\r\nclass Bar { }\r\n";
        var filePath = Path.Combine(_tempDirectory, "Foo.cs");

        var result = _processor.Apply(
            source,
            filePath,
            Encoding.UTF8,
            (text, path) => "// " + Path.GetFileName(path) + "\r\n" + text,
            transformUpdatedSource: false);

        Assert.IsTrue(result.Changed);
        Assert.IsFalse(result.UpdatedSource.StartsWith("// ", StringComparison.Ordinal));
        var generatedFile = result.CreatedFiles.Single();
        StringAssert.StartsWith(File.ReadAllText(generatedFile), "// Bar.cs");
    }

    [TestMethod]
    public void Apply_WhenSplitCreatesFiles_DoesNotLeaveTemporaryFiles()
    {
        var source =
            "namespace Demo;\r\n\r\nclass Foo { }\r\nclass Bar { }\r\nclass Baz { }\r\n";
        var filePath = Path.Combine(_tempDirectory, "Foo.cs");

        var result = _processor.Apply(source, filePath, Encoding.UTF8, null);

        Assert.IsTrue(result.Changed);
        var tempArtifacts = Directory.GetFiles(_tempDirectory, "*.codejanitor.tmp.*", SearchOption.TopDirectoryOnly);
        Assert.AreEqual(0, tempArtifacts.Length);
    }
}
