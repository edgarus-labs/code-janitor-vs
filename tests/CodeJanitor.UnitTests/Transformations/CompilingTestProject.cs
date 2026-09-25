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
/// Builds an in-memory C# project (mscorlib only) so tests can bind a document semantically and
/// compile transformed output.
/// </summary>
internal static class CompilingTestProject
{
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
        params string[] librarySources)
    {
        var project = new AdhocWorkspace().CurrentSolution
            .AddProject("TestProject", "TestProject", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(new CSharpParseOptions(languageVersion))
            .AddMetadataReference(MscorlibReference)
            .AddMetadataReferences(additionalReferences);

        for (var i = 0; i < librarySources.Length; i++)
        {
            project = project.AddDocument($"Library{i}.cs", SourceText.From(librarySources[i])).Project;
        }

        return project.AddDocument("Target.cs", SourceText.From(source));
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
