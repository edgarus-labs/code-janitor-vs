using System;
using System.Collections.Generic;
using System.IO;
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
    /// <c>System.Index</c> and <c>System.Range</c>, which mscorlib lacks but <c>^1</c>, <c>1..</c>, list patterns and
    /// slice patterns need.
    /// </summary>
    public const string IndexAndRangeSource =
        "namespace System\r\n" +
        "{\r\n" +
        "    public readonly struct Index\r\n" +
        "    {\r\n" +
        "        private readonly int _value;\r\n" +
        "        public Index(int value, bool fromEnd = false) { _value = fromEnd ? ~value : value; }\r\n" +
        "        public int Value => _value < 0 ? ~_value : _value;\r\n" +
        "        public bool IsFromEnd => _value < 0;\r\n" +
        "        public static Index Start => new Index(0);\r\n" +
        "        public static Index End => new Index(0, true);\r\n" +
        "        public int GetOffset(int length) => IsFromEnd ? length - Value : Value;\r\n" +
        "        public static implicit operator Index(int value) => new Index(value);\r\n" +
        "    }\r\n" +
        "\r\n" +
        "    public readonly struct Range\r\n" +
        "    {\r\n" +
        "        public Range(Index start, Index end) { Start = start; End = end; }\r\n" +
        "        public Index Start { get; }\r\n" +
        "        public Index End { get; }\r\n" +
        "        public static Range All => new Range(Index.Start, Index.End);\r\n" +
        "        public static Range StartAt(Index start) => new Range(start, Index.End);\r\n" +
        "        public static Range EndAt(Index end) => new Range(Index.Start, end);\r\n" +
        "        public (int Offset, int Length) GetOffsetAndLength(int length) { var start = Start.GetOffset(length); return (start, End.GetOffset(length) - start); }\r\n" +
        "    }\r\n" +
        "}\r\n";

    /// <summary>
    /// Creates the target document <c>Target.cs</c> in a project that also contains <paramref name="librarySources" />.
    /// </summary>
    public static Document CreateDocument(string source, params string[] librarySources) =>
        CreateDocument(source, LanguageVersion.Latest, new MetadataReference[0], librarySources);

    /// <summary>
    /// Creates the target document <c>Target.cs</c> in a project with the given language version and additional
    /// metadata references (for example a reference with an extern alias).
    /// </summary>
    public static Document CreateDocument(
        string source,
        LanguageVersion languageVersion,
        IEnumerable<MetadataReference> additionalReferences,
        params string[] librarySources) =>
        CreateDocument(source, new CSharpParseOptions(languageVersion), additionalReferences, librarySources);

    /// <summary>
    /// Creates the target document <c>Target.cs</c> in a project with the given parse options (for example the
    /// preprocessor symbols of a build configuration) and additional metadata references.
    /// </summary>
    public static Document CreateDocument(
        string source,
        CSharpParseOptions parseOptions,
        IEnumerable<MetadataReference> additionalReferences,
        params string[] librarySources) =>
        AddProject(new AdhocWorkspace().CurrentSolution, "TestProject", parseOptions, additionalReferences, librarySources)
            .AddDocument("Target.cs", SourceText.From(source));

    /// <summary>
    /// Creates the target document <c>Target.cs</c> in a project that also contains <paramref name="librarySources" />
    /// and references a second project, <c>ReferencedProject</c>, made of <paramref name="referencedSources" />.
    /// </summary>
    public static Document CreateDocumentReferencingProject(string source, IEnumerable<string> referencedSources, params string[] librarySources)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var referenced = AddProject(new AdhocWorkspace().CurrentSolution, "ReferencedProject", parseOptions, new MetadataReference[0], referencedSources);

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
        var project = solution
            .AddProject(name, name, LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .WithParseOptions(parseOptions)
            .AddMetadataReference(MscorlibReference)
            .AddMetadataReferences(additionalReferences);

        var index = 0;
        foreach (var source in sources)
        {
            project = project.AddDocument($"Library{index++}.cs", SourceText.From(source)).Project;
        }

        return project;
    }

    /// <summary>
    /// Compiles <paramref name="source" /> into an in-memory assembly and returns a reference to it with the given
    /// extern alias.
    /// </summary>
    public static MetadataReference CreateAliasedReference(string assemblyName, string source, string alias)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            new[] { MscorlibReference },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using (var stream = new MemoryStream())
        {
            var result = compilation.Emit(stream);
            if (!result.Success)
            {
                throw new InvalidOperationException("The aliased test assembly does not compile: " + string.Join("; ", result.Diagnostics));
            }

            return MetadataReference.CreateFromImage(stream.ToArray()).WithAliases(new[] { alias });
        }
    }

    private static MetadataReference MscorlibReference { get; } = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    /// <summary>
    /// Compiles the project of <paramref name="document" /> with the document text replaced by
    /// <paramref name="text" /> and returns every compile error as "ID: message".
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetCompileErrorsAsync(Document document, string text)
    {
        var compilation = await document.WithText(SourceText.From(text)).Project.GetCompilationAsync();

        return compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}")
            .ToList();
    }
}
