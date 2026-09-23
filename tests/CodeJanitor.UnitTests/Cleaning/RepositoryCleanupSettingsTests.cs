using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using System;
using System.IO;

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
        var overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": { \"convertToVarWhenApparent\": true, \"removeEndOfLineWhitespace\": false } }");

        Assert.IsTrue(overrides.TryGetBoolean("Cleaning_ConvertToVarWhenApparent", false));
        Assert.IsFalse(overrides.TryGetBoolean("Cleaning_RemoveEndOfLineWhitespace", true));
        Assert.AreEqual(2, overrides.Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Parse_AppliesGroupAliases_AndIndividualKeysWin()
    {
        var overrides = RepositoryCleanupSettings.Parse(
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
        var fromInvalidJson = RepositoryCleanupSettings.Parse("{ this is not json");
        Assert.AreEqual(0, fromInvalidJson.Count);

        var fromMissingSection = RepositoryCleanupSettings.Parse("{ \"other\": {} }");
        Assert.AreEqual(0, fromMissingSection.Count);

        var overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": { \"unknownKey\": true, \"convertToVarWhenApparent\": \"yes\", \"removeRegions\": \"no\" } }");
        Assert.AreEqual(0, overrides.Count);
        Assert.IsNull(overrides.RemoveRegions);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Parse_MapsHeaderEnumsAndText()
    {
        var overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": { \"fileHeaderCSharp\": \"// Copyright\", \"fileHeaderPosition\": \"afterUsings\", \"fileHeaderUpdateMode\": \"replace\" } }");

        Assert.AreEqual("// Copyright", overrides.TryGetString("Cleaning_UpdateFileHeaderCSharp", null));
        Assert.AreEqual(1, overrides.TryGetInt32("Cleaning_UpdateFileHeader_HeaderPosition", 0));
        Assert.AreEqual(1, overrides.TryGetInt32("Cleaning_UpdateFileHeader_HeaderUpdateMode", 0));

        var invalidEnum = RepositoryCleanupSettings.Parse("{ \"cleanup\": { \"fileHeaderPosition\": \"middle\" } }");
        Assert.AreEqual(0, invalidEnum.Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Parse_AcceptsVsCodeAliasForReturnAndThrowPadding()
    {
        var overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": { \"insertBlankLineBeforeReturnAndThrow\": true } }");

        Assert.IsTrue(overrides.TryGetBoolean("Cleaning_InsertBlankLineBeforeReturnAndThrowStatements", false));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Parse_ReadsRegionAndUsingOrganizationPolicies()
    {
        var overrides = RepositoryCleanupSettings.Parse(
            "{ \"cleanup\": { \"removeRegions\": false, \"organizeUsings\": true } }");

        Assert.AreEqual(false, overrides.RemoveRegions);
        Assert.AreEqual(true, overrides.OrganizeUsings);

        // Policies are not Visual Studio user settings and are not counted as overrides.
        Assert.AreEqual(0, overrides.Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void LoadForFile_WalksUpToNearestConfig()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".codejanitor"),
            "{ \"cleanup\": { \"convertToVarWhenApparent\": true } }");
        var nested = Directory.CreateDirectory(Path.Combine(_tempDirectory, "src", "App"));
        var filePath = Path.Combine(nested.FullName, "Sample.cs");

        var overrides = RepositoryCleanupSettings.LoadForFile(filePath);

        Assert.IsTrue(overrides.TryGetBoolean("Cleaning_ConvertToVarWhenApparent", false));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void LoadForFile_PrefersNearestDirectory_AndAlternateFileName()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".code-janitor.json"),
            "{ \"cleanup\": { \"convertToVarWhenApparent\": true } }");
        var nested = Directory.CreateDirectory(Path.Combine(_tempDirectory, "src"));
        File.WriteAllText(Path.Combine(nested.FullName, ".code-janitor.json"),
            "{ \"cleanup\": { \"convertToVarWhenApparent\": false } }");
        var filePath = Path.Combine(nested.FullName, "Sample.cs");

        var overrides = RepositoryCleanupSettings.LoadForFile(filePath);

        Assert.IsFalse(overrides.TryGetBoolean("Cleaning_ConvertToVarWhenApparent", true));
        Assert.IsTrue(RepositoryCleanupSettings.TryFindConfigFile(nested.FullName, out var configPath));
        StringAssert.EndsWith(configPath, ".code-janitor.json");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void LoadForFile_WithoutConfig_ReturnsEmpty()
    {
        var filePath = Path.Combine(_tempDirectory, "Sample.cs");

        var overrides = RepositoryCleanupSettings.LoadForFile(filePath);

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

        var json = RepositoryCleanupSettings.BuildJson(Settings.Default);
        var overrides = RepositoryCleanupSettings.Parse(json);

        Assert.IsTrue(overrides.TryGetBoolean("Cleaning_ConvertToVarWhenApparent", false));
        Assert.IsFalse(overrides.TryGetBoolean("Cleaning_RemoveEndOfLineWhitespace", true));
        Assert.AreEqual("// \"Quoted\" header", overrides.TryGetString("Cleaning_UpdateFileHeaderCSharp", null));
        Assert.AreEqual(1, overrides.TryGetInt32("Cleaning_UpdateFileHeader_HeaderPosition", 0));
        Assert.AreEqual(1, overrides.TryGetInt32("Cleaning_UpdateFileHeader_HeaderUpdateMode", 0));
        StringAssert.Contains(json, "\"cleanup\"");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyToSettings_AppliesOverrides()
    {
        var original = Settings.Default.Cleaning_ConvertToVarWhenApparent;
        try
        {
            Settings.Default.Cleaning_ConvertToVarWhenApparent = false;
            var overrides = RepositoryCleanupSettings.Parse(
                "{ \"cleanup\": { \"convertToVarWhenApparent\": true, \"organizeUsings\": true } }");

            var applied = RepositoryCleanupSettings.ApplyToSettings(overrides, Settings.Default);

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
    public void ApplyHeadlessCSharpTransformations_HonorsRepositoryOverride()
    {
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = false;
        File.WriteAllText(Path.Combine(_tempDirectory, ".codejanitor"),
            "{ \"cleanup\": { \"convertToFileScopedNamespace\": true } }");

        var filePath = Path.Combine(_tempDirectory, "Sample.cs");
        var input = "namespace Demo\r\n{\r\n    public class C\r\n    {\r\n    }\r\n}\r\n";

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        StringAssert.Contains(output, "namespace Demo;");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ApplyHeadlessCSharpTransformations_RepositoryPolicyCanKeepRegions()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".codejanitor"),
            "{ \"cleanup\": { \"removeRegions\": false } }");

        var filePath = Path.Combine(_tempDirectory, "Sample.cs");
        var input = "namespace Demo;\r\n\r\n#region Helpers\r\npublic class C { }\r\n#endregion\r\n";

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        StringAssert.Contains(output, "#region Helpers");
    }
}
