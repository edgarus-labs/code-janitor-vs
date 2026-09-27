using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning.Diagnostics;

/// <summary>
/// Characterizes how Roslyn combines the severity sources of a built-in code-style rule (IDE0011,
/// <c>csharp_prefer_braces</c>, category Style), which <see cref="CodeJanitor.Logic.Cleaning.EffectiveCleanupSettings" />
/// mirrors: a <c>:none</c> option suffix stops the analyzer whatever else is configured; otherwise
/// <c>dotnet_diagnostic.&lt;id&gt;.severity</c> wins, then <c>dotnet_analyzer_diagnostic.category-&lt;category&gt;.severity</c>,
/// then <c>dotnet_analyzer_diagnostic.severity</c>, and only then the option's severity suffix.
/// </summary>
[TestClass]
public sealed class CodeStyleSeverityPrecedenceTests
{
    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("csharp_prefer_braces = true:warning\ndotnet_diagnostic.IDE0011.severity = none", null, DisplayName = "diagnostic none beats the suffix")]
    [DataRow("csharp_prefer_braces = true:silent\ndotnet_diagnostic.IDE0011.severity = warning", DiagnosticSeverity.Warning, DisplayName = "diagnostic warning beats a silent suffix")]
    [DataRow("csharp_prefer_braces = true:silent\ndotnet_analyzer_diagnostic.category-Style.severity = warning", DiagnosticSeverity.Warning, DisplayName = "category warning beats a silent suffix")]
    [DataRow("csharp_prefer_braces = true:warning\ndotnet_analyzer_diagnostic.category-Style.severity = none", null, DisplayName = "category none beats a warning suffix")]
    [DataRow("csharp_prefer_braces = true:silent\ndotnet_analyzer_diagnostic.severity = warning", DiagnosticSeverity.Warning, DisplayName = "global warning beats a silent suffix")]
    [DataRow("csharp_prefer_braces = true\ndotnet_analyzer_diagnostic.category-Style.severity = warning", DiagnosticSeverity.Warning, DisplayName = "category warning applies to an option without suffix")]
    [DataRow("csharp_prefer_braces = true\ndotnet_analyzer_diagnostic.category-Style.severity = none", null, DisplayName = "category none silences an option without suffix")]
    [DataRow("dotnet_analyzer_diagnostic.category-style.severity = warning", DiagnosticSeverity.Warning, DisplayName = "category key is case-insensitive")]
    [DataRow("dotnet_analyzer_diagnostic.category-Style.severity = suggestion\ndotnet_analyzer_diagnostic.severity = none", DiagnosticSeverity.Info, DisplayName = "category beats the global severity")]
    [DataRow("csharp_prefer_braces = true:none\ndotnet_diagnostic.IDE0011.severity = warning", null, DisplayName = "none suffix beats a diagnostic warning")]
    [DataRow("csharp_prefer_braces = true:none\ndotnet_analyzer_diagnostic.category-Style.severity = warning", null, DisplayName = "none suffix beats a category warning")]
    [DataRow("csharp_prefer_braces = true:none\ndotnet_analyzer_diagnostic.severity = warning", null, DisplayName = "none suffix beats the global warning")]
    public async Task EffectiveSeverity_FollowsRoslynPrecedence(string editorConfig, DiagnosticSeverity? expected)
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig("precedence", "root = true\n\n[*.cs]\n" + editorConfig + "\n");
        DocumentId documentId = workspace.AddDocument(
            "precedence/Probe.cs",
            "class Probe\n{\n    int Get(bool open)\n    {\n        if (open)\n            return 1;\n        return 0;\n    }\n}\n");
        Project project = workspace.CreateSolution().GetDocument(documentId).Project;
        Compilation compilation = await project.GetCompilationAsync();
        ImmutableArray<DiagnosticAnalyzer> analyzers = DiagnosticCleanupTestWorkspace.HostAnalyzers
            .Where(analyzer => analyzer.SupportedDiagnostics.Any(descriptor => descriptor.Id == "IDE0011"))
            .ToImmutableArray();

        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers(analyzers, project.AnalyzerOptions)
            .GetAnalyzerDiagnosticsAsync();

        Assert.AreEqual(expected, diagnostics.Where(diagnostic => diagnostic.Id == "IDE0011").Select(diagnostic => (DiagnosticSeverity?)diagnostic.Severity).SingleOrDefault());
    }
}
