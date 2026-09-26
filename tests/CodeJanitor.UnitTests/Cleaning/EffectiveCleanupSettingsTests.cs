using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using System;
using System.IO;
using System.Linq;

namespace CodeJanitor.UnitTests.Cleaning;

/// <summary>
/// Behavioral tests for <see cref="EffectiveCleanupSettings" />: .editorconfig wins for every mapped key, the
/// repository policy (.codejanitor) wins otherwise, and the user's Visual Studio settings apply last.
/// </summary>
[TestClass]
public sealed class EffectiveCleanupSettingsTests
{
    private static readonly string[] ExplicitAccessModifierSettings =
    {
        "Cleaning_InsertExplicitAccessModifiersOnClasses",
        "Cleaning_InsertExplicitAccessModifiersOnDelegates",
        "Cleaning_InsertExplicitAccessModifiersOnEnumerations",
        "Cleaning_InsertExplicitAccessModifiersOnEvents",
        "Cleaning_InsertExplicitAccessModifiersOnFields",
        "Cleaning_InsertExplicitAccessModifiersOnInterfaces",
        "Cleaning_InsertExplicitAccessModifiersOnMethods",
        "Cleaning_InsertExplicitAccessModifiersOnProperties",
        "Cleaning_InsertExplicitAccessModifiersOnStructs",
    };

    private string _tempDirectory;
    private string _filePath;

    [TestInitialize]
    public void TestInitialize()
    {
        Settings.Default.Reset();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _filePath = Path.Combine(_tempDirectory, "Sample.cs");

        // Isolate every test from configuration files above the temp directory: an empty root .editorconfig and an
        // empty repository policy (the nearest policy file wins).
        WriteRootEditorConfig();
        WritePolicy(string.Empty);
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
    public void GetBoolean_EditorConfigValue_BeatsRepositoryPolicyAndUserSetting()
    {
        Settings.Default.Cleaning_ConvertToVarWhenApparent = true;
        WritePolicy("\"convertToVarWhenApparent\": true");
        WriteRootEditorConfig("csharp_style_var_when_type_is_apparent = false:warning");

        Assert.IsFalse(EffectiveCleanupSettings.For(_filePath).GetBoolean("Cleaning_ConvertToVarWhenApparent"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void GetBoolean_EditorConfigSilentOnTheKey_UsesRepositoryPolicyOverUserSetting()
    {
        Settings.Default.Cleaning_MakeFieldsReadonlyWhenSafe = false;
        WritePolicy("\"makeFieldsReadonlyWhenSafe\": true");
        WriteRootEditorConfig("csharp_style_var_when_type_is_apparent = true");

        Assert.IsTrue(EffectiveCleanupSettings.For(_filePath).GetBoolean("Cleaning_MakeFieldsReadonlyWhenSafe"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void GetBoolean_NeitherFileDefinesTheKey_UsesUserSetting()
    {
        Settings.Default.Cleaning_MakeFieldsReadonlyWhenSafe = true;
        WritePolicy("\"convertToVarWhenApparent\": false");
        WriteRootEditorConfig("csharp_style_var_when_type_is_apparent = true");

        Assert.IsTrue(EffectiveCleanupSettings.For(_filePath).GetBoolean("Cleaning_MakeFieldsReadonlyWhenSafe"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(null, DisplayName = "null path")]
    [DataRow("", DisplayName = "empty path")]
    [DataRow(@"C:\bad<|>path\Sample.cs", DisplayName = "path with invalid characters")]
    public void For_BlankOrInvalidPath_UsesUserSettingsOnly(string filePath)
    {
        Settings.Default.Cleaning_ConvertToVarWhenApparent = true;
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = true;
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = false;

        var settings = EffectiveCleanupSettings.For(filePath);

        Assert.IsTrue(settings.GetBoolean("Cleaning_ConvertToVarWhenApparent"));
        Assert.AreEqual(NamespaceDeclarationPreference.FileScoped, settings.NamespaceDeclarations);
        Assert.AreEqual(UsingDirectivePlacementPreference.Unchanged, settings.UsingDirectivePlacement);
        Assert.AreEqual(IndentationPreference.Unchanged, settings.Indentation);
        Assert.AreEqual(4, settings.TabSize);
        Assert.IsTrue(settings.InsertFinalNewline);
        Assert.IsTrue(settings.RemovesRegions);
        Assert.IsFalse(settings.OrganizeUsings);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void NamespaceDeclarations_FileScopedInEditorConfig_OverridesDisabledUserSetting()
    {
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = false;
        WriteRootEditorConfig("csharp_style_namespace_declarations = file_scoped:silent");

        var settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(NamespaceDeclarationPreference.FileScoped, settings.NamespaceDeclarations);
        Assert.IsTrue(settings.GetBoolean("Cleaning_ConvertToFileScopedNamespace"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void NamespaceDeclarations_BlockScopedInEditorConfig_BeatsRepositoryPolicyAndUserSetting()
    {
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = true;
        WritePolicy("\"convertToFileScopedNamespace\": true");
        WriteRootEditorConfig("csharp_style_namespace_declarations = block_scoped:warning");

        var settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(NamespaceDeclarationPreference.BlockScoped, settings.NamespaceDeclarations);
        Assert.IsFalse(settings.GetBoolean("Cleaning_ConvertToFileScopedNamespace"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(true, false, NamespaceDeclarationPreference.FileScoped, DisplayName = "policy enables conversion")]
    [DataRow(false, true, NamespaceDeclarationPreference.Unchanged, DisplayName = "policy disables conversion")]
    public void NamespaceDeclarations_EditorConfigSilent_RepositoryPolicyBeatsUserSetting(bool policy, bool userSetting, object expected)
    {
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = userSetting;
        WritePolicy($"\"convertToFileScopedNamespace\": {(policy ? "true" : "false")}");

        Assert.AreEqual(expected, EffectiveCleanupSettings.For(_filePath).NamespaceDeclarations);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("inside_namespace", true, UsingDirectivePlacementPreference.InsideNamespace, false)]
    [DataRow("outside_namespace:suggestion", false, UsingDirectivePlacementPreference.OutsideNamespace, true)]
    public void UsingDirectivePlacement_EditorConfigValue_BeatsUserSetting(
        string option,
        bool userSetting,
        object expected,
        bool expectedMoveOutside)
    {
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = userSetting;
        WriteRootEditorConfig($"csharp_using_directive_placement = {option}");

        var settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(expected, settings.UsingDirectivePlacement);
        Assert.AreEqual(expectedMoveOutside, settings.GetBoolean("Cleaning_MoveUsingsOutsideNamespace"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(null, true, UsingDirectivePlacementPreference.OutsideNamespace, DisplayName = "user setting enabled")]
    [DataRow(null, false, UsingDirectivePlacementPreference.Unchanged, DisplayName = "user setting disabled")]
    [DataRow(false, true, UsingDirectivePlacementPreference.Unchanged, DisplayName = "policy disables the move")]
    [DataRow(true, false, UsingDirectivePlacementPreference.OutsideNamespace, DisplayName = "policy enables the move")]
    public void UsingDirectivePlacement_EditorConfigSilent_RepositoryPolicyBeatsUserSetting(bool? policy, bool userSetting, object expected)
    {
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = userSetting;
        if (policy.HasValue)
        {
            WritePolicy($"\"moveUsingsOutsideNamespace\": {(policy.Value ? "true" : "false")}");
        }

        Assert.AreEqual(expected, EffectiveCleanupSettings.For(_filePath).UsingDirectivePlacement);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(new[] { "indent_style = tab", "tab_width = 8", "indent_size = 2" }, IndentationPreference.Tabs, 8, DisplayName = "tab_width wins over indent_size")]
    [DataRow(new[] { "indent_style = space", "indent_size = 2" }, IndentationPreference.Spaces, 2, DisplayName = "indent_size without tab_width")]
    [DataRow(new[] { "indent_style = tab", "indent_size = tab" }, IndentationPreference.Tabs, 4, DisplayName = "indent_size = tab without tab_width")]
    [DataRow(new[] { "indent_size = 3" }, IndentationPreference.Unchanged, 3, DisplayName = "no indent_style")]
    [DataRow(new[] { "indent_style = tab", "tab_width = 0" }, IndentationPreference.Tabs, 4, DisplayName = "non-positive tab_width ignored")]
    public void Indentation_ComesFromEditorConfigOnly(string[] options, object expected, int expectedTabSize)
    {
        WriteRootEditorConfig(options);

        var settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(expected, settings.Indentation);
        Assert.AreEqual(expectedTabSize, settings.TabSize);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(new[] { "indent_size = 4", "tab_width = 8" }, 4, 8, DisplayName = "indent_size with a wider tab_width")]
    [DataRow(new[] { "indent_size = tab", "tab_width = 8" }, 8, 8, DisplayName = "indent_size = tab uses tab_width")]
    [DataRow(new[] { "indent_size = tab" }, 4, 4, DisplayName = "indent_size = tab without tab_width")]
    [DataRow(new[] { "tab_width = 8" }, 4, 8, DisplayName = "tab_width without indent_size")]
    public void IndentSize_ComesFromIndentSize_NotFromTabWidth(string[] options, int expectedIndentSize, int expectedTabSize)
    {
        WriteRootEditorConfig(options);

        var settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(expectedIndentSize, settings.IndentSize);
        Assert.AreEqual(expectedTabSize, settings.TabSize);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void NamespaceConverter_MovesTheBodyByIndentSize_NotByTabWidth()
    {
        WriteRootEditorConfig("indent_size = 4", "tab_width = 8");
        var converter = FileScopedNamespaceLogic.CreateConverter(EffectiveCleanupSettings.For(_filePath));
        var blockScoped = "namespace N\n{\n    public class C\n    {\n        void M() {}\n    }\n}\n";
        var fileScoped = "namespace N;\n\npublic class C\n{\n    void M() {}\n}\n";

        Assert.AreEqual(fileScoped, converter.ConvertToFileScoped(blockScoped));
        Assert.AreEqual(blockScoped, converter.ConvertToBlockScoped(fileScoped));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("false", false, false, true)]
    [DataRow("true", true, true, false)]
    public void InsertFinalNewline_EditorConfigValue_DrivesInsertAndRemoveSettings(
        string option,
        bool expectedInsertFinalNewline,
        bool expectedInsertSetting,
        bool expectedRemoveSetting)
    {
        Settings.Default.Cleaning_InsertEndOfFileTrailingNewLine = !expectedInsertSetting;
        Settings.Default.Cleaning_RemoveEndOfFileTrailingNewLine = !expectedRemoveSetting;
        WriteRootEditorConfig($"insert_final_newline = {option}");

        var settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(expectedInsertFinalNewline, settings.InsertFinalNewline);
        Assert.AreEqual(expectedInsertSetting, settings.GetBoolean("Cleaning_InsertEndOfFileTrailingNewLine"));
        Assert.AreEqual(expectedRemoveSetting, settings.GetBoolean("Cleaning_RemoveEndOfFileTrailingNewLine"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void InsertFinalNewline_WithoutEditorConfig_KeepsEnsuringFinalNewlineRegardlessOfUserSettings()
    {
        Settings.Default.Cleaning_InsertEndOfFileTrailingNewLine = false;
        Settings.Default.Cleaning_RemoveEndOfFileTrailingNewLine = true;

        Assert.IsTrue(EffectiveCleanupSettings.For(_filePath).InsertFinalNewline);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("false", true, false)]
    [DataRow("true", false, true)]
    public void TrimTrailingWhitespace_EditorConfigValue_BeatsUserSetting(string option, bool userSetting, bool expected)
    {
        Settings.Default.Cleaning_RemoveEndOfLineWhitespace = userSetting;
        WriteRootEditorConfig($"trim_trailing_whitespace = {option}");

        Assert.AreEqual(expected, EffectiveCleanupSettings.For(_filePath).GetBoolean("Cleaning_RemoveEndOfLineWhitespace"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("true", "false", null, true, DisplayName = "System first, groups not separated")]
    [DataRow("true", null, false, true, DisplayName = "System first beats policy opt-out")]
    [DataRow("true", "true", true, false, DisplayName = "separated groups beat policy")]
    [DataRow("false", null, true, false, DisplayName = "System not first beats policy")]
    [DataRow("true:none", "true:none", true, true, DisplayName = "none is ignored, policy decides")]
    [DataRow(null, "false", true, true, DisplayName = "sort order undefined falls back to policy")]
    [DataRow(null, null, true, true, DisplayName = "editorconfig silent uses policy")]
    [DataRow(null, null, null, false, DisplayName = "nothing configured")]
    [DataRow("true:none", null, null, false, DisplayName = "System first ignored by none, no policy")]
    [DataRow("true:none", null, false, false, DisplayName = "System first ignored by none, policy opt-out")]
    public void OrganizeUsings_EditorConfigDecidesWhenItDefinesTheSortOrder(
        string sortSystemDirectivesFirst,
        string separateImportDirectiveGroups,
        bool? policy,
        bool expected)
    {
        WriteRootEditorConfig(
            sortSystemDirectivesFirst is null ? null : $"dotnet_sort_system_directives_first = {sortSystemDirectivesFirst}",
            separateImportDirectiveGroups is null ? null : $"dotnet_separate_import_directive_groups = {separateImportDirectiveGroups}");
        if (policy.HasValue)
        {
            WritePolicy($"\"organizeUsings\": {(policy.Value ? "true" : "false")}");
        }

        Assert.AreEqual(expected, EffectiveCleanupSettings.For(_filePath).OrganizeUsings);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("csharp_style_var_when_type_is_apparent = true:suggestion", "Cleaning_ConvertToVarWhenApparent", true)]
    [DataRow("csharp_style_var_when_type_is_apparent = false", "Cleaning_ConvertToVarWhenApparent", false)]
    [DataRow("csharp_style_inlined_variable_declaration = false:warning", "Cleaning_InlineOutVariableDeclarations", false)]
    [DataRow("csharp_style_inlined_variable_declaration = true", "Cleaning_InlineOutVariableDeclarations", true)]
    [DataRow("dotnet_style_readonly_field = true:warning", "Cleaning_MakeFieldsReadonlyWhenSafe", true)]
    [DataRow("dotnet_style_readonly_field = false", "Cleaning_MakeFieldsReadonlyWhenSafe", false)]
    [DataRow("dotnet_style_prefer_collection_expression = true", "Cleaning_ConvertToCollectionExpressions", true)]
    [DataRow("dotnet_style_prefer_collection_expression = when_types_exactly_match", "Cleaning_ConvertToCollectionExpressions", true)]
    [DataRow("dotnet_style_prefer_collection_expression = when_types_loosely_match:suggestion", "Cleaning_ConvertToCollectionExpressions", true)]
    [DataRow("dotnet_style_prefer_collection_expression = false", "Cleaning_ConvertToCollectionExpressions", false)]
    [DataRow("dotnet_style_prefer_collection_expression = never", "Cleaning_ConvertToCollectionExpressions", false)]
    public void GetBoolean_CodeStyleOption_MapsToCleanupSetting(string option, string settingName, bool expected)
    {
        Settings.Default[settingName] = !expected;
        WritePolicy($"\"{char.ToLowerInvariant(settingName["Cleaning_".Length])}{settingName.Substring("Cleaning_".Length + 1)}\": {(expected ? "false" : "true")}");
        WriteRootEditorConfig(option);

        Assert.AreEqual(expected, EffectiveCleanupSettings.For(_filePath).GetBoolean(settingName));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("always", true)]
    [DataRow("for_non_interface_members:warning", true)]
    [DataRow("never", false)]
    [DataRow("omit_if_default:silent", false)]
    public void GetBoolean_RequireAccessibilityModifiers_DrivesEveryExplicitAccessModifierSetting(string option, bool expected)
    {
        foreach (var settingName in ExplicitAccessModifierSettings)
        {
            Settings.Default[settingName] = !expected;
        }

        WriteRootEditorConfig($"dotnet_style_require_accessibility_modifiers = {option}");
        var settings = EffectiveCleanupSettings.For(_filePath);

        foreach (var settingName in ExplicitAccessModifierSettings)
        {
            Assert.AreEqual(expected, settings.GetBoolean(settingName), settingName);
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void GetString_FileHeaderTemplate_BuildsCommentHeaderFromEscapesAndFileName()
    {
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// User header";
        WritePolicy("\"fileHeaderCSharp\": \"// Policy header\"");
        WriteRootEditorConfig(@"file_header_template = Copyright: Contoso Ltd.\n\n{fileName} is licensed under MIT.");

        var header = EffectiveCleanupSettings.For(_filePath).GetString("Cleaning_UpdateFileHeaderCSharp");

        Assert.AreEqual(
            string.Join(Environment.NewLine, "// Copyright: Contoso Ltd.", "//", "// Sample.cs is licensed under MIT."),
            header);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void GetString_FileHeaderTemplateUnset_ClearsRepositoryAndUserHeader()
    {
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// User header";
        WritePolicy("\"fileHeaderCSharp\": \"// Policy header\"");
        WriteRootEditorConfig("file_header_template = unset");

        Assert.AreEqual(string.Empty, EffectiveCleanupSettings.For(_filePath).GetString("Cleaning_UpdateFileHeaderCSharp"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void NoneSeverity_IgnoresTheOption_SoThePolicyAndThenTheUserSettingsDecide()
    {
        // The policy defines some of the steps; the user settings decide the others.
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = true;
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = true;
        Settings.Default.Cleaning_ConvertToVarWhenApparent = true;
        Settings.Default.Cleaning_InsertEndOfFileTrailingNewLine = true;
        Settings.Default.Cleaning_RemoveEndOfFileTrailingNewLine = false;
        WritePolicy("\"convertToFileScopedNamespace\": false, \"convertToVarWhenApparent\": false");
        WriteRootEditorConfig(
            "csharp_style_namespace_declarations = file_scoped:none",
            "csharp_using_directive_placement = inside_namespace:none",
            "csharp_style_var_when_type_is_apparent = true:none",
            "indent_style = tab:none",
            "insert_final_newline = false:none");

        var settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(NamespaceDeclarationPreference.Unchanged, settings.NamespaceDeclarations, "policy");
        Assert.IsFalse(settings.GetBoolean("Cleaning_ConvertToFileScopedNamespace"), "policy");
        Assert.IsFalse(settings.GetBoolean("Cleaning_ConvertToVarWhenApparent"), "policy");
        Assert.AreEqual(UsingDirectivePlacementPreference.OutsideNamespace, settings.UsingDirectivePlacement, "user setting");
        Assert.IsTrue(settings.GetBoolean("Cleaning_InsertEndOfFileTrailingNewLine"), "user setting");
        Assert.IsFalse(settings.GetBoolean("Cleaning_RemoveEndOfFileTrailingNewLine"), "user setting");
        Assert.IsTrue(settings.InsertFinalNewline);
        Assert.AreEqual(IndentationPreference.Unchanged, settings.Indentation);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("", DisplayName = "no suffix")]
    [DataRow(":silent")]
    [DataRow(":refactoring")]
    [DataRow(":suggestion")]
    [DataRow(":warning")]
    [DataRow(":error")]
    [DataRow(" : Warning", DisplayName = "spaced mixed-case suffix")]
    public void EnforcingSeverity_AppliesTheValue(string suffix)
    {
        Settings.Default.Cleaning_InlineOutVariableDeclarations = true;
        WriteRootEditorConfig($"csharp_style_inlined_variable_declaration = false{suffix}");

        Assert.IsFalse(EffectiveCleanupSettings.For(_filePath).GetBoolean("Cleaning_InlineOutVariableDeclarations"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("sometimes", DisplayName = "unknown value")]
    [DataRow("block_scoped:loud", DisplayName = "unknown severity")]
    [DataRow("block_scoped:none:warning", DisplayName = "two severities")]
    public void UnknownOptionValue_IsIgnored_AndFallsBackToRepositoryPolicy(string option)
    {
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = false;
        WritePolicy("\"convertToFileScopedNamespace\": true");
        WriteRootEditorConfig($"csharp_style_namespace_declarations = {option}");

        var settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(NamespaceDeclarationPreference.FileScoped, settings.NamespaceDeclarations);
        Assert.IsTrue(settings.GetBoolean("Cleaning_ConvertToFileScopedNamespace"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void UnknownBooleanValue_IsIgnored_AndFallsBackToUserSetting()
    {
        Settings.Default.Cleaning_ConvertToVarWhenApparent = true;
        WriteRootEditorConfig("csharp_style_var_when_type_is_apparent = maybe:warning");

        Assert.IsTrue(EffectiveCleanupSettings.For(_filePath).GetBoolean("Cleaning_ConvertToVarWhenApparent"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void NestedEditorConfig_OverridesParentKey_AndInheritsTheOthers()
    {
        WriteRootEditorConfig(
            "trim_trailing_whitespace = true",
            "csharp_style_var_when_type_is_apparent = true");
        var nested = Directory.CreateDirectory(Path.Combine(_tempDirectory, "src", "App"));
        File.WriteAllText(Path.Combine(_tempDirectory, "src", ".editorconfig"), "[*.cs]\r\ntrim_trailing_whitespace = false\r\n");

        var settings = EffectiveCleanupSettings.For(Path.Combine(nested.FullName, "Sample.cs"));

        Assert.IsFalse(settings.GetBoolean("Cleaning_RemoveEndOfLineWhitespace"));
        Assert.IsTrue(settings.GetBoolean("Cleaning_ConvertToVarWhenApparent"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void RootEditorConfig_HidesEditorConfigsAboveIt()
    {
        Settings.Default.Cleaning_ConvertToVarWhenApparent = false;
        WriteRootEditorConfig("csharp_style_var_when_type_is_apparent = true");
        var nested = Directory.CreateDirectory(Path.Combine(_tempDirectory, "src"));
        File.WriteAllText(Path.Combine(nested.FullName, ".editorconfig"), "root = true\r\n\r\n[*.cs]\r\ntrim_trailing_whitespace = false\r\n");

        var settings = EffectiveCleanupSettings.For(Path.Combine(nested.FullName, "Sample.cs"));

        Assert.IsFalse(settings.GetBoolean("Cleaning_ConvertToVarWhenApparent"), "The parent .editorconfig must not apply below a root .editorconfig.");
        Assert.IsFalse(settings.GetBoolean("Cleaning_RemoveEndOfLineWhitespace"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void SectionGlobs_MatchPathsRelativeToTheEditorConfigDirectory()
    {
        // Pin the user fallbacks so only .editorconfig sections can make these settings true.
        Settings.Default.Cleaning_ConvertToVarWhenApparent = false;
        Settings.Default.Cleaning_MakeFieldsReadonlyWhenSafe = false;
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"), string.Join("\r\n",
            "root = true",
            "[*.cs]",
            "trim_trailing_whitespace = false",
            "[src/**.cs]",
            "csharp_style_var_when_type_is_apparent = true",
            "[*.vb]",
            "dotnet_style_readonly_field = true",
            string.Empty));
        var source = Directory.CreateDirectory(Path.Combine(_tempDirectory, "src", "App"));
        var tests = Directory.CreateDirectory(Path.Combine(_tempDirectory, "tests"));

        var inSource = EffectiveCleanupSettings.For(Path.Combine(source.FullName, "Sample.cs"));
        var inTests = EffectiveCleanupSettings.For(Path.Combine(tests.FullName, "Sample.cs"));

        Assert.IsFalse(inSource.GetBoolean("Cleaning_RemoveEndOfLineWhitespace"));
        Assert.IsTrue(inSource.GetBoolean("Cleaning_ConvertToVarWhenApparent"));
        Assert.IsFalse(inSource.GetBoolean("Cleaning_MakeFieldsReadonlyWhenSafe"), "A [*.vb] section must not apply to C# files.");
        Assert.IsFalse(inTests.GetBoolean("Cleaning_RemoveEndOfLineWhitespace"));
        Assert.IsFalse(inTests.GetBoolean("Cleaning_ConvertToVarWhenApparent"), "[src/**.cs] must not match files outside src.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void For_EditedEditorConfig_IsReadAgain()
    {
        WriteRootEditorConfig("csharp_style_var_when_type_is_apparent = true");
        Assert.IsTrue(EffectiveCleanupSettings.For(_filePath).GetBoolean("Cleaning_ConvertToVarWhenApparent"));

        WriteRootEditorConfig("csharp_style_var_when_type_is_apparent = false:warning");

        Assert.IsFalse(EffectiveCleanupSettings.For(_filePath).GetBoolean("Cleaning_ConvertToVarWhenApparent"));
    }

    /// <summary>
    /// Writes the root .editorconfig of the test directory with the given options in a [*.cs] section; null options are skipped.
    /// </summary>
    private void WriteRootEditorConfig(params string[] csharpOptions)
    {
        var lines = new[] { "root = true", string.Empty, "[*.cs]" }
            .Concat(csharpOptions.Where(option => option is not null))
            .Concat(new[] { string.Empty });

        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"), string.Join("\r\n", lines));
    }

    /// <summary>
    /// Writes the repository policy of the test directory with the given cleanup entries.
    /// </summary>
    private void WritePolicy(string cleanupEntries)
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".codejanitor"), "{ \"cleanup\": { " + cleanupEntries + " } }");
    }
}
