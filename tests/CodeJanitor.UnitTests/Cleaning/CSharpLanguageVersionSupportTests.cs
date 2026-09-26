using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using CodeJanitor.UnitTests.Transformations;

namespace CodeJanitor.UnitTests.Cleaning;

/// <summary>
/// Tests that cleanup emits syntax newer than C# 7.3 only when every project compiling the file uses a C# language
/// version that supports it.
/// </summary>
[TestClass]
public sealed class CSharpLanguageVersionSupportTests
{
    private const string BlockScopedSource = "namespace Demo\r\n{\r\n    public class C\r\n    {\r\n    }\r\n}\r\n";

    private const string CollectionSource = "public class C\r\n{\r\n    private int[] _values = new int[] { 1, 2 };\r\n}\r\n";

    private const string NullCheckSource = "public class C\r\n{\r\n    public bool M(object x, object y)\r\n    {\r\n        return x == null || y != null;\r\n    }\r\n}\r\n";

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "CodeJanitor.FileScopedSupport");

    private string _tempDirectory;

    [TestInitialize]
    public void TestInitialize()
    {
        Settings.Default.Reset();
        Settings.Default.Cleaning_AiXmlDocumentationEnabled = false;
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = true;
        _tempDirectory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Settings.Default.Reset();
        CSharpLanguageVersionSupport.SetLanguageVersionResolver(null);

        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(LanguageVersion.CSharp7_3, false, DisplayName = "C# 7.3 project")]
    [DataRow(LanguageVersion.CSharp9, false, DisplayName = "C# 9 project")]
    [DataRow(LanguageVersion.CSharp10, true, DisplayName = "C# 10 project")]
    [DataRow(LanguageVersion.Latest, true, DisplayName = "latest language version")]
    public void HeadlessCleanup_ConvertsToFileScopedNamespace_OnlyForCSharp10OrNewer(LanguageVersion languageVersion, bool converted)
    {
        CSharpLanguageVersionSupport.SetLanguageVersionResolver(_ => new[] { languageVersion });

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(BlockScopedSource, Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.AreEqual(converted, output.Contains("namespace Demo;"), output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void HeadlessCleanup_KeepsBlockScopedNamespace_WhenAnyTargetFrameworkUsesAnOlderLanguageVersion()
    {
        // A project multi-targeting net48 (C# 7.3 by default) and net8.0 compiles the file twice.
        CSharpLanguageVersionSupport.SetLanguageVersionResolver(_ => new[] { LanguageVersion.CSharp12, LanguageVersion.CSharp7_3 });

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(BlockScopedSource, Path.Combine(_tempDirectory, "Sample.cs"));

        StringAssert.Contains(output, "namespace Demo\r\n{");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(LanguageVersion.CSharp7_3, false, DisplayName = "C# 7.3 project")]
    [DataRow(LanguageVersion.CSharp11, false, DisplayName = "C# 11 project")]
    [DataRow(LanguageVersion.CSharp12, true, DisplayName = "C# 12 project")]
    public async Task HeadlessCleanup_ConvertsToCollectionExpressions_OnlyForCSharp12OrNewer(LanguageVersion languageVersion, bool converted)
    {
        // The .editorconfig preference enables the step regardless of the Visual Studio setting.
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"), "root = true\r\n\r\n[*.cs]\r\ndotnet_style_prefer_collection_expression = true\r\n");
        CSharpLanguageVersionSupport.SetLanguageVersionResolver(_ => new[] { languageVersion });

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(CollectionSource, Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.AreEqual(converted, output.Contains("_values = [1, 2];"), output);
        await AssertCompilesAsync(CollectionSource, languageVersion, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(LanguageVersion.CSharp7_3, false, DisplayName = "C# 7.3 project")]
    [DataRow(LanguageVersion.CSharp8, false, DisplayName = "C# 8 project")]
    [DataRow(LanguageVersion.CSharp9, true, DisplayName = "C# 9 project")]
    public async Task HeadlessCleanup_ConvertsInequalityNullChecksToIsNotNull_OnlyForCSharp9OrNewer(LanguageVersion languageVersion, bool converted)
    {
        Settings.Default.Cleaning_ConvertToPatternMatchingNullChecks = true;
        CSharpLanguageVersionSupport.SetLanguageVersionResolver(_ => new[] { languageVersion });

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(NullCheckSource, Path.Combine(_tempDirectory, "Sample.cs"));

        StringAssert.Contains(output, "x is null");
        Assert.AreEqual(converted, output.Contains("y is not null"), output);
        Assert.AreEqual(!converted, output.Contains("y != null"), output);
        await AssertCompilesAsync(NullCheckSource, languageVersion, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task HeadlessCleanup_EmitsNoSyntaxNewerThanCSharp7_3_WhenTheLanguageVersionIsUnknown()
    {
        Settings.Default.Cleaning_ConvertToCollectionExpressions = true;
        Settings.Default.Cleaning_ConvertToPatternMatchingNullChecks = true;
        const string source =
            "namespace Demo\r\n{\r\n    public class C\r\n    {\r\n        private int[] _values = new int[] { 1, 2 };\r\n\r\n" +
            "        public bool M(object x, object y)\r\n        {\r\n            return x == null || y != null;\r\n        }\r\n    }\r\n}\r\n";
        var filePath = Path.Combine(_tempDirectory, "Sample.cs");
        var resolvers = new Func<string, IReadOnlyList<LanguageVersion>>[]
        {
            _ => Array.Empty<LanguageVersion>(),
            null,
            _ => throw new InvalidOperationException("workspace unavailable"),
        };

        foreach (var resolver in resolvers)
        {
            CSharpLanguageVersionSupport.SetLanguageVersionResolver(resolver);

            var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(source, filePath);

            StringAssert.Contains(output, "namespace Demo\r\n{");
            StringAssert.Contains(output, "new int[] { 1, 2 }");
            StringAssert.Contains(output, "x is null || y != null");
            await AssertCompilesAsync(source, LanguageVersion.CSharp7_3, output);
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(LanguageVersion.CSharp11, false, DisplayName = "C# 11 project")]
    [DataRow(LanguageVersion.CSharp12, true, DisplayName = "C# 12 project")]
    public void EditorCollectionExpressionStep_ConvertsOnlyForCSharp12OrNewer(LanguageVersion languageVersion, bool converted)
    {
        CSharpLanguageVersionSupport.SetLanguageVersionResolver(_ => new[] { languageVersion });

        var output = CollectionExpressionLogic.GetInstance(null).ConvertToCollectionExpressions(CollectionSource, Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.AreEqual(converted ? CollectionSource.Replace("new int[] { 1, 2 }", "[1, 2]") : CollectionSource, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Supports_ExplainsWhichLanguageVersionBlocksTheSyntax()
    {
        CSharpLanguageVersionSupport.SetLanguageVersionResolver(_ => new[] { LanguageVersion.CSharp12, LanguageVersion.CSharp7_3 });
        var support = CSharpLanguageVersionSupport.For("Sample.cs");

        Assert.IsFalse(support.Supports(CSharpLanguageVersionSupport.CollectionExpressions, out var skipMessage));
        StringAssert.Contains(skipMessage, "'Sample.cs'");
        StringAssert.Contains(skipMessage, "C# 7.3 and collection expressions require C# 12.0 or newer");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void GetCSharpLanguageVersions_ReturnsTheVersionOfEveryProjectCompilingTheFile()
    {
        var filePath = Path.Combine(Root, "Shared", "Linked.cs");
        var solution = new AdhocWorkspace().CurrentSolution;
        solution = AddProject(solution, "Modern", Path.Combine(Root, "Modern", "Modern.csproj"), LanguageVersion.CSharp12, filePath);
        solution = AddProject(solution, "Legacy", Path.Combine(Root, "Legacy", "Legacy.csproj"), LanguageVersion.CSharp7_3, filePath);

        var versions = VisualStudioRoslynWorkspace.GetCSharpLanguageVersions(solution, filePath);

        CollectionAssert.AreEquivalent(new[] { LanguageVersion.CSharp12, LanguageVersion.CSharp7_3 }, versions.ToList());
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void GetCSharpLanguageVersions_UsesTheClosestProjectDirectory_ForAFileNotInTheSolutionYet()
    {
        var solution = new AdhocWorkspace().CurrentSolution;
        solution = AddProject(solution, "Outer", Path.Combine(Root, "App", "App.csproj"), LanguageVersion.CSharp12, Path.Combine(Root, "App", "Existing.cs"));
        solution = AddProject(solution, "Inner", Path.Combine(Root, "App", "Legacy", "Legacy.csproj"), LanguageVersion.CSharp7_3, Path.Combine(Root, "App", "Legacy", "Existing.cs"));

        CollectionAssert.AreEqual(
            new[] { LanguageVersion.CSharp7_3 },
            VisualStudioRoslynWorkspace.GetCSharpLanguageVersions(solution, Path.Combine(Root, "App", "Legacy", "Split.cs")).ToList());
        CollectionAssert.AreEqual(
            new[] { LanguageVersion.CSharp12 },
            VisualStudioRoslynWorkspace.GetCSharpLanguageVersions(solution, Path.Combine(Root, "App", "Models", "Split.cs")).ToList());
        Assert.AreEqual(0, VisualStudioRoslynWorkspace.GetCSharpLanguageVersions(solution, Path.Combine(Root, "Elsewhere", "Split.cs")).Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void GetCSharpLanguageVersions_ReturnsTheEffectiveVersion_WhenTheProjectUsesTheDefault()
    {
        var filePath = Path.Combine(Root, "Default", "Sample.cs");
        var solution = AddProject(new AdhocWorkspace().CurrentSolution, "Default", Path.Combine(Root, "Default", "Default.csproj"), LanguageVersion.Default, filePath);

        var version = VisualStudioRoslynWorkspace.GetCSharpLanguageVersions(solution, filePath).Single();

        Assert.AreEqual(LanguageVersion.Default.MapSpecifiedToEffectiveVersion(), version);
    }

    /// <summary>
    /// Asserts that the cleanup output of <paramref name="source" /> compiles in a project of the specified language
    /// version.
    /// </summary>

    private static async Task AssertCompilesAsync(string source, LanguageVersion languageVersion, string output)
    {
        var document = CompilingTestProject.CreateDocument(source, languageVersion, new MetadataReference[0]);
        var errors = await CompilingTestProject.GetCompileErrorsAsync(document, output);

        Assert.AreEqual(0, errors.Count, output + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }

    private static Solution AddProject(Solution solution, string name, string projectFilePath, LanguageVersion languageVersion, string documentFilePath)
    {
        var projectId = ProjectId.CreateNewId(name);
        solution = solution.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name,
            name,
            LanguageNames.CSharp,
            filePath: projectFilePath,
            parseOptions: new CSharpParseOptions(languageVersion)));

        return solution.AddDocument(
            DocumentId.CreateNewId(projectId),
            Path.GetFileName(documentFilePath),
            SourceText.From("namespace Demo { }"),
            filePath: documentFilePath);
    }
}
