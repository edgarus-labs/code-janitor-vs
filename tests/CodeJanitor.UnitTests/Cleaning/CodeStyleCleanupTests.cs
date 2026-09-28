using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Logic.Cleaning.Diagnostics;
using CodeJanitor.Properties;
using CodeJanitor.UnitTests.Cleaning.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning;

/// <summary>
/// End-to-end tests of the Code Janitor code-style rules: the rules resolved for a file by
/// <see cref="EffectiveCleanupSettings" /> (.editorconfig, then .codejanitor, then the user setting) are applied by
/// <see cref="DiagnosticCleanupEngine" /> through the built-in Roslyn analyzers and code fixes, the way Visual Studio
/// cleans a file. One rule of every group is covered.
/// </summary>
[TestClass]
public sealed class CodeStyleCleanupTests
{
    private string _directoryName;
    private string _directory;

    [TestInitialize]
    public void TestInitialize()
    {
        Settings.Default.Reset();
        _directoryName = Guid.NewGuid().ToString("N");
        _directory = DiagnosticCleanupTestWorkspace.GetPath(_directoryName);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, ".codejanitor"), "{ \"cleanup\": { } }");
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Settings.Default.Reset();
        Directory.Delete(_directory, true);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(
        "csharp_preferred_modifier_order=public,private,protected,internal,static,readonly",
        "class Probe\n{\n    static public int Get() { return 1; }\n}\n",
        "public static int Get()",
        DisplayName = "modifiers: modifier order")]
    [DataRow(
        "csharp_prefer_braces=true",
        "class Probe\n{\n    int Get(bool open)\n    {\n        if (open)\n            return 1;\n        return 0;\n    }\n}\n",
        "if (open)\n        {\n            return 1;\n        }",
        DisplayName = "blocks: braces")]
    [DataRow(
        "csharp_style_expression_bodied_methods=true",
        "class Probe\n{\n    int Get()\n    {\n        return 1;\n    }\n}\n",
        "int Get() => 1;",
        DisplayName = "expression-bodied members: methods")]
    [DataRow(
        "csharp_style_prefer_not_pattern=true",
        "class Probe\n{\n    bool Check(object value)\n    {\n        return !(value is string);\n    }\n}\n",
        "return value is not string;",
        DisplayName = "pattern matching: not pattern")]
    [DataRow(
        "dotnet_style_coalesce_expression=true",
        "class Probe\n{\n    string Get(string value)\n    {\n        return value != null ? value : \"\";\n    }\n}\n",
        "return value ?? \"\";",
        DisplayName = "null checking: coalesce")]
    [DataRow(
        "csharp_style_implicit_object_creation_when_type_is_apparent=true",
        "class Probe\n{\n    private readonly Probe _other = new Probe();\n}\n",
        "private readonly Probe _other = new();",
        DisplayName = "modern expressions: target-typed new (suggestion by default)")]
    [DataRow(
        "dotnet_style_qualification_for_field=true",
        "class Probe\n{\n    private int count;\n\n    int Size { get; set; }\n\n    int Get()\n    {\n        return count + this.Size;\n    }\n}\n",
        "return this.count + this.Size;",
        DisplayName = "'this.' qualification: fields only, other qualification rules untouched")]
    [DataRow(
        "dotnet_style_predefined_type_for_locals_parameters_members=true",
        "using System;\n\nclass Probe\n{\n    Int32 Get()\n    {\n        return 1;\n    }\n}\n",
        "int Get()",
        DisplayName = "language keywords vs. framework type names")]
    [DataRow(
        "dotnet_style_parentheses_in_arithmetic_binary_operators=always_for_clarity",
        "class Probe\n{\n    int Get(int a, int b, int c)\n    {\n        return a + b * c;\n    }\n}\n",
        "return a + (b * c);",
        DisplayName = "parentheses: arithmetic")]
    public async Task UserSettingRule_WithoutEditorConfig_IsAppliedThroughTheRoslynCodeFix(string rule, string input, string expectedFragment)
    {
        Settings.Default.Cleaning_CodeStyleRules = rule;

        string output = await CleanupAsync(input, editorConfig: null);

        Assert.Contains(expectedFragment, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(null, true, DisplayName = "no rule in .editorconfig")]
    [DataRow("csharp_prefer_braces = false", false, DisplayName = "no severity")]
    [DataRow("csharp_prefer_braces = false:none", true)]
    [DataRow("csharp_prefer_braces = false:silent", true)]
    [DataRow("csharp_prefer_braces = false:suggestion", false)]
    [DataRow("csharp_prefer_braces = false:warning", false)]
    [DataRow("csharp_prefer_braces = false:error", false)]
    public async Task EditorConfigEnforcingTheRule_WinsOverTheCodeJanitorRule(string editorConfigOption, bool bracesAdded)
    {
        Settings.Default.Cleaning_CodeStyleRules = "csharp_prefer_braces=true";
        string input = "class Probe\n{\n    int Get(bool open)\n    {\n        if (open)\n            return 1;\n        return 0;\n    }\n}\n";

        string output = await CleanupAsync(input, editorConfigOption);

        Assert.AreEqual(bracesAdded, output.Contains("        {\n            return 1;"), output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task PolicyDisablingTheRule_WinsOverTheUserSetting()
    {
        Settings.Default.Cleaning_CodeStyleRules = "csharp_prefer_braces=true";
        File.WriteAllText(Path.Combine(_directory, ".codejanitor"), "{ \"cleanup\": { \"codeStyle\": { \"csharp_prefer_braces\": null } } }");
        string input = "class Probe\n{\n    int Get(bool open)\n    {\n        if (open)\n            return 1;\n        return 0;\n    }\n}\n";

        Assert.AreEqual(input, await CleanupAsync(input, editorConfig: null));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void EveryRule_ReportsDiagnosticsOfTheRoslynAnalyzers_AndProposesAValidValue()
    {
        HashSet<string> hostDiagnosticIds = new HashSet<string>(
            DiagnosticCleanupTestWorkspace.HostAnalyzers.SelectMany(analyzer => analyzer.SupportedDiagnostics.Select(descriptor => descriptor.Id)));

        foreach (CodeStyleRule rule in CodeStyleRules.All)
        {
            Assert.IsTrue(rule.IsValidValue(rule.DefaultValue), rule.Key);
            foreach (string diagnosticId in rule.DiagnosticIds)
            {
                Assert.Contains(diagnosticId, hostDiagnosticIds, rule.Key);
            }
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ParseSetting_AcceptsValuesIgnoringCase_AndNormalizesThem()
    {
        IReadOnlyDictionary<string, string> values = CodeStyleRules.ParseSetting(
            "csharp_prefer_braces=False;dotnet_style_null_propagation=sometimes;csharp_style_expression_bodied_methods=When_On_Single_Line");

        CollectionAssert.AreEquivalent(
            new Dictionary<string, string>
            {
                ["csharp_prefer_braces"] = "false",
                ["csharp_style_expression_bodied_methods"] = "when_on_single_line",
            },
            values.ToDictionary(entry => entry.Key, entry => entry.Value));
        Assert.AreEqual("csharp_prefer_braces=false", CodeStyleRules.FormatSetting(new Dictionary<string, string> { ["csharp_prefer_braces"] = "FALSE" }));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(null, DisplayName = "null setting")]
    [DataRow("", DisplayName = "empty setting")]
    [DataRow(";;  ;", DisplayName = "separators only")]
    [DataRow("=true", DisplayName = "missing key")]
    [DataRow("csharp_prefer_braces", DisplayName = "missing separator")]
    [DataRow("csharp_prefer_braces=", DisplayName = "missing value")]
    [DataRow("csharp_prefer_braces=true=false", DisplayName = "second separator in the value")]
    [DataRow("CSharp_Prefer_Braces=true", DisplayName = "keys are case-sensitive")]
    [DataRow("csharp_prefer_braces=true:warning", DisplayName = "severity suffix")]
    [DataRow("dotnet_naming_rule.x.severity=warning", DisplayName = "naming rule")]
    [DataRow("csharp_preferred_modifier_order=public,public", DisplayName = "duplicate modifier")]
    [DataRow("csharp_preferred_modifier_order=public,Static", DisplayName = "modifiers are case-sensitive")]
    [DataRow("csharp_preferred_modifier_order=public,,static", DisplayName = "empty modifier")]
    public void ParseSetting_MalformedEntries_EnableNoRule(string setting)
    {
        Assert.IsEmpty(CodeStyleRules.ParseSetting(setting));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ParseSetting_TrimsEntries_AndTheLastEntryOfARepeatedKeyWins()
    {
        IReadOnlyDictionary<string, string> values = CodeStyleRules.ParseSetting(
            " csharp_prefer_braces = true ;csharp_prefer_braces=When_Multiline;; csharp_preferred_modifier_order = public , static,readonly ;csharp_prefer_braces=bogus");

        CollectionAssert.AreEquivalent(
            new Dictionary<string, string>
            {
                ["csharp_prefer_braces"] = "when_multiline",
                ["csharp_preferred_modifier_order"] = "public , static,readonly",
            },
            values.ToDictionary(entry => entry.Key, entry => entry.Value));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void FormatSetting_WritesValidRulesInCatalogOrder_AndDropsUnknownOrInvalidOnes()
    {
        string setting = CodeStyleRules.FormatSetting(new Dictionary<string, string>
        {
            ["dotnet_style_parentheses_in_other_operators"] = "NEVER_IF_UNNECESSARY",
            ["csharp_prefer_braces"] = "maybe",
            ["unknown_rule"] = "true",
            ["csharp_style_expression_bodied_methods"] = "When_On_Single_Line",
            ["csharp_preferred_modifier_order"] = "static,public",
            ["dotnet_style_null_propagation"] = null,
        });

        Assert.AreEqual(
            "csharp_preferred_modifier_order=static,public;csharp_style_expression_bodied_methods=when_on_single_line;dotnet_style_parentheses_in_other_operators=never_if_unnecessary",
            setting);
        Assert.AreEqual(string.Empty, CodeStyleRules.FormatSetting(new Dictionary<string, string>()));
        Assert.AreEqual(setting, CodeStyleRules.FormatSetting(CodeStyleRules.ParseSetting(setting)), "Formatting a parsed setting must round-trip.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void TryGet_FindsOnlyCatalogKeys_Exactly()
    {
        Assert.IsTrue(CodeStyleRules.TryGet("csharp_prefer_braces", out CodeStyleRule rule));
        Assert.AreEqual("csharp_prefer_braces", rule.Key);
        CollectionAssert.AreEqual(new[] { "IDE0011" }, rule.DiagnosticIds.ToArray());

        Assert.IsFalse(CodeStyleRules.TryGet(null, out rule));
        Assert.IsNull(rule);
        Assert.IsFalse(CodeStyleRules.TryGet("CSHARP_PREFER_BRACES", out rule));
        Assert.IsFalse(CodeStyleRules.TryGet("csharp_style_namespace_declarations", out rule), "Rules Code Janitor maps to its own steps are not code-style rules.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Rule_NullValue_IsInvalid_AndNormalizesToNull()
    {
        CodeStyleRules.TryGet("csharp_prefer_braces", out CodeStyleRule enumerated);
        CodeStyleRules.TryGet("csharp_preferred_modifier_order", out CodeStyleRule freeText);

        Assert.IsFalse(enumerated.IsValidValue(null));
        Assert.IsFalse(freeText.IsValidValue(null));
        Assert.IsNull(enumerated.Normalize(null));
        Assert.IsNull(freeText.Normalize(null));
        Assert.AreEqual("when_multiline", enumerated.Normalize("  WHEN_MULTILINE "));
        Assert.AreEqual("unknown", enumerated.Normalize(" unknown "), "A value outside the list is only trimmed.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Catalog_GroupsAreContiguous_AndDescriptionsAreDistinctWithinAGroup()
    {
        List<string> groupOrder = new List<string>();
        foreach (CodeStyleRule rule in CodeStyleRules.All)
        {
            if (groupOrder.Count == 0 || groupOrder[groupOrder.Count - 1] != rule.Group)
            {
                Assert.DoesNotContain(rule.Group, groupOrder, $"Group '{rule.Group}' is split in Options by '{rule.Key}'.");
                groupOrder.Add(rule.Group);
            }
        }

        foreach (IGrouping<string, CodeStyleRule> group in CodeStyleRules.All.GroupBy(rule => rule.Group))
        {
            List<string> descriptions = group.Select(rule => rule.Description).ToList();
            CollectionAssert.AllItemsAreUnique(descriptions, group.Key);
        }

        CollectionAssert.AllItemsAreUnique(CodeStyleRules.All.Select(rule => rule.Key).ToList());
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task Rule_AlreadySatisfied_LeavesTheFileUnchanged()
    {
        Settings.Default.Cleaning_CodeStyleRules = "csharp_prefer_braces=true;csharp_style_expression_bodied_methods=false";
        string input =
            "using System;\n\n" +
            "namespace Demo;\n\n" +
            "/// <summary>Doc with code: if (x) return;</summary>\n" +
            "public sealed record Probe<T>(T Value) where T : class\n" +
            "{\n" +
            "#if DEBUG\n" +
            "    // if (debug) return 1;\n" +
            "#endif\n" +
            "    public int Get(bool open)\n" +
            "    {\n" +
            "        const string Text = @\"if (open) return 1;\";\n" +
            "        if (open)\n" +
            "        {\n" +
            "            return Text.Length;\n" +
            "        }\n\n" +
            "        return 0;\n" +
            "    }\n" +
            "}\n";

        Assert.AreEqual(input, await CleanupAsync(input, editorConfig: null));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task BracesRule_AddsOnlyBraces_InAFileWithCommentsConditionalsAndLiterals()
    {
        Settings.Default.Cleaning_CodeStyleRules = "csharp_prefer_braces=true";
        string input =
            "namespace Demo\n" +
            "{\n" +
            "    internal static class Probe\n" +
            "    {\n" +
            "        /// <summary>Returns 1 when open.</summary>\n" +
            "        internal static int Get(bool open)\n" +
            "        {\n" +
            "            // leading comment\n" +
            "            if (open)\n" +
            "                return \"if (open) return 1;\".Length; // trailing comment\n" +
            "#if DEBUG\n" +
            "            return -1;\n" +
            "#else\n" +
            "            return 0;\n" +
            "#endif\n" +
            "        }\n" +
            "    }\n" +
            "}\n";
        string expected =
            "namespace Demo\n" +
            "{\n" +
            "    internal static class Probe\n" +
            "    {\n" +
            "        /// <summary>Returns 1 when open.</summary>\n" +
            "        internal static int Get(bool open)\n" +
            "        {\n" +
            "            // leading comment\n" +
            "            if (open)\n" +
            "            {\n" +
            "                return \"if (open) return 1;\".Length; // trailing comment\n" +
            "            }\n" +
            "#if DEBUG\n" +
            "            return -1;\n" +
            "#else\n" +
            "            return 0;\n" +
            "#endif\n" +
            "        }\n" +
            "    }\n" +
            "}\n";

        Assert.AreEqual(expected, await CleanupAsync(input, editorConfig: null));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task Rules_InATopLevelStatementsFile_ChangeOnlyTheTargetedStatements()
    {
        Settings.Default.Cleaning_CodeStyleRules = "csharp_prefer_braces=true;csharp_style_expression_bodied_local_functions=true";
        string input =
            "using System;\n\n" +
            "// Entry point\n" +
            "var open = args.Length > 0;\n" +
            "if (open)\n" +
            "    Console.WriteLine(Get());\n\n" +
            "int Get()\n" +
            "{\n" +
            "    return 1;\n" +
            "}\n\n" +
            "record Person(string Name);\n";
        string expected =
            "using System;\n\n" +
            "// Entry point\n" +
            "var open = args.Length > 0;\n" +
            "if (open)\n" +
            "{\n" +
            "    Console.WriteLine(Get());\n" +
            "}\n\n" +
            "int Get() => 1;\n\n" +
            "record Person(string Name);\n";

        Assert.AreEqual(expected, await CleanupAsync(input, editorConfig: null));
    }

    /// <summary>
    /// Cleans <paramref name="input" /> as <c>Probe.cs</c> in the test directory. The .editorconfig, when given, is
    /// written to disk (read by <see cref="EffectiveCleanupSettings" />) and added to the project (read by Roslyn).
    /// </summary>
    private async Task<string> CleanupAsync(string input, string editorConfig)
    {
        string text = "root = true\n\n[*]\nend_of_line = lf\n\n[*.cs]\n" + (editorConfig is null ? string.Empty : editorConfig + "\n");
        File.WriteAllText(Path.Combine(_directory, ".editorconfig"), text);

        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        workspace.AddEditorConfig(_directoryName, text);
        DocumentId documentId = workspace.AddDocument(_directoryName + "/Probe.cs", input);
        Solution solution = workspace.CreateSolution();
        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(Path.Combine(_directory, "Probe.cs"));
        DiagnosticCleanupOptions options = new DiagnosticCleanupOptions(
            (DiagnosticCleanupCategory[])Enum.GetValues(typeof(DiagnosticCleanupCategory)),
            analyzerConfigOverrides: settings.AnalyzerConfigOverrides);

        DiagnosticCleanupResult result = await new DiagnosticCleanupEngine(new CodeFixProviderCatalog()).CleanupAsync(solution.GetDocument(documentId), options, CancellationToken.None);

        return await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId);
    }
}
