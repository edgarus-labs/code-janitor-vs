using System;
using System.Threading;
using System.Threading.Tasks;
using CodeJanitor.Logic.Cleaning.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning.Diagnostics;

/// <summary>
/// Behavioral tests for <see cref="RoslynDocumentCleanup" />, the Roslyn equivalents of the Visual Studio "Remove and
/// Sort Usings" and "Format Document" commands.
/// </summary>
[TestClass]
public sealed class RoslynDocumentCleanupTests
{
    private const string SourceWithUnusedUsing =
        "using System.Text;\r\nusing System.Collections.Generic;\r\nusing System;\r\n\r\nnamespace Demo;\r\n\r\npublic class C\r\n{\r\n    public List<int> Items = new List<int>();\r\n    public Type T;\r\n}\r\n";

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task RemoveAndSortUsings_RemovesUnnecessaryUsings_AndSortsSystemFirst()
    {
        string output = await CleanupAsync(SourceWithUnusedUsing, removeAndSortUsings: true, format: false);

        Assert.StartsWith("using System;\r\nusing System.Collections.Generic;\r\n\r\nnamespace Demo;", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task RemoveAndSortUsings_KeepsUsingsOnTheKeepList()
    {
        string output = await CleanupAsync(SourceWithUnusedUsing, removeAndSortUsings: true, format: false, editorConfig: null, "using System.Text;");

        Assert.StartsWith("using System;\r\nusing System.Collections.Generic;\r\nusing System.Text;\r\n\r\nnamespace Demo;", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task RemoveAndSortUsings_LeavesUsingsOfAFileWithConditionalCompilation()
    {
        string source = "using System.Text;\r\nusing System;\r\n\r\nnamespace Demo;\r\n\r\npublic class C\r\n{\r\n#if DEBUG\r\n    public StringBuilder Builder;\r\n#endif\r\n    public Type T;\r\n}\r\n";

        string output = await CleanupAsync(source, removeAndSortUsings: true, format: false);

        Assert.Contains("using System.Text;", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task Format_IndentsAccordingToTheEditorConfigOfTheFile()
    {
        string source = "namespace Demo;\r\n\r\npublic class C\r\n{\r\npublic int X;\r\n}\r\n";

        string output = await CleanupAsync(source, removeAndSortUsings: false, format: true, editorConfig: "root = true\r\n\r\n[*.cs]\r\nindent_style = space\r\nindent_size = 2\r\n");

        Assert.AreEqual("namespace Demo;\r\n\r\npublic class C\r\n{\r\n  public int X;\r\n}\r\n", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task NoStepRequested_ReturnsTheSameText()
    {
        string output = await CleanupAsync(SourceWithUnusedUsing, removeAndSortUsings: false, format: false);

        Assert.AreEqual(SourceWithUnusedUsing, output);
    }

    private static Task<string> CleanupAsync(string source, bool removeAndSortUsings, bool format, params string[] usingsToKeep)
        => CleanupAsync(source, removeAndSortUsings, format, editorConfig: null, usingsToKeep);

    private static async Task<string> CleanupAsync(string source, bool removeAndSortUsings, bool format, string editorConfig, params string[] usingsToKeep)
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        if (editorConfig is not null)
        {
            workspace.AddEditorConfig(string.Empty, editorConfig);
        }

        DocumentId documentId = workspace.AddDocument("Sample.cs", source);
        Document document = workspace.CreateSolution().GetDocument(documentId);

        Document cleaned = await RoslynDocumentCleanup.ApplyAsync(document, removeAndSortUsings, format, usingsToKeep ?? Array.Empty<string>(), CancellationToken.None);

        return (await cleaned.GetTextAsync()).ToString();
    }
}
