using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace CodeJanitor.UnitTests.Cleaning.Diagnostics;

/// <summary>
/// In-memory Roslyn workspace that mirrors how Visual Studio hosts the IDE analyzers: the
/// Microsoft.CodeAnalysis(.CSharp).Features assemblies are solution-level analyzer references (host analyzers),
/// the MEF host includes the Features assemblies (needed by fixes such as rename), and every .editorconfig is an
/// analyzer config document with an absolute path next to the documents it governs. Nothing touches the disk.
/// </summary>
internal sealed class DiagnosticCleanupTestWorkspace : IDisposable
{
    private static readonly Lazy<ImmutableArray<Assembly>> s_featuresAssemblies = new Lazy<ImmutableArray<Assembly>>(
        () => ImmutableArray.Create(
            Assembly.Load(new AssemblyName("Microsoft.CodeAnalysis.Features")),
            Assembly.Load(new AssemblyName("Microsoft.CodeAnalysis.CSharp.Features"))));

    private static readonly Lazy<MefHostServices> s_hostServices = new Lazy<MefHostServices>(
        () => MefHostServices.Create(MefHostServices.DefaultAssemblies.Concat(s_featuresAssemblies.Value).Distinct()));

    private static readonly Lazy<ImmutableArray<AnalyzerReference>> s_hostAnalyzerReferences = new Lazy<ImmutableArray<AnalyzerReference>>(
        () => s_featuresAssemblies.Value
            .Select(assembly => (AnalyzerReference)new AnalyzerFileReference(assembly.Location, LoadedAssemblyLoader.Instance))
            .ToImmutableArray());

    private readonly ProjectId _projectId = ProjectId.CreateNewId("TestProject");
    private readonly List<DocumentInfo> _documents = new List<DocumentInfo>();
    private readonly List<DocumentInfo> _editorConfigs = new List<DocumentInfo>();
    private readonly ImmutableArray<DiagnosticAnalyzer> _projectAnalyzers;
    private AdhocWorkspace _workspace;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticCleanupTestWorkspace" /> class.
    /// </summary>
    /// <param name="projectAnalyzers">Test analyzers added as a project-level analyzer reference.</param>
    public DiagnosticCleanupTestWorkspace(params DiagnosticAnalyzer[] projectAnalyzers)
    {
        _projectAnalyzers = projectAnalyzers.ToImmutableArray();
    }

    /// <summary>
    /// Gets the host (Features) analyzers exactly as the solution-level analyzer references expose them.
    /// </summary>
    public static ImmutableArray<DiagnosticAnalyzer> HostAnalyzers =>
        s_hostAnalyzerReferences.Value.SelectMany(reference => reference.GetAnalyzers(LanguageNames.CSharp)).ToImmutableArray();

    /// <summary>
    /// Gets the absolute directory all test documents and .editorconfig files live in.
    /// </summary>
    public static string RootDirectory { get; } = Path.Combine(Path.GetTempPath(), "CodeJanitor.DiagnosticCleanupTests", "repo");

    /// <summary>
    /// Gets the workspace; available after <see cref="CreateSolution" />.
    /// </summary>
    public Workspace Workspace => _workspace;

    /// <summary>
    /// Builds an absolute path below <see cref="RootDirectory" />.
    /// </summary>
    public static string GetPath(string relativePath) =>
        string.IsNullOrEmpty(relativePath)
            ? RootDirectory
            : Path.Combine(RootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Adds a C# document at <paramref name="relativePath" />.
    /// </summary>
    public DocumentId AddDocument(string relativePath, string text)
    {
        var filePath = GetPath(relativePath);
        var documentId = DocumentId.CreateNewId(_projectId, relativePath);
        var segments = relativePath.Split('/');
        var folders = segments.Take(segments.Length - 1).ToArray();

        _documents.Add(DocumentInfo.Create(documentId, Path.GetFileName(filePath), folders, SourceCodeKind.Regular, CreateLoader(text, filePath), filePath));

        return documentId;
    }

    /// <summary>
    /// Adds an .editorconfig located in <paramref name="relativeDirectory" /> (empty for the root directory).
    /// </summary>
    public void AddEditorConfig(string relativeDirectory, string text)
    {
        var filePath = Path.Combine(GetPath(relativeDirectory), ".editorconfig");

        _editorConfigs.Add(DocumentInfo.Create(DocumentId.CreateNewId(_projectId, filePath), ".editorconfig", loader: CreateLoader(text, filePath), filePath: filePath));
    }

    /// <summary>
    /// Creates the workspace on first use and returns its current solution.
    /// </summary>
    public Solution CreateSolution()
    {
        if (_workspace == null)
        {
            var projectInfo = ProjectInfo.Create(
                    _projectId,
                    VersionStamp.Default,
                    "TestProject",
                    "TestProject",
                    LanguageNames.CSharp,
                    filePath: GetPath("TestProject.csproj"),
                    compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                    documents: _documents,
                    metadataReferences: new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                    analyzerReferences: _projectAnalyzers.IsEmpty ? null : new[] { new AnalyzerImageReference(_projectAnalyzers) })
                .WithAnalyzerConfigDocuments(_editorConfigs);

            _workspace = new AdhocWorkspace(s_hostServices.Value);
            _workspace.AddSolution(SolutionInfo.Create(
                SolutionId.CreateNewId(),
                VersionStamp.Default,
                projects: new[] { projectInfo },
                analyzerReferences: s_hostAnalyzerReferences.Value));
        }

        return _workspace.CurrentSolution;
    }

    /// <summary>
    /// Reads the full text of a document in <paramref name="solution" />.
    /// </summary>
    public static async Task<string> GetTextAsync(Solution solution, DocumentId documentId)
    {
        var text = await solution.GetDocument(documentId).GetTextAsync().ConfigureAwait(false);

        return text.ToString();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _workspace?.Dispose();
    }

    private static TextLoader CreateLoader(string text, string filePath) =>
        TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Default, filePath));

    /// <summary>
    /// Resolves analyzer assemblies through the default load context so analyzer, fixer and MEF types are the
    /// same runtime types the test process already loaded.
    /// </summary>
    private sealed class LoadedAssemblyLoader : IAnalyzerAssemblyLoader
    {
        public static readonly LoadedAssemblyLoader Instance = new LoadedAssemblyLoader();

        public void AddDependencyLocation(string fullPath)
        {
        }

        public Assembly LoadFromPath(string fullPath) => Assembly.Load(AssemblyName.GetAssemblyName(fullPath));
    }
}
