using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeJanitor.Logic.Cleaning.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning.Diagnostics;

/// <summary>
/// Behavioral tests for <see cref="DiagnosticCleanupEngine" />: .editorconfig + enabled categories decide which
/// Roslyn diagnostics are fixed, and the fixes come from the existing analyzers/CodeFixProviders (the built-in IDE
/// ones from Microsoft.CodeAnalysis.CSharp.Features, hosted the way Visual Studio hosts them). Every assertion is on
/// the exact resulting text, not merely on diagnostics disappearing.
/// </summary>
[TestClass]
public sealed class DiagnosticCleanupEngineTests
{
    private static readonly CodeFixProviderCatalog s_catalog = new CodeFixProviderCatalog();

    private static readonly string[] s_useBraceOnSameLine =
    {
        "end_of_line = lf",
        "csharp_new_line_before_open_brace = none",
        "dotnet_diagnostic.IDE0055.severity = warning",
    };

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_NamingRuleWithPrefix_RenamesPrivateFieldsAndTheirReferences()
    {
        using var workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case")));
        var documentId = workspace.AddDocument("Counter.cs", Lines(
            "class Counter",
            "{",
            "    private int count;",
            "    private int total;",
            "",
            "    public int Sum()",
            "    {",
            "        return count + total;",
            "    }",
            "}"));

        var result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming);

        Assert.AreEqual(
            Lines(
                "class Counter",
                "{",
                "    private int m_Count;",
                "    private int m_Total;",
                "",
                "    public int Sum()",
                "    {",
                "        return m_Count + m_Total;",
                "    }",
                "}"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.IsTrue(result.HasChanges);
        Assert.IsTrue(result.IsComplete);
        Assert.AreEqual(0, result.Unresolved.Count);
        var applied = result.AppliedFixes.Single();
        Assert.AreEqual("IDE1006", applied.DiagnosticId);
        Assert.AreEqual(DiagnosticCleanupCategory.Naming, applied.Category);
        Assert.AreEqual(2, applied.Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_DifferentNamingStyle_UsesConfiguredPrefixCapitalizationAndModifiers()
    {
        using var workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(
            "dotnet_naming_rule.private_static_fields_rule.symbols = private_static_fields",
            "dotnet_naming_rule.private_static_fields_rule.style = s_prefix_camel",
            "dotnet_naming_rule.private_static_fields_rule.severity = warning",
            "dotnet_naming_symbols.private_static_fields.applicable_kinds = field",
            "dotnet_naming_symbols.private_static_fields.applicable_accessibilities = private",
            "dotnet_naming_symbols.private_static_fields.required_modifiers = static",
            "dotnet_naming_style.s_prefix_camel.required_prefix = s_",
            "dotnet_naming_style.s_prefix_camel.capitalization = camel_case"));
        var documentId = workspace.AddDocument("Registry.cs", Lines(
            "class Registry",
            "{",
            "    private static int Instances;",
            "    private int count;",
            "",
            "    public int Next()",
            "    {",
            "        return ++Instances + count;",
            "    }",
            "}"));

        var result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming);

        Assert.AreEqual(
            Lines(
                "class Registry",
                "{",
                "    private static int s_instances;",
                "    private int count;",
                "",
                "    public int Next()",
                "    {",
                "        return ++s_instances + count;",
                "    }",
                "}"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.IsTrue(result.IsComplete);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_SymbolSpecificationAccessibility_LeavesUnmatchedPublicFieldUnchanged()
    {
        using var workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case")));
        var documentId = workspace.AddDocument("Account.cs", Lines(
            "class Account",
            "{",
            "    public int balance;",
            "    private int limit;",
            "",
            "    public int Available()",
            "    {",
            "        return balance - limit;",
            "    }",
            "}"));

        var result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming);

        Assert.AreEqual(
            Lines(
                "class Account",
                "{",
                "    public int balance;",
                "    private int m_Limit;",
                "",
                "    public int Available()",
                "    {",
                "        return balance - m_Limit;",
                "    }",
                "}"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_RenameFix_UpdatesReferencesInOtherDocumentsButTargetsOnlyTheCleanedDocument()
    {
        using var workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case")));
        var cleanedId = workspace.AddDocument("Gauge.cs", Lines(
            "partial class Gauge",
            "{",
            "    private int level;",
            "}"));
        var otherId = workspace.AddDocument("Gauge.Read.cs", Lines(
            "partial class Gauge",
            "{",
            "    public int Read()",
            "    {",
            "        return level;",
            "    }",
            "}",
            "",
            "class Needle",
            "{",
            "    private int angle;",
            "",
            "    public int Angle()",
            "    {",
            "        return angle;",
            "    }",
            "}"));

        var result = await CleanupAsync(workspace.CreateSolution(), cleanedId, DiagnosticCleanupCategory.Naming);

        Assert.AreEqual(
            Lines(
                "partial class Gauge",
                "{",
                "    private int m_Level;",
                "}"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, cleanedId));
        Assert.AreEqual(
            Lines(
                "partial class Gauge",
                "{",
                "    public int Read()",
                "    {",
                "        return m_Level;",
                "    }",
                "}",
                "",
                "class Needle",
                "{",
                "    private int angle;",
                "",
                "    public int Angle()",
                "    {",
                "        return angle;",
                "    }",
                "}"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, otherId));
        Assert.IsTrue(result.IsComplete);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_FormattingRuleEnabled_AppliesConfiguredBracePlacement()
    {
        using var workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(s_useBraceOnSameLine));
        var documentId = workspace.AddDocument("Widget.cs", Lines(
            "class Widget",
            "{",
            "    void Draw()",
            "    {",
            "    }",
            "}"));

        var result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Formatting);

        Assert.AreEqual(
            Lines(
                "class Widget {",
                "    void Draw() {",
                "    }",
                "}"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.IsTrue(result.IsComplete);
        Assert.AreEqual(DiagnosticCleanupCategory.Formatting, result.AppliedFixes.Single().Category);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_ExplicitTypePreference_ReplacesVarWithExplicitTypes()
    {
        using var workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(VarPreference(false, "IDE0008", "warning")));
        var documentId = workspace.AddDocument("Calculator.cs", Lines(
            "class Calculator",
            "{",
            "    int Compute()",
            "    {",
            "        var count = 1;",
            "        var name = \"two\";",
            "        return count + name.Length;",
            "    }",
            "}"));

        var result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.CodeStyle);

        Assert.AreEqual(
            Lines(
                "class Calculator",
                "{",
                "    int Compute()",
                "    {",
                "        int count = 1;",
                "        string name = \"two\";",
                "        return count + name.Length;",
                "    }",
                "}"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.IsTrue(result.IsComplete);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_VarPreference_ReplacesExplicitTypesWithVar()
    {
        using var workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(VarPreference(true, "IDE0007", "warning")));
        var documentId = workspace.AddDocument("Calculator.cs", Lines(
            "class Calculator",
            "{",
            "    int Compute()",
            "    {",
            "        int count = 1;",
            "        string name = \"two\";",
            "        return count + name.Length;",
            "    }",
            "}"));

        var result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.CodeStyle);

        Assert.AreEqual(
            Lines(
                "class Calculator",
                "{",
                "    int Compute()",
                "    {",
                "        var count = 1;",
                "        var name = \"two\";",
                "        return count + name.Length;",
                "    }",
                "}"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("silent", false)]
    [DataRow("none", false)]
    [DataRow("suggestion", true)]
    [DataRow("warning", true)]
    [DataRow("error", true)]
    public Task CleanupAsync_DotnetDiagnosticSeverity_DecidesWhetherCodeStyleRuleIsApplied(string severity, bool expectChange) =>
        AssertExplicitTypeRuleOutcomeAsync(VarPreference(false, "IDE0008", severity), expectChange);

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("silent", false)]
    [DataRow("none", false)]
    [DataRow("suggestion", true)]
    [DataRow("warning", true)]
    [DataRow("error", true)]
    public Task CleanupAsync_OptionSeveritySuffix_DecidesWhetherCodeStyleRuleIsApplied(string severity, bool expectChange) =>
        AssertExplicitTypeRuleOutcomeAsync(
            new[]
            {
                "csharp_style_var_for_built_in_types = false:" + severity,
                "csharp_style_var_when_type_is_apparent = false:" + severity,
                "csharp_style_var_elsewhere = false:" + severity,
            },
            expectChange);

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("silent", false)]
    [DataRow("none", false)]
    [DataRow("suggestion", true)]
    [DataRow("warning", true)]
    [DataRow("error", true)]
    public async Task CleanupAsync_NamingRuleSeverity_DecidesWhetherRenameIsApplied(string severity, bool expectChange)
    {
        var input = Lines(
            "class Counter",
            "{",
            "    private int count;",
            "",
            "    public int Get()",
            "    {",
            "        return count;",
            "    }",
            "}");
        using var workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case", severity)));
        var documentId = workspace.AddDocument("Counter.cs", input);

        var result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming);

        var expected = expectChange ? input.Replace("count", "m_Count") : input;
        Assert.AreEqual(expected, await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.AreEqual(expectChange, result.HasChanges);
        Assert.AreEqual(0, result.Unresolved.Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_NestedEditorConfig_OverridesInheritedRuleForFilesBelowIt()
    {
        using var workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case")));
        workspace.AddEditorConfig("Legacy", Lines(
            "[*.cs]",
            "dotnet_naming_style.prefix_style.required_prefix = f_"));
        workspace.AddEditorConfig("Generated", Lines(
            "[*.cs]",
            "dotnet_naming_rule.private_fields_rule.severity = none"));
        var rootId = workspace.AddDocument("Probe.cs", CounterClass("Probe"));
        var legacyId = workspace.AddDocument("Legacy/Old.cs", CounterClass("Old"));
        var generatedId = workspace.AddDocument("Generated/Gen.cs", CounterClass("Gen"));
        var solution = workspace.CreateSolution();

        var rootResult = await CleanupAsync(solution, rootId, DiagnosticCleanupCategory.Naming);
        var legacyResult = await CleanupAsync(solution, legacyId, DiagnosticCleanupCategory.Naming);
        var generatedResult = await CleanupAsync(solution, generatedId, DiagnosticCleanupCategory.Naming);

        Assert.AreEqual(CounterClass("Probe").Replace("count", "m_Count"), await DiagnosticCleanupTestWorkspace.GetTextAsync(rootResult.ChangedSolution, rootId));
        Assert.AreEqual(CounterClass("Old").Replace("count", "f_Count"), await DiagnosticCleanupTestWorkspace.GetTextAsync(legacyResult.ChangedSolution, legacyId));
        Assert.AreEqual(CounterClass("Gen"), await DiagnosticCleanupTestWorkspace.GetTextAsync(generatedResult.ChangedSolution, generatedId));
        Assert.IsFalse(generatedResult.HasChanges);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_OnlyNamingEnabled_LeavesFormattingViolationsUntouched()
    {
        var (workspace, documentId) = CreateNamingAndFormattingWorkspace();
        using (workspace)
        {
            var result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming);

            Assert.AreEqual(
                Lines(
                    "class Widget",
                    "{",
                    "    private int m_Size;",
                    "",
                    "    int Measure()",
                    "    {",
                    "        return m_Size;",
                    "    }",
                    "}"),
                await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
            Assert.IsTrue(result.AppliedFixes.All(fix => fix.Category == DiagnosticCleanupCategory.Naming));
            Assert.AreEqual(0, result.Unresolved.Count, "Diagnostics of disabled categories are not actionable, hence never unresolved.");
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_OnlyFormattingEnabled_LeavesNamingViolationsUntouched()
    {
        var (workspace, documentId) = CreateNamingAndFormattingWorkspace();
        using (workspace)
        {
            var result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Formatting);

            Assert.AreEqual(
                Lines(
                    "class Widget {",
                    "    private int size;",
                    "",
                    "    int Measure() {",
                    "        return size;",
                    "    }",
                    "}"),
                await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
            Assert.IsTrue(result.AppliedFixes.All(fix => fix.Category == DiagnosticCleanupCategory.Formatting));
            Assert.AreEqual(0, result.Unresolved.Count);
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_NamingAndFormattingEnabled_AppliesBoth()
    {
        var (workspace, documentId) = CreateNamingAndFormattingWorkspace();
        using (workspace)
        {
            var result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming, DiagnosticCleanupCategory.Formatting);

            Assert.AreEqual(
                Lines(
                    "class Widget {",
                    "    private int m_Size;",
                    "",
                    "    int Measure() {",
                    "        return m_Size;",
                    "    }",
                    "}"),
                await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
            Assert.IsTrue(result.IsComplete);
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_NoCategoryEnabled_ReturnsInputWithoutRunningAnalysis()
    {
        var probe = new AnalysisProbeAnalyzer();
        using var workspace = new DiagnosticCleanupTestWorkspace(probe);
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case").Concat(s_useBraceOnSameLine).ToArray()));
        var documentId = workspace.AddDocument("Widget.cs", Lines(
            "class Widget",
            "{",
            "    private int size;",
            "}"));
        var solution = workspace.CreateSolution();

        var result = await CleanupAsync(solution, documentId);

        Assert.AreSame(solution, result.OriginalSolution);
        Assert.AreSame(solution, result.ChangedSolution);
        Assert.IsFalse(result.HasChanges);
        Assert.IsTrue(result.IsComplete);
        Assert.AreEqual(0, result.AppliedFixes.Count);
        Assert.AreEqual(0, result.Unresolved.Count);
        Assert.AreEqual(0, probe.AnalyzedTreeCount, "No category enabled must not run any analyzer.");

        await CleanupAsync(solution, documentId, DiagnosticCleanupCategory.CodeStyle);

        Assert.IsTrue(probe.AnalyzedTreeCount > 0, "Control: the probe runs as soon as a matching category is enabled.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_FixAllCapableRule_FixesEveryViolationInASinglePass()
    {
        using var workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(VarPreference(false, "IDE0008", "warning")));
        var documentId = workspace.AddDocument("Calculator.cs", Lines(
            "class Calculator",
            "{",
            "    long Compute()",
            "    {",
            "        var a = 1;",
            "        var b = 2;",
            "        var c = 3;",
            "        var d = \"four\";",
            "        var e = 5L;",
            "        return a + b + c + d.Length + e;",
            "    }",
            "}"));

        var result = await CleanupAsync(workspace.CreateSolution(), documentId, 1, s_catalog, DiagnosticCleanupCategory.CodeStyle);

        Assert.AreEqual(
            Lines(
                "class Calculator",
                "{",
                "    long Compute()",
                "    {",
                "        int a = 1;",
                "        int b = 2;",
                "        int c = 3;",
                "        string d = \"four\";",
                "        long e = 5L;",
                "        return a + b + c + d.Length + e;",
                "    }",
                "}"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.IsTrue(result.IsComplete);
        var applied = result.AppliedFixes.Single();
        Assert.AreEqual("IDE0008", applied.DiagnosticId);
        Assert.AreEqual(5, applied.Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_RuleWithoutFixAllProvider_FixesEveryViolationAcrossPasses()
    {
        using var workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case")));
        var documentId = workspace.AddDocument("Triple.cs", TripleFieldClass());

        var result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming);

        Assert.AreEqual(
            TripleFieldClass().Replace("first", "m_First").Replace("second", "m_Second").Replace("third", "m_Third"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.IsTrue(result.IsComplete);
        Assert.AreEqual(3, result.AppliedFixes.Single().Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_MaxPassesExhausted_ReportsRemainingDiagnosticsAsNotConverged()
    {
        using var workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case")));
        var documentId = workspace.AddDocument("Triple.cs", TripleFieldClass());

        var result = await CleanupAsync(workspace.CreateSolution(), documentId, 1, s_catalog, DiagnosticCleanupCategory.Naming);

        Assert.AreEqual(
            TripleFieldClass().Replace("first", "m_First"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.IsFalse(result.IsComplete);
        Assert.AreEqual(1, result.AppliedFixes.Single().Count);
        CollectionAssert.AreEqual(new[] { 4, 5 }, result.Unresolved.Select(unresolved => unresolved.Line).ToArray());
        Assert.IsTrue(result.Unresolved.All(unresolved =>
            unresolved.Reason == UnresolvedDiagnosticReason.NotConverged
            && unresolved.DiagnosticId == "IDE1006"
            && unresolved.Category == DiagnosticCleanupCategory.Naming));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_ActionableDiagnosticWithoutCodeFixProvider_IsReportedAndNeverModified()
    {
        using var workspace = new DiagnosticCleanupTestWorkspace(new LegacyFieldAnalyzer("CJT0001", "Style"));
        var input = LegacySettingsClass();
        var documentId = workspace.AddDocument("Settings.cs", input);
        var solution = workspace.CreateSolution();

        var result = await CleanupAsync(solution, documentId, DiagnosticCleanupCategory.CodeStyle);

        Assert.AreEqual(input, await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.AreSame(solution, result.ChangedSolution);
        Assert.IsFalse(result.HasChanges);
        Assert.IsTrue(result.IsComplete, "An unsupported diagnostic is reported, it does not make the cleanup incomplete.");
        var unresolved = result.Unresolved.Single();
        Assert.AreEqual("CJT0001", unresolved.DiagnosticId);
        Assert.AreEqual(DiagnosticCleanupCategory.CodeStyle, unresolved.Category);
        Assert.AreEqual(DiagnosticSeverity.Warning, unresolved.Severity);
        Assert.AreEqual(DiagnosticCleanupTestWorkspace.GetPath("Settings.cs"), unresolved.FilePath);
        Assert.AreEqual(3, unresolved.Line);
        Assert.AreEqual("Field 'legacyValue' uses the legacy prefix", unresolved.Message);
        Assert.AreEqual(UnresolvedDiagnosticReason.NoCodeFixProvider, unresolved.Reason);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_AnalyzerReportingSeveralCategories_OnlyEnabledCategoryIsActionable()
    {
        using var workspace = new DiagnosticCleanupTestWorkspace(new LegacyFieldAnalyzer(("CJT0007", "Style"), ("CJT0008", "Performance")));
        var documentId = workspace.AddDocument("Settings.cs", LegacySettingsClass());

        var result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.AnalyzerFixes);

        var unresolved = result.Unresolved.Single();
        Assert.AreEqual("CJT0008", unresolved.DiagnosticId);
        Assert.AreEqual(DiagnosticCleanupCategory.AnalyzerFixes, unresolved.Category);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_ProviderOffersOnlyNestedActions_ReportsNoApplicableCodeAction()
    {
        using var workspace = CreateLegacySettingsWorkspace("CJT0003", out var documentId);
        var solution = workspace.CreateSolution();

        var result = await CleanupAsync(solution, documentId, new NestedOnlyLegacyFieldCodeFixProvider("CJT0003"));

        Assert.AreEqual(LegacySettingsClass(), await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.AreSame(solution, result.ChangedSolution);
        Assert.IsTrue(result.IsComplete);
        Assert.AreEqual(UnresolvedDiagnosticReason.NoApplicableCodeAction, result.Unresolved.Single().Reason);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_FixWithoutEffect_IsNotCountedAndReportsNoApplicableCodeAction()
    {
        using var workspace = CreateLegacySettingsWorkspace("CJT0009", out var documentId);
        var solution = workspace.CreateSolution();

        var result = await CleanupAsync(solution, documentId, new NoOpLegacyFieldCodeFixProvider("CJT0009"));

        Assert.AreSame(solution, result.ChangedSolution);
        Assert.AreEqual(0, result.AppliedFixes.Count);
        Assert.IsTrue(result.IsComplete);
        Assert.AreEqual(UnresolvedDiagnosticReason.NoApplicableCodeAction, result.Unresolved.Single().Reason);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_ProviderOffersNestedGroupThenPlainAction_AppliesThePlainAction()
    {
        using var workspace = CreateLegacySettingsWorkspace("CJT0004", out var documentId);

        var result = await CleanupAsync(workspace.CreateSolution(), documentId, new RenameLegacyFieldCodeFixProvider("CJT0004"));

        Assert.AreEqual(
            LegacySettingsClass().Replace("legacyValue", "renamedValue"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.IsTrue(result.IsComplete);
        Assert.AreEqual(0, result.Unresolved.Count);
        var applied = result.AppliedFixes.Single();
        Assert.AreEqual("CJT0004", applied.DiagnosticId);
        Assert.AreEqual(DiagnosticCleanupCategory.AnalyzerFixes, applied.Category);
        Assert.AreEqual(1, applied.Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_FixIntroducingCompilerError_IsRejectedAndOriginalTextPreserved()
    {
        using var workspace = CreateLegacySettingsWorkspace("CJT0005", out var documentId);
        var solution = workspace.CreateSolution();

        var result = await CleanupAsync(solution, documentId, new BreakingLegacyFieldCodeFixProvider("CJT0005"));

        Assert.AreEqual(LegacySettingsClass(), await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.AreSame(solution, result.ChangedSolution);
        Assert.IsFalse(result.HasChanges);
        Assert.IsFalse(result.IsComplete);
        Assert.AreEqual(0, result.AppliedFixes.Count);
        var unresolved = result.Unresolved.Single();
        Assert.AreEqual("CJT0005", unresolved.DiagnosticId);
        Assert.AreEqual(UnresolvedDiagnosticReason.FixRejectedIntroducesCompilerErrors, unresolved.Reason);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_FixChangingSolutionStructure_IsRejectedAsUnsupported()
    {
        using var workspace = CreateLegacySettingsWorkspace("CJT0006", out var documentId);
        var solution = workspace.CreateSolution();

        var result = await CleanupAsync(solution, documentId, new AddDocumentLegacyFieldCodeFixProvider("CJT0006"));

        Assert.AreSame(solution, result.ChangedSolution, "The added document must not leak into the result.");
        Assert.IsFalse(result.IsComplete);
        Assert.AreEqual(UnresolvedDiagnosticReason.FixRejectedUnsupportedChanges, result.Unresolved.Single().Reason);
        Assert.AreEqual(LegacySettingsClass(), await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_ChangedSolution_CanBeAppliedToTheHostWorkspace()
    {
        using var workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case")));
        var documentId = workspace.AddDocument("Probe.cs", CounterClass("Probe"));

        var result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming);

        Assert.IsTrue(workspace.Workspace.TryApplyChanges(result.ChangedSolution));
        Assert.AreEqual(
            CounterClass("Probe").Replace("count", "m_Count"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(workspace.Workspace.CurrentSolution, documentId));
    }

    private static async Task AssertExplicitTypeRuleOutcomeAsync(string[] editorConfigProperties, bool expectChange)
    {
        var input = Lines(
            "class Calculator",
            "{",
            "    int Compute()",
            "    {",
            "        var count = 1;",
            "        return count;",
            "    }",
            "}");
        using var workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(editorConfigProperties));
        var documentId = workspace.AddDocument("Calculator.cs", input);

        var result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.CodeStyle);

        var expected = expectChange ? input.Replace("var count", "int count") : input;
        Assert.AreEqual(expected, await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.AreEqual(expectChange, result.HasChanges);
        Assert.AreEqual(0, result.Unresolved.Count);
    }

    private static Task<DiagnosticCleanupResult> CleanupAsync(Solution solution, DocumentId documentId, params DiagnosticCleanupCategory[] categories) =>
        CleanupAsync(solution, documentId, 50, s_catalog, categories);

    private static Task<DiagnosticCleanupResult> CleanupAsync(Solution solution, DocumentId documentId, int maxPasses, CodeFixProviderCatalog catalog, params DiagnosticCleanupCategory[] categories)
    {
        var engine = new DiagnosticCleanupEngine(catalog);

        return engine.CleanupAsync(solution.GetDocument(documentId), new DiagnosticCleanupOptions(categories, maxPasses), CancellationToken.None);
    }

    private static Task<DiagnosticCleanupResult> CleanupAsync(Solution solution, DocumentId documentId, CodeFixProvider hostProvider) =>
        CleanupAsync(solution, documentId, 50, new CodeFixProviderCatalog(new[] { hostProvider }), DiagnosticCleanupCategory.AnalyzerFixes);

    private static DiagnosticCleanupTestWorkspace CreateLegacySettingsWorkspace(string diagnosticId, out DocumentId documentId)
    {
        var workspace = new DiagnosticCleanupTestWorkspace(new LegacyFieldAnalyzer(diagnosticId, "Performance"));
        documentId = workspace.AddDocument("Settings.cs", LegacySettingsClass());

        return workspace;
    }

    private static (DiagnosticCleanupTestWorkspace Workspace, DocumentId DocumentId) CreateNamingAndFormattingWorkspace()
    {
        var workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case").Concat(s_useBraceOnSameLine).ToArray()));
        var documentId = workspace.AddDocument("Widget.cs", Lines(
            "class Widget",
            "{",
            "    private int size;",
            "",
            "    int Measure()",
            "    {",
            "        return size;",
            "    }",
            "}"));

        return (workspace, documentId);
    }

    private static string[] PrivateFieldsPrefixRule(string prefix, string capitalization, string severity = "warning") => new[]
    {
        "dotnet_naming_rule.private_fields_rule.symbols = private_fields",
        "dotnet_naming_rule.private_fields_rule.style = prefix_style",
        "dotnet_naming_rule.private_fields_rule.severity = " + severity,
        "dotnet_naming_symbols.private_fields.applicable_kinds = field",
        "dotnet_naming_symbols.private_fields.applicable_accessibilities = private",
        "dotnet_naming_style.prefix_style.required_prefix = " + prefix,
        "dotnet_naming_style.prefix_style.capitalization = " + capitalization,
    };

    private static string[] VarPreference(bool useVar, string diagnosticId, string severity)
    {
        var value = useVar ? "true" : "false";

        return new[]
        {
            "csharp_style_var_for_built_in_types = " + value,
            "csharp_style_var_when_type_is_apparent = " + value,
            "csharp_style_var_elsewhere = " + value,
            "dotnet_diagnostic." + diagnosticId + ".severity = " + severity,
        };
    }

    private static string CounterClass(string className) => Lines(
        "class " + className,
        "{",
        "    private int count;",
        "",
        "    public int Get()",
        "    {",
        "        return count;",
        "    }",
        "}");

    private static string TripleFieldClass() => Lines(
        "class Triple",
        "{",
        "    private int first;",
        "    private int second;",
        "    private int third;",
        "",
        "    public int Sum()",
        "    {",
        "        return first + second + third;",
        "    }",
        "}");

    private static string LegacySettingsClass() => Lines(
        "class Settings",
        "{",
        "    public int legacyValue;",
        "}");

    private static string EditorConfig(params string[] properties) =>
        Lines(new[] { "root = true", string.Empty, "[*.cs]" }.Concat(properties).ToArray());

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";
}
