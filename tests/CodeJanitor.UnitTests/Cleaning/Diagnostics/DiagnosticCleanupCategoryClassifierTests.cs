using System.Linq;
using CodeJanitor.Logic.Cleaning.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning.Diagnostics;

/// <summary>
/// Unit tests for <see cref="DiagnosticCleanupCategoryClassifier" />. Built-in IDE descriptors all share the
/// "Style" category, so formatting and naming must be recognized from the analyzer family; other analyzers are
/// classified by their descriptor category; compiler diagnostics are never actionable.
/// </summary>
[TestClass]
public sealed class DiagnosticCleanupCategoryClassifierTests
{
    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("IDE0055", DiagnosticCleanupCategory.Formatting)]
    [DataRow("IDE1006", DiagnosticCleanupCategory.Naming)]
    [DataRow("IDE0008", DiagnosticCleanupCategory.CodeStyle)]
    public void Classify_BuiltInIdeAnalyzer_UsesAnalyzerFamily(string diagnosticId, DiagnosticCleanupCategory expected)
    {
        var (analyzer, descriptor) = FindHostAnalyzer(diagnosticId);

        Assert.AreEqual(expected, DiagnosticCleanupCategoryClassifier.Classify(analyzer, descriptor));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Classify_BuiltInAnalyzerMirroringCompilerErrors_IsNeverActionable()
    {
        var (analyzer, descriptor) = FindHostAnalyzer("IDE1007");

        Assert.IsNull(DiagnosticCleanupCategoryClassifier.Classify(analyzer, descriptor));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("Compiler")]
    [DataRow("compiler")]
    public void Classify_CompilerCategoryDescriptor_IsNeverActionable(string category)
    {
        var descriptor = new DiagnosticDescriptor("CJT0102", "Title", "Message", category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        Assert.IsNull(DiagnosticCleanupCategoryClassifier.Classify(new LegacyFieldAnalyzer(), descriptor));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("Naming", DiagnosticCleanupCategory.Naming)]
    [DataRow("StyleCop.CSharp.NamingRules", DiagnosticCleanupCategory.Naming)]
    [DataRow("Formatting", DiagnosticCleanupCategory.Formatting)]
    [DataRow("formatting", DiagnosticCleanupCategory.Formatting)]
    [DataRow("Style", DiagnosticCleanupCategory.CodeStyle)]
    [DataRow("StyleCop.CSharp.SpacingRules", DiagnosticCleanupCategory.AnalyzerFixes)]
    [DataRow("Performance", DiagnosticCleanupCategory.AnalyzerFixes)]
    public void Classify_CustomAnalyzer_UsesDescriptorCategory(string category, DiagnosticCleanupCategory expected)
    {
        var descriptor = new DiagnosticDescriptor("CJT0100", "Title", "Message", category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        Assert.AreEqual(expected, DiagnosticCleanupCategoryClassifier.Classify(new LegacyFieldAnalyzer(), descriptor));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Classify_CompilerTaggedDescriptor_IsNeverActionableWhateverItsCategory()
    {
        var descriptor = new DiagnosticDescriptor(
            "CJT0101",
            "Title",
            "Message",
            "Naming",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            customTags: WellKnownDiagnosticTags.Compiler);

        Assert.IsNull(DiagnosticCleanupCategoryClassifier.Classify(new LegacyFieldAnalyzer(), descriptor));
    }

    private static (DiagnosticAnalyzer Analyzer, DiagnosticDescriptor Descriptor) FindHostAnalyzer(string diagnosticId)
    {
        var analyzer = DiagnosticCleanupTestWorkspace.HostAnalyzers.First(candidate => candidate.SupportedDiagnostics.Any(d => d.Id == diagnosticId));

        return (analyzer, analyzer.SupportedDiagnostics.First(d => d.Id == diagnosticId));
    }
}
