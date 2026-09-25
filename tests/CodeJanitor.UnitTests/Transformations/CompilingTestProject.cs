using System.Collections.Generic;
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
    public static Document CreateDocument(string source, params string[] librarySources)
    {
        var project = new AdhocWorkspace().CurrentSolution
            .AddProject("TestProject", "TestProject", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        for (var i = 0; i < librarySources.Length; i++)
        {
            project = project.AddDocument($"Library{i}.cs", SourceText.From(librarySources[i])).Project;
        }

        return project.AddDocument("Target.cs", SourceText.From(source));
    }

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
