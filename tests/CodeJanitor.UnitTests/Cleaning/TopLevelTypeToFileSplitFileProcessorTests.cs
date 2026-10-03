using System;
using System.Collections.Generic;
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
        string isolationDirectory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        _tempDirectory = Path.Combine(isolationDirectory, "Work");
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(Path.Combine(isolationDirectory, ".editorconfig"), "root = true\n");
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Settings.Default.Reset();

        string isolationDirectory = Path.GetDirectoryName(_tempDirectory);
        if (Directory.Exists(isolationDirectory))
        {
            Directory.Delete(isolationDirectory, true);
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
    public void Apply_TargetFileAppearingAfterPlanning_IsKeptAndTheTypeGetsTheNextFreeName()
    {
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");
        string foreignFile = Path.Combine(_tempDirectory, "Bar.cs");

        TopLevelTypeToFileSplitFileProcessor.ApplyResult result = _processor.Apply(
            "class Foo { }\r\nclass Bar { }\r\n",
            filePath,
            Encoding.UTF8,
            null,
            transformCreatedFile: (text, path) =>
            {
                File.WriteAllText(foreignFile, "content written by someone else");

                return "// created\r\n" + text;
            });

        string createdFile = result.CreatedFiles.Single();
        Assert.AreEqual(Path.Combine(_tempDirectory, "Bar~1.cs"), createdFile);
        Assert.AreEqual("// created\r\nclass Bar { }\r\n", File.ReadAllText(createdFile));
        Assert.AreEqual("content written by someone else", File.ReadAllText(foreignFile));
        Assert.AreSequenceEqual(new[] { foreignFile, createdFile }, Directory.GetFiles(_tempDirectory).OrderBy(x => x, StringComparer.Ordinal).ToArray());
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

        Assert.IsTrue(exception is IOException or UnauthorizedAccessException, exception.ToString());
        Assert.IsTrue(Directory.Exists(occupiedPath));
        Assert.IsEmpty(Directory.GetFiles(_tempDirectory));
    }

    [TestMethod]
    public void Apply_WhenALaterFileFails_ThrowsAndLeavesNoCreatedFilesBehind()
    {
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");
        string originalContent = "class Foo { }\r\nclass Bar { }\r\nclass Baz { }\r\n";
        File.WriteAllText(filePath, originalContent);
        string foreignFile = Path.Combine(_tempDirectory, "Other.cs");
        File.WriteAllText(foreignFile, "content written by someone else");
        int transformCalls = 0;

        Exception exception = Assert.Throws<InvalidOperationException>(() => _processor.Apply(
            originalContent,
            filePath,
            Encoding.UTF8,
            null,
            transformCreatedFile: (text, path) =>
            {
                transformCalls++;
                if (transformCalls == 2)
                {
                    throw new InvalidOperationException("second file failed");
                }

                return text;
            }));

        Assert.AreEqual("second file failed", exception.Message);
        Assert.AreSequenceEqual(new[] { filePath, foreignFile }.OrderBy(x => x, StringComparer.Ordinal).ToArray(), Directory.GetFiles(_tempDirectory).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.AreEqual(originalContent, File.ReadAllText(filePath));
        Assert.AreEqual("content written by someone else", File.ReadAllText(foreignFile));

        TopLevelTypeToFileSplitFileProcessor.ApplyResult retry = _processor.Apply(originalContent, filePath, Encoding.UTF8, null);

        Assert.AreSequenceEqual(
            new[] { Path.Combine(_tempDirectory, "Bar.cs"), Path.Combine(_tempDirectory, "Baz.cs") },
            retry.CreatedFiles.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [TestMethod]
    public void Apply_WhenTheSecondFileCannotBeWritten_DeletesTheFirstOneAndKeepsTheOriginalException()
    {
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");
        string blockedPath = Path.Combine(_tempDirectory, "Baz.cs");
        int transformCalls = 0;

        Exception exception = Assert.Throws<Exception>(() => _processor.Apply(
            "class Foo { }\r\nclass Bar { }\r\nclass Baz { }\r\n",
            filePath,
            Encoding.UTF8,
            null,
            transformCreatedFile: (text, path) =>
            {
                // The second planned file is blocked once its content is transformed, i.e. right before it is written.
                if (++transformCalls == 2)
                {
                    Directory.CreateDirectory(path);
                    blockedPath = path;
                }

                return text;
            }));

        Assert.AreEqual(2, transformCalls);
        Assert.IsTrue(exception is IOException or UnauthorizedAccessException, exception.ToString());
        Assert.IsTrue(Directory.Exists(blockedPath), "The blocked target is the directory that caused the failure.");
        Assert.IsEmpty(Directory.GetFiles(_tempDirectory), "The first file written before the failure must be deleted again.");
    }

    [TestMethod]
    public void Apply_WhenTheRollbackCannotDeleteAFile_StillThrowsTheOriginalException()
    {
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");
        string firstCreated = null;
        FileStream lockOnFirstFile = null;
        int transformCalls = 0;
        try
        {
            Exception exception = Assert.Throws<Exception>(() => _processor.Apply(
                "class Foo { }\r\nclass Bar { }\r\nclass Baz { }\r\n",
                filePath,
                Encoding.UTF8,
                null,
                transformCreatedFile: (text, path) =>
                {
                    if (++transformCalls == 1)
                    {
                        firstCreated = path;
                    }
                    else
                    {
                        // Keeps the first file from being deleted by the rollback, then blocks the second target.
                        lockOnFirstFile = new FileStream(firstCreated, FileMode.Open, FileAccess.Read, FileShare.None);
                        Directory.CreateDirectory(path);
                    }

                    return text;
                }));

            Assert.IsTrue(exception is IOException or UnauthorizedAccessException, exception.ToString());
            Assert.IsNotNull(lockOnFirstFile, "The second file must have been attempted.");
            Assert.IsTrue(File.Exists(firstCreated), "A file that cannot be deleted stays; the rollback must not mask the write failure.");
        }
        finally
        {
            lockOnFirstFile?.Dispose();
        }
    }

    [TestMethod]
    public void Apply_WhenTheRollbackCannotDeleteAFile_ReportsThatFile()
    {
        List<string> warnings = new List<string>();
        TopLevelTypeToFileSplitFileProcessor processor = new TopLevelTypeToFileSplitFileProcessor(reportWarning: warnings.Add);
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");
        string firstCreated = null;
        FileStream lockOnFirstFile = null;
        int transformCalls = 0;
        try
        {
            Assert.Throws<Exception>(() => processor.Apply(
                "class Foo { }\r\nclass Bar { }\r\nclass Baz { }\r\n",
                filePath,
                Encoding.UTF8,
                null,
                transformCreatedFile: (text, path) =>
                {
                    if (++transformCalls == 1)
                    {
                        firstCreated = path;
                    }
                    else
                    {
                        lockOnFirstFile = new FileStream(firstCreated, FileMode.Open, FileAccess.Read, FileShare.None);
                        Directory.CreateDirectory(path);
                    }

                    return text;
                }));

            Assert.HasCount(1, warnings);
            Assert.Contains(firstCreated, warnings[0]);
            Assert.Contains(filePath, warnings[0]);
        }
        finally
        {
            lockOnFirstFile?.Dispose();
        }
    }

    [TestMethod]
    public void Apply_WhenTheRollbackDeletesEveryFile_ReportsNothing()
    {
        List<string> warnings = new List<string>();
        TopLevelTypeToFileSplitFileProcessor processor = new TopLevelTypeToFileSplitFileProcessor(reportWarning: warnings.Add);
        int transformCalls = 0;

        Assert.Throws<Exception>(() => processor.Apply(
            "class Foo { }\r\nclass Bar { }\r\nclass Baz { }\r\n",
            Path.Combine(_tempDirectory, "Foo.cs"),
            Encoding.UTF8,
            null,
            transformCreatedFile: (text, path) =>
            {
                if (++transformCalls == 2)
                {
                    Directory.CreateDirectory(path);
                }

                return text;
            }));

        Assert.IsEmpty(warnings);
    }

    [TestMethod]
    public void DeleteCreatedFiles_RemovesTheGivenFilesAndIgnoresMissingOnes()
    {
        string first = Path.Combine(_tempDirectory, "First.cs");
        string second = Path.Combine(_tempDirectory, "Second.cs");
        string untouched = Path.Combine(_tempDirectory, "Untouched.cs");
        File.WriteAllText(first, "class First { }");
        File.WriteAllText(untouched, "class Untouched { }");

        TopLevelTypeToFileSplitFileProcessor.DeleteCreatedFiles(new[] { first, second });

        Assert.AreSequenceEqual(new[] { untouched }, Directory.GetFiles(_tempDirectory));
    }

    [TestMethod]
    public void DeleteCreatedFiles_FileThatCannotBeDeleted_IsReturnedAndKept()
    {
        string locked = Path.Combine(_tempDirectory, "Locked.cs");
        string deletable = Path.Combine(_tempDirectory, "Deletable.cs");
        File.WriteAllText(locked, "class Locked { }");
        File.WriteAllText(deletable, "class Deletable { }");

        IReadOnlyList<string> notDeleted;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            notDeleted = TopLevelTypeToFileSplitFileProcessor.DeleteCreatedFiles(new[] { locked, deletable });
        }

        Assert.AreSequenceEqual(new[] { locked }, notDeleted);
        Assert.IsTrue(File.Exists(locked));
        Assert.IsFalse(File.Exists(deletable));
    }

    [TestMethod]
    public void Apply_WhenTransformingTheUpdatedSourceFails_ThrowsAndLeavesNoCreatedFilesBehind()
    {
        string filePath = Path.Combine(_tempDirectory, "Foo.cs");

        Assert.Throws<InvalidOperationException>(() => _processor.Apply(
            "class Foo { }\r\nclass Bar { }\r\n",
            filePath,
            Encoding.UTF8,
            (text, path) => throw new InvalidOperationException("updated source failed"),
            transformCreatedFile: (text, path) => text));

        Assert.IsEmpty(Directory.GetFileSystemEntries(_tempDirectory));
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
