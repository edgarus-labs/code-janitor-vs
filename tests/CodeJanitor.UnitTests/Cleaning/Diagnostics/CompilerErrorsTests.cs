using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeJanitor.Logic.Cleaning.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning.Diagnostics;

/// <summary>
/// Unit tests for <see cref="CompilerErrors" />, the gate deciding whether a candidate change introduces a compiler
/// error: an error only counts as existing when its message is unchanged or when its old location maps onto its new
/// location through the text changes.
/// </summary>
[TestClass]
public sealed class CompilerErrorsTests
{
    private const string Text = "aaaa bbbb cccc dddd\n";

    private static readonly DiagnosticDescriptor ErrorDescriptor = new DiagnosticDescriptor(
        "CJT0400",
        "Test error",
        "Error {0}",
        "Compiler",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task FindFirstNewAsync_ErrorInAnUnchangedFileWhoseMessageChanged_IsNotNew()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        DocumentId settingsId = workspace.AddDocument("Settings.cs", "public class Settings\n{\n    public int Value;\n}\n");
        workspace.AddDocument("User.cs", "class User\n{\n    string Read(Settings settings) => settings.Value;\n}\n");
        Project before = workspace.CreateSolution().GetProject(settingsId.ProjectId);
        Project after = before.Solution.WithDocumentText(settingsId, SourceText.From("public class Settings\n{\n    public long Value;\n}\n")).GetProject(before.Id);
        IReadOnlyList<Diagnostic> beforeErrors = await CompilerErrors.GetAsync(before, CancellationToken.None);
        IReadOnlyList<Diagnostic> afterErrors = await CompilerErrors.GetAsync(after, CancellationToken.None);

        Diagnostic newError = await CompilerErrors.FindFirstNewAsync(before, beforeErrors, after, afterErrors, CancellationToken.None);

        Assert.IsNull(newError);
        Assert.AreEqual("CS0029", beforeErrors.Single().Id);
        Assert.AreNotEqual(beforeErrors.Single().GetMessage(), afterErrors.Single().GetMessage(), "Control: the message of the existing error changed.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task FindFirstNewAsync_ChangeBreakingAnotherFile_ReturnsTheNewError()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        DocumentId settingsId = workspace.AddDocument("Settings.cs", "public class Settings\n{\n    public int Value;\n}\n");
        workspace.AddDocument("User.cs", "class User\n{\n    int Read(Settings settings) => settings.Value;\n}\n");
        Project before = workspace.CreateSolution().GetProject(settingsId.ProjectId);
        Project after = before.Solution.WithDocumentText(settingsId, SourceText.From("public class Settings\n{\n    public int Renamed;\n}\n")).GetProject(before.Id);

        Diagnostic newError = await CompilerErrors.FindFirstNewAsync(
            before,
            await CompilerErrors.GetAsync(before, CancellationToken.None),
            after,
            await CompilerErrors.GetAsync(after, CancellationToken.None),
            CancellationToken.None);

        Assert.AreEqual("CS1061", newError.Id);
        Assert.AreEqual(DiagnosticCleanupTestWorkspace.GetPath("User.cs"), newError.Location.SourceTree.FilePath);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task FindFirstNewAsync_NewErrorWithoutASourceLocation_IsNew()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        DocumentId documentId = workspace.AddDocument("Sample.cs", Text);
        Project project = workspace.CreateSolution().GetProject(documentId.ProjectId);
        SyntaxTree tree = await project.GetDocument(documentId).GetSyntaxTreeAsync();
        Diagnostic existing = Diagnostic.Create(ErrorDescriptor, Location.Create(tree, new TextSpan(5, 4)), "old");
        Diagnostic withoutLocation = Diagnostic.Create(ErrorDescriptor, Location.None, "new");

        Diagnostic newError = await CompilerErrors.FindFirstNewAsync(project, new[] { existing }, project, new[] { withoutLocation }, CancellationToken.None);

        Assert.AreSame(withoutLocation, newError);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(5, 4, 15, 4, "D", 5, 4, false, DisplayName = "Change after the error keeps its location")]
    [DataRow(5, 4, 15, 4, "D", 6, 4, true, DisplayName = "Change after the error but the error moved")]
    [DataRow(5, 4, 0, 0, "ZZ", 7, 4, false, DisplayName = "Insertion before the error shifts it")]
    [DataRow(5, 4, 6, 2, "XYZ", 5, 5, false, DisplayName = "Change inside the error grows it")]
    [DataRow(5, 4, 7, 5, "X", 5, 3, true, DisplayName = "Change across the end of the error")]
    [DataRow(5, 4, 3, 4, "X", 3, 3, true, DisplayName = "Change across the start of the error")]
    [DataRow(5, 4, 5, 0, "XX", 7, 4, false, DisplayName = "Insertion at the start of the error, outside it")]
    [DataRow(5, 4, 5, 0, "XX", 5, 6, false, DisplayName = "Insertion at the start of the error, inside it")]
    [DataRow(5, 4, 9, 0, "XX", 5, 6, false, DisplayName = "Insertion at the end of the error, inside it")]
    [DataRow(5, 0, 5, 0, "XX", 7, 0, false, DisplayName = "Insertion at an empty error, before it")]
    [DataRow(5, 0, 5, 0, "XX", 5, 0, false, DisplayName = "Insertion at an empty error, after it")]
    [DataRow(5, 0, 5, 0, "XX", 6, 0, true, DisplayName = "Insertion at an empty error, error inside the insertion")]
    public async Task FindFirstNewAsync_ErrorWithAChangedMessage_IsExistingOnlyWhenItsLocationMapsThroughTheEdit(
        int oldStart,
        int oldLength,
        int changeStart,
        int changeLength,
        string insertedText,
        int newStart,
        int newLength,
        bool expectNew)
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        DocumentId documentId = workspace.AddDocument("Sample.cs", Text);
        Project before = workspace.CreateSolution().GetProject(documentId.ProjectId);
        SourceText oldText = await before.GetDocument(documentId).GetTextAsync();
        Project after = before.Solution
            .WithDocumentText(documentId, oldText.WithChanges(new TextChange(new TextSpan(changeStart, changeLength), insertedText)))
            .GetProject(before.Id);
        Diagnostic beforeError = Diagnostic.Create(ErrorDescriptor, Location.Create(await before.GetDocument(documentId).GetSyntaxTreeAsync(), new TextSpan(oldStart, oldLength)), "old");
        Diagnostic afterError = Diagnostic.Create(ErrorDescriptor, Location.Create(await after.GetDocument(documentId).GetSyntaxTreeAsync(), new TextSpan(newStart, newLength)), "new");

        Diagnostic newError = await CompilerErrors.FindFirstNewAsync(before, new[] { beforeError }, after, new[] { afterError }, CancellationToken.None);

        Assert.AreEqual(expectNew, newError is not null);
    }
}
