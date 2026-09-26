using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeJanitor.Logic.Cleaning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning;

/// <summary>
/// Unit tests for <see cref="EditorConfigDiagnosticCleanupLogic.FindNewErrorInOtherFlavorsAsync" />: diagnostic fixes
/// computed in one project flavor of a file compiled by several projects (linked files, shared projects,
/// multi-targeted projects) are applied only when they add no compiler error in any other flavor.
/// </summary>
[TestClass]
public sealed class EditorConfigDiagnosticCleanupFlavorTests
{
    private const string TargetPath = @"C:\src\Shared\Target.cs";
    private const string AppProjectPath = @"C:\src\App\App.csproj";
    private const string ToolProjectPath = @"C:\src\Tool\Tool.csproj";

    private const string Target = "namespace Company.App\r\n{\r\n    using Services;\r\n    class C { Svc s; }\r\n}\r\n";
    private const string MovedTarget = "using Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; }\r\n}\r\n";

    private const string NestedServicesLibrary = "namespace Company.App.Services { public class Svc { } }\r\n";
    private const string GlobalServicesLibrary = "namespace Services { public class Svc { } }\r\n";

    private static readonly MetadataReference MscorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task LinkedFile_ChangeBreaksOtherProject_IsRejectedNamingThatProjectAndError()
    {
        // In App, "Services" means Company.App.Services; in Tool it means the global Services namespace. The change
        // compiles in App but "using Company.App.Services;" does not exist in Tool (CS0234).
        var solution = CreateSolution(("App", AppProjectPath, NestedServicesLibrary), ("Tool", ToolProjectPath, GlobalServicesLibrary));

        var reason = await ValidateChangeInAppAsync(solution, MovedTarget);

        Assert.IsNotNull(reason, "The change adds a compiler error in Tool and must be rejected.");
        StringAssert.Contains(reason, "'Tool'");
        StringAssert.Matches(reason, new System.Text.RegularExpressions.Regex("CS0(234|246)"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task LinkedFile_ChangeValidInEveryProject_IsAccepted()
    {
        var solution = CreateSolution(("App", AppProjectPath, NestedServicesLibrary), ("Tool", ToolProjectPath, NestedServicesLibrary));

        var reason = await ValidateChangeInAppAsync(solution, MovedTarget);

        Assert.IsNull(reason);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task LinkedFile_OtherProjectAlreadyHasErrors_OnlyNewErrorsReject()
    {
        // Tool already fails to bind Svc (CS0246) before the change; a change that keeps exactly that error adds none.
        var solution = CreateSolution(("App", AppProjectPath, NestedServicesLibrary), ("Tool", ToolProjectPath, "namespace Other { }\r\n"));

        var reason = await ValidateChangeInAppAsync(solution, Target.Replace("Svc s;", "Svc t;"));

        Assert.IsNull(reason);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task SingleFlavor_IsAcceptedWithoutRevalidation()
    {
        // The engine's own compiler-error gate owns the flavor it ran in; a file compiled by one project needs no
        // extra compilation, so even a change the validator would reject in another flavor passes here.
        var solution = CreateSolution(("App", AppProjectPath, GlobalServicesLibrary));

        var reason = await ValidateChangeInAppAsync(solution, MovedTarget);

        Assert.IsNull(reason);
    }

    /// <summary>
    /// Mimics the engine running in App: only App's document of the linked file gets <paramref name="newText" />.
    /// </summary>
    private static Task<string> ValidateChangeInAppAsync(Solution solution, string newText)
    {
        var appDocumentId = solution.GetDocumentIdsWithFilePath(TargetPath)
            .Single(id => solution.GetProject(id.ProjectId).Name == "App");
        var changedSolution = solution.WithDocumentText(appDocumentId, SourceText.From(newText));

        return EditorConfigDiagnosticCleanupLogic.FindNewErrorInOtherFlavorsAsync(solution, changedSolution, CancellationToken.None);
    }

    /// <summary>
    /// Creates a solution where <see cref="Target" /> is linked into every project (same file path), each project also
    /// compiling its own library source.
    /// </summary>
    private static Solution CreateSolution(params (string Name, string FilePath, string LibrarySource)[] projects)
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
                    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(Target), VersionStamp.Create())),
                    filePath: TargetPath));
        }

        return solution;
    }
}
