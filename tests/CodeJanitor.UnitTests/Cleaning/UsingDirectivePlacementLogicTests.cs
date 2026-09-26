using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning;

/// <summary>
/// Unit tests for placing the using directives of a file during cleanup: the direction comes from
/// <see cref="EffectiveCleanupSettings.UsingDirectivePlacement" /> of the file, and for a file compiled by several
/// projects (linked files, shared projects, multi-targeted projects) <see cref="VisualStudioRoslynWorkspace.GetDocumentsAsync" />
/// resolves every flavor of the file and <see cref="UsingDirectivePlacementLogic.PlaceInEveryFlavorAsync" /> moves
/// only when all of them agree.
/// </summary>
[TestClass]
public sealed class UsingDirectivePlacementLogicTests
{
    private const string TargetPath = @"C:\src\Shared\Target.cs";
    private const string AppProjectPath = @"C:\src\App\App.csproj";
    private const string ToolProjectPath = @"C:\src\Tool\Tool.csproj";

    private const string Target = "namespace Company.App\r\n{\r\n    using Services;\r\n    class C { Svc s; }\r\n}\r\n";
    private const string MovedOutside = "using Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; }\r\n}\r\n";

    private const string FileLevelTarget = "using Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; }\r\n}\r\n";
    private const string MovedInside = "namespace Company.App\r\n{\r\n    using Company.App.Services;\r\n\r\n    class C { Svc s; }\r\n}\r\n";
    private const string FileLevelTargetInRegion = "#region Usings\r\nusing Company.App.Services;\r\n#endregion\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; }\r\n}\r\n";

    private const string NestedServicesLibrary = "namespace Company.App.Services { public class Svc { } }\r\n";
    private const string GlobalServicesLibrary = "namespace Services { public class Svc { } }\r\n";
    private const string UnrelatedLibrary = "namespace Other { public class Unrelated { } }\r\n";

    private static readonly MetadataReference MscorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    private string _tempDirectory;

    [TestInitialize]
    public void TestInitialize()
    {
        Settings.Default.Reset();
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
    [TestCategory("Cleaning UnitTests")]
    public async Task LinkedFile_SameMoveInEveryProject_IsMoved()
    {
        var solution = CreateSolution(Target, ("App", AppProjectPath, NestedServicesLibrary), ("Tool", ToolProjectPath, NestedServicesLibrary));

        var result = await PlaceAsync(solution, Target, UsingDirectivePlacementPreference.OutsideNamespace);

        Assert.AreEqual(UsingDirectivePlacementStatus.Moved, result.Status, result.Reason);
        Assert.AreEqual(MovedOutside, result.Text);
        await AssertCompilesInEveryProjectAsync(solution, TargetPath, result.Text);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task LinkedFile_SameInwardMoveInEveryProject_IsMoved()
    {
        var solution = CreateSolution(FileLevelTarget, ("App", AppProjectPath, NestedServicesLibrary), ("Tool", ToolProjectPath, NestedServicesLibrary));

        var result = await PlaceAsync(solution, FileLevelTarget, UsingDirectivePlacementPreference.InsideNamespace);

        Assert.AreEqual(UsingDirectivePlacementStatus.Moved, result.Status, result.Reason);
        Assert.AreEqual(MovedInside, result.Text);
        await AssertCompilesInEveryProjectAsync(solution, TargetPath, result.Text);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task LinkedFile_QualificationDiffersBetweenProjects_IsSkippedNamingTheOtherProject()
    {
        // In App, "Services" means Company.App.Services; in Tool it means the global Services namespace. Applying
        // App's "using Company.App.Services;" would break Tool (CS0234).
        var solution = CreateSolution(Target, ("App", AppProjectPath, NestedServicesLibrary), ("Tool", ToolProjectPath, GlobalServicesLibrary));

        var result = await PlaceAsync(solution, Target, UsingDirectivePlacementPreference.OutsideNamespace);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status);
        StringAssert.Contains(result.Reason, "'Tool'");
        Assert.IsNull(result.Text);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task LinkedFile_MoveUnsafeInOneProject_IsSkippedWithThatProjectsReason()
    {
        var solution = CreateSolution(Target, ("App", AppProjectPath, NestedServicesLibrary), ("Tool", ToolProjectPath, UnrelatedLibrary));

        var result = await PlaceAsync(solution, Target, UsingDirectivePlacementPreference.OutsideNamespace);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status);
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

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("inside_namespace", DisplayName = "no severity")]
    [DataRow("inside_namespace:silent", DisplayName = "silent")]
    [DataRow("inside_namespace:warning", DisplayName = "warning")]
    public async Task EditorConfigInsideNamespace_MovesFileLevelUsingsInside_OverTheUserSetting(string placement)
    {
        WriteEditorConfig("csharp_using_directive_placement = " + placement);
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = true;

        var result = await PlaceAsConfiguredAsync(FileLevelTarget);

        Assert.AreEqual(UsingDirectivePlacementStatus.Moved, result.Status, result.Reason);
        Assert.AreEqual(MovedInside, result.Text);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task EditorConfigInsideNamespace_LeavesUsingsThatAreAlreadyInside()
    {
        WriteEditorConfig("csharp_using_directive_placement = inside_namespace:suggestion");

        var result = await PlaceAsConfiguredAsync(Target);

        Assert.AreEqual(UsingDirectivePlacementStatus.NothingToMove, result.Status, result.Reason);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task EditorConfigOutsideNamespace_MovesUsingsOutside_OverTheRepositoryPolicy()
    {
        WriteEditorConfig("csharp_using_directive_placement = outside_namespace:error");
        WriteRepositoryPolicy(moveUsingsOutsideNamespace: false);

        var result = await PlaceAsConfiguredAsync(Target);

        Assert.AreEqual(UsingDirectivePlacementStatus.Moved, result.Status, result.Reason);
        Assert.AreEqual(MovedOutside, result.Text);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task RepositoryPolicy_MovesUsingsOutside_OverTheUserSetting_WhenEditorConfigDoesNotSetThePlacement()
    {
        WriteEditorConfig("indent_style = space");
        WriteRepositoryPolicy(moveUsingsOutsideNamespace: true);
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = false;

        var result = await PlaceAsConfiguredAsync(Target);

        Assert.AreEqual(UsingDirectivePlacementStatus.Moved, result.Status, result.Reason);
        Assert.AreEqual(MovedOutside, result.Text);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("outside_namespace:none", false, false, DisplayName = "outside_namespace:none, user off")]
    [DataRow("inside_namespace:none", true, true, DisplayName = "inside_namespace:none, user on")]
    [DataRow(null, false, false, DisplayName = "nothing configured, user off")]
    public async Task NoneSeverity_IsIgnored_SoTheUserSettingDecides(string editorConfigPlacement, bool userSetting, bool movedOutside)
    {
        WriteEditorConfig(editorConfigPlacement == null ? "indent_style = space" : "csharp_using_directive_placement = " + editorConfigPlacement);
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = userSetting;

        var inside = await PlaceAsConfiguredAsync(Target);
        var outside = await PlaceAsConfiguredAsync(FileLevelTarget);

        Assert.AreEqual(movedOutside ? UsingDirectivePlacementStatus.Moved : UsingDirectivePlacementStatus.NothingToMove, inside.Status, inside.Reason);
        Assert.AreEqual(UsingDirectivePlacementStatus.NothingToMove, outside.Status, outside.Text);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("utf-8", "\r\n", DisplayName = "UTF-8 without byte order mark, CRLF")]
    [DataRow("utf-8-bom", "\n", DisplayName = "UTF-8 with byte order mark, LF")]
    [DataRow("utf-16", "\r\n", DisplayName = "UTF-16 with byte order mark, CRLF")]
    public async Task ClosedFile_Moved_IsWrittenBackWithTheEncodingAndLineEndingsOfTheFile(string encodingName, string newline)
    {
        WriteEditorConfig("csharp_using_directive_placement = outside_namespace:warning");
        var encoding = encodingName switch
        {
            "utf-8-bom" => new UTF8Encoding(true),
            "utf-16" => new UnicodeEncoding(false, true),
            _ => (Encoding)new UTF8Encoding(false),
        };

        // The comment is not ASCII, so the written bytes differ between encodings.
        const string ClassLineEndWithNonAsciiComment = "Svc s; } // Za\u017C\u00F3\u0142\u0107";
        var source = Target.Replace("Svc s; }", ClassLineEndWithNonAsciiComment).Replace("\r\n", newline);
        var expected = MovedOutside.Replace("Svc s; }", ClassLineEndWithNonAsciiComment).Replace("\r\n", newline);
        var filePath = Path.Combine(_tempDirectory, "Target.cs");
        File.WriteAllBytes(filePath, encoding.GetPreamble().Concat(encoding.GetBytes(source)).ToArray());

        var outcome = await PlaceInClosedFileAsync(filePath);

        Assert.AreEqual(UsingsMoveOutcome.Moved, outcome);
        CollectionAssert.AreEqual(
            encoding.GetPreamble().Concat(encoding.GetBytes(expected)).ToArray(),
            File.ReadAllBytes(filePath),
            File.ReadAllText(filePath));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task ClosedFile_UsingsInARegion_AreMovedWithTheRegionRemoved()
    {
        WriteEditorConfig("csharp_using_directive_placement = inside_namespace:warning");
        var filePath = Path.Combine(_tempDirectory, "Target.cs");
        File.WriteAllText(filePath, FileLevelTargetInRegion);

        var outcome = await PlaceInClosedFileAsync(filePath);

        Assert.AreEqual(UsingsMoveOutcome.Moved, outcome);
        Assert.AreEqual(MovedInside, File.ReadAllText(filePath));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task ClosedFile_UsingsInARegion_AreLeftInPlaceWithTheRegion_WhenThePolicyKeepsRegions()
    {
        WriteEditorConfig("csharp_using_directive_placement = inside_namespace:warning");
        File.WriteAllText(Path.Combine(_tempDirectory, ".codejanitor"), "{ \"cleanup\": { \"removeRegions\": false } }");
        var filePath = Path.Combine(_tempDirectory, "Target.cs");
        File.WriteAllText(filePath, FileLevelTargetInRegion);

        var outcome = await PlaceInClosedFileAsync(filePath);

        Assert.AreEqual(UsingsMoveOutcome.LeftInPlace, outcome);
        Assert.AreEqual(FileLevelTargetInRegion, File.ReadAllText(filePath));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task ClosedFile_CanceledMove_ThrowsAndLeavesTheFileUnchanged()
    {
        WriteEditorConfig("csharp_using_directive_placement = outside_namespace:warning");
        var filePath = Path.Combine(_tempDirectory, "Target.cs");
        File.WriteAllText(filePath, Target);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => PlaceInClosedFileAsync(filePath, canceled.Token));

        Assert.AreEqual(Target, File.ReadAllText(filePath));
    }

    private static async Task<UsingDirectivePlacementResult> PlaceAsync(Solution solution, string currentText, UsingDirectivePlacementPreference placement)
    {
        var documents = await VisualStudioRoslynWorkspace.GetDocumentsAsync(solution, TargetPath, AppProjectPath, currentText, CancellationToken.None);

        return await UsingDirectivePlacementLogic.PlaceInEveryFlavorAsync(new UsingDirectivePlacementConverter(), placement, documents, CancellationToken.None);
    }

    /// <summary>
    /// Places the using directives of <paramref name="source" />, saved as a file of the temporary directory, in the
    /// direction its effective cleanup settings enforce.
    /// </summary>
    private async Task<UsingDirectivePlacementResult> PlaceAsConfiguredAsync(string source)
    {
        var filePath = Path.Combine(_tempDirectory, "Target.cs");
        var projectPath = Path.Combine(_tempDirectory, "App.csproj");
        File.WriteAllText(filePath, source);
        var solution = CreateSolution(source, filePath, ("App", projectPath, NestedServicesLibrary));
        var documents = await VisualStudioRoslynWorkspace.GetDocumentsAsync(solution, filePath, projectPath, source, CancellationToken.None);

        return await UsingDirectivePlacementLogic.PlaceInEveryFlavorAsync(
            new UsingDirectivePlacementConverter(),
            EffectiveCleanupSettings.For(filePath).UsingDirectivePlacement,
            documents,
            CancellationToken.None);
    }

    /// <summary>
    /// Places the using directives of the closed file <paramref name="filePath" /> of the temporary directory, as
    /// <see cref="UsingDirectivePlacementLogic.PlaceUsingDirectivesAsync" /> does, with the semantic move running on a
    /// solution that holds the file as saved on disk (the Visual Studio workspace in production).
    /// </summary>
    private Task<UsingsMoveOutcome> PlaceInClosedFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var projectPath = Path.Combine(_tempDirectory, "App.csproj");
        var solution = CreateSolution(File.ReadAllText(filePath), filePath, ("App", projectPath, NestedServicesLibrary));

        return UsingDirectivePlacementLogic.PlaceUsingDirectivesInFileAsync(filePath, async (currentText, placement) =>
        {
            var documents = await VisualStudioRoslynWorkspace.GetDocumentsAsync(solution, filePath, projectPath, currentText, cancellationToken);
            var result = await UsingDirectivePlacementLogic.PlaceInEveryFlavorAsync(new UsingDirectivePlacementConverter(), placement, documents, cancellationToken);

            return result.Status switch
            {
                UsingDirectivePlacementStatus.Moved => (UsingsMoveOutcome.Moved, result.Text),
                UsingDirectivePlacementStatus.Skipped => (UsingsMoveOutcome.LeftInPlace, (string)null),
                _ => (UsingsMoveOutcome.NotApplicable, (string)null),
            };
        });
    }

    private void WriteEditorConfig(string option) =>
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"), "root = true\r\n\r\n[*.cs]\r\n" + option + "\r\n");

    private void WriteRepositoryPolicy(bool moveUsingsOutsideNamespace) =>
        File.WriteAllText(
            Path.Combine(_tempDirectory, ".codejanitor"),
            "{ \"cleanup\": { \"moveUsingsOutsideNamespace\": " + (moveUsingsOutsideNamespace ? "true" : "false") + " } }");

    private static async Task AssertCompilesInEveryProjectAsync(Solution solution, string targetPath, string text)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetDocument(project.DocumentIds.Single(id => project.GetDocument(id).FilePath == targetPath))
                .WithText(SourceText.From(text))
                .Project
                .GetCompilationAsync();

            var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
            Assert.AreEqual(0, errors.Count, $"{project.Name}: {string.Join("; ", errors)}");
        }
    }

    private static Solution CreateSolution(string targetSource, params (string Name, string FilePath, string LibrarySource)[] projects) =>
        CreateSolution(targetSource, TargetPath, projects);

    /// <summary>
    /// Creates a solution where <paramref name="targetSource" /> is linked into every project (same file path), each
    /// project also compiling its own library source.
    /// </summary>
    private static Solution CreateSolution(string targetSource, string targetPath, params (string Name, string FilePath, string LibrarySource)[] projects)
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
                    filePath: targetPath));
        }

        return solution;
    }
}
