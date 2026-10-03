using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
    [DataRow("Sample\0.cs", DisplayName = "path with a null character")]
    public void For_BlankOrInvalidPath_UsesUserSettingsOnly(string filePath)
    {
        Settings.Default.Cleaning_ConvertToVarWhenApparent = true;
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = true;
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = false;

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(filePath);

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
        WriteRootEditorConfig("csharp_style_namespace_declarations = file_scoped:suggestion");

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

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

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

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

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

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

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

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

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(expectedIndentSize, settings.IndentSize);
        Assert.AreEqual(expectedTabSize, settings.TabSize);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void NamespaceConverter_MovesTheBodyByIndentSize_NotByTabWidth()
    {
        WriteRootEditorConfig("indent_size = 4", "tab_width = 8");
        FileScopedNamespaceConverter converter = FileScopedNamespaceLogic.CreateConverter(EffectiveCleanupSettings.For(_filePath));
        string blockScoped = "namespace N\n{\n    public class C\n    {\n        void M() {}\n    }\n}\n";
        string fileScoped = "namespace N;\n\npublic class C\n{\n    void M() {}\n}\n";

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

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

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
    [DataRow("omit_if_default:suggestion", false)]
    public void GetBoolean_RequireAccessibilityModifiers_DrivesEveryExplicitAccessModifierSetting(string option, bool expected)
    {
        foreach (string settingName in ExplicitAccessModifierSettings)
        {
            Settings.Default[settingName] = !expected;
        }

        WriteRootEditorConfig($"dotnet_style_require_accessibility_modifiers = {option}");
        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        foreach (string settingName in ExplicitAccessModifierSettings)
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

        string header = EffectiveCleanupSettings.For(_filePath).GetString("Cleaning_UpdateFileHeaderCSharp");

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

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

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
    [DataRow(":none")]
    [DataRow(":silent")]
    [DataRow(":refactoring")]
    [DataRow(" : Silent", DisplayName = "spaced mixed-case silent")]
    public void NonEnforcingSeverity_IgnoresTheValue_SoTheUserSettingDecides(string suffix)
    {
        Settings.Default.Cleaning_InlineOutVariableDeclarations = true;
        WriteRootEditorConfig($"csharp_style_inlined_variable_declaration = false{suffix}");

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.IsTrue(settings.GetBoolean("Cleaning_InlineOutVariableDeclarations"));
        Assert.IsFalse(settings.EditorConfigKeys.ContainsKey("Cleaning_InlineOutVariableDeclarations"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("csharp_style_expression_bodied_lambdas = false:warning", "Cleaning_SimplifySingleStatementLambdas", false, "csharp_style_expression_bodied_lambdas")]
    [DataRow("csharp_style_expression_bodied_lambdas = when_on_single_line:suggestion", "Cleaning_SimplifySingleStatementLambdas", true, "csharp_style_expression_bodied_lambdas")]
    [DataRow("dotnet_diagnostic.IDE0053.severity = warning", "Cleaning_SimplifySingleStatementLambdas", true, "dotnet_diagnostic.ide0053.severity")]
    [DataRow("csharp_style_prefer_null_check_over_type_check = false:warning", "Cleaning_ConvertToPatternMatchingNullChecks", false, "csharp_style_prefer_null_check_over_type_check")]
    [DataRow("dotnet_style_prefer_is_null_check_over_reference_equality_method = true:error", "Cleaning_ConvertToPatternMatchingNullChecks", true, "dotnet_style_prefer_is_null_check_over_reference_equality_method")]
    [DataRow("dotnet_diagnostic.CA1852.severity = warning", "Cleaning_SealClassesWhenSafe", true, "dotnet_diagnostic.ca1852.severity")]
    [DataRow("dotnet_diagnostic.CA1507.severity = suggestion", "Cleaning_ConvertToStringNameOf", true, "dotnet_diagnostic.ca1507.severity")]
    [DataRow("dotnet_diagnostic.CA1869.severity = error", "Cleaning_ReuseJsonSerializerOptionsForCA1869", true, "dotnet_diagnostic.ca1869.severity")]
    [DataRow("dotnet_style_allow_multiple_blank_lines_experimental = false:warning", "Cleaning_RemoveMultipleConsecutiveBlankLines", true, "dotnet_style_allow_multiple_blank_lines_experimental")]
    [DataRow("dotnet_style_allow_multiple_blank_lines_experimental = true:warning", "Cleaning_RemoveMultipleConsecutiveBlankLines", false, "dotnet_style_allow_multiple_blank_lines_experimental")]
    [DataRow("dotnet_diagnostic.IDE2000.severity = warning", "Cleaning_RemoveMultipleConsecutiveBlankLines", false, "dotnet_diagnostic.ide2000.severity")]
    [DataRow("csharp_style_allow_blank_lines_between_consecutive_braces_experimental = false:warning", "Cleaning_RemoveBlankLinesAfterOpeningBrace", true, "csharp_style_allow_blank_lines_between_consecutive_braces_experimental")]
    [DataRow("csharp_style_allow_blank_lines_between_consecutive_braces_experimental = false:warning", "Cleaning_RemoveBlankLinesBeforeClosingBrace", true, "csharp_style_allow_blank_lines_between_consecutive_braces_experimental")]
    [DataRow("csharp_style_allow_blank_lines_between_consecutive_braces_experimental = true:error", "Cleaning_RemoveBlankLinesBeforeClosingBrace", false, "csharp_style_allow_blank_lines_between_consecutive_braces_experimental")]
    [DataRow("dotnet_diagnostic.IDE0005.severity = warning", "Cleaning_RunVisualStudioRemoveAndSortUsingStatements", true, "dotnet_diagnostic.ide0005.severity")]
    [DataRow("dotnet_diagnostic.IDE0055.severity = warning", "Cleaning_RunVisualStudioFormatDocumentCommand", true, "dotnet_diagnostic.ide0055.severity")]
    public void LinkedOption_EnforcedByEditorConfig_BeatsPolicyAndUserSetting(string option, string settingName, bool expected, string expectedKey)
    {
        Settings.Default[settingName] = !expected;
        WritePolicy($"\"{char.ToLowerInvariant(settingName["Cleaning_".Length])}{settingName.Substring("Cleaning_".Length + 1)}\": {(expected ? "false" : "true")}");
        WriteRootEditorConfig(option);

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(expected, settings.GetBoolean(settingName));
        Assert.AreEqual(expectedKey, settings.EditorConfigKeys[settingName]);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("dotnet_diagnostic.CA1852.severity = silent")]
    [DataRow("dotnet_diagnostic.CA1852.severity = none")]
    [DataRow(null, DisplayName = "not configured")]
    public void LinkedOption_NotEnforcedByEditorConfig_UsesThePolicy(string option)
    {
        Settings.Default.Cleaning_SealClassesWhenSafe = false;
        WritePolicy("\"sealClassesWhenSafe\": true");
        WriteRootEditorConfig(option);

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.IsTrue(settings.GetBoolean("Cleaning_SealClassesWhenSafe"));
        Assert.IsFalse(settings.EditorConfigKeys.ContainsKey("Cleaning_SealClassesWhenSafe"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void LinkedOption_DiagnosticSeverityNone_BeatsTheOptionSuffix()
    {
        Settings.Default.Cleaning_SimplifySingleStatementLambdas = true;
        WriteRootEditorConfig("csharp_style_expression_bodied_lambdas = false:warning", "dotnet_diagnostic.IDE0053.severity = none");

        Assert.IsTrue(EffectiveCleanupSettings.For(_filePath).GetBoolean("Cleaning_SimplifySingleStatementLambdas"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void LinkedOption_TwoKeysEnforced_ConvertsNullChecksOnlyWhenBothAllowIt()
    {
        Settings.Default.Cleaning_ConvertToPatternMatchingNullChecks = true;
        WriteRootEditorConfig(
            "csharp_style_prefer_null_check_over_type_check = true:warning",
            "dotnet_style_prefer_is_null_check_over_reference_equality_method = false:warning");

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.IsFalse(settings.GetBoolean("Cleaning_ConvertToPatternMatchingNullChecks"));
        Assert.AreEqual("dotnet_style_prefer_is_null_check_over_reference_equality_method", settings.EditorConfigKeys["Cleaning_ConvertToPatternMatchingNullChecks"]);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(null, true, DisplayName = "missing")]
    [DataRow("csharp_prefer_braces = false:none", true)]
    [DataRow("csharp_prefer_braces = false:silent", true)]
    [DataRow("csharp_prefer_braces = false:suggestion", false)]
    [DataRow("csharp_prefer_braces = false:warning", false)]
    [DataRow("csharp_prefer_braces = false:error", false)]
    [DataRow("csharp_prefer_braces = false", false, DisplayName = "no suffix")]
    [DataRow("dotnet_diagnostic.IDE0011.severity = warning", false, DisplayName = "severity only")]
    [DataRow("dotnet_diagnostic.IDE0011.severity = silent", true, DisplayName = "severity silent only")]
    public void CodeStyleRule_AppliesOnlyWhenEditorConfigDoesNotEnforceIt(string option, bool applied)
    {
        Settings.Default.Cleaning_CodeStyleRules = "csharp_prefer_braces=when_multiline";
        WriteRootEditorConfig(option);

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(applied, settings.CodeStyleValues.ContainsKey("csharp_prefer_braces"));
        Assert.AreEqual(!applied, settings.CodeStyleEditorConfigKeys.ContainsKey("csharp_prefer_braces"));
        Assert.AreEqual(applied ? "when_multiline:suggestion" : null, settings.AnalyzerConfigOverrides.TryGetValue("csharp_prefer_braces", out string value) ? value : null);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CodeStyleRule_DiagnosticSeverityNone_BeatsAnEnforcingOptionSuffix()
    {
        Settings.Default.Cleaning_CodeStyleRules = "csharp_prefer_braces=true";
        WriteRootEditorConfig("csharp_prefer_braces = false:warning", "dotnet_diagnostic.IDE0011.severity = none");

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual("true", settings.CodeStyleValues["csharp_prefer_braces"]);
        Assert.AreEqual("suggestion", settings.AnalyzerConfigOverrides["dotnet_diagnostic.IDE0011.severity"]);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CodeStyleRule_EnforcedThroughTheDiagnosticSeverity_NamesTheSeverityKey()
    {
        Settings.Default.Cleaning_CodeStyleRules = "csharp_prefer_braces=true";
        WriteRootEditorConfig("dotnet_diagnostic.IDE0011.severity = error");

        Assert.AreEqual("dotnet_diagnostic.ide0011.severity", EffectiveCleanupSettings.For(_filePath).CodeStyleEditorConfigKeys["csharp_prefer_braces"]);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CodeStyleRule_DiagnosticSeverityNoneOnOneId_TheOtherIdFollowsTheOptionSuffix()
    {
        Settings.Default.Cleaning_CodeStyleRules = "dotnet_style_qualification_for_field=true";
        WriteRootEditorConfig("dotnet_style_qualification_for_field = false:warning", "dotnet_diagnostic.IDE0009.severity = none");

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual("dotnet_style_qualification_for_field", settings.CodeStyleEditorConfigKeys["dotnet_style_qualification_for_field"]);
        Assert.IsFalse(settings.CodeStyleValues.ContainsKey("dotnet_style_qualification_for_field"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void NamespaceDeclarations_DiagnosticSeverityEnforcesASilentOption()
    {
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = false;
        WriteRootEditorConfig("csharp_style_namespace_declarations = file_scoped:silent", "dotnet_diagnostic.IDE0161.severity = warning");

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(NamespaceDeclarationPreference.FileScoped, settings.NamespaceDeclarations);
        Assert.IsTrue(settings.GetBoolean("Cleaning_ConvertToFileScopedNamespace"));
        Assert.AreEqual("csharp_style_namespace_declarations", settings.EditorConfigKeys["Cleaning_ConvertToFileScopedNamespace"]);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("dotnet_analyzer_diagnostic.category-Style.severity = warning", "dotnet_analyzer_diagnostic.category-style.severity", DisplayName = "category severity")]
    [DataRow("dotnet_analyzer_diagnostic.severity = suggestion", "dotnet_analyzer_diagnostic.severity", DisplayName = "global severity")]
    public void BulkSeverity_EnforcesCodeStyleRulesAndIdeSteps_WithRoslynDefaults(string option, string expectedKey)
    {
        Settings.Default.Cleaning_CodeStyleRules = "csharp_prefer_braces=when_multiline";
        Settings.Default.Cleaning_RunVisualStudioRemoveAndSortUsingStatements = false;
        Settings.Default.Cleaning_MakeFieldsReadonlyWhenSafe = false;
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = true;
        WriteRootEditorConfig(option);

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(expectedKey, settings.CodeStyleEditorConfigKeys["csharp_prefer_braces"]);
        Assert.IsEmpty(settings.CodeStyleValues);
        Assert.IsEmpty(settings.AnalyzerConfigOverrides);
        Assert.IsTrue(settings.GetBoolean("Cleaning_RunVisualStudioRemoveAndSortUsingStatements"));
        Assert.AreEqual(expectedKey, settings.EditorConfigKeys["Cleaning_RunVisualStudioRemoveAndSortUsingStatements"]);
        Assert.IsTrue(settings.GetBoolean("Cleaning_MakeFieldsReadonlyWhenSafe"), "Roslyn default of dotnet_style_readonly_field");
        Assert.AreEqual(NamespaceDeclarationPreference.BlockScoped, settings.NamespaceDeclarations, "Roslyn default of csharp_style_namespace_declarations");
        Assert.IsFalse(settings.GetBoolean("Cleaning_ConvertToFileScopedNamespace"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CategorySeverity_OnlyAppliesToEnabledByDefaultDiagnosticsOfThatCategory()
    {
        Settings.Default.Cleaning_SealClassesWhenSafe = false;
        Settings.Default.Cleaning_ReuseJsonSerializerOptionsForCA1869 = false;
        Settings.Default.Cleaning_ConvertToStringNameOf = false;
        WriteRootEditorConfig("dotnet_analyzer_diagnostic.category-Performance.severity = warning");

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.IsTrue(settings.GetBoolean("Cleaning_ReuseJsonSerializerOptionsForCA1869"), "CA1869 is an enabled Performance rule");
        Assert.IsFalse(settings.GetBoolean("Cleaning_SealClassesWhenSafe"), "CA1852 is disabled by default, so bulk severities do not enable it");
        Assert.IsFalse(settings.EditorConfigKeys.ContainsKey("Cleaning_SealClassesWhenSafe"));
        Assert.IsFalse(settings.GetBoolean("Cleaning_ConvertToStringNameOf"), "CA1507 is a Maintainability rule");
        Assert.IsFalse(settings.CodeStyleEditorConfigKeys.ContainsKey("csharp_prefer_braces"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(null, DisplayName = "category none only")]
    [DataRow("csharp_prefer_braces = false:warning", DisplayName = "category none beats an enforcing suffix")]
    [DataRow("csharp_prefer_braces = false", DisplayName = "category none beats an option without suffix")]
    public void CategorySeverityNone_DoesNotEnforce_SoTheUserSettingDecides(string option)
    {
        Settings.Default.Cleaning_CodeStyleRules = "csharp_prefer_braces=true";
        Settings.Default.Cleaning_RunVisualStudioRemoveAndSortUsingStatements = false;
        WriteRootEditorConfig("dotnet_analyzer_diagnostic.category-Style.severity = none", option);

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual("true", settings.CodeStyleValues["csharp_prefer_braces"]);
        Assert.IsFalse(settings.CodeStyleEditorConfigKeys.ContainsKey("csharp_prefer_braces"));
        Assert.IsFalse(settings.GetBoolean("Cleaning_RunVisualStudioRemoveAndSortUsingStatements"));
        Assert.IsFalse(settings.EditorConfigKeys.ContainsKey("Cleaning_RunVisualStudioRemoveAndSortUsingStatements"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("dotnet_diagnostic.IDE0011.severity = warning", DisplayName = "diagnostic severity")]
    [DataRow("dotnet_analyzer_diagnostic.category-Style.severity = warning", DisplayName = "category severity")]
    public void NoneSuffix_StopsTheRule_WhateverSeverityIsConfigured(string severity)
    {
        Settings.Default.Cleaning_CodeStyleRules = "csharp_prefer_braces=when_multiline";
        WriteRootEditorConfig("csharp_prefer_braces = true:none", severity);

        Assert.AreEqual("when_multiline", EffectiveCleanupSettings.For(_filePath).CodeStyleValues["csharp_prefer_braces"]);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("csharp_style_var_when_type_is_apparent = true:silent", "dotnet_diagnostic.IDE0007.severity = warning", "Cleaning_ConvertToVarWhenApparent", true, "csharp_style_var_when_type_is_apparent")]
    [DataRow(null, "dotnet_diagnostic.IDE0008.severity = warning", "Cleaning_ConvertToVarWhenApparent", false, "dotnet_diagnostic.ide0008.severity")]
    [DataRow("csharp_style_inlined_variable_declaration = false:warning", "dotnet_diagnostic.IDE0018.severity = none", "Cleaning_InlineOutVariableDeclarations", true, null)]
    [DataRow(null, "dotnet_diagnostic.IDE0018.severity = warning", "Cleaning_InlineOutVariableDeclarations", true, "dotnet_diagnostic.ide0018.severity")]
    [DataRow(null, "dotnet_diagnostic.IDE0044.severity = error", "Cleaning_MakeFieldsReadonlyWhenSafe", true, "dotnet_diagnostic.ide0044.severity")]
    [DataRow("dotnet_style_readonly_field = true:warning", "dotnet_diagnostic.IDE0044.severity = silent", "Cleaning_MakeFieldsReadonlyWhenSafe", false, null)]
    [DataRow(null, "dotnet_diagnostic.IDE0040.severity = warning", "Cleaning_InsertExplicitAccessModifiersOnClasses", true, "dotnet_diagnostic.ide0040.severity")]
    [DataRow("dotnet_style_prefer_collection_expression = never:silent", "dotnet_diagnostic.IDE0305.severity = warning", "Cleaning_ConvertToCollectionExpressions", false, "dotnet_style_prefer_collection_expression")]
    [DataRow(null, "dotnet_diagnostic.IDE0300.severity = suggestion", "Cleaning_ConvertToCollectionExpressions", true, "dotnet_diagnostic.ide0300.severity")]
    public void LegacyOption_ResolvedPerDiagnostic(string option, string severity, string settingName, bool expected, string expectedKey)
    {
        // A locked setting must beat the opposite user setting; an unlocked one must keep the user setting.
        Settings.Default[settingName] = expectedKey is null ? expected : !expected;
        WriteRootEditorConfig(option, severity);

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(expected, settings.GetBoolean(settingName));
        Assert.AreEqual(expectedKey, settings.EditorConfigKeys.TryGetValue(settingName, out string key) ? key : null);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("csharp_using_directive_placement = inside_namespace:warning", "dotnet_diagnostic.IDE0065.severity = none", UsingDirectivePlacementPreference.OutsideNamespace, false)]
    [DataRow("csharp_using_directive_placement = inside_namespace:silent", "dotnet_diagnostic.IDE0065.severity = warning", UsingDirectivePlacementPreference.InsideNamespace, true)]
    [DataRow(null, "dotnet_diagnostic.IDE0065.severity = warning", UsingDirectivePlacementPreference.OutsideNamespace, true)]
    public void UsingDirectivePlacement_ResolvedWithTheDiagnosticSeverity(string option, string severity, object expected, bool locked)
    {
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = true;
        WriteRootEditorConfig(option, severity);

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(expected, settings.UsingDirectivePlacement);
        Assert.AreEqual(locked, settings.EditorConfigKeys.ContainsKey("Cleaning_MoveUsingsOutsideNamespace"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CodeStyleRule_Policy_BeatsUserSetting_AndNullDisablesTheRule()
    {
        Settings.Default.Cleaning_CodeStyleRules = "csharp_prefer_braces=true;dotnet_style_null_propagation=true;csharp_prefer_simple_using_statement=true";
        WritePolicy("\"codeStyle\": { \"csharp_prefer_braces\": \"when_multiline\", \"dotnet_style_null_propagation\": null, \"csharp_style_throw_expression\": \"false\" }");

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreSequenceEqual(
            new Dictionary<string, string>
            {
                ["csharp_prefer_braces"] = "when_multiline",
                ["csharp_style_throw_expression"] = "false",
                ["csharp_prefer_simple_using_statement"] = "true",
            }, settings.CodeStyleValues.ToDictionary(entry => entry.Key, entry => entry.Value), Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void AnalyzerConfigOverrides_SilenceOtherRulesOfTheSameDiagnostic_ThatCodeJanitorDoesNotApply()
    {
        Settings.Default.Cleaning_CodeStyleRules = "dotnet_style_qualification_for_field=true";
        WriteRootEditorConfig("dotnet_style_qualification_for_method = true:silent", "dotnet_style_qualification_for_event = false:warning");

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreSequenceEqual(
            new Dictionary<string, string>
            {
                ["dotnet_style_qualification_for_field"] = "true:suggestion",
                ["dotnet_diagnostic.IDE0003.severity"] = "suggestion",
                ["dotnet_diagnostic.IDE0009.severity"] = "suggestion",
                ["dotnet_style_qualification_for_property"] = "false:none",
                ["dotnet_style_qualification_for_method"] = "true:none",
            }, settings.AnalyzerConfigOverrides.ToDictionary(entry => entry.Key, entry => entry.Value), Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CodeStyleRule_InvalidValues_AreIgnored()
    {
        Settings.Default.Cleaning_CodeStyleRules = "csharp_prefer_braces=sometimes;unknown_key=true;csharp_preferred_modifier_order=public,loud";
        WritePolicy("\"codeStyle\": { \"dotnet_style_null_propagation\": true }");

        Assert.IsEmpty(EffectiveCleanupSettings.For(_filePath).CodeStyleValues);
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

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

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
    [DataRow("dotnet_style_prefer_collection_expression = sometimes:warning", "Cleaning_ConvertToCollectionExpressions", DisplayName = "collection expressions")]
    [DataRow("dotnet_style_require_accessibility_modifiers = whenever:warning", "Cleaning_InsertExplicitAccessModifiersOnMethods", DisplayName = "accessibility modifiers")]
    [DataRow("csharp_style_expression_bodied_lambdas = when_possible:warning", "Cleaning_SimplifySingleStatementLambdas", DisplayName = "expression-bodied lambdas")]
    [DataRow("dotnet_style_allow_multiple_blank_lines_experimental = maybe:warning", "Cleaning_RemoveMultipleConsecutiveBlankLines", DisplayName = "multiple blank lines")]
    [DataRow("csharp_style_allow_blank_lines_between_consecutive_braces_experimental = 1:warning", "Cleaning_RemoveBlankLinesAfterOpeningBrace", DisplayName = "blank lines between braces")]
    [DataRow("trim_trailing_whitespace = yes", "Cleaning_RemoveEndOfLineWhitespace", DisplayName = "trim trailing whitespace")]
    public void UnknownValueOfAMappedOption_IsIgnored_SoThePolicyDecides(string option, string settingName)
    {
        Settings.Default[settingName] = false;
        WritePolicy($"\"{char.ToLowerInvariant(settingName["Cleaning_".Length])}{settingName.Substring("Cleaning_".Length + 1)}\": true");
        WriteRootEditorConfig(option);

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.IsTrue(settings.GetBoolean(settingName));
        Assert.IsFalse(settings.EditorConfigKeys.ContainsKey(settingName));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("dotnet_style_allow_multiple_blank_lines_experimental = false:warning", "Cleaning_RemoveMultipleConsecutiveBlankLines", true)]
    [DataRow("dotnet_style_allow_multiple_blank_lines_experimental = true:warning", "Cleaning_RemoveMultipleConsecutiveBlankLines", false)]
    [DataRow("csharp_style_allow_blank_lines_between_consecutive_braces_experimental = false:error", "Cleaning_RemoveBlankLinesBeforeClosingBrace", true)]
    [DataRow("csharp_style_expression_bodied_lambdas = when_on_single_line:warning", "Cleaning_SimplifySingleStatementLambdas", true)]
    [DataRow("csharp_style_expression_bodied_lambdas = false:warning", "Cleaning_SimplifySingleStatementLambdas", false)]
    [DataRow("dotnet_style_prefer_collection_expression = When_Types_Loosely_Match:warning", "Cleaning_ConvertToCollectionExpressions", true)]
    [DataRow("dotnet_style_prefer_collection_expression = NEVER:warning", "Cleaning_ConvertToCollectionExpressions", false)]
    public void EnforcedOption_MapsItsValue_IgnoringCase(string option, string settingName, bool expected)
    {
        Settings.Default[settingName] = !expected;
        WriteRootEditorConfig(option);

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(expected, settings.GetBoolean(settingName));
        Assert.AreEqual(option.Substring(0, option.IndexOf(' ')), settings.EditorConfigKeys[settingName]);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("inside_namespace", UsingDirectivePlacementPreference.InsideNamespace)]
    [DataRow("Outside_Namespace", UsingDirectivePlacementPreference.OutsideNamespace)]
    public void UsingDirectivePlacement_ValidValueWinsOverTheDisabledUserSetting(string value, object expected)
    {
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = false;
        WriteRootEditorConfig($"csharp_using_directive_placement = {value}");

        Assert.AreEqual(expected, EffectiveCleanupSettings.For(_filePath).UsingDirectivePlacement);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("anywhere", DisplayName = "unknown value")]
    [DataRow("inside_namespace:loud", DisplayName = "unknown severity")]
    public void UsingDirectivePlacement_UnknownValueFallsBackToTheDisabledUserSetting(string value)
    {
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = false;
        WriteRootEditorConfig($"csharp_using_directive_placement = {value}");

        Assert.AreEqual(UsingDirectivePlacementPreference.Unchanged, EffectiveCleanupSettings.For(_filePath).UsingDirectivePlacement);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(new[] { "indent_style = tabs" }, IndentationPreference.Unchanged, 4, 4, DisplayName = "unknown indent_style")]
    [DataRow(new[] { "indent_style = Space" }, IndentationPreference.Spaces, 4, 4, DisplayName = "indent_style ignores case")]
    [DataRow(new[] { "indent_style = tab:warning" }, IndentationPreference.Tabs, 4, 4, DisplayName = "severity suffix on indent_style")]
    [DataRow(new[] { "indent_style = tab:silent" }, IndentationPreference.Unchanged, 4, 4, DisplayName = "non-enforcing suffix on indent_style")]
    [DataRow(new[] { "indent_size = wide", "tab_width = 8" }, IndentationPreference.Unchanged, 4, 8, DisplayName = "unknown indent_size keeps the default")]
    [DataRow(new[] { "indent_size = -2", "tab_width = x" }, IndentationPreference.Unchanged, 4, 4, DisplayName = "negative and non-numeric sizes")]
    [DataRow(new[] { "indent_size = 2.5" }, IndentationPreference.Unchanged, 4, 4, DisplayName = "fractional size")]
    [DataRow(new[] { "indent_size = TAB", "tab_width = 3" }, IndentationPreference.Unchanged, 3, 3, DisplayName = "indent_size = tab ignores case")]
    public void Indentation_InvalidValues_FallBackToTheDefaults(string[] options, object expectedIndentation, int expectedIndentSize, int expectedTabSize)
    {
        WriteRootEditorConfig(options);

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(expectedIndentation, settings.Indentation);
        Assert.AreEqual(expectedIndentSize, settings.IndentSize);
        Assert.AreEqual(expectedTabSize, settings.TabSize);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("true:warning", "false:warning", false, "dotnet_style_prefer_is_null_check_over_reference_equality_method", DisplayName = "second key disallows")]
    [DataRow("false:warning", "true:warning", false, "csharp_style_prefer_null_check_over_type_check", DisplayName = "first key disallows, the second cannot allow")]
    [DataRow("false:warning", "false:warning", false, "csharp_style_prefer_null_check_over_type_check", DisplayName = "both disallow: the first decides")]
    [DataRow("true:warning", "true:warning", true, "csharp_style_prefer_null_check_over_type_check", DisplayName = "both allow: the first decides")]
    [DataRow("true:silent", "false:warning", false, "dotnet_style_prefer_is_null_check_over_reference_equality_method", DisplayName = "only the second enforced")]
    [DataRow("true:warning", "false:silent", true, "csharp_style_prefer_null_check_over_type_check", DisplayName = "only the first enforced")]
    public void NullCheckRules_EveryEnforcedKeyMustAllowTheConversion_AndTheFirstBlockingKeyIsReported(string typeCheck, string referenceEquality, bool expected, string expectedKey)
    {
        Settings.Default.Cleaning_ConvertToPatternMatchingNullChecks = !expected;
        WriteRootEditorConfig(
            $"csharp_style_prefer_null_check_over_type_check = {typeCheck}",
            $"dotnet_style_prefer_is_null_check_over_reference_equality_method = {referenceEquality}");

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(expected, settings.GetBoolean("Cleaning_ConvertToPatternMatchingNullChecks"));
        Assert.AreEqual(expectedKey, settings.EditorConfigKeys["Cleaning_ConvertToPatternMatchingNullChecks"]);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("file_header_template =", DisplayName = "empty")]
    [DataRow("file_header_template =    ", DisplayName = "whitespace")]
    [DataRow("file_header_template = UNSET", DisplayName = "unset in upper case")]
    public void GetString_EmptyFileHeaderTemplate_ClearsRepositoryAndUserHeader(string option)
    {
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// User header";
        WritePolicy("\"fileHeaderCSharp\": \"// Policy header\"");
        WriteRootEditorConfig(option);

        Assert.AreEqual(string.Empty, EffectiveCleanupSettings.For(_filePath).GetString("Cleaning_UpdateFileHeaderCSharp"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void GetString_FileHeaderTemplateContainingAColon_IsUsedVerbatim()
    {
        WriteRootEditorConfig(@"file_header_template = Copyright (c) Contoso: all rights reserved.\n{fileName}");

        Assert.AreEqual(
            string.Join(Environment.NewLine, "// Copyright (c) Contoso: all rights reserved.", "// Sample.cs"),
            EffectiveCleanupSettings.For(_filePath).GetString("Cleaning_UpdateFileHeaderCSharp"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(true, "// Policy header")]
    [DataRow(false, "// User header")]
    public void GetString_WithoutFileHeaderTemplate_PolicyBeatsUserSetting(bool policy, string expected)
    {
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// User header";
        WritePolicy(policy ? "\"fileHeaderCSharp\": \"// Policy header\"" : string.Empty);
        WriteRootEditorConfig("indent_size = 4");

        Assert.AreEqual(expected, EffectiveCleanupSettings.For(_filePath).GetString("Cleaning_UpdateFileHeaderCSharp"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("\"fileHeaderPosition\": \"afterUsings\", \"fileHeaderUpdateMode\": \"replace\"", 1, 1, DisplayName = "policy")]
    [DataRow("\"fileHeaderPosition\": \"middle\", \"fileHeaderUpdateMode\": 1", 0, 0, DisplayName = "invalid policy values")]
    [DataRow("", 0, 0, DisplayName = "no policy")]
    public void GetInt32_PolicyBeatsUserSetting(string policy, int expectedPosition, int expectedMode)
    {
        Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition = 0;
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = 0;
        WritePolicy(policy);

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.AreEqual(expectedPosition, settings.GetInt32("Cleaning_UpdateFileHeader_HeaderPosition"));
        Assert.AreEqual(expectedMode, settings.GetInt32("Cleaning_UpdateFileHeader_HeaderUpdateMode"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void MalformedPolicy_IsIgnored_SoEditorConfigThenUserSettingsDecide()
    {
        Settings.Default.Cleaning_ConvertToVarWhenApparent = true;
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = true;
        File.WriteAllText(Path.Combine(_tempDirectory, ".codejanitor"), "{ \"cleanup\": { \"convertToVarWhenApparent\": false, \"removeRegions\": false ");
        WriteRootEditorConfig("csharp_style_namespace_declarations = block_scoped:warning");

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(_filePath);

        Assert.IsTrue(settings.GetBoolean("Cleaning_ConvertToVarWhenApparent"));
        Assert.AreEqual(NamespaceDeclarationPreference.BlockScoped, settings.NamespaceDeclarations);
        Assert.IsTrue(settings.RemovesRegions);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void EditorConfigInAnotherSection_DoesNotBeatThePolicy()
    {
        Settings.Default.Cleaning_ConvertToVarWhenApparent = false;
        WritePolicy("\"convertToVarWhenApparent\": true");
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"), string.Join("\n",
            "root = true",
            "[*.{vb,fs}]",
            "csharp_style_var_when_type_is_apparent = false:error",
            "[{Program,Startup}.cs]",
            "csharp_style_var_when_type_is_apparent = false:error",
            string.Empty));

        Assert.IsTrue(EffectiveCleanupSettings.For(_filePath).GetBoolean("Cleaning_ConvertToVarWhenApparent"));
        Assert.IsFalse(EffectiveCleanupSettings.For(Path.Combine(_tempDirectory, "Program.cs")).GetBoolean("Cleaning_ConvertToVarWhenApparent"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void NestedEditorConfig_OverridesParentKey_AndInheritsTheOthers()
    {
        WriteRootEditorConfig(
            "trim_trailing_whitespace = true",
            "csharp_style_var_when_type_is_apparent = true");
        DirectoryInfo nested = Directory.CreateDirectory(Path.Combine(_tempDirectory, "src", "App"));
        File.WriteAllText(Path.Combine(_tempDirectory, "src", ".editorconfig"), "[*.cs]\r\ntrim_trailing_whitespace = false\r\n");

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(Path.Combine(nested.FullName, "Sample.cs"));

        Assert.IsFalse(settings.GetBoolean("Cleaning_RemoveEndOfLineWhitespace"));
        Assert.IsTrue(settings.GetBoolean("Cleaning_ConvertToVarWhenApparent"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void RootEditorConfig_HidesEditorConfigsAboveIt()
    {
        Settings.Default.Cleaning_ConvertToVarWhenApparent = false;
        WriteRootEditorConfig("csharp_style_var_when_type_is_apparent = true");
        DirectoryInfo nested = Directory.CreateDirectory(Path.Combine(_tempDirectory, "src"));
        File.WriteAllText(Path.Combine(nested.FullName, ".editorconfig"), "root = true\r\n\r\n[*.cs]\r\ntrim_trailing_whitespace = false\r\n");

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(Path.Combine(nested.FullName, "Sample.cs"));

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
        DirectoryInfo source = Directory.CreateDirectory(Path.Combine(_tempDirectory, "src", "App"));
        DirectoryInfo tests = Directory.CreateDirectory(Path.Combine(_tempDirectory, "tests"));

        EffectiveCleanupSettings inSource = EffectiveCleanupSettings.For(Path.Combine(source.FullName, "Sample.cs"));
        EffectiveCleanupSettings inTests = EffectiveCleanupSettings.For(Path.Combine(tests.FullName, "Sample.cs"));

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
        IEnumerable<string> lines = new[] { "root = true", string.Empty, "[*.cs]" }
            .Concat(csharpOptions.Where(option => option is not null))
            .Concat(new[] { string.Empty });

        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"), string.Join("\r\n", lines));
    }

    /// <summary>
    /// Writes the repository policy of the test directory with the given cleanup entries.
    /// </summary>
    private void WritePolicy(string cleanupEntries) => File.WriteAllText(Path.Combine(_tempDirectory, ".codejanitor"), "{ \"cleanup\": { " + cleanupEntries + " } }");
}
