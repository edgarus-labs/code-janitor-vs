using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using Microsoft.CodeAnalysis.CSharp;
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
        CSharpLanguageVersionSupport.SetLanguageVersionResolver(_ => new[] { LanguageVersion.CSharp12 });
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Settings.Default.Reset();
        CSharpLanguageVersionSupport.SetLanguageVersionResolver(null);

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
            "{ \"cleanup\": { \"insertBlankLinePadding\": false, \"insertBlankLinePaddingBeforeClasses\": true, \"insertExplicitAccessModifiers\": false } }");

        // Alias fans out to the group members.
        Assert.IsFalse(overrides.TryGetBoolean("Cleaning_InsertBlankLinePaddingAfterMethods", true));
        Assert.IsFalse(overrides.TryGetBoolean("Cleaning_InsertExplicitAccessModifiersOnMethods", true));

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

        CollectionAssert.AreEquivalent(
            new Dictionary<string, string>
            {
                ["csharp_preferred_modifier_order"] = "public,static",
                ["csharp_prefer_braces"] = "when_multiline",
            },
            overrides.CodeStyle.ToDictionary(entry => entry.Key, entry => entry.Value));
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

        CollectionAssert.AreEquivalent(
            new Dictionary<string, string> { ["csharp_prefer_braces"] = "true" },
            overrides.CodeStyle.ToDictionary(entry => entry.Key, entry => entry.Value));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyHeadlessCSharpTransformations_HonorsRepositoryOverride()
    {
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = false;
        File.WriteAllText(Path.Combine(_tempDirectory, ".codejanitor"),
            "{ \"cleanup\": { \"convertToFileScopedNamespace\": true } }");

        string filePath = Path.Combine(_tempDirectory, "Sample.cs");
        string input = "namespace Demo\r\n{\r\n    public class C\r\n    {\r\n    }\r\n}\r\n";

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.Contains("namespace Demo;", output);
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
}
