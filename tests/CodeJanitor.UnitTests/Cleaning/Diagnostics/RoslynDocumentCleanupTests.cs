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
    public async Task RemoveAndSortUsings_LeavesNoBlankLineAtTheTop_WhenEveryUsingIsRemoved()
    {
        string source = "using System.Text;\r\nusing System.Linq;\r\n\r\nnamespace Demo;\r\n\r\npublic enum E { A }\r\n";

        string output = await CleanupAsync(source, removeAndSortUsings: true, format: false);

        Assert.AreEqual("namespace Demo;\r\n\r\npublic enum E { A }\r\n", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task RemoveAndSortUsings_RemovesTheFirstUsingAfterAFileHeader_AndKeepsTheHeader()
    {
        string source = "// Copyright (c) Demo.\r\n\r\nusing System.Text;\r\nusing System;\r\n\r\nnamespace Demo;\r\n\r\npublic class C { public Type T; }\r\n";

        string output = await CleanupAsync(source, removeAndSortUsings: true, format: false);

        Assert.AreEqual("// Copyright (c) Demo.\r\n\r\nusing System;\r\n\r\nnamespace Demo;\r\n\r\npublic class C { public Type T; }\r\n", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task RemoveAndSortUsings_LeavesOneBlankLineAfterTheHeader_WhenEveryUsingIsRemoved()
    {
        string source = "// Copyright (c) Demo.\r\n#nullable enable\r\n\r\nusing System.Text;\r\nusing System.Linq;\r\n\r\nnamespace Demo;\r\n\r\npublic enum E { A }\r\n";

        string output = await CleanupAsync(source, removeAndSortUsings: true, format: false);

        Assert.AreEqual("// Copyright (c) Demo.\r\n#nullable enable\r\n\r\nnamespace Demo;\r\n\r\npublic enum E { A }\r\n", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task RemoveAndSortUsings_KeepsAnUnusedUsingWithACommentOnItsLine()
    {
        string source = "using System.Text; // kept for the generator\r\nusing System;\r\n\r\nnamespace Demo;\r\n\r\npublic class C { public Type T; }\r\n";

        string output = await CleanupAsync(source, removeAndSortUsings: true, format: false);

        Assert.Contains("using System.Text; // kept for the generator", output);
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

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task RemoveAndSortUsings_WithoutAKeepList_RemovesEveryUnnecessaryUsing()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        DocumentId documentId = workspace.AddDocument("Sample.cs", SourceWithUnusedUsing);

        Document cleaned = await RoslynDocumentCleanup.ApplyAsync(workspace.CreateSolution().GetDocument(documentId), true, false, null, CancellationToken.None);

        Assert.StartsWith("using System;\r\nusing System.Collections.Generic;\r\n\r\nnamespace Demo;", (await cleaned.GetTextAsync(TestContext.CancellationToken)).ToString());
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task RemoveAndSortUsings_UnusedUsingsSeparatedByAUsedOne_AreAllRemoved()
    {
        string source = "using System.Text;\nusing System;\nusing System.Collections.Generic;\n\nnamespace Demo;\n\npublic class C { public Type T; }\n";

        string output = await CleanupAsync(source, removeAndSortUsings: true, format: false);

        Assert.AreEqual("using System;\n\nnamespace Demo;\n\npublic class C { public Type T; }\n", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task RemoveAndSortUsings_KeepsAnUnusedUsingPrecededByACommentOnItsLine()
    {
        string source = "/* keep */ using System.Text;\nusing System;\n\nnamespace Demo;\n\npublic class C { public Type T; }\n";

        string output = await CleanupAsync(source, removeAndSortUsings: true, format: false);

        Assert.Contains("/* keep */ using System.Text;", output);
        Assert.Contains("using System;\n", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task RemoveAndSortUsings_TopLevelStatementsFile_KeepsTheUsingsTheStatementsNeed()
    {
        string source =
            "using System.Text;\nusing System;\nusing System.Collections.Generic;\n\nvar builder = new StringBuilder();\nbuilder.Append(Math.Max(1, Local()));\n\nstatic int Local() => 1;\n\nclass Greeter\n{\n    public static string Greet() => \"using System.Collections.Generic;\";\n}\n";

        string output = await CleanupAsync(source, removeAndSortUsings: true, format: false);

        Assert.AreEqual(
            "using System;\nusing System.Text;\n\nvar builder = new StringBuilder();\nbuilder.Append(Math.Max(1, Local()));\n\nstatic int Local() => 1;\n\nclass Greeter\n{\n    public static string Greet() => \"using System.Collections.Generic;\";\n}\n",
            output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task RemoveAndSortUsings_UsingsInsideABlockNamespace_AreCleanedInPlace()
    {
        string source = "namespace Demo\n{\n    using System.Text;\n    using System;\n\n    public class C { public Type T; }\n}\n";

        string output = await CleanupAsync(source, removeAndSortUsings: true, format: false);

        Assert.AreEqual("namespace Demo\n{\n    using System;\n\n    public class C { public Type T; }\n}\n", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task RemoveAndSortUsings_FileWithSyntaxErrors_StillOnlyRemovesTheUnusedUsing()
    {
        string source = "using System.Text;\nusing System;\n\nnamespace Demo;\n\npublic class C { public Type T\n";

        string output = await CleanupAsync(source, removeAndSortUsings: true, format: false);

        Assert.AreEqual("using System;\n\nnamespace Demo;\n\npublic class C { public Type T\n", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("")]
    [DataRow("  \n\t\n")]
    [DataRow("// only a comment\n")]
    public async Task RemoveAndSortUsings_FileWithoutUsings_IsUnchanged(string source)
    {
        string output = await CleanupAsync(source, removeAndSortUsings: true, format: false);

        Assert.AreEqual(source, output);
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

    public TestContext TestContext { get; set; }
}
