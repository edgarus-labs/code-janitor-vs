using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Logic.Transformations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning;

/// <summary>
/// Unit tests for moving the using directives of a file compiled by several projects (linked files, shared projects,
/// multi-targeted projects): <see cref="VisualStudioRoslynWorkspace.GetDocumentsAsync" /> resolves every flavor of the
/// file and <see cref="MoveUsingsOutsideNamespaceLogic.MoveInEveryFlavorAsync" /> moves only when all of them agree.
/// </summary>
[TestClass]
public sealed class MoveUsingsOutsideNamespaceLogicTests
{
    private const string TargetPath = @"C:\src\Shared\Target.cs";
    private const string AppProjectPath = @"C:\src\App\App.csproj";
    private const string ToolProjectPath = @"C:\src\Tool\Tool.csproj";

    private const string Target = "namespace Company.App\r\n{\r\n    using Services;\r\n    class C { Svc s; }\r\n}\r\n";

    private const string NestedServicesLibrary = "namespace Company.App.Services { public class Svc { } }\r\n";
    private const string GlobalServicesLibrary = "namespace Services { public class Svc { } }\r\n";
    private const string UnrelatedLibrary = "namespace Other { public class Unrelated { } }\r\n";

    private static readonly MetadataReference MscorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task LinkedFile_SameMoveInEveryProject_IsMoved()
    {
        var solution = CreateSolution(Target, ("App", AppProjectPath, NestedServicesLibrary), ("Tool", ToolProjectPath, NestedServicesLibrary));

        var result = await MoveAsync(solution, Target);

        Assert.AreEqual(MoveUsingsOutsideNamespaceStatus.Moved, result.Status, result.Reason);
        StringAssert.StartsWith(result.Text, "using Company.App.Services;\r\n\r\nnamespace Company.App\r\n");
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetDocument(project.DocumentIds.Single(id => project.GetDocument(id).FilePath == TargetPath))
                .WithText(SourceText.From(result.Text))
                .Project
                .GetCompilationAsync();

            var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
            Assert.AreEqual(0, errors.Count, $"{project.Name}: {string.Join("; ", errors)}");
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task LinkedFile_QualificationDiffersBetweenProjects_IsSkippedNamingTheOtherProject()
    {
        // In App, "Services" means Company.App.Services; in Tool it means the global Services namespace. Applying
        // App's "using Company.App.Services;" would break Tool (CS0234).
        var solution = CreateSolution(Target, ("App", AppProjectPath, NestedServicesLibrary), ("Tool", ToolProjectPath, GlobalServicesLibrary));

        var result = await MoveAsync(solution, Target);

        Assert.AreEqual(MoveUsingsOutsideNamespaceStatus.Skipped, result.Status);
        StringAssert.Contains(result.Reason, "'Tool'");
        Assert.IsNull(result.Text);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task LinkedFile_MoveUnsafeInOneProject_IsSkippedWithThatProjectsReason()
    {
        var solution = CreateSolution(Target, ("App", AppProjectPath, NestedServicesLibrary), ("Tool", ToolProjectPath, UnrelatedLibrary));

        var result = await MoveAsync(solution, Target);

        Assert.AreEqual(MoveUsingsOutsideNamespaceStatus.Skipped, result.Status);
        StringAssert.EndsWith(result.Reason, "in project 'Tool'");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task GetDocumentsAsync_ReturnsEveryFlavorWithCurrentText_ContainingProjectFirst()
    {
        var solution = CreateSolution(Target, ("App", AppProjectPath, NestedServicesLibrary), ("Tool", ToolProjectPath, NestedServicesLibrary));
        var currentText = Target.Replace("Svc s;", "Svc t;");

        var documents = await VisualStudioRoslynWorkspace.GetDocumentsAsync(solution, TargetPath, ToolProjectPath, currentText, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "Tool", "App" }, documents.Select(document => document.Project.Name).ToList());
        foreach (var document in documents)
        {
            Assert.AreEqual(currentText, (await document.GetTextAsync()).ToString(), document.Project.Name);
        }
    }

    private static async Task<MoveUsingsOutsideNamespaceResult> MoveAsync(Solution solution, string currentText)
    {
        var documents = await VisualStudioRoslynWorkspace.GetDocumentsAsync(solution, TargetPath, AppProjectPath, currentText, CancellationToken.None);

        return await MoveUsingsOutsideNamespaceLogic.MoveInEveryFlavorAsync(new MoveUsingsOutsideNamespaceConverter(), documents, CancellationToken.None);
    }

    /// <summary>
    /// Creates a solution where <paramref name="targetSource" /> is linked into every project (same file path), each
    /// project also compiling its own library source.
    /// </summary>
    private static Solution CreateSolution(string targetSource, params (string Name, string FilePath, string LibrarySource)[] projects)
    {
        var solution = new AdhocWorkspace().CurrentSolution;
        foreach (var (name, filePath, librarySource) in projects)
        {
            var projectId = ProjectId.CreateNewId(name);
            solution = solution.AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                name,
                name,
                LanguageNames.CSharp,
                filePath: filePath,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
                metadataReferences: new List<MetadataReference> { MscorlibReference }));

            solution = solution
                .AddDocument(DocumentId.CreateNewId(projectId), "Library.cs", SourceText.From(librarySource))
                .AddDocument(DocumentInfo.Create(
                    DocumentId.CreateNewId(projectId),
                    "Target.cs",
                    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(targetSource), VersionStamp.Create())),
                    filePath: TargetPath));
        }

        return solution;
    }
}
