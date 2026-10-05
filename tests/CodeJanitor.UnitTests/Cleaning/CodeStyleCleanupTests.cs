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
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning;

/// <summary>
/// End-to-end tests of the Code Janitor code-style rules: the rules resolved for a file by
/// <see cref="EffectiveCleanupSettings" /> (.editorconfig, then .codejanitor, then the user setting) are applied by
/// <see cref="DiagnosticCleanupEngine" /> through the built-in Roslyn analyzers and code fixes, the way Visual Studio
/// cleans a file. One rule of every group is covered, as is every cleanup step applied through a Roslyn rule. The
/// expected behavior of those steps comes from the Microsoft Learn page of the rule:
/// <list type="bullet">
/// <item>IDE0007: https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0007-ide0008</item>
/// <item>IDE0018: https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0018</item>
/// <item>IDE0040: https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0040</item>
/// <item>IDE0044: https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0044</item>
/// <item>IDE0053: https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0053</item>
/// <item>IDE0065: https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0065</item>
/// <item>IDE0130: https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0130</item>
/// <item>IDE0161: https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0160-ide0161</item>
/// <item>IDE0300: https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0300</item>
/// <item>IDE2000: https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide2000</item>
/// </list>
/// </summary>
[TestClass]
public sealed class CodeStyleCleanupTests
{
    private static readonly string[] RoslynStepSettings =
    {
        nameof(Settings.Cleaning_ConvertToCollectionExpressions),
        nameof(Settings.Cleaning_ConvertToFileScopedNamespace),
        nameof(Settings.Cleaning_ConvertToVarWhenApparent),
        nameof(Settings.Cleaning_InlineOutVariableDeclarations),
        nameof(Settings.Cleaning_InsertExplicitAccessModifiers),
        nameof(Settings.Cleaning_MakeFieldsReadonlyWhenSafe),
        nameof(Settings.Cleaning_MoveUsingsOutsideNamespace),
        nameof(Settings.Cleaning_RemoveMultipleConsecutiveBlankLines),
        nameof(Settings.Cleaning_SimplifySingleStatementLambdas),
    };

    private string _directoryName;
    private string _directory;

    [TestInitialize]
    public void TestInitialize()
    {
        Settings.Default.Reset();

        // Every test enables only the Roslyn cleanup steps it covers.
        foreach (string settingName in RoslynStepSettings)
        {
            Settings.Default[settingName] = false;
        }

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

        Assert.AreSequenceEqual(
            new Dictionary<string, string>
            {
                ["csharp_prefer_braces"] = "false",
                ["csharp_style_expression_bodied_methods"] = "when_on_single_line",
            }, values.ToDictionary(entry => entry.Key, entry => entry.Value), Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
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
    public void ParseSetting_MalformedEntries_EnableNoRule(string setting) => Assert.IsEmpty(CodeStyleRules.ParseSetting(setting));

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ParseSetting_TrimsEntries_AndTheLastEntryOfARepeatedKeyWins()
    {
        IReadOnlyDictionary<string, string> values = CodeStyleRules.ParseSetting(
            " csharp_prefer_braces = true ;csharp_prefer_braces=When_Multiline;; csharp_preferred_modifier_order = public , static,readonly ;csharp_prefer_braces=bogus");

        Assert.AreSequenceEqual(
            new Dictionary<string, string>
            {
                ["csharp_prefer_braces"] = "when_multiline",
                ["csharp_preferred_modifier_order"] = "public , static,readonly",
            }, values.ToDictionary(entry => entry.Key, entry => entry.Value), Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
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
        Assert.AreSequenceEqual(new[] { "IDE0011" }, rule.DiagnosticIds.ToArray());

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
        List<string> groupOrder = [];
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
            Assert.AreAllDistinct(descriptions, group.Key);
        }

        Assert.AreAllDistinct(CodeStyleRules.All.Select(rule => rule.Key).ToList());
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
    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(
        "class Probe\n{\n    private int _x;\n\n    Probe()\n    {\n        _x = 1;\n    }\n\n    int Get() => _x;\n}\n",
        "class Probe\n{\n    private readonly int _x;\n\n    Probe()\n    {\n        _x = 1;\n    }\n\n    int Get() => _x;\n}\n",
        DisplayName = "field written only in the constructor")]
    public async Task MakeFieldsReadonly_IsAppliedThroughTheRoslynCodeFix(string input, string expected)
    {
        Settings.Default.Cleaning_MakeFieldsReadonlyWhenSafe = true;

        Assert.AreEqual(expected, await CleanupAsync(input, editorConfig: null));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(
        "static class Ext\n{\n    public static void Inc(ref this int value) => value++;\n}\n\nclass Probe\n{\n    private int _n;\n\n    void M() => _n.Inc();\n}\n",
        DisplayName = "ref this extension called on the field")]
    [DataRow(
        "struct Counter\n{\n    int n;\n\n    public int Next => n++;\n}\n\nclass Probe\n{\n    private Counter _c;\n\n    int Get() => _c.Next;\n}\n",
        DisplayName = "mutating getter of a struct field")]
    [DataRow(
        "class Probe\n{\n    private int _x;\n\n    void Set() => _x = 1;\n\n    int Get() => _x;\n}\n",
        DisplayName = "field written outside the constructor")]
    public async Task MakeFieldsReadonly_LeavesFieldsThatCannotBeReadonlyUnchanged(string input)
    {
        Settings.Default.Cleaning_MakeFieldsReadonlyWhenSafe = true;

        Assert.AreEqual(input, await CleanupAsync(input, editorConfig: null));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task MakeFieldsReadonly_Disabled_LeavesTheFieldUnchanged()
    {
        Settings.Default.Cleaning_MakeFieldsReadonlyWhenSafe = false;
        string input = "class Probe\n{\n    private int _x;\n\n    Probe()\n    {\n        _x = 1;\n    }\n\n    int Get() => _x;\n}\n";

        Assert.AreEqual(input, await CleanupAsync(input, editorConfig: null));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(
        nameof(Settings.Cleaning_InlineOutVariableDeclarations),
        "class Probe\n{\n    bool Parse(string text)\n    {\n        int value;\n        return int.TryParse(text, out value);\n    }\n}\n",
        "class Probe\n{\n    bool Parse(string text)\n    {\n        return int.TryParse(text, out int value);\n    }\n}\n",
        DisplayName = "IDE0018: out variable declared inline")]
    [DataRow(
        nameof(Settings.Cleaning_SimplifySingleStatementLambdas),
        "using System;\n\nclass Probe\n{\n    Func<int, int> Square() => x => { return x * x; };\n}\n",
        "using System;\n\nclass Probe\n{\n    Func<int, int> Square() => x => x * x;\n}\n",
        DisplayName = "IDE0053: expression body for a lambda")]
    [DataRow(
        nameof(Settings.Cleaning_ConvertToCollectionExpressions),
        "class Probe\n{\n    int[] Get() { int[] i = new int[] { 1, 2, 3 }; return i; }\n}\n",
        "class Probe\n{\n    int[] Get() { int[] i = [1, 2, 3]; return i; }\n}\n",
        DisplayName = "IDE0300: collection expression for an array of the same type")]
    [DataRow(
        nameof(Settings.Cleaning_ConvertToVarWhenApparent),
        "class Customer\n{\n    Customer Create()\n    {\n        Customer obj = new Customer();\n        return obj;\n    }\n}\n",
        "class Customer\n{\n    Customer Create()\n    {\n        var obj = new Customer();\n        return obj;\n    }\n}\n",
        DisplayName = "IDE0007: var when the type is apparent")]
    [DataRow(
        nameof(Settings.Cleaning_InsertExplicitAccessModifiers),
        "class MyClass\n{\n    const string thisFieldIsConst = \"constant\";\n}\n",
        "internal class MyClass\n{\n    private const string thisFieldIsConst = \"constant\";\n}\n",
        DisplayName = "IDE0040: accessibility modifiers added")]
    [DataRow(
        nameof(Settings.Cleaning_ConvertToFileScopedNamespace),
        "using System;\n\nnamespace Convention\n{\n    class C\n    {\n    }\n}\n",
        "using System;\n\nnamespace Convention;\n\nclass C\n{\n}\n",
        DisplayName = "IDE0161: file-scoped namespace")]
    [DataRow(
        nameof(Settings.Cleaning_MoveUsingsOutsideNamespace),
        "namespace Conventions\n{\n    using System;\n\n    class C\n    {\n        Action Get() => null;\n    }\n}\n",
        "using System;\n\nnamespace Conventions\n{\n    class C\n    {\n        Action Get() => null;\n    }\n}\n",
        DisplayName = "IDE0065: using directive moved outside the namespace")]
    [DataRow(
        nameof(Settings.Cleaning_RemoveMultipleConsecutiveBlankLines),
        "class Probe\n{\n    void Run(bool done)\n    {\n        if (done)\n        {\n            Run(false);\n        }\n\n\n        return;\n    }\n}\n",
        "class Probe\n{\n    void Run(bool done)\n    {\n        if (done)\n        {\n            Run(false);\n        }\n\n        return;\n    }\n}\n",
        DisplayName = "IDE2000: multiple blank lines")]
    public async Task RoslynStep_Enabled_IsAppliedThroughTheRoslynCodeFix(string settingName, string input, string expected)
    {
        Settings.Default[settingName] = true;

        Assert.AreEqual(expected, await CleanupAsync(input, editorConfig: null));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(
        nameof(Settings.Cleaning_ConvertToCollectionExpressions),
        "using System.Collections.Generic;\n\nclass Probe\n{\n    IEnumerable<int> Get() { IEnumerable<int> j = new int[] { 1, 2, 3 }; return j; }\n}\n",
        DisplayName = "IDE0300: types that only match loosely keep the array (true = when_types_exactly_match)")]
    [DataRow(
        nameof(Settings.Cleaning_ConvertToVarWhenApparent),
        "class Probe\n{\n    int Get() => 1;\n\n    int Sum()\n    {\n        int x = 5;\n        int y = Get();\n        return x + y;\n    }\n}\n",
        DisplayName = "IDE0007: built-in types and types that are not apparent keep the explicit type")]
    [DataRow(
        nameof(Settings.Cleaning_InsertExplicitAccessModifiers),
        "internal interface IProbe\n{\n    void Run();\n}\n",
        DisplayName = "IDE0040: interface members get no modifier (for_non_interface_members)")]
    public async Task RoslynStep_Enabled_LeavesTheDocumentedCasesUnchanged(string settingName, string input)
    {
        Settings.Default[settingName] = true;

        Assert.AreEqual(input, await CleanupAsync(input, editorConfig: null));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(nameof(Settings.Cleaning_InlineOutVariableDeclarations), "class Probe\n{\n    bool Parse(string text)\n    {\n        int value;\n        return int.TryParse(text, out value);\n    }\n}\n")]
    [DataRow(nameof(Settings.Cleaning_SimplifySingleStatementLambdas), "using System;\n\nclass Probe\n{\n    Func<int, int> Square() => x => { return x * x; };\n}\n")]
    [DataRow(nameof(Settings.Cleaning_ConvertToCollectionExpressions), "class Probe\n{\n    int[] Get() { int[] i = new int[] { 1, 2, 3 }; return i; }\n}\n")]
    [DataRow(nameof(Settings.Cleaning_ConvertToVarWhenApparent), "class Customer\n{\n    Customer Create()\n    {\n        Customer obj = new Customer();\n        return obj;\n    }\n}\n")]
    [DataRow(nameof(Settings.Cleaning_InsertExplicitAccessModifiers), "class MyClass\n{\n    const string thisFieldIsConst = \"constant\";\n}\n")]
    [DataRow(nameof(Settings.Cleaning_ConvertToFileScopedNamespace), "using System;\n\nnamespace Convention\n{\n    class C\n    {\n    }\n}\n")]
    [DataRow(nameof(Settings.Cleaning_MoveUsingsOutsideNamespace), "namespace Conventions\n{\n    using System;\n\n    class C\n    {\n        Action Get() => null;\n    }\n}\n")]
    [DataRow(nameof(Settings.Cleaning_RemoveMultipleConsecutiveBlankLines), "class Probe\n{\n    void Run()\n    {\n        Run();\n\n\n        return;\n    }\n}\n")]
    public async Task RoslynStep_Disabled_LeavesTheFileUnchanged(string settingName, string input)
    {
        Settings.Default[settingName] = false;

        Assert.AreEqual(input, await CleanupAsync(input, editorConfig: null));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(
        nameof(Settings.Cleaning_ConvertToFileScopedNamespace),
        LanguageVersion.CSharp9,
        "using System;\n\nnamespace Convention\n{\n    class C\n    {\n    }\n}\n",
        DisplayName = "IDE0161: C# 9 keeps the block-scoped namespace (file-scoped namespaces need C# 10)")]
    [DataRow(
        nameof(Settings.Cleaning_ConvertToCollectionExpressions),
        LanguageVersion.CSharp11,
        "class Probe\n{\n    int[] Get() { int[] i = new int[] { 1, 2, 3 }; return i; }\n}\n",
        DisplayName = "IDE0300: C# 11 keeps the array (collection expressions need C# 12)")]
    public async Task RoslynStep_Enabled_LeavesTheFileUnchanged_WhenTheLanguageVersionLacksTheSyntax(string settingName, LanguageVersion languageVersion, string input)
    {
        Settings.Default[settingName] = true;

        Assert.AreEqual(input, await CleanupAsync(input, editorConfig: null, languageVersion));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(
        nameof(Settings.Cleaning_ConvertToFileScopedNamespace),
        "csharp_style_namespace_declarations = block_scoped:warning",
        "using System;\n\nnamespace Convention;\n\nclass C\n{\n}\n",
        "using System;\n\nnamespace Convention\n{\n    class C\n    {\n    }\n}\n",
        DisplayName = "IDE0160: .editorconfig block_scoped wins")]
    [DataRow(
        nameof(Settings.Cleaning_MoveUsingsOutsideNamespace),
        "csharp_using_directive_placement = inside_namespace:warning",
        "namespace Conventions\n{\n    using System;\n\n    class C\n    {\n        Action Get() => null;\n    }\n}\n",
        "namespace Conventions\n{\n    using System;\n\n    class C\n    {\n        Action Get() => null;\n    }\n}\n",
        DisplayName = "IDE0065: .editorconfig inside_namespace wins")]
    [DataRow(
        nameof(Settings.Cleaning_ConvertToVarWhenApparent),
        "csharp_style_var_for_built_in_types = true:silent",
        "class Probe\n{\n    int Get() => 1;\n\n    int Twice()\n    {\n        int y = Get();\n        return y + y;\n    }\n}\n",
        "class Probe\n{\n    int Get() => 1;\n\n    int Twice()\n    {\n        int y = Get();\n        return y + y;\n    }\n}\n",
        DisplayName = "IDE0007: a var option .editorconfig does not enforce stays unapplied")]
    [DataRow(
        nameof(Settings.Cleaning_ConvertToVarWhenApparent),
        "csharp_style_var_elsewhere = true:silent",
        "class Customer\n{\n    static Customer Create() => new Customer();\n\n    Customer Get()\n    {\n        Customer c = Create();\n        return c;\n    }\n}\n",
        "class Customer\n{\n    static Customer Create() => new Customer();\n\n    Customer Get()\n    {\n        Customer c = Create();\n        return c;\n    }\n}\n",
        DisplayName = "IDE0007: a type that is not apparent keeps its explicit type when .editorconfig does not enforce csharp_style_var_elsewhere")]
    public async Task RoslynStep_EditorConfig_DecidesOverTheSetting(string settingName, string editorConfig, string input, string expected)
    {
        Settings.Default[settingName] = true;

        Assert.AreEqual(expected, await CleanupAsync(input, editorConfig));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(
        nameof(Settings.Cleaning_InlineOutVariableDeclarations),
        "csharp_style_inlined_variable_declaration = true",
        "class Probe\n{\n    bool Parse(string text)\n    {\n        int value;\n        return int.TryParse(text, out value);\n    }\n}\n",
        "class Probe\n{\n    bool Parse(string text)\n    {\n        return int.TryParse(text, out int value);\n    }\n}\n",
        DisplayName = "IDE0018: option without severity")]
    [DataRow(
        nameof(Settings.Cleaning_InlineOutVariableDeclarations),
        "csharp_style_inlined_variable_declaration = true:suggestion",
        "class Probe\n{\n    bool Parse(string text)\n    {\n        int value;\n        return int.TryParse(text, out value);\n    }\n}\n",
        "class Probe\n{\n    bool Parse(string text)\n    {\n        return int.TryParse(text, out int value);\n    }\n}\n",
        DisplayName = "IDE0018: severity equal to Roslyn's default")]
    [DataRow(
        nameof(Settings.Cleaning_InsertExplicitAccessModifiers),
        "dotnet_style_require_accessibility_modifiers = for_non_interface_members",
        "class MyClass\n{\n    const string thisFieldIsConst = \"constant\";\n}\n",
        "internal class MyClass\n{\n    private const string thisFieldIsConst = \"constant\";\n}\n",
        DisplayName = "IDE0040: option without severity")]
    [DataRow(
        nameof(Settings.Cleaning_SimplifySingleStatementLambdas),
        "csharp_style_expression_bodied_lambdas = true",
        "using System;\n\nclass Probe\n{\n    Func<int, int> Square() => x => { return x * x; };\n}\n",
        "using System;\n\nclass Probe\n{\n    Func<int, int> Square() => x => x * x;\n}\n",
        DisplayName = "IDE0053: option without severity")]
    [DataRow(
        nameof(Settings.Cleaning_RemoveMultipleConsecutiveBlankLines),
        "dotnet_style_allow_multiple_blank_lines_experimental = false",
        "class Probe\n{\n    void Run(bool done)\n    {\n        if (done)\n        {\n            Run(false);\n        }\n\n\n        return;\n    }\n}\n",
        "class Probe\n{\n    void Run(bool done)\n    {\n        if (done)\n        {\n            Run(false);\n        }\n\n        return;\n    }\n}\n",
        DisplayName = "IDE2000: option without severity")]
    [DataRow(
        nameof(Settings.Cleaning_ConvertToCollectionExpressions),
        "dotnet_style_prefer_collection_expression = when_types_loosely_match:suggestion",
        "class Probe\n{\n    int[] Get() { int[] i = new int[] { 1, 2, 3 }; return i; }\n}\n",
        "class Probe\n{\n    int[] Get() { int[] i = [1, 2, 3]; return i; }\n}\n",
        DisplayName = "IDE0300: Roslyn's default value and severity")]
    [DataRow(
        nameof(Settings.Cleaning_MakeFieldsReadonlyWhenSafe),
        "dotnet_style_readonly_field = true",
        "class Probe\n{\n    private int _x;\n\n    Probe()\n    {\n        _x = 1;\n    }\n\n    int Get() => _x;\n}\n",
        "class Probe\n{\n    private readonly int _x;\n\n    Probe()\n    {\n        _x = 1;\n    }\n\n    int Get() => _x;\n}\n",
        DisplayName = "IDE0044: option without severity")]
    [DataRow(
        nameof(Settings.Cleaning_MoveUsingsOutsideNamespace),
        "csharp_using_directive_placement = inside_namespace",
        "using System;\n\nnamespace Conventions\n{\n    class C\n    {\n        Action Get() => null;\n    }\n}\n",
        "namespace Conventions\n{\n    using System;\n\n    class C\n    {\n        Action Get() => null;\n    }\n}\n",
        DisplayName = "IDE0065: inside_namespace without severity")]
    public async Task RoslynStep_EditorConfigEnablingTheRuleWithoutAnEnforcingDiagnosticSeverity_IsApplied(string settingName, string editorConfig, string input, string expected)
    {
        // The user setting is the opposite of what .editorconfig decides, so the result shows that .editorconfig wins.
        Settings.Default[settingName] = settingName == nameof(Settings.Cleaning_MoveUsingsOutsideNamespace);

        Assert.AreEqual(expected, await CleanupAsync(input, editorConfig));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(
        nameof(Settings.Cleaning_ConvertToFileScopedNamespace),
        true,
        "csharp_style_namespace_declarations = Block_Scoped",
        "using System;\n\nnamespace Convention;\n\nclass C\n{\n}\n",
        "using System;\n\nnamespace Convention\n{\n    class C\n    {\n    }\n}\n",
        DisplayName = "IDE0160: a value .editorconfig writes in another case")]
    [DataRow(
        nameof(Settings.Cleaning_MoveUsingsOutsideNamespace),
        true,
        "csharp_using_directive_placement = Inside_Namespace",
        "using System;\n\nnamespace Conventions\n{\n    class C\n    {\n        Action Get() => null;\n    }\n}\n",
        "namespace Conventions\n{\n    using System;\n\n    class C\n    {\n        Action Get() => null;\n    }\n}\n",
        DisplayName = "IDE0065: a value .editorconfig writes in another case")]
    [DataRow(
        nameof(Settings.Cleaning_ConvertToCollectionExpressions),
        false,
        "dotnet_style_prefer_collection_expression = true\ndotnet_diagnostic.IDE0300.severity = warning",
        "using System;\n\nclass Probe\n{\n    int[] Get() { int[] i = Array.Empty<int>(); return i; }\n}\n",
        "using System;\n\nclass Probe\n{\n    int[] Get() { int[] i = []; return i; }\n}\n",
        DisplayName = "IDE0301: a severity configured for another diagnostic of the rule")]
    [DataRow(
        nameof(Settings.Cleaning_ConvertToVarWhenApparent),
        true,
        "csharp_style_var_when_type_is_apparent = true:silent\ncsharp_style_var_for_built_in_types = false:silent\ncsharp_style_var_elsewhere = false:silent\ndotnet_diagnostic.IDE0008.severity = warning",
        "class Probe\n{\n    int Get() => 1;\n\n    int Sum()\n    {\n        var x = Get();\n        var y = 5;\n        return x + y;\n    }\n}\n",
        "class Probe\n{\n    int Get() => 1;\n\n    int Sum()\n    {\n        int x = Get();\n        int y = 5;\n        return x + y;\n    }\n}\n",
        DisplayName = "IDE0008: explicit types .editorconfig enforces are still applied")]
    [DataRow(
        nameof(Settings.Cleaning_ConvertToVarWhenApparent),
        false,
        "csharp_style_var_when_type_is_apparent = true\ncsharp_style_var_elsewhere = true:silent",
        "class Customer\n{\n    static Customer Create() => new Customer();\n\n    Customer Get()\n    {\n        Customer created = new Customer();\n        Customer c = Create();\n        return c ?? created;\n    }\n}\n",
        "class Customer\n{\n    static Customer Create() => new Customer();\n\n    Customer Get()\n    {\n        var created = new Customer();\n        Customer c = Create();\n        return c ?? created;\n    }\n}\n",
        DisplayName = "IDE0007: decided by value, a var option .editorconfig does not enforce stays unapplied")]
    [DataRow(
        nameof(Settings.Cleaning_InsertExplicitAccessModifiers),
        true,
        "dotnet_style_require_accessibility_modifiers = never",
        "internal class MyClass\n{\n    private const string thisFieldIsConst = \"constant\";\n}\n",
        "internal class MyClass\n{\n    private const string thisFieldIsConst = \"constant\";\n}\n",
        DisplayName = "IDE0040: a disabling value without a severity removes nothing")]
    public async Task RoslynStep_EditorConfigDecidingTheRule_IsAppliedAsEditorConfigSaysIt(string settingName, bool userSetting, string editorConfig, string input, string expected)
    {
        Settings.Default[settingName] = userSetting;

        Assert.AreEqual(expected, await CleanupAsync(input, editorConfig));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task MoveUsingsOutsideNamespace_NamespaceRelativeDirective_IsQualifiedSoTheFileKeepsCompiling()
    {
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = true;
        string input = "namespace Company.App\n{\n    using Services;\n\n    public class C\n    {\n        public Svc Service { get; set; }\n    }\n}\n";
        string expected = "using Company.App.Services;\n\nnamespace Company.App\n{\n    public class C\n    {\n        public Svc Service { get; set; }\n    }\n}\n";

        Assert.AreEqual(expected, await CleanupAsync(input, editorConfig: null, otherDocument: "namespace Company.App.Services { public class Svc { } }\n"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task CodeFix_InAFileWithMixedLineEndings_LeavesTheLineEndingsAsTheFixWroteThem()
    {
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = true;
        string input = "namespace Conventions\r\n{\n    using System;\n\n    class C\n    {\n        Action Get() => null;\n    }\n}\n";

        string output = await CleanupAsync(input, "end_of_line = unset");

        Assert.AreEqual("using System;\n\nnamespace Conventions\n{\n    class C\n    {\n        Action Get() => null;\n    }\n}\n", output.Replace("\r\n", "\n"));
        Assert.Contains("\r\n", output);
        Assert.MatchesRegex("[^\r]\n", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(UsingDirectiveSorting.None, "using aa;\nusing AB;\n", DisplayName = "None: the order the fix wrote")]
    [DataRow(UsingDirectiveSorting.RemoveAndSortUsings, "using aa;\nusing AB;\n", DisplayName = "Remove and Sort Usings: Roslyn's order, ignoring case")]
    [DataRow(UsingDirectiveSorting.OrganizeUsings, "using AB;\nusing aa;\n", DisplayName = "organizeUsings: Code Janitor's ordinal order")]
    public async Task MoveUsingsOutsideNamespace_SortsTheMovedDirectivesAsTheStepThatSortsTheFile(UsingDirectiveSorting sorting, string expectedUsings)
    {
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = true;
        string input = "using aa;\n\nnamespace Conventions\n{\n    using AB;\n\n    class C\n    {\n        X GetX() => null;\n\n        Y GetY() => null;\n    }\n}\n";

        string output = await CleanupAsync(input, null, otherDocument: "namespace aa\n{\n    public class X\n    {\n    }\n}\n\nnamespace AB\n{\n    public class Y\n    {\n    }\n}\n", usingDirectiveSorting: sorting);

        Assert.AreEqual(expectedUsings + "\nnamespace Conventions\n{\n    class C\n    {\n        X GetX() => null;\n\n        Y GetY() => null;\n    }\n}\n", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(UsingDirectiveSorting.None, "using System.Text;\nusing System;\n", DisplayName = "None: the order the fix wrote")]
    [DataRow(UsingDirectiveSorting.RemoveAndSortUsings, "using System;\nusing System.Text;\n", DisplayName = "Remove and Sort Usings")]
    [DataRow(UsingDirectiveSorting.OrganizeUsings, "using System;\nusing System.Text;\n", DisplayName = "organizeUsings")]
    public async Task MoveUsingsOutsideNamespace_SortsSystemDirectives(UsingDirectiveSorting sorting, string expectedUsings)
    {
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = true;
        string input = "using System.Text;\n\nnamespace Conventions\n{\n    using System;\n\n    class C\n    {\n        Action Get() => null;\n\n        StringBuilder Build() => null;\n    }\n}\n";

        string output = await CleanupAsync(input, null, usingDirectiveSorting: sorting);

        Assert.AreEqual(expectedUsings + "\nnamespace Conventions\n{\n    class C\n    {\n        Action Get() => null;\n\n        StringBuilder Build() => null;\n    }\n}\n", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("end_of_line = lf", "\n", "\n", DisplayName = "end_of_line = lf")]
    [DataRow("end_of_line = crlf", "\n", "\r\n", DisplayName = "end_of_line = crlf")]
    [DataRow("end_of_line = unset", "\n", "\n", DisplayName = "no end_of_line: the file's own LF")]
    [DataRow("end_of_line = unset", "\r\n", "\r\n", DisplayName = "no end_of_line: the file's own CRLF")]
    public async Task CodeFixInsertingItsOwnLineBreak_KeepsTheLineEndingOfTheFile(string endOfLine, string inputLineEnding, string expectedLineEnding)
    {
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = true;
        string input = "namespace Conventions\n{\n    using System;\n\n    class C\n    {\n        Action Get() => null;\n    }\n}\n".Replace("\n", inputLineEnding);
        string expected = "using System;\n\nnamespace Conventions\n{\n    class C\n    {\n        Action Get() => null;\n    }\n}\n".Replace("\n", expectedLineEnding);

        Assert.AreEqual(expected, await CleanupAsync(input, endOfLine));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task NamespaceMatchFolder_FixesTheNamespaceFromTheFolder_AndNoOtherDiagnostic()
    {
        string editorConfig = "root = true\n\n[*]\nend_of_line = lf\n\n[*.cs]\ncsharp_prefer_braces = true:warning\n";
        string input = "namespace Root.BadExample\n{\n    class Example\n    {\n        int Get(bool open)\n        {\n            if (open)\n                return 1;\n            return 0;\n        }\n    }\n}\n";
        string expected = "namespace Root.Data\n{\n    class Example\n    {\n        int Get(bool open)\n        {\n            if (open)\n                return 1;\n            return 0;\n        }\n    }\n}\n";

        // As in a Visual Studio project: the project directory is the folder root and RootNamespace is the default namespace.
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace { DefaultNamespace = "Root" };
        workspace.AddEditorConfig(string.Empty, editorConfig);
        workspace.AddGlobalConfig("BuildProperties", $"build_property.RootNamespace = Root\nbuild_property.ProjectDir = {DiagnosticCleanupTestWorkspace.RootDirectory}{Path.DirectorySeparatorChar}\n");
        DocumentId documentId = workspace.AddDocument("Data/Example.cs", input);
        Solution solution = workspace.CreateSolution();

        DiagnosticCleanupResult result = await new DiagnosticCleanupEngine(new CodeFixProviderCatalog()).CleanupAsync(
            solution.GetDocument(documentId), DiagnosticCleanupOptions.NamespaceMatchFolder, CancellationToken.None);

        Assert.AreEqual(expected, await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task NamespaceMatchFolder_UpdatesReferencesInOtherFiles_WithTheEndOfLineOfEachFile()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace { DefaultNamespace = "Root" };
        workspace.AddEditorConfig(string.Empty, "root = true\n\n[*.cs]\nend_of_line = crlf\n");
        workspace.AddGlobalConfig("BuildProperties", $"build_property.RootNamespace = Root\nbuild_property.ProjectDir = {DiagnosticCleanupTestWorkspace.RootDirectory}{Path.DirectorySeparatorChar}\n");
        DocumentId documentId = workspace.AddDocument("Data/Example.cs", "namespace Root.BadExample\n{\n    public class Example\n    {\n    }\n}\n");
        DocumentId referenceId = workspace.AddDocument("Use.cs", "using Root.BadExample;\n\nnamespace Root\n{\n    class Use\n    {\n        Example Get() => null;\n    }\n}\n");
        Solution solution = workspace.CreateSolution();

        DiagnosticCleanupResult result = await new DiagnosticCleanupEngine(new CodeFixProviderCatalog()).CleanupAsync(
            solution.GetDocument(documentId), DiagnosticCleanupOptions.NamespaceMatchFolder, CancellationToken.None);

        Assert.AreEqual("namespace Root.Data\r\n{\r\n    public class Example\r\n    {\r\n    }\r\n}\r\n", await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId));
        Assert.AreEqual("using Root.Data;\r\n\r\nnamespace Root\r\n{\r\n    class Use\r\n    {\r\n        Example Get() => null;\r\n    }\r\n}\r\n", await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, referenceId));
    }

    private async Task<string> CleanupAsync(string input, string editorConfig, LanguageVersion languageVersion = LanguageVersion.Default, string otherDocument = null, UsingDirectiveSorting usingDirectiveSorting = UsingDirectiveSorting.None)
    {
        string text = "root = true\n\n[*]\nend_of_line = lf\n\n[*.cs]\n" + (editorConfig is null ? string.Empty : editorConfig + "\n");
        File.WriteAllText(Path.Combine(_directory, ".editorconfig"), text);

        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace { ParseOptions = new CSharpParseOptions(languageVersion) };
        workspace.AddEditorConfig(_directoryName, text);
        DocumentId documentId = workspace.AddDocument(_directoryName + "/Probe.cs", input);
        if (otherDocument is not null)
        {
            workspace.AddDocument(_directoryName + "/Other.cs", otherDocument);
        }

        Solution solution = workspace.CreateSolution();
        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(Path.Combine(_directory, "Probe.cs"));
        DiagnosticCleanupOptions options = new DiagnosticCleanupOptions(
            (DiagnosticCleanupCategory[])Enum.GetValues(typeof(DiagnosticCleanupCategory)),
            analyzerConfigOverrides: settings.AnalyzerConfigOverrides,
            usingDirectiveSorting: usingDirectiveSorting);

        DiagnosticCleanupResult result = await new DiagnosticCleanupEngine(new CodeFixProviderCatalog()).CleanupAsync(solution.GetDocument(documentId), options, CancellationToken.None);

        return await DiagnosticCleanupTestWorkspace.GetTextAsync(result.ChangedSolution, documentId);
    }
}
