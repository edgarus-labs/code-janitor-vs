using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.Options;
using System;
using System.IO;
using System.Linq;

namespace CodeJanitor.UnitTests.UI;

/// <summary>
/// Tests for <see cref="EditorConfigOverrideNotes" />: the mapping between Code Janitor options and the
/// .editorconfig keys that override them, evaluated against the .editorconfig chain of the open solution's root.
/// </summary>
[TestClass]
public sealed class EditorConfigOverrideNotesTests
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
    private string _solutionDirectory;
    private string _solutionPath;

    [TestInitialize]
    public void TestInitialize()
    {
        Settings.Default.Reset();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        _solutionDirectory = Path.Combine(_tempDirectory, "repo");
        Directory.CreateDirectory(_solutionDirectory);
        _solutionPath = Path.Combine(_solutionDirectory, "Sample.sln");

        // An empty root .editorconfig above the solution isolates every test from configuration files on the machine.
        WriteEditorConfig(_tempDirectory, isRoot: true);
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
    [TestCategory("UI UnitTests")]
    [DataRow("trim_trailing_whitespace = true", "Cleaning_RemoveEndOfLineWhitespace", "trim_trailing_whitespace")]
    [DataRow("csharp_style_var_when_type_is_apparent = false:warning", "Cleaning_ConvertToVarWhenApparent", "csharp_style_var_when_type_is_apparent")]
    [DataRow("csharp_style_inlined_variable_declaration = true:suggestion", "Cleaning_InlineOutVariableDeclarations", "csharp_style_inlined_variable_declaration")]
    [DataRow("dotnet_style_prefer_collection_expression = when_types_loosely_match", "Cleaning_ConvertToCollectionExpressions", "dotnet_style_prefer_collection_expression")]
    [DataRow("dotnet_style_readonly_field = true:silent", "Cleaning_MakeFieldsReadonlyWhenSafe", "dotnet_style_readonly_field")]
    [DataRow("csharp_style_namespace_declarations = file_scoped:warning", "Cleaning_ConvertToFileScopedNamespace", "csharp_style_namespace_declarations")]
    [DataRow("csharp_using_directive_placement = inside_namespace", "Cleaning_MoveUsingsOutsideNamespace", "csharp_using_directive_placement")]
    [DataRow("file_header_template = Copyright (c) Contoso", "Cleaning_UpdateFileHeaderCSharp", "file_header_template")]
    [DataRow("insert_final_newline = true", "Cleaning_InsertEndOfFileTrailingNewLine", "insert_final_newline")]
    [DataRow("insert_final_newline = false", "Cleaning_RemoveEndOfFileTrailingNewLine", "insert_final_newline")]
    public void Indexer_EnforcedEditorConfigKey_NamesKeyAndDefiningFile(string option, string settingName, string key)
    {
        var configPath = WriteEditorConfig(_solutionDirectory, isRoot: false, option);

        var notes = EditorConfigOverrideNotes.ForSolution(_solutionPath);

        Assert.AreEqual($"Overridden by .editorconfig: {key} in {configPath}", notes[settingName]);
    }

    [TestMethod]
    [TestCategory("UI UnitTests")]
    public void Indexer_AccessibilityModifiersKey_AnnotatesEveryExplicitAccessModifierSetting()
    {
        WriteEditorConfig(_solutionDirectory, isRoot: false, "dotnet_style_require_accessibility_modifiers = always:warning");

        var notes = EditorConfigOverrideNotes.ForSolution(_solutionPath);

        foreach (var settingName in ExplicitAccessModifierSettings)
        {
            StringAssert.Contains(notes[settingName], "dotnet_style_require_accessibility_modifiers", settingName);
        }
    }

    [TestMethod]
    [TestCategory("UI UnitTests")]
    public void Indexer_EveryMappedKeyDefined_AnnotatesExactlyTheMappedSettings()
    {
        WriteEditorConfig(
            _solutionDirectory,
            isRoot: false,
            "trim_trailing_whitespace = true",
            "insert_final_newline = true",
            "file_header_template = Header",
            "csharp_style_var_when_type_is_apparent = true",
            "csharp_style_inlined_variable_declaration = true",
            "dotnet_style_prefer_collection_expression = true",
            "dotnet_style_readonly_field = true",
            "dotnet_style_require_accessibility_modifiers = always",
            "csharp_style_namespace_declarations = file_scoped",
            "csharp_using_directive_placement = outside_namespace",
            "indent_style = space",
            "dotnet_sort_system_directives_first = true");

        var notes = EditorConfigOverrideNotes.ForSolution(_solutionPath);

        var annotated = Settings.Default.Properties
            .Cast<System.Configuration.SettingsProperty>()
            .Select(property => property.Name)
            .Where(name => notes[name] is not null)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var expected = ExplicitAccessModifierSettings
            .Concat(new[]
            {
                "Cleaning_ConvertToCollectionExpressions",
                "Cleaning_ConvertToFileScopedNamespace",
                "Cleaning_ConvertToVarWhenApparent",
                "Cleaning_InlineOutVariableDeclarations",
                "Cleaning_InsertEndOfFileTrailingNewLine",
                "Cleaning_MakeFieldsReadonlyWhenSafe",
                "Cleaning_MoveUsingsOutsideNamespace",
                "Cleaning_RemoveEndOfFileTrailingNewLine",
                "Cleaning_RemoveEndOfLineWhitespace",
                "Cleaning_UpdateFileHeaderCSharp",
            })
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(expected, annotated, string.Join(", ", annotated));
    }

    [TestMethod]
    [TestCategory("UI UnitTests")]
    [DataRow("csharp_style_var_when_type_is_apparent = true:none", DisplayName = "severity none")]
    [DataRow("csharp_style_var_when_type_is_apparent = maybe", DisplayName = "unrecognized value")]
    [DataRow("csharp_style_var_when_type_is_apparent = true:loud", DisplayName = "unrecognized severity")]
    public void Indexer_KeyNotEnforced_HasNoNote(string option)
    {
        WriteEditorConfig(_solutionDirectory, isRoot: false, option);

        var notes = EditorConfigOverrideNotes.ForSolution(_solutionPath);

        Assert.IsNull(notes["Cleaning_ConvertToVarWhenApparent"]);
    }

    [TestMethod]
    [TestCategory("UI UnitTests")]
    public void Indexer_SettingWithoutEditorConfigKey_HasNoNote()
    {
        WriteEditorConfig(_solutionDirectory, isRoot: false, "csharp_style_var_when_type_is_apparent = true");

        var notes = EditorConfigOverrideNotes.ForSolution(_solutionPath);

        Assert.IsNull(notes["Cleaning_SealClassesWhenSafe"]);
    }

    [TestMethod]
    [TestCategory("UI UnitTests")]
    public void Indexer_KeyOnlyInParentConfig_NamesParentFile()
    {
        WriteEditorConfig(_solutionDirectory, isRoot: false, "dotnet_style_readonly_field = true");
        var parentPath = WriteEditorConfig(_tempDirectory, isRoot: true, "csharp_style_var_when_type_is_apparent = true");

        var notes = EditorConfigOverrideNotes.ForSolution(_solutionPath);

        Assert.AreEqual($"Overridden by .editorconfig: csharp_style_var_when_type_is_apparent in {parentPath}", notes["Cleaning_ConvertToVarWhenApparent"]);
    }

    [TestMethod]
    [TestCategory("UI UnitTests")]
    public void Indexer_KeyInSolutionAndParentConfig_NamesNearestFile()
    {
        WriteEditorConfig(_tempDirectory, isRoot: true, "csharp_style_var_when_type_is_apparent = true");
        var nearestPath = WriteEditorConfig(_solutionDirectory, isRoot: false, "csharp_style_var_when_type_is_apparent = false");

        var notes = EditorConfigOverrideNotes.ForSolution(_solutionPath);

        Assert.AreEqual($"Overridden by .editorconfig: csharp_style_var_when_type_is_apparent in {nearestPath}", notes["Cleaning_ConvertToVarWhenApparent"]);
    }

    [TestMethod]
    [TestCategory("UI UnitTests")]
    public void Indexer_KeyAboveRootConfig_HasNoNote()
    {
        var outer = Path.Combine(_tempDirectory, "outer");
        var solutionDirectory = Path.Combine(outer, "repo");
        Directory.CreateDirectory(solutionDirectory);
        WriteEditorConfig(outer, isRoot: false, "csharp_style_var_when_type_is_apparent = true");
        WriteEditorConfig(solutionDirectory, isRoot: true, "dotnet_style_readonly_field = true");

        var notes = EditorConfigOverrideNotes.ForSolution(Path.Combine(solutionDirectory, "Sample.sln"));

        Assert.IsNull(notes["Cleaning_ConvertToVarWhenApparent"]);
    }

    [TestMethod]
    [TestCategory("UI UnitTests")]
    public void Indexer_KeyOnlyInNestedProjectConfig_HasNoNote()
    {
        var project = Directory.CreateDirectory(Path.Combine(_solutionDirectory, "src"));
        WriteEditorConfig(project.FullName, isRoot: false, "csharp_style_var_when_type_is_apparent = true");

        var notes = EditorConfigOverrideNotes.ForSolution(_solutionPath);

        Assert.IsNull(notes["Cleaning_ConvertToVarWhenApparent"]);
    }

    [TestMethod]
    [TestCategory("UI UnitTests")]
    public void Indexer_KeyInNonCSharpSection_HasNoNote()
    {
        File.WriteAllText(
            Path.Combine(_solutionDirectory, ".editorconfig"),
            "[*.vb]\r\ndotnet_style_readonly_field = true\r\n");

        var notes = EditorConfigOverrideNotes.ForSolution(_solutionPath);

        Assert.IsNull(notes["Cleaning_MakeFieldsReadonlyWhenSafe"]);
    }

    [TestMethod]
    [TestCategory("UI UnitTests")]
    [DataRow(null, DisplayName = "no solution")]
    [DataRow("", DisplayName = "empty solution path")]
    [DataRow(@"C:\bad<|>path\Sample.sln", DisplayName = "invalid solution path")]
    public void ForSolution_NoUsableSolution_HasNoNotes(string solutionPath)
    {
        WriteEditorConfig(_solutionDirectory, isRoot: false, "csharp_style_var_when_type_is_apparent = true");

        var notes = EditorConfigOverrideNotes.ForSolution(solutionPath);

        Assert.IsNull(notes["Cleaning_ConvertToVarWhenApparent"]);
    }

    /// <summary>
    /// Writes an .editorconfig with the given options in a [*.cs] section and returns its full path.
    /// </summary>
    private static string WriteEditorConfig(string directory, bool isRoot, params string[] csharpOptions)
    {
        var lines = (isRoot ? new[] { "root = true", string.Empty } : Array.Empty<string>())
            .Concat(new[] { "[*.cs]" })
            .Concat(csharpOptions)
            .Concat(new[] { string.Empty });

        var path = Path.Combine(directory, ".editorconfig");
        File.WriteAllText(path, string.Join("\r\n", lines));
        return path;
    }
}
