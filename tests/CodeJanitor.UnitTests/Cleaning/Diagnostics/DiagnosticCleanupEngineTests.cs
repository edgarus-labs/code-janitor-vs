using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeJanitor.Logic.Cleaning.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
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
    private static readonly CodeFixProviderCatalog Catalog = new CodeFixProviderCatalog();

    private static readonly string[] UseBraceOnSameLine =
    {
        "end_of_line = lf",
        "csharp_new_line_before_open_brace = none",
        "dotnet_diagnostic.IDE0055.severity = warning",
    };

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_NamingRuleWithPrefix_RenamesPrivateFieldsAndTheirReferences()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case")));
        DocumentId documentId = workspace.AddDocument("Counter.cs", Lines(
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

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming);

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
        Assert.IsEmpty(result.Unresolved);
        AppliedDiagnosticFix applied = result.AppliedFixes.Single();
        Assert.AreEqual("IDE1006", applied.DiagnosticId);
        Assert.AreEqual(DiagnosticCleanupCategory.Naming, applied.Category);
        Assert.AreEqual(2, applied.Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_DifferentNamingStyle_UsesConfiguredPrefixCapitalizationAndModifiers()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(
            "dotnet_naming_rule.private_static_fields_rule.symbols = private_static_fields",
            "dotnet_naming_rule.private_static_fields_rule.style = s_prefix_camel",
            "dotnet_naming_rule.private_static_fields_rule.severity = warning",
            "dotnet_naming_symbols.private_static_fields.applicable_kinds = field",
            "dotnet_naming_symbols.private_static_fields.applicable_accessibilities = private",
            "dotnet_naming_symbols.private_static_fields.required_modifiers = static",
            "dotnet_naming_style.s_prefix_camel.required_prefix = s_",
            "dotnet_naming_style.s_prefix_camel.capitalization = camel_case"));
        DocumentId documentId = workspace.AddDocument("Registry.cs", Lines(
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

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming);

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
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case")));
        DocumentId documentId = workspace.AddDocument("Account.cs", Lines(
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

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming);

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
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case")));
        DocumentId cleanedId = workspace.AddDocument("Gauge.cs", Lines(
            "partial class Gauge",
            "{",
            "    private int level;",
            "}"));
        DocumentId otherId = workspace.AddDocument("Gauge.Read.cs", Lines(
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

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), cleanedId, DiagnosticCleanupCategory.Naming);

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
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(UseBraceOnSameLine));
        DocumentId documentId = workspace.AddDocument("Widget.cs", Lines(
            "class Widget",
            "{",
            "    void Draw()",
            "    {",
            "    }",
            "}"));

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Formatting);

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
    public async Task CleanupAsync_FixOnlyMovingCodeWithAnExistingCompilerError_IsApplied()
    {
        // The existing CS0246 moves to another line; the gate still matches it by its unchanged message.
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(UseBraceOnSameLine));
        DocumentId documentId = workspace.AddDocument("Widget.cs", Lines(
            "class Widget",
            "{",
            "    void Draw()",
            "    {",
            "        MissingType value = null;",
            "    }",
            "}"));

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Formatting);

        Assert.AreEqual(
            Lines(
                "class Widget {",
                "    void Draw() {",
                "        MissingType value = null;",
                "    }",
                "}"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.IsTrue(result.IsComplete);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_ExplicitTypePreference_ReplacesVarWithExplicitTypes()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(VarPreference(false, "IDE0008", "warning")));
        DocumentId documentId = workspace.AddDocument("Calculator.cs", Lines(
            "class Calculator",
            "{",
            "    int Compute()",
            "    {",
            "        var count = 1;",
            "        var name = \"two\";",
            "        return count + name.Length;",
            "    }",
            "}"));

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.CodeStyle);

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
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(VarPreference(true, "IDE0007", "warning")));
        DocumentId documentId = workspace.AddDocument("Calculator.cs", Lines(
            "class Calculator",
            "{",
            "    int Compute()",
            "    {",
            "        int count = 1;",
            "        string name = \"two\";",
            "        return count + name.Length;",
            "    }",
            "}"));

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.CodeStyle);

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
    public Task CleanupAsync_DotnetDiagnosticSeverity_DecidesWhetherCodeStyleRuleIsApplied(string severity, bool expectChange)
        => AssertExplicitTypeRuleOutcomeAsync(VarPreference(false, "IDE0008", severity), expectChange);

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("silent", false)]
    [DataRow("none", false)]
    [DataRow("suggestion", true)]
    [DataRow("warning", true)]
    [DataRow("error", true)]
    public Task CleanupAsync_OptionSeveritySuffix_DecidesWhetherCodeStyleRuleIsApplied(string severity, bool expectChange)
        => AssertExplicitTypeRuleOutcomeAsync(
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
        string input = Lines(
            "class Counter",
            "{",
            "    private int count;",
            "",
            "    public int Get()",
            "    {",
            "        return count;",
            "    }",
            "}");
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case", severity)));
        DocumentId documentId = workspace.AddDocument("Counter.cs", input);

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming);

        string expected = expectChange ? input.Replace("count", "m_Count") : input;
        Assert.AreEqual(expected, await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.AreEqual(expectChange, result.HasChanges);
        Assert.IsEmpty(result.Unresolved);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_NestedEditorConfig_OverridesInheritedRuleForFilesBelowIt()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case")));
        workspace.AddEditorConfig("Legacy", Lines(
            "[*.cs]",
            "dotnet_naming_style.prefix_style.required_prefix = f_"));
        workspace.AddEditorConfig("Generated", Lines(
            "[*.cs]",
            "dotnet_naming_rule.private_fields_rule.severity = none"));
        DocumentId rootId = workspace.AddDocument("Probe.cs", CounterClass("Probe"));
        DocumentId legacyId = workspace.AddDocument("Legacy/Old.cs", CounterClass("Old"));
        DocumentId generatedId = workspace.AddDocument("Generated/Gen.cs", CounterClass("Gen"));
        Solution solution = workspace.CreateSolution();

        DiagnosticCleanupResult rootResult = await CleanupAsync(solution, rootId, DiagnosticCleanupCategory.Naming);
        DiagnosticCleanupResult legacyResult = await CleanupAsync(solution, legacyId, DiagnosticCleanupCategory.Naming);
        DiagnosticCleanupResult generatedResult = await CleanupAsync(solution, generatedId, DiagnosticCleanupCategory.Naming);

        Assert.AreEqual(CounterClass("Probe").Replace("count", "m_Count"), await DiagnosticCleanupTestWorkspace.GetTextAsync(rootResult.ChangedSolution, rootId));
        Assert.AreEqual(CounterClass("Old").Replace("count", "f_Count"), await DiagnosticCleanupTestWorkspace.GetTextAsync(legacyResult.ChangedSolution, legacyId));
        Assert.AreEqual(CounterClass("Gen"), await DiagnosticCleanupTestWorkspace.GetTextAsync(generatedResult.ChangedSolution, generatedId));
        Assert.IsFalse(generatedResult.HasChanges);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_OnlyNamingEnabled_LeavesFormattingViolationsUntouched()
    {
        (DiagnosticCleanupTestWorkspace workspace, DocumentId documentId) = CreateNamingAndFormattingWorkspace();
        using (workspace)
        {
            DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming);

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
            Assert.IsEmpty(result.Unresolved, "Diagnostics of disabled categories are not actionable, hence never unresolved.");
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_OnlyFormattingEnabled_LeavesNamingViolationsUntouched()
    {
        (DiagnosticCleanupTestWorkspace workspace, DocumentId documentId) = CreateNamingAndFormattingWorkspace();
        using (workspace)
        {
            DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Formatting);

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
            Assert.IsEmpty(result.Unresolved);
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_NamingAndFormattingEnabled_AppliesBoth()
    {
        (DiagnosticCleanupTestWorkspace workspace, DocumentId documentId) = CreateNamingAndFormattingWorkspace();
        using (workspace)
        {
            DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming, DiagnosticCleanupCategory.Formatting);

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
        AnalysisProbeAnalyzer probe = new AnalysisProbeAnalyzer();
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace(probe);
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case").Concat(UseBraceOnSameLine).ToArray()));
        DocumentId documentId = workspace.AddDocument("Widget.cs", Lines(
            "class Widget",
            "{",
            "    private int size;",
            "}"));
        Solution solution = workspace.CreateSolution();

        DiagnosticCleanupResult result = await CleanupAsync(solution, documentId);

        Assert.AreSame(solution, result.OriginalSolution);
        Assert.AreSame(solution, result.ChangedSolution);
        Assert.IsFalse(result.HasChanges);
        Assert.IsTrue(result.IsComplete);
        Assert.IsEmpty(result.AppliedFixes);
        Assert.IsEmpty(result.Unresolved);
        Assert.IsEmpty(result.PostApplyOperations);
        Assert.AreEqual(0, probe.AnalyzedTreeCount, "No category enabled must not run any analyzer.");

        await CleanupAsync(solution, documentId, DiagnosticCleanupCategory.CodeStyle);

        Assert.IsGreaterThan(0, probe.AnalyzedTreeCount, "Control: the probe runs as soon as a matching category is enabled.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_FixAllCapableRule_FixesEveryViolationInASinglePass()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(VarPreference(false, "IDE0008", "warning")));
        DocumentId documentId = workspace.AddDocument("Calculator.cs", Lines(
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

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, 1, Catalog, DiagnosticCleanupCategory.CodeStyle);

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
        AppliedDiagnosticFix applied = result.AppliedFixes.Single();
        Assert.AreEqual("IDE0008", applied.DiagnosticId);
        Assert.AreEqual(5, applied.Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_RuleWithoutFixAllProvider_FixesEveryViolationAcrossPasses()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case")));
        DocumentId documentId = workspace.AddDocument("Triple.cs", TripleFieldClass());

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming);

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
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case")));
        DocumentId documentId = workspace.AddDocument("Triple.cs", TripleFieldClass());

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, 1, Catalog, DiagnosticCleanupCategory.Naming);

        Assert.AreEqual(
            TripleFieldClass().Replace("first", "m_First"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.IsFalse(result.IsComplete);
        Assert.AreEqual(1, result.AppliedFixes.Single().Count);
        Assert.AreSequenceEqual(new[] { 4, 5 }, result.Unresolved.Select(unresolved => unresolved.Line).ToArray());
        Assert.IsTrue(result.Unresolved.All(unresolved =>
            unresolved.Reason == UnresolvedDiagnosticReason.NotConverged
            && unresolved.DiagnosticId == "IDE1006"
            && unresolved.Category == DiagnosticCleanupCategory.Naming));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_IdeRuleActiveOnlyThroughItsDefaultOption_IsLeftAlone()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        string input = Lines(
            "class Point",
            "{",
            "    public int X;",
            "",
            "    public static Point Create()",
            "    {",
            "        var point = new Point();",
            "        point.X = 1;",
            "        return point;",
            "    }",
            "}");
        DocumentId documentId = workspace.AddDocument("Point.cs", input);

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.CodeStyle);

        Assert.AreEqual(input, await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.IsEmpty(result.Unresolved);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_IdeRuleEnabledThroughAnOptionSeveritySuffix_IsFixed()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig("dotnet_style_object_initializer = true:warning"));
        DocumentId documentId = workspace.AddDocument("Point.cs", Lines(
            "class Point",
            "{",
            "    public int X;",
            "",
            "    public static Point Create()",
            "    {",
            "        var point = new Point();",
            "        point.X = 1;",
            "        return point;",
            "    }",
            "}"));

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.CodeStyle);

        Assert.Contains("X = 1", await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.DoesNotContain("point.X = 1;", await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_RuleActiveOnlyByDefault_IsNotConfiguredByTheRepositoryAndIsLeftAlone()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace(new LegacyFieldAnalyzer("CJT0020", "Performance"));
        DocumentId documentId = workspace.AddDocument("Settings.cs", LegacySettingsClass());
        Solution solution = workspace.CreateSolution();

        DiagnosticCleanupResult result = await CleanupAsync(solution, documentId, new RenameLegacyFieldCodeFixProvider("CJT0020"));

        Assert.AreSame(solution, result.ChangedSolution);
        Assert.IsEmpty(result.Unresolved);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_RuleConfiguredAtItsDefaultSeverity_IsFixed()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace(new LegacyFieldAnalyzer("CJT0021", "Performance"));
        workspace.ConfigureRuleSeverity("CJT0021", "warning");
        DocumentId documentId = workspace.AddDocument("Settings.cs", LegacySettingsClass());

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, new RenameLegacyFieldCodeFixProvider("CJT0021"));

        Assert.AreEqual(
            LegacySettingsClass().Replace("legacyValue", "renamedValue"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(true, DisplayName = "hidden by default")]
    [DataRow(false, DisplayName = "warning by default")]
    public async Task CleanupAsync_RuleConfiguredAsSilent_IsNeitherFixedNorReported(bool hiddenByDefault)
    {
        // Silent is refactoring-only: even an explicit silent severity with an available fix leaves the code alone.
        DiagnosticAnalyzer analyzer = hiddenByDefault
            ? new HiddenLegacyFieldAnalyzer("CJT0030")
            : new LegacyFieldAnalyzer("CJT0030", "Performance");
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace(analyzer);
        workspace.AddEditorConfig(string.Empty, EditorConfig("dotnet_diagnostic.CJT0030.severity = silent"));
        DocumentId documentId = workspace.AddDocument("Settings.cs", LegacySettingsClass());
        Solution solution = workspace.CreateSolution();

        DiagnosticCleanupResult result = await CleanupAsync(solution, documentId, new RenameLegacyFieldCodeFixProvider("CJT0030"));

        Assert.AreSame(solution, result.ChangedSolution);
        Assert.IsEmpty(result.Unresolved);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_ActionableDiagnosticWithoutCodeFixProvider_IsReportedAndNeverModified()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace(new LegacyFieldAnalyzer("CJT0001", "Style"));
        workspace.ConfigureRuleSeverity("CJT0001", "warning");
        string input = LegacySettingsClass();
        DocumentId documentId = workspace.AddDocument("Settings.cs", input);
        Solution solution = workspace.CreateSolution();

        DiagnosticCleanupResult result = await CleanupAsync(solution, documentId, DiagnosticCleanupCategory.CodeStyle);

        Assert.AreEqual(input, await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.AreSame(solution, result.ChangedSolution);
        Assert.IsFalse(result.HasChanges);
        Assert.IsTrue(result.IsComplete, "An unsupported diagnostic is reported, it does not make the cleanup incomplete.");
        UnresolvedDiagnostic unresolved = result.Unresolved.Single();
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
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace(new LegacyFieldAnalyzer(("CJT0007", "Style"), ("CJT0008", "Performance")));
        workspace.ConfigureRuleSeverity("CJT0007", "warning");
        workspace.ConfigureRuleSeverity("CJT0008", "warning");
        DocumentId documentId = workspace.AddDocument("Settings.cs", LegacySettingsClass());

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.AnalyzerFixes);

        UnresolvedDiagnostic unresolved = result.Unresolved.Single();
        Assert.AreEqual("CJT0008", unresolved.DiagnosticId);
        Assert.AreEqual(DiagnosticCleanupCategory.AnalyzerFixes, unresolved.Category);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_ProviderOffersOnlyNestedActions_ReportsNoApplicableCodeAction()
    {
        using DiagnosticCleanupTestWorkspace workspace = CreateLegacySettingsWorkspace("CJT0003", out DocumentId documentId);
        Solution solution = workspace.CreateSolution();

        DiagnosticCleanupResult result = await CleanupAsync(solution, documentId, new NestedOnlyLegacyFieldCodeFixProvider("CJT0003"));

        Assert.AreEqual(LegacySettingsClass(), await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.AreSame(solution, result.ChangedSolution);
        Assert.IsTrue(result.IsComplete);
        Assert.AreEqual(UnresolvedDiagnosticReason.NoApplicableCodeAction, result.Unresolved.Single().Reason);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_FixWithoutEffect_IsNotCountedAndReportsNoApplicableCodeAction()
    {
        using DiagnosticCleanupTestWorkspace workspace = CreateLegacySettingsWorkspace("CJT0009", out DocumentId documentId);
        Solution solution = workspace.CreateSolution();

        DiagnosticCleanupResult result = await CleanupAsync(solution, documentId, new NoOpLegacyFieldCodeFixProvider("CJT0009"));

        Assert.AreSame(solution, result.ChangedSolution);
        Assert.IsEmpty(result.AppliedFixes);
        Assert.IsTrue(result.IsComplete);
        Assert.AreEqual(UnresolvedDiagnosticReason.NoApplicableCodeAction, result.Unresolved.Single().Reason);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_AlternativeActionsForMoreDiagnosticsThanThePassLimit_FixesAllOfThem()
    {
        string[] fields = Enumerable.Range(0, 60).Select(index => $"    public int legacy{index};").ToArray();
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace(new LegacyFieldAnalyzer("CJT0023", "Performance"));
        workspace.ConfigureRuleSeverity("CJT0023", "warning");
        DocumentId documentId = workspace.AddDocument("Settings.cs", Lines(new[] { "class Settings", "{" }.Concat(fields).Concat(new[] { "}" }).ToArray()));

        CodeFixProviderCatalog catalog = new CodeFixProviderCatalog(new[] { new AlternativesWithoutEquivalenceKeyLegacyFieldCodeFixProvider("CJT0023") });

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, 1, catalog, DiagnosticCleanupCategory.AnalyzerFixes);

        string[] expectedFields = Enumerable.Range(0, 60).Select(index => $"    public int first{index};").ToArray();
        Assert.AreEqual(
            Lines(new[] { "class Settings", "{" }.Concat(expectedFields).Concat(new[] { "}" }).ToArray()),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.AreEqual(60, result.AppliedFixes.Single().Count);
        Assert.IsTrue(result.IsComplete);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CleanupAsync_AlternativeActionsWithTheSameEquivalenceKey_AppliesOnlyTheChosenActionToEveryDiagnosticInOnePass(bool providerHasFixAll)
    {
        using DiagnosticCleanupTestWorkspace workspace = CreateTwoLegacyFieldsWorkspace("CJT0020", out DocumentId documentId);
        CodeFixProviderCatalog catalog = new CodeFixProviderCatalog(new[] { new AlternativesWithoutEquivalenceKeyLegacyFieldCodeFixProvider("CJT0020", providerHasFixAll) });

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, 1, catalog, DiagnosticCleanupCategory.AnalyzerFixes);

        Assert.AreEqual(
            TwoLegacyFieldsClass().Replace("legacy", "first"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.IsTrue(result.IsComplete);
        Assert.IsEmpty(result.Unresolved);
        Assert.AreEqual(2, result.AppliedFixes.Single().Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("CJT0021", false)]
    [DataRow("CJT0022", true)]
    public async Task CleanupAsync_SingleActionWithoutEquivalenceKey_StillFixesAllDiagnosticsInOnePassThroughFixAll(string diagnosticId, bool withNestedChoice)
    {
        using DiagnosticCleanupTestWorkspace workspace = CreateTwoLegacyFieldsWorkspace(diagnosticId, out DocumentId documentId);
        CodeFixProviderCatalog catalog = new CodeFixProviderCatalog(new[] { new BatchRenameLegacyFieldCodeFixProvider(diagnosticId, withNestedChoice) });

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, 1, catalog, DiagnosticCleanupCategory.AnalyzerFixes);

        Assert.AreEqual(TwoLegacyFieldsClass().Replace("legacy", "batch"), await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.IsTrue(result.IsComplete);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_ProviderOffersNestedGroupThenPlainAction_AppliesThePlainAction()
    {
        using DiagnosticCleanupTestWorkspace workspace = CreateLegacySettingsWorkspace("CJT0004", out DocumentId documentId);

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, new RenameLegacyFieldCodeFixProvider("CJT0004"));

        Assert.AreEqual(
            LegacySettingsClass().Replace("legacyValue", "renamedValue"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.IsTrue(result.IsComplete);
        Assert.IsEmpty(result.Unresolved);
        AppliedDiagnosticFix applied = result.AppliedFixes.Single();
        Assert.AreEqual("CJT0004", applied.DiagnosticId);
        Assert.AreEqual(DiagnosticCleanupCategory.AnalyzerFixes, applied.Category);
        Assert.AreEqual(1, applied.Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_FixIntroducingCompilerError_IsRejectedAndOriginalTextPreserved()
    {
        using DiagnosticCleanupTestWorkspace workspace = CreateLegacySettingsWorkspace("CJT0005", out DocumentId documentId);
        Solution solution = workspace.CreateSolution();

        DiagnosticCleanupResult result = await CleanupAsync(solution, documentId, new BreakingLegacyFieldCodeFixProvider("CJT0005"));

        Assert.AreEqual(LegacySettingsClass(), await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.AreSame(solution, result.ChangedSolution);
        Assert.IsFalse(result.HasChanges);
        Assert.IsFalse(result.IsComplete);
        Assert.IsEmpty(result.AppliedFixes);
        UnresolvedDiagnostic unresolved = result.Unresolved.Single();
        Assert.AreEqual("CJT0005", unresolved.DiagnosticId);
        Assert.AreEqual(UnresolvedDiagnosticReason.FixRejectedIntroducesCompilerErrors, unresolved.Reason);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_FixChangingSolutionStructure_IsRejectedAsUnsupported()
    {
        using DiagnosticCleanupTestWorkspace workspace = CreateLegacySettingsWorkspace("CJT0006", out DocumentId documentId);
        Solution solution = workspace.CreateSolution();

        DiagnosticCleanupResult result = await CleanupAsync(solution, documentId, new AddDocumentLegacyFieldCodeFixProvider("CJT0006"));

        Assert.AreSame(solution, result.ChangedSolution, "The added document must not leak into the result.");
        Assert.IsFalse(result.IsComplete);
        Assert.AreEqual(UnresolvedDiagnosticReason.FixRejectedUnsupportedChanges, result.Unresolved.Single().Reason);
        Assert.AreEqual(LegacySettingsClass(), await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_FixesWithHostNotifications_ApplyTheSolutionChangesAndHandTheNotificationsToTheHostInOrder()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace(new LegacyFieldAnalyzer("CJT0010", "Performance"));
        workspace.ConfigureRuleSeverity("CJT0010", "warning");
        DocumentId documentId = workspace.AddDocument("Settings.cs", Lines(
            "class Settings",
            "{",
            "    public int legacyFirst;",
            "    public int legacySecond;",
            "}"));
        CustomOperationsLegacyFieldCodeFixProvider provider = new CustomOperationsLegacyFieldCodeFixProvider(
            "CJT0010",
            (name, _, renamed) => new CodeActionOperation[] { new ApplyChangesOperation(renamed), new HostNotificationOperation(name) });

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, provider);

        Assert.AreEqual(
            Lines(
                "class Settings",
                "{",
                "    public int renamedFirst;",
                "    public int renamedSecond;",
                "}"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.IsTrue(result.IsComplete);
        Assert.AreEqual(2, result.AppliedFixes.Single().Count);
        Assert.AreSequenceEqual(
            new[] { "Notify host: legacyFirst", "Notify host: legacySecond" }, result.PostApplyOperations.Select(operation => operation.Title).ToArray(), "Each accepted fix's host operations are handed over exactly once, in the order the fixes were applied.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_RejectedFixWithHostNotification_HandsNoOperationToTheHost()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace(new LegacyFieldAnalyzer("CJT0013", "Performance"));
        workspace.ConfigureRuleSeverity("CJT0013", "warning");
        DocumentId documentId = workspace.AddDocument("Settings.cs", Lines(
            "class Settings",
            "{",
            "    public int legacyValue;",
            "",
            "    public int Read()",
            "    {",
            "        return legacyValue;",
            "    }",
            "}"));
        Solution solution = workspace.CreateSolution();
        CustomOperationsLegacyFieldCodeFixProvider provider = new CustomOperationsLegacyFieldCodeFixProvider(
            "CJT0013",
            (name, _, renamed) => new CodeActionOperation[] { new ApplyChangesOperation(renamed), new HostNotificationOperation(name) });

        DiagnosticCleanupResult result = await CleanupAsync(solution, documentId, provider);

        Assert.AreSame(solution, result.ChangedSolution);
        Assert.AreEqual(UnresolvedDiagnosticReason.FixRejectedIntroducesCompilerErrors, result.Unresolved.Single().Reason, "Renaming only the declaration breaks the reference.");
        Assert.IsEmpty(result.PostApplyOperations);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_FixRemovingOneCompilerErrorButAddingAnother_IsRejected()
    {
        // Renaming only the declaration resolves 'renamedValue' (one CS0103 fewer) but breaks 'legacyValue' (a new
        // CS0103): the error count is unchanged, yet the fix introduces an error.
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace(new LegacyFieldAnalyzer("CJT0014", "Performance"));
        workspace.ConfigureRuleSeverity("CJT0014", "warning");
        DocumentId documentId = workspace.AddDocument("Settings.cs", Lines(
            "class Settings",
            "{",
            "    public int legacyValue;",
            "",
            "    public int Read()",
            "    {",
            "        return legacyValue;",
            "    }",
            "",
            "    public int ReadRenamed()",
            "    {",
            "        return renamedValue;",
            "    }",
            "}"));
        Solution solution = workspace.CreateSolution();
        CustomOperationsLegacyFieldCodeFixProvider provider = new CustomOperationsLegacyFieldCodeFixProvider(
            "CJT0014",
            (_, _, renamed) => new CodeActionOperation[] { new ApplyChangesOperation(renamed) });

        DiagnosticCleanupResult result = await CleanupAsync(solution, documentId, provider);

        Assert.AreSame(solution, result.ChangedSolution, "The fix swaps one compiler error for another and must not be applied.");
        Assert.AreEqual(UnresolvedDiagnosticReason.FixRejectedIntroducesCompilerErrors, result.Unresolved.Single().Reason);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_RenameChangingTheMessageOfAnExistingCompilerError_IsApplied()
    {
        // The existing CS0161 names the renamed method, so its message changes ('Widget.doWork()' becomes
        // 'Widget.DoWork()'), but it stays at the same position once mapped through the rename: no error is added.
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(
            "dotnet_naming_rule.methods_rule.symbols = methods",
            "dotnet_naming_rule.methods_rule.style = pascal_style",
            "dotnet_naming_rule.methods_rule.severity = warning",
            "dotnet_naming_symbols.methods.applicable_kinds = method",
            "dotnet_naming_symbols.methods.applicable_accessibilities = *",
            "dotnet_naming_style.pascal_style.capitalization = pascal_case"));
        DocumentId documentId = workspace.AddDocument("Widget.cs", Lines(
            "class Widget",
            "{",
            "    int doWork()",
            "    {",
            "    }",
            "}"));

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming);

        Assert.AreEqual(
            Lines(
                "class Widget",
                "{",
                "    int DoWork()",
                "    {",
                "    }",
                "}"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.IsEmpty(result.Unresolved);
        Assert.AreEqual("IDE1006", result.AppliedFixes.Single().DiagnosticId);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_RenameAddingASuffixToASymbolNamedByAnExistingCompilerError_IsApplied()
    {
        // The rename is diffed as a pure insertion of 'Async' at the end of 'Get', so the existing CS0161 on the
        // identifier grows to cover the suffix: it must still match, not count as a new error.
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(
            "dotnet_naming_rule.methods_rule.symbols = methods",
            "dotnet_naming_rule.methods_rule.style = async_style",
            "dotnet_naming_rule.methods_rule.severity = warning",
            "dotnet_naming_symbols.methods.applicable_kinds = method",
            "dotnet_naming_symbols.methods.applicable_accessibilities = *",
            "dotnet_naming_style.async_style.required_suffix = Async",
            "dotnet_naming_style.async_style.capitalization = pascal_case"));
        DocumentId documentId = workspace.AddDocument("Widget.cs", Lines(
            "class Widget",
            "{",
            "    int Get()",
            "    {",
            "    }",
            "}"));

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming);

        Assert.AreEqual(
            Lines(
                "class Widget",
                "{",
                "    int GetAsync()",
                "    {",
                "    }",
                "}"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.IsEmpty(result.Unresolved);
        Assert.AreEqual("IDE1006", result.AppliedFixes.Single().DiagnosticId);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_RenameAddingAPrefixToASymbolNamedByAnExistingCompilerError_IsApplied()
    {
        // The rename is diffed as a pure insertion of 'Try' at the start of 'Get', so the existing CS0161 on the
        // identifier keeps its start and grows to cover the prefix: it must still match, not count as a new error.
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(
            "dotnet_naming_rule.methods_rule.symbols = methods",
            "dotnet_naming_rule.methods_rule.style = try_style",
            "dotnet_naming_rule.methods_rule.severity = warning",
            "dotnet_naming_symbols.methods.applicable_kinds = method",
            "dotnet_naming_symbols.methods.applicable_accessibilities = *",
            "dotnet_naming_style.try_style.required_prefix = Try",
            "dotnet_naming_style.try_style.capitalization = pascal_case"));
        DocumentId documentId = workspace.AddDocument("Widget.cs", Lines(
            "class Widget",
            "{",
            "    int Get()",
            "    {",
            "    }",
            "}"));

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming);

        Assert.AreEqual(
            Lines(
                "class Widget",
                "{",
                "    int TryGet()",
                "    {",
                "    }",
                "}"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.IsEmpty(result.Unresolved);
        Assert.AreEqual("IDE1006", result.AppliedFixes.Single().DiagnosticId);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_FixWithSeveralSolutionChanges_IsRejectedAsUnsupported()
    {
        using DiagnosticCleanupTestWorkspace workspace = CreateLegacySettingsWorkspace("CJT0011", out DocumentId documentId);
        Solution solution = workspace.CreateSolution();
        CustomOperationsLegacyFieldCodeFixProvider provider = new CustomOperationsLegacyFieldCodeFixProvider(
            "CJT0011",
            (_, _, renamed) => new CodeActionOperation[] { new ApplyChangesOperation(renamed), new ApplyChangesOperation(renamed) });

        DiagnosticCleanupResult result = await CleanupAsync(solution, documentId, provider);

        Assert.AreSame(solution, result.ChangedSolution);
        Assert.AreEqual(UnresolvedDiagnosticReason.FixRejectedUnsupportedChanges, result.Unresolved.Single().Reason);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_FixWithOnlyHostOperations_IsRejectedAsUnsupported()
    {
        using DiagnosticCleanupTestWorkspace workspace = CreateLegacySettingsWorkspace("CJT0012", out DocumentId documentId);
        Solution solution = workspace.CreateSolution();
        CustomOperationsLegacyFieldCodeFixProvider provider = new CustomOperationsLegacyFieldCodeFixProvider(
            "CJT0012",
            (name, _, _) => new CodeActionOperation[] { new HostNotificationOperation(name) });

        DiagnosticCleanupResult result = await CleanupAsync(solution, documentId, provider);

        Assert.AreSame(solution, result.ChangedSolution);
        Assert.AreEqual(UnresolvedDiagnosticReason.FixRejectedUnsupportedChanges, result.Unresolved.Single().Reason);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CleanupAsync_ChangedSolution_CanBeAppliedToTheHostWorkspace()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case")));
        DocumentId documentId = workspace.AddDocument("Probe.cs", CounterClass("Probe"));

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.Naming);

        Assert.IsTrue(workspace.Workspace.TryApplyChanges(result.ChangedSolution));
        Assert.AreEqual(
            CounterClass("Probe").Replace("count", "m_Count"),
            await DiagnosticCleanupTestWorkspace.GetTextAsync(workspace.Workspace.CurrentSolution, documentId));
    }

    private static async Task AssertExplicitTypeRuleOutcomeAsync(string[] editorConfigProperties, bool expectChange)
    {
        string input = Lines(
            "class Calculator",
            "{",
            "    int Compute()",
            "    {",
            "        var count = 1;",
            "        return count;",
            "    }",
            "}");
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(editorConfigProperties));
        DocumentId documentId = workspace.AddDocument("Calculator.cs", input);

        DiagnosticCleanupResult result = await CleanupAsync(workspace.CreateSolution(), documentId, DiagnosticCleanupCategory.CodeStyle);

        string expected = expectChange ? input.Replace("var count", "int count") : input;
        Assert.AreEqual(expected, await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.AreEqual(expectChange, result.HasChanges);
        Assert.IsEmpty(result.Unresolved);
    }

    private static Task<DiagnosticCleanupResult> CleanupAsync(Solution solution, DocumentId documentId, params DiagnosticCleanupCategory[] categories)
        => CleanupAsync(solution, documentId, 50, Catalog, categories);

    private static Task<DiagnosticCleanupResult> CleanupAsync(Solution solution, DocumentId documentId, int maxPasses, CodeFixProviderCatalog catalog, params DiagnosticCleanupCategory[] categories)
    {
        DiagnosticCleanupEngine engine = new DiagnosticCleanupEngine(catalog);

        return engine.CleanupAsync(solution.GetDocument(documentId), new DiagnosticCleanupOptions(categories, maxPasses), CancellationToken.None);
    }

    private static Task<DiagnosticCleanupResult> CleanupAsync(Solution solution, DocumentId documentId, CodeFixProvider hostProvider)
        => CleanupAsync(solution, documentId, 50, new CodeFixProviderCatalog(new[] { hostProvider }), DiagnosticCleanupCategory.AnalyzerFixes);

    private static DiagnosticCleanupTestWorkspace CreateLegacySettingsWorkspace(string diagnosticId, out DocumentId documentId)
    {
        DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace(new LegacyFieldAnalyzer(diagnosticId, "Performance"));
        workspace.ConfigureRuleSeverity(diagnosticId, "warning");
        documentId = workspace.AddDocument("Settings.cs", LegacySettingsClass());

        return workspace;
    }

    private static (DiagnosticCleanupTestWorkspace Workspace, DocumentId DocumentId) CreateNamingAndFormattingWorkspace()
    {
        DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(string.Empty, EditorConfig(PrivateFieldsPrefixRule("m_", "pascal_case").Concat(UseBraceOnSameLine).ToArray()));
        DocumentId documentId = workspace.AddDocument("Widget.cs", Lines(
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
        string value = useVar ? "true" : "false";

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

    private static DiagnosticCleanupTestWorkspace CreateTwoLegacyFieldsWorkspace(string diagnosticId, out DocumentId documentId)
    {
        DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace(new LegacyFieldAnalyzer(diagnosticId, "Performance"));
        workspace.ConfigureRuleSeverity(diagnosticId, "warning");
        documentId = workspace.AddDocument("Settings.cs", TwoLegacyFieldsClass());

        return workspace;
    }

    private static string TwoLegacyFieldsClass() => Lines(
        "class Settings",
        "{",
        "    public int legacyValue;",
        "    public int legacyLimit;",
        "}");

    private static string LegacySettingsClass() => Lines(
        "class Settings",
        "{",
        "    public int legacyValue;",
        "}");

    private static string EditorConfig(params string[] properties)
        => Lines(new[] { "root = true", string.Empty, "[*.cs]" }.Concat(properties).ToArray());

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    // A test double handed to the workspace as an instance; the analyzer-authoring rules about shipping compiler
    // extensions do not apply.
#pragma warning disable RS1036, RS1038, RS1041, RS2008

    /// <summary>
    /// Reports every source field whose name starts with <c>legacy</c> with a rule that is hidden by default, like
    /// the refactoring-only rules of the IDE analyzers.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class HiddenLegacyFieldAnalyzer : DiagnosticAnalyzer
    {
        private readonly DiagnosticDescriptor _descriptor;

        public HiddenLegacyFieldAnalyzer(string diagnosticId)
        {
            _descriptor = new DiagnosticDescriptor(
                diagnosticId,
                "Legacy field",
                "Field '{0}' uses the legacy prefix",
                "Performance",
                DiagnosticSeverity.Hidden,
                isEnabledByDefault: true);
        }

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(_descriptor);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(
                symbolContext =>
                {
                    if (symbolContext.Symbol.Name.StartsWith("legacy", System.StringComparison.Ordinal))
                    {
                        symbolContext.ReportDiagnostic(Diagnostic.Create(_descriptor, symbolContext.Symbol.Locations[0], symbolContext.Symbol.Name));
                    }
                },
                SymbolKind.Field);
        }
    }

#pragma warning restore RS1036, RS1038, RS1041, RS2008
}
