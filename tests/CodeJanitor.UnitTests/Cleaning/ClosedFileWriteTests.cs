using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.UnitTests.Cleaning.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning;

/// <summary>
/// Tests for writing the diagnostic cleanup result of a closed file to disk in the background instead of through the
/// Visual Studio workspace on the UI thread.
/// </summary>
[TestClass]
public sealed class ClosedFileWriteTests
{
    private string _tempDirectory;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"), "root = true\n");
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
    [TestCategory("Cleaning UnitTests")]
    public void SemanticFileRewriter_FileEditedDuringTheAnalysis_IsNotOverwritten()
    {
        string filePath = WriteFile("class Edited { }", new UTF8Encoding(false));
        string reason = null;

        bool written = SemanticFileRewriter.TryWriteRewrittenText(filePath, "class C { }", "sealed class C { }", notWritten => reason = notWritten);

        Assert.IsFalse(written);
        Assert.AreEqual("class Edited { }", File.ReadAllText(filePath));
        Assert.IsNotNull(reason, "The user must be told why the file was left unchanged.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void SemanticFileRewriter_UnchangedFile_IsRewrittenKeepingItsByteOrderMark()
    {
        string filePath = WriteFile("class C { }", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        bool written = SemanticFileRewriter.TryWriteRewrittenText(filePath, "class C { }", "sealed class C { }", _ => Assert.Fail("The file must be written."));

        Assert.IsTrue(written);
        Assert.AreSequenceEqual(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("sealed class C { }")).ToArray(), File.ReadAllBytes(filePath));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void TryWriteClosedFileText_KeepsTheUtf8ByteOrderMark()
    {
        string filePath = WriteFile("class C { }", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.AreEqual(ClosedFileWriteResult.Written, VisualStudioRoslynWorkspace.TryWriteClosedFileText(filePath, "class C { }", "class D { }"));

        Assert.AreSequenceEqual(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("class D { }")).ToArray(), File.ReadAllBytes(filePath));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void TryWriteClosedFileText_WritesNoByteOrderMark_WhenTheFileHadNone()
    {
        string filePath = WriteFile("class C { }", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Assert.AreEqual(ClosedFileWriteResult.Written, VisualStudioRoslynWorkspace.TryWriteClosedFileText(filePath, "class C { }", "class D { }"));

        Assert.AreSequenceEqual(Encoding.UTF8.GetBytes("class D { }"), File.ReadAllBytes(filePath));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void TryWriteClosedFileText_KeepsUtf16()
    {
        string filePath = WriteFile("class C { }", Encoding.Unicode);

        Assert.AreEqual(ClosedFileWriteResult.Written, VisualStudioRoslynWorkspace.TryWriteClosedFileText(filePath, "class C { }", "class D { }"));

        Assert.AreSequenceEqual(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("class D { }")).ToArray(), File.ReadAllBytes(filePath));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void TryWriteClosedFileText_LeavesTheFile_WhenItChangedOnDisk()
    {
        string filePath = WriteFile("class Edited { }", new UTF8Encoding(false));

        Assert.AreEqual(ClosedFileWriteResult.ChangedOnDisk, VisualStudioRoslynWorkspace.TryWriteClosedFileText(filePath, "class C { }", "class D { }"));

        Assert.AreEqual("class Edited { }", File.ReadAllText(filePath));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void TryWriteClosedFileText_ReportsNotWritable_ForAReadOnlyFile()
    {
        string filePath = WriteFile("class C { }", new UTF8Encoding(false));
        File.SetAttributes(filePath, FileAttributes.ReadOnly);
        try
        {
            Assert.AreEqual(ClosedFileWriteResult.NotWritable, VisualStudioRoslynWorkspace.TryWriteClosedFileText(filePath, "class C { }", "class D { }"));

            Assert.AreEqual("class C { }", File.ReadAllText(filePath));
        }
        finally
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task GetTextWhenOnlyTheFileChanged_ReturnsTheNewText_WhenOnlyThatFileChanged()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        DocumentId targetId = workspace.AddDocument("Target.cs", "class Target { }");
        workspace.AddDocument("Other.cs", "class Other { }");
        Solution original = workspace.CreateSolution();
        Solution changed = original.WithDocumentText(targetId, SourceText.From("sealed class Target { }"));

        string text = await EditorConfigDiagnosticCleanupLogic.GetTextWhenOnlyTheFileChangedAsync(
            original, changed, targetId, original.GetDocument(targetId).FilePath, CancellationToken.None);

        Assert.AreEqual("sealed class Target { }", text);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task GetTextWhenOnlyTheFileChanged_ReturnsNull_WhenAnotherFileChanged()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        DocumentId targetId = workspace.AddDocument("Target.cs", "class Target { }");
        DocumentId otherId = workspace.AddDocument("Other.cs", "class Other { }");
        Solution original = workspace.CreateSolution();
        Solution changed = original
            .WithDocumentText(targetId, SourceText.From("class Renamed { }"))
            .WithDocumentText(otherId, SourceText.From("class Other { Renamed R; }"));

        string text = await EditorConfigDiagnosticCleanupLogic.GetTextWhenOnlyTheFileChangedAsync(
            original, changed, targetId, original.GetDocument(targetId).FilePath, CancellationToken.None);

        Assert.IsNull(text);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task GetTextWhenOnlyTheFileChanged_ReturnsNull_WhenADocumentWasAdded()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        DocumentId targetId = workspace.AddDocument("Target.cs", "class Target { }");
        Solution original = workspace.CreateSolution();
        Solution changed = original
            .WithDocumentText(targetId, SourceText.From("class Target { }\r\n"))
            .AddDocument(DocumentId.CreateNewId(targetId.ProjectId), "New.cs", "class New { }");

        string text = await EditorConfigDiagnosticCleanupLogic.GetTextWhenOnlyTheFileChangedAsync(
            original, changed, targetId, original.GetDocument(targetId).FilePath, CancellationToken.None);

        Assert.IsNull(text);
    }

    private string WriteFile(string text, Encoding encoding)
    {
        string filePath = Path.Combine(_tempDirectory, "Sample.cs");
        File.WriteAllText(filePath, text, encoding);

        return filePath;
    }
}
