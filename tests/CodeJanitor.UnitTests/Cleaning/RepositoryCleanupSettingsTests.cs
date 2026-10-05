using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public sealed class RepositoryCleanupSettingsTests
{
    private string _tempDirectory;

    [TestInitialize]
    public void TestInitialize()
    {
        Settings.Default.Reset();
        Settings.Default.Cleaning_AiXmlDocumentationEnabled = false;
        _tempDirectory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Settings.Default.Reset();

        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Parse_MapsCamelCaseKeysToVisualStudioSettingNames()
    {
        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": { \"convertToVarWhenApparent\": true, \"removeEndOfLineWhitespace\": false } }");

        Assert.IsTrue(overrides.TryGetBoolean("Cleaning_ConvertToVarWhenApparent", false));
        Assert.IsFalse(overrides.TryGetBoolean("Cleaning_RemoveEndOfLineWhitespace", true));
        Assert.AreEqual(2, overrides.Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Parse_AppliesGroupAliases_AndIndividualKeysWin()
    {
        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": { \"insertBlankLinePadding\": false, \"insertBlankLinePaddingBeforeClasses\": true } }");

        // Alias fans out to the group members.
        Assert.IsFalse(overrides.TryGetBoolean("Cleaning_InsertBlankLinePaddingAfterMethods", true));

        // The individual key overrides the alias.
        Assert.IsTrue(overrides.TryGetBoolean("Cleaning_InsertBlankLinePaddingBeforeClasses", false));

        // Keys outside the alias target list remain untouched.
        Assert.IsTrue(overrides.TryGetBoolean("Cleaning_InsertBlankLinePaddingBeforeSingleLineComments", true));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Parse_IgnoresUnknownKeysWrongTypesAndInvalidJson()
    {
        RepositoryCleanupOverrides fromInvalidJson = RepositoryCleanupSettings.Parse("{ this is not json");
        Assert.AreEqual(0, fromInvalidJson.Count);

        RepositoryCleanupOverrides fromMissingSection = RepositoryCleanupSettings.Parse("{ \"other\": {} }");
        Assert.AreEqual(0, fromMissingSection.Count);

        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": { \"unknownKey\": true, \"convertToVarWhenApparent\": \"yes\", \"removeRegions\": \"no\" } }");
        Assert.AreEqual(0, overrides.Count);
        Assert.IsNull(overrides.RemoveRegions);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Parse_MapsHeaderEnumsAndText()
    {
        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": { \"fileHeaderCSharp\": \"// Copyright\", \"fileHeaderPosition\": \"afterUsings\", \"fileHeaderUpdateMode\": \"replace\" } }");

        Assert.AreEqual("// Copyright", overrides.TryGetString("Cleaning_UpdateFileHeaderCSharp", null));
        Assert.AreEqual(1, overrides.TryGetInt32("Cleaning_UpdateFileHeader_HeaderPosition", 0));
        Assert.AreEqual(1, overrides.TryGetInt32("Cleaning_UpdateFileHeader_HeaderUpdateMode", 0));

        RepositoryCleanupOverrides invalidEnum = RepositoryCleanupSettings.Parse("{ \"cleanup\": { \"fileHeaderPosition\": \"middle\" } }");
        Assert.AreEqual(0, invalidEnum.Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Parse_AcceptsVsCodeAliasForReturnAndThrowPadding()
    {
        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": { \"insertBlankLineBeforeReturnAndThrow\": true } }");

        Assert.IsTrue(overrides.TryGetBoolean("Cleaning_InsertBlankLineBeforeReturnAndThrowStatements", false));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Parse_ReadsRegionAndUsingOrganizationPolicies()
    {
        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": { \"removeRegions\": false, \"organizeUsings\": true } }");

        Assert.IsFalse(overrides.RemoveRegions);
        Assert.IsTrue(overrides.OrganizeUsings);

        // Policies are not Visual Studio user settings and are not counted as overrides.
        Assert.AreEqual(0, overrides.Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void LoadForFile_WalksUpToNearestConfig()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".codejanitor"),
            "{ \"cleanup\": { \"convertToVarWhenApparent\": true } }");
        DirectoryInfo nested = Directory.CreateDirectory(Path.Combine(_tempDirectory, "src", "App"));
        string filePath = Path.Combine(nested.FullName, "Sample.cs");

        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.LoadForFile(filePath);

        Assert.IsTrue(overrides.TryGetBoolean("Cleaning_ConvertToVarWhenApparent", false));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void LoadForFile_PrefersNearestDirectory_AndAlternateFileName()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".code-janitor.json"),
            "{ \"cleanup\": { \"convertToVarWhenApparent\": true } }");
        DirectoryInfo nested = Directory.CreateDirectory(Path.Combine(_tempDirectory, "src"));
        File.WriteAllText(Path.Combine(nested.FullName, ".code-janitor.json"),
            "{ \"cleanup\": { \"convertToVarWhenApparent\": false } }");
        string filePath = Path.Combine(nested.FullName, "Sample.cs");

        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.LoadForFile(filePath);

        Assert.IsFalse(overrides.TryGetBoolean("Cleaning_ConvertToVarWhenApparent", true));
        Assert.IsTrue(RepositoryCleanupSettings.TryFindConfigFile(nested.FullName, out string configPath));
        Assert.EndsWith(".code-janitor.json", configPath);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void LoadForFile_WithoutConfig_ReturnsEmpty()
    {
        string filePath = Path.Combine(_tempDirectory, "Sample.cs");

        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.LoadForFile(filePath);

        Assert.AreEqual(0, overrides.Count);
        Assert.IsNull(overrides.RemoveRegions);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void BuildJson_ProducesParseableRoundTrip()
    {
        Settings.Default.Cleaning_ConvertToVarWhenApparent = true;
        Settings.Default.Cleaning_RemoveEndOfLineWhitespace = false;
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// \"Quoted\" header";
        Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition = 1;
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = 1;

        string json = RepositoryCleanupSettings.BuildJson(Settings.Default);
        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(json);

        Assert.IsTrue(overrides.TryGetBoolean("Cleaning_ConvertToVarWhenApparent", false));
        Assert.IsFalse(overrides.TryGetBoolean("Cleaning_RemoveEndOfLineWhitespace", true));
        Assert.AreEqual("// \"Quoted\" header", overrides.TryGetString("Cleaning_UpdateFileHeaderCSharp", null));
        Assert.AreEqual(1, overrides.TryGetInt32("Cleaning_UpdateFileHeader_HeaderPosition", 0));
        Assert.AreEqual(1, overrides.TryGetInt32("Cleaning_UpdateFileHeader_HeaderUpdateMode", 0));
        Assert.Contains("\"cleanup\"", json);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyToSettings_AppliesOverrides()
    {
        bool original = Settings.Default.Cleaning_ConvertToVarWhenApparent;
        try
        {
            Settings.Default.Cleaning_ConvertToVarWhenApparent = false;
            RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(
                "{ \"cleanup\": { \"convertToVarWhenApparent\": true, \"organizeUsings\": true } }");

            int applied = RepositoryCleanupSettings.ApplyToSettings(overrides, Settings.Default);

            Assert.AreEqual(1, applied, "Policy-only entries without a Visual Studio setting must be skipped.");
            Assert.IsTrue(Settings.Default.Cleaning_ConvertToVarWhenApparent);
        }
        finally
        {
            Settings.Default.Cleaning_ConvertToVarWhenApparent = original;
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void BuildJson_ExportsEnabledCodeStyleRules_ThatParseBack()
    {
        Settings.Default.Cleaning_CodeStyleRules = "csharp_prefer_braces=when_multiline;csharp_preferred_modifier_order=public,static";

        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(RepositoryCleanupSettings.BuildJson(Settings.Default));

        Assert.AreSequenceEqual(
            new Dictionary<string, string>
            {
                ["csharp_preferred_modifier_order"] = "public,static",
                ["csharp_prefer_braces"] = "when_multiline",
            }, overrides.CodeStyle.ToDictionary(entry => entry.Key, entry => entry.Value), Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyToSettings_MergesCodeStyleRules_EnablingAndDisablingThem()
    {
        Settings.Default.Cleaning_CodeStyleRules = "csharp_prefer_braces=true;dotnet_style_null_propagation=true";
        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": { \"codeStyle\": { \"csharp_prefer_braces\": \"false\", \"dotnet_style_null_propagation\": null, \"csharp_style_throw_expression\": \"true\", \"unknown_rule\": \"true\" } } }");

        int applied = RepositoryCleanupSettings.ApplyToSettings(overrides, Settings.Default);

        Assert.AreEqual(3, applied);
        Assert.AreEqual("csharp_prefer_braces=false;csharp_style_throw_expression=true", Settings.Default.Cleaning_CodeStyleRules);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Parse_CodeStyleValues_IgnoreCase_AndAreNormalized()
    {
        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": { \"codeStyle\": { \"csharp_prefer_braces\": \"True\", \"dotnet_style_null_propagation\": \"sometimes\" } } }");

        Assert.AreSequenceEqual(
            new Dictionary<string, string> { ["csharp_prefer_braces"] = "true" }, overrides.CodeStyle.ToDictionary(entry => entry.Key, entry => entry.Value), Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyHeadlessCSharpTransformations_HonorsRepositoryOverride()
    {
        Settings.Default.Cleaning_RemoveEndOfLineWhitespace = false;
        File.WriteAllText(Path.Combine(_tempDirectory, ".codejanitor"),
            "{ \"cleanup\": { \"removeEndOfLineWhitespace\": true } }");

        string filePath = Path.Combine(_tempDirectory, "Sample.cs");
        string input = "namespace Demo;\r\n\r\npublic class C   \r\n{\r\n}\r\n";

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.Contains("public class C\r\n", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyHeadlessCSharpTransformations_RepositoryPolicyCanKeepRegions()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".codejanitor"),
            "{ \"cleanup\": { \"removeRegions\": false } }");

        string filePath = Path.Combine(_tempDirectory, "Sample.cs");
        string input = "namespace Demo;\r\n\r\n#region Helpers\r\npublic class C { }\r\n#endregion\r\n";

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.Contains("#region Helpers", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(null, DisplayName = "null")]
    [DataRow("", DisplayName = "empty")]
    [DataRow("   \r\n\t", DisplayName = "whitespace only")]
    [DataRow("null", DisplayName = "JSON null")]
    [DataRow("[ { \"cleanup\": { \"convertToVarWhenApparent\": true } } ]", DisplayName = "array root")]
    [DataRow("42", DisplayName = "number root")]
    [DataRow("\"cleanup\"", DisplayName = "string root")]
    [DataRow("{ \"cleanup\": null }", DisplayName = "null cleanup section")]
    [DataRow("{ \"cleanup\": [ \"convertToVarWhenApparent\" ] }", DisplayName = "array cleanup section")]
    [DataRow("{ \"cleanup\": true }", DisplayName = "boolean cleanup section")]
    [DataRow("{ \"Cleanup\": { \"convertToVarWhenApparent\": true } }", DisplayName = "section name is case-sensitive")]
    [DataRow("{ \"cleanup\": { \"convertToVarWhenApparent\": true }", DisplayName = "truncated JSON")]
    [DataRow("{ \"cleanup\": { \"convertToVarWhenApparent\": tru } }", DisplayName = "invalid literal")]
    public void Parse_WithoutAUsableCleanupObject_DefinesNothing(string json)
    {
        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(json);

        Assert.AreEqual(0, overrides.Count);
        Assert.IsEmpty(overrides.CodeStyle);
        Assert.IsNull(overrides.RemoveRegions);
        Assert.IsNull(overrides.OrganizeUsings);
        Assert.IsTrue(overrides.RemovesRegions);
        Assert.IsTrue(overrides.TryGetBoolean("Cleaning_ConvertToVarWhenApparent", true));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Parse_WrongValueTypes_AreIgnored_WhileValidEntriesOfTheSameFileApply()
    {
        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": {" +
            " \"organizeUsings\": \"true\", \"removeRegions\": 0," +
            " \"insertBlankLinePadding\": \"false\", \"insertExplicitAccessModifiers\": null," +
            " \"fileHeaderPosition\": 1, \"fileHeaderUpdateMode\": true, \"fileHeaderCSharp\": 5," +
            " \"convertToVarWhenApparent\": 1, \"removeEndOfLineWhitespace\": null, \"sealClassesWhenSafe\": [ true ]," +
            " \"maxDegreeOfParallelism\": \"2\"," +
            " \"codeStyle\": { \"csharp_prefer_braces\": true, \"csharp_style_throw_expression\": 1, \"dotnet_style_null_propagation\": \"false\" }," +
            " \"\": true," +
            " \"makeFieldsReadonlyWhenSafe\": false } }");

        Assert.AreSequenceEqual(new[] { "Cleaning_MakeFieldsReadonlyWhenSafe" }, overrides.Values.Keys.ToList(), Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
        Assert.IsFalse(overrides.TryGetBoolean("Cleaning_MakeFieldsReadonlyWhenSafe", true));
        Assert.IsNull(overrides.OrganizeUsings);
        Assert.IsNull(overrides.RemoveRegions);
        Assert.AreSequenceEqual(
            new Dictionary<string, string> { ["dotnet_style_null_propagation"] = "false" }, overrides.CodeStyle.ToDictionary(entry => entry.Key, entry => entry.Value), Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("{ \"cleanup\": { \"codeStyle\": \"csharp_prefer_braces=true\" } }", DisplayName = "codeStyle as a string")]
    [DataRow("{ \"cleanup\": { \"codeStyle\": [ \"csharp_prefer_braces\" ] } }", DisplayName = "codeStyle as an array")]
    [DataRow("{ \"cleanup\": { \"codeStyle\": null } }", DisplayName = "codeStyle null")]
    [DataRow("{ \"cleanup\": { \"codeStyle\": { \"dotnet_naming_rule.x.severity\": \"warning\", \"CSharp_Prefer_Braces\": \"true\" } } }", DisplayName = "unknown or differently-cased rule keys")]
    public void Parse_UnusableCodeStyleSection_DefinesNoRule(string json) => Assert.IsEmpty(RepositoryCleanupSettings.Parse(json).CodeStyle);

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("\"documentStart\"", 0)]
    [DataRow("\"afterUsings\"", 1)]
    [DataRow("\"AfterUsings\"", -1, DisplayName = "enum names are case-sensitive")]
    [DataRow("\"\"", -1, DisplayName = "empty name")]
    public void Parse_FileHeaderPosition_AcceptsOnlyTheExactNames(string value, int expected)
    {
        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse("{ \"cleanup\": { \"fileHeaderPosition\": " + value + " } }");

        Assert.AreEqual(expected, overrides.TryGetInt32("Cleaning_UpdateFileHeader_HeaderPosition", -1));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("\"insert\"", 0)]
    [DataRow("\"replace\"", 1)]
    [DataRow("\"Replace\"", -1, DisplayName = "enum names are case-sensitive")]
    public void Parse_FileHeaderUpdateMode_AcceptsOnlyTheExactNames(string value, int expected)
    {
        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse("{ \"cleanup\": { \"fileHeaderUpdateMode\": " + value + " } }");

        Assert.AreEqual(expected, overrides.TryGetInt32("Cleaning_UpdateFileHeader_HeaderUpdateMode", -1));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Parse_MapsIntegerAndSpeciallyNamedSettings_ByTheirSettingType()
    {
        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": { \"maxDegreeOfParallelism\": 2, \"formatComments\": true, \"usingStatementsToReinsertWhenRemovedExpression\": \"System\" } }");

        Assert.AreEqual(2, overrides.TryGetInt32("Cleaning_MaxDegreeOfParallelism", 0));
        Assert.IsTrue(overrides.TryGetBoolean("Formatting_CommentRunDuringCleanup", false));
        Assert.AreEqual("System", overrides.TryGetString("Cleaning_UsingStatementsToReinsertWhenRemovedExpression", null));

        // A value read with the wrong accessor, or a setting the policy does not define, falls back.
        Assert.AreEqual("fallback", overrides.TryGetString("Cleaning_MaxDegreeOfParallelism", "fallback"));
        Assert.AreEqual(7, overrides.TryGetInt32("Formatting_CommentRunDuringCleanup", 7));
        Assert.AreEqual(7, overrides.TryGetInt32("Cleaning_UpdateFileHeader_HeaderPosition", 7));
        Assert.AreEqual("fallback", overrides.TryGetString("Cleaning_UpdateFileHeaderCSharp", "fallback"));
        Assert.IsTrue(overrides.TryGetBoolean("Cleaning_MaxDegreeOfParallelism", true));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("2.5", DisplayName = "fraction")]
    [DataRow("3000000000", DisplayName = "beyond Int32")]
    [DataRow("true", DisplayName = "boolean")]
    public void Parse_NonInt32Number_IsIgnoredForAnIntegerSetting(string value)
    {
        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse("{ \"cleanup\": { \"maxDegreeOfParallelism\": " + value + " } }");

        Assert.AreEqual(0, overrides.Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Parse_LargePolicyWithinTheLimit_IsRead()
    {
        string padding = new string(' ', 1024 * 1024);

        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": {" + padding + "\"convertToVarWhenApparent\": true, \"fileHeaderCSharp\": \"" + new string('x', 100000) + "\" } }");

        Assert.IsTrue(overrides.TryGetBoolean("Cleaning_ConvertToVarWhenApparent", false));
        Assert.AreEqual(100000, overrides.TryGetString("Cleaning_UpdateFileHeaderCSharp", null).Length);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Parse_PolicyLargerThanFourMegabytes_IsIgnoredAsAWhole()
    {
        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": { \"convertToVarWhenApparent\": true, \"fileHeaderCSharp\": \"" + new string('x', 4 * 1024 * 1024) + "\" } }");

        Assert.AreEqual(0, overrides.Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Parse_DeeplyNestedUnknownValue_IgnoresThePolicyAsAWhole()
    {
        string nested = new string('[', 200) + new string(']', 200);

        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": { \"convertToVarWhenApparent\": true, \"unknown\": " + nested + " } }");

        Assert.AreEqual(0, overrides.Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Parse_GroupAliasWithoutIndividualKeys_SetsEveryMemberOfTheGroup()
    {
        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": { \"insertExplicitAccessModifiers\": true, \"insertBlankLinePadding\": true } }");

        // The alias covers every Before/After padding setting except the single-line fields/properties and single-line
        // comments; the "Between" setting for property accessors is a separate switch.
        string[] notInTheAlias =
        {
            "Cleaning_InsertBlankLinePaddingBeforeFieldsSingleLine", "Cleaning_InsertBlankLinePaddingAfterFieldsSingleLine",
            "Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine", "Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine",
            "Cleaning_InsertBlankLinePaddingBeforeSingleLineComments",
        };
        string[] expectedPadding = Settings.Default.Properties.Cast<System.Configuration.SettingsProperty>()
            .Select(property => property.Name)
            .Where(name => (name.StartsWith("Cleaning_InsertBlankLinePaddingBefore", System.StringComparison.Ordinal)
                    || name.StartsWith("Cleaning_InsertBlankLinePaddingAfter", System.StringComparison.Ordinal))
                && !notInTheAlias.Contains(name))
            .ToArray();

        Assert.AreSequenceEqual(
            new[] { "Cleaning_InsertExplicitAccessModifiers" }.Concat(expectedPadding).ToArray(), overrides.Values.Keys.ToArray(), Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
        Assert.IsTrue(overrides.Values.Values.All(value => value is bool flag && flag));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("Sample\0.cs", DisplayName = "null character")]
    [DataRow("", DisplayName = "empty")]
    [DataRow(null, DisplayName = "null")]
    public void LoadForFile_InvalidPath_ReturnsEmpty(string filePath)
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".codejanitor"), "{ \"cleanup\": { \"convertToVarWhenApparent\": true } }");

        Assert.AreEqual(0, RepositoryCleanupSettings.LoadForFile(filePath).Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void LoadForFile_PrimaryFileName_WinsOverTheAlternateInTheSameDirectory()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".codejanitor"), "{ \"cleanup\": { \"convertToVarWhenApparent\": true } }");
        File.WriteAllText(Path.Combine(_tempDirectory, ".code-janitor.json"), "{ \"cleanup\": { \"convertToVarWhenApparent\": false } }");

        Assert.IsTrue(RepositoryCleanupSettings.LoadForFile(Path.Combine(_tempDirectory, "Sample.cs")).TryGetBoolean("Cleaning_ConvertToVarWhenApparent", false));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void LoadForFile_DirectoryNamedLikeThePolicy_IsNotAPolicy()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".codejanitor"), "{ \"cleanup\": { \"convertToVarWhenApparent\": true } }");
        DirectoryInfo nested = Directory.CreateDirectory(Path.Combine(_tempDirectory, "src"));
        Directory.CreateDirectory(Path.Combine(nested.FullName, ".codejanitor"));

        Assert.IsTrue(RepositoryCleanupSettings.LoadForFile(Path.Combine(nested.FullName, "Sample.cs")).TryGetBoolean("Cleaning_ConvertToVarWhenApparent", false));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void LoadForFile_NearestPolicyIsInvalid_DoesNotFallBackToAParentPolicy()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".codejanitor"), "{ \"cleanup\": { \"convertToVarWhenApparent\": true } }");
        DirectoryInfo nested = Directory.CreateDirectory(Path.Combine(_tempDirectory, "src"));
        File.WriteAllText(Path.Combine(nested.FullName, ".codejanitor"), "{ not json");

        Assert.AreEqual(0, RepositoryCleanupSettings.LoadForFile(Path.Combine(nested.FullName, "Sample.cs")).Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void LoadForFile_PolicyLockedByAnotherWriter_ReturnsEmpty()
    {
        string policyPath = Path.Combine(_tempDirectory, ".codejanitor");
        File.WriteAllText(policyPath, "{ \"cleanup\": { \"convertToVarWhenApparent\": true } }");

        using (new FileStream(policyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.AreEqual(0, RepositoryCleanupSettings.LoadForFile(Path.Combine(_tempDirectory, "Sample.cs")).Count);
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("", DisplayName = "empty header")]
    [DataRow(null, DisplayName = "null header")]
    [DataRow("// C:\\src\\Project\r\n// Line 2\n//\tTabbed", DisplayName = "backslashes, CRLF, LF and tab")]
    [DataRow("// \u0001 bell \u001f unit \u007f del \u00e9 \u4e2d \"q\"", DisplayName = "control and non-ASCII characters")]
    public void BuildJson_FileHeader_RoundTripsExactly(string header)
    {
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = header;

        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(RepositoryCleanupSettings.BuildJson(Settings.Default));

        Assert.AreEqual(header ?? string.Empty, overrides.TryGetString("Cleaning_UpdateFileHeaderCSharp", "missing"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void BuildJson_WithoutCodeStyleRules_ExportsAnEmptyObject_AndDefaultEnums()
    {
        Settings.Default.Cleaning_CodeStyleRules = null;
        Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition = 0;
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = 0;

        string json = RepositoryCleanupSettings.BuildJson(Settings.Default);
        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(json);

        Assert.Contains("\"codeStyle\": {}", json);
        Assert.Contains("\"removeRegions\": true", json);
        Assert.DoesNotContain("organizeUsings", json);
        Assert.IsEmpty(overrides.CodeStyle);
        Assert.IsTrue(overrides.RemoveRegions);
        Assert.AreEqual(0, overrides.TryGetInt32("Cleaning_UpdateFileHeader_HeaderPosition", -1));
        Assert.AreEqual(0, overrides.TryGetInt32("Cleaning_UpdateFileHeader_HeaderUpdateMode", -1));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void BuildJson_ThenApplyToSettings_RestoresEveryExportedSetting()
    {
        Dictionary<string, object> exported = new Dictionary<string, object>();
        foreach (System.Configuration.SettingsProperty property in Settings.Default.Properties)
        {
            if (property.PropertyType == typeof(bool) && property.Name.StartsWith("Cleaning_", StringComparison.Ordinal))
            {
                Settings.Default[property.Name] = !(bool)Settings.Default[property.Name];
            }
        }

        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// Exported";
        Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition = 1;
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = 1;
        Settings.Default.Cleaning_CodeStyleRules = "csharp_prefer_braces=when_multiline";
        RepositoryCleanupOverrides overrides = RepositoryCleanupSettings.Parse(RepositoryCleanupSettings.BuildJson(Settings.Default));
        foreach (KeyValuePair<string, object> value in overrides.Values)
        {
            exported[value.Key] = Settings.Default[value.Key];
        }

        Settings.Default.Reset();
        int applied = RepositoryCleanupSettings.ApplyToSettings(overrides, Settings.Default);

        Assert.AreEqual(overrides.Count + 1, applied);
        Assert.IsGreaterThan(50, exported.Count);
        foreach (KeyValuePair<string, object> value in exported)
        {
            Assert.AreEqual(value.Value, Settings.Default[value.Key], value.Key);
        }

        Assert.AreEqual("csharp_prefer_braces=when_multiline", Settings.Default.Cleaning_CodeStyleRules);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyToSettings_SkipsValuesWithoutAVisualStudioSetting_AndLeavesCodeStyleRulesAloneWhenThePolicyHasNone()
    {
        Settings.Default.Cleaning_CodeStyleRules = "csharp_prefer_braces=true";
        RepositoryCleanupOverrides overrides = new RepositoryCleanupOverrides(
            new Dictionary<string, object> { ["Cleaning_NoSuchSetting"] = true, ["Cleaning_ConvertToVarWhenApparent"] = true },
            removeRegions: null,
            organizeUsings: null);

        int applied = RepositoryCleanupSettings.ApplyToSettings(overrides, Settings.Default);

        Assert.AreEqual(1, applied);
        Assert.IsTrue(Settings.Default.Cleaning_ConvertToVarWhenApparent);
        Assert.AreEqual("csharp_prefer_braces=true", Settings.Default.Cleaning_CodeStyleRules);
    }
}
