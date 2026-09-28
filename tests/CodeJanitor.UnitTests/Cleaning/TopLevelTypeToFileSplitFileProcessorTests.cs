using System;
using System.IO;
using System.Linq;
using System.Text;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
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
        Settings.Default.Reset();

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

    [TestMethod]
    [DataRow("namespace Demo;\r\n\r\nclass Foo { }\r\n", "NotMultipleEligibleTypes")]
    [DataRow("using System;\r\n\r\nConsole.WriteLine();\r\nclass Foo { }\r\nclass Bar { }\r\n", "UnsupportedStructure")]
    [DataRow("", "EmptySource")]
    public void Apply_WhenNotSplit_ReportsWhyAndNeverCallsTheTransform(string source, string expectedReason)
    {
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");
        int transformCalls = 0;

        TopLevelTypeToFileSplitFileProcessor.ApplyResult result = _processor.Apply(
            source,
            filePath,
            Encoding.UTF8,
            (text, path) =>
            {
                transformCalls++;

                return text;
            });

        Assert.IsFalse(result.Changed);
        Assert.AreEqual(expectedReason, result.SkipReason.ToString());
        Assert.AreEqual(source, result.UpdatedSource);
        Assert.AreEqual(0, transformCalls);
        Assert.IsEmpty(Directory.GetFiles(_tempDirectory));
    }

    [TestMethod]
    public void Apply_WhenSplit_ReportsNoSkipReason()
    {
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");

        TopLevelTypeToFileSplitFileProcessor.ApplyResult result = _processor.Apply("class Foo { }\r\nclass Bar { }\r\n", filePath, Encoding.UTF8, null);

        Assert.AreEqual(TopLevelTypeSplitSkipReason.None, result.SkipReason);
        Assert.AreEqual("class Foo { }\r\n", result.UpdatedSource);
        Assert.AreEqual("class Bar { }\r\n", File.ReadAllText(Path.Combine(_tempDirectory, "Bar.cs")));
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void Apply_ByteOrderMarkOfCreatedFiles_FollowsTheRemoveByteOrderMarkSetting(bool removeByteOrderMark, bool expectByteOrderMark)
    {
        Settings.Default.Cleaning_RemoveByteOrderMark = removeByteOrderMark;
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");

        _processor.Apply("class Foo { }\r\nclass Bar { }\r\n", filePath, new UTF8Encoding(true), null);

        byte[] bytes = File.ReadAllBytes(Path.Combine(_tempDirectory, "Bar.cs"));
        bool hasByteOrderMark = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        Assert.AreEqual(expectByteOrderMark, hasByteOrderMark);
        Assert.AreEqual("class Bar { }\r\n", File.ReadAllText(Path.Combine(_tempDirectory, "Bar.cs")));
    }

    [TestMethod]
    public void Apply_TargetFileAppearingAfterPlanning_IsReplacedWithoutLeavingTemporaryFiles()
    {
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");

        TopLevelTypeToFileSplitFileProcessor.ApplyResult result = _processor.Apply(
            "class Foo { }\r\nclass Bar { }\r\n",
            filePath,
            Encoding.UTF8,
            null,
            transformCreatedFile: (text, path) =>
            {
                File.WriteAllText(path, "stale content written by someone else");

                return "// created\r\n" + text;
            });

        string createdFile = result.CreatedFiles.Single();
        Assert.AreEqual(Path.Combine(_tempDirectory, "Bar.cs"), createdFile);
        Assert.AreEqual("// created\r\nclass Bar { }\r\n", File.ReadAllText(createdFile));
        Assert.AreSequenceEqual(new[] { createdFile }, Directory.GetFiles(_tempDirectory));
    }

    [TestMethod]
    public void Apply_TargetPathOccupiedByADirectory_ThrowsAndRemovesTheTemporaryFile()
    {
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");
        string occupiedPath = Path.Combine(_tempDirectory, "Bar.cs");

        Exception exception = Assert.Throws<Exception>(() => _processor.Apply(
            "class Foo { }\r\nclass Bar { }\r\n",
            filePath,
            Encoding.UTF8,
            null,
            transformCreatedFile: (text, path) =>
            {
                Directory.CreateDirectory(path);

                return text;
            }));

        Assert.IsTrue(exception is IOException || exception is UnauthorizedAccessException, exception.ToString());
        Assert.IsTrue(Directory.Exists(occupiedPath));
        Assert.IsEmpty(Directory.GetFiles(_tempDirectory));
    }

    [TestMethod]
    public void Apply_WhenTheTemporaryFileCannotBeWritten_ThrowsWithoutCreatingAnyFile()
    {
        // The target name fits the file system limit, the temporary name next to it does not.
        string longName = "Bar" + new string('x', 220);
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");

        Assert.Throws<IOException>(() => _processor.Apply(
            "class Foo { }\r\nclass " + longName + " { }\r\n",
            filePath,
            Encoding.UTF8,
            null));

        Assert.IsEmpty(Directory.GetFileSystemEntries(_tempDirectory));
    }
}
