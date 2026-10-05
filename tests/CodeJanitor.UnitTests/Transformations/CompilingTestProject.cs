using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Builds an in-memory C# project (mscorlib only, unsafe code allowed) so tests can bind a document semantically and
/// compile transformed output.
/// </summary>
internal static class CompilingTestProject
{
    /// <summary>
    /// Creates the target document <c>Target.cs</c> in a project that also contains <paramref name="librarySources" />.
    /// </summary>
    public static Document CreateDocument(string source, params string[] librarySources)
        => CreateDocument(source, LanguageVersion.Latest, new MetadataReference[0], librarySources);

    /// <summary>
    /// Creates the target document <c>Target.cs</c> in a project with the given language version and additional
    /// metadata references (for example System.Core or System.Text.Json).
    /// </summary>
    public static Document CreateDocument(
        string source,
        LanguageVersion languageVersion,
        IEnumerable<MetadataReference> additionalReferences,
        params string[] librarySources)
        => CreateDocument(source, new CSharpParseOptions(languageVersion), additionalReferences, librarySources);

    /// <summary>
    /// Creates the target document <c>Target.cs</c> in a project with the given parse options (for example the
    /// preprocessor symbols of a build configuration) and additional metadata references.
    /// </summary>
    public static Document CreateDocument(
        string source,
        CSharpParseOptions parseOptions,
        IEnumerable<MetadataReference> additionalReferences,
        params string[] librarySources)
        => AddProject(new AdhocWorkspace().CurrentSolution, "TestProject", parseOptions, additionalReferences, librarySources)
            .AddDocument("Target.cs", SourceText.From(source));

    /// <summary>
    /// Creates the target document <c>Target.cs</c> in a project that also contains <paramref name="librarySources" />
    /// and references a second project, <c>ReferencedProject</c>, made of <paramref name="referencedSources" />.
    /// </summary>
    public static Document CreateDocumentReferencingProject(string source, IEnumerable<string> referencedSources, params string[] librarySources)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        Project referenced = AddProject(new AdhocWorkspace().CurrentSolution, "ReferencedProject", parseOptions, new MetadataReference[0], referencedSources);

        return AddProject(referenced.Solution, "TestProject", parseOptions, new MetadataReference[0], librarySources)
            .AddProjectReference(new ProjectReference(referenced.Id))
            .AddDocument("Target.cs", SourceText.From(source));
    }

    private static Project AddProject(
        Solution solution,
        string name,
        CSharpParseOptions parseOptions,
        IEnumerable<MetadataReference> additionalReferences,
        IEnumerable<string> sources)
    {
        Project project = solution
            .AddProject(name, name, LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .WithParseOptions(parseOptions)
            .AddMetadataReference(MscorlibReference)
            .AddMetadataReferences(additionalReferences);

        int index = 0;
        foreach (string source in sources)
        {
            project = project.AddDocument($"Library{index++}.cs", SourceText.From(source)).Project;
        }

        return project;
    }

    private static MetadataReference MscorlibReference { get; } = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    /// <summary>
    /// Compiles the project of <paramref name="document" /> with the document text replaced by
    /// <paramref name="text" /> and returns every compile error as "ID: message".
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetCompileErrorsAsync(Document document, string text)
    {
        Compilation compilation = await document.WithText(SourceText.From(text)).Project.GetCompilationAsync();

        return compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}")
            .ToList();
    }
}
