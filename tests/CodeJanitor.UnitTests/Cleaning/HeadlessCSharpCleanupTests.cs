using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.CodeAnalysis.CSharp;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;
using CodeJanitor.UnitTests.Transformations;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public sealed class HeadlessCSharpCleanupTests
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
    public void Preview_UsesSameConfiguredPipelineWithoutWritingSourceFile()
    {
        var filePath = Path.Combine(_tempDirectory, "Preview.cs");
        var source = "namespace Demo;\r\n#region Sample\r\nclass C {}\r\n#endregion\r\n";
        File.WriteAllText(filePath, source);

        var preview = CodeCleanupManager.CreateHeadlessCSharpPipeline(source, filePath).Preview(source);

        Assert.AreEqual(CodeCleanupManager.ApplyHeadlessCSharpTransformations(source, filePath), preview.UpdatedSource);
        Assert.AreEqual(source, File.ReadAllText(filePath));
        Assert.IsTrue(preview.HasChanges);
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_PreservesFileScopedNamespace_WhenEditorConfigWhitespaceRulesApply()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"),
            "root = true\r\n\r\n[*.cs]\r\ntrim_trailing_whitespace = true\r\ninsert_final_newline = true\r\n");

        var filePath = Path.Combine(_tempDirectory, "Sample.cs");
        var input =
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n    public void M()    \r\n    {\r\n    }\r\n}\r\n   ";

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        StringAssert.Contains(output, "namespace Demo;");
        Assert.IsFalse(output.Contains("namespace Demo\r\n{"), "File-scoped namespace must remain file-scoped.");
        Assert.IsFalse(output.Contains("M()    \r\n"), "Trailing whitespace should be removed by EditorConfig-driven cleanup.");
        Assert.IsTrue(output.EndsWith("\r\n", StringComparison.Ordinal), "Final newline should be inserted by EditorConfig-driven cleanup.");
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_LeavesExactlyOneFinalNewline_UnlessEditorConfigDisablesIt()
    {
        Settings.Default.Cleaning_InsertEndOfFileTrailingNewLine = false;
        Settings.Default.Cleaning_RemoveEndOfFileTrailingNewLine = true;

        var filePath = Path.Combine(_tempDirectory, "FinalNewlineSample.cs");
        var input = "namespace Demo;\r\n\r\npublic class C { }\r\n\r\n";

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.IsTrue(output.EndsWith("\r\n", StringComparison.Ordinal));
        Assert.IsFalse(output.EndsWith("\r\n\r\n", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_UsesEditorConfigIndentStyleSpace_ForTabIndentation()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"),
            "root = true\r\n\r\n[*.cs]\r\nindent_style = space\r\ntab_width = 2\r\n");

        var filePath = Path.Combine(_tempDirectory, "Sample.cs");
        var input =
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n\tpublic void M()\r\n\t{\r\n\t}\r\n}\r\n";

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        StringAssert.Contains(output, "namespace Demo;");
        Assert.IsFalse(output.Contains("\tpublic void M()"), "Tab indentation should be expanded to spaces from EditorConfig.");
        StringAssert.Contains(output, "  public void M()");
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_PreservesPreprocessorDirectives_DuringWhitespaceCleanup()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"),
            "root = true\r\n\r\n[*.cs]\r\ntrim_trailing_whitespace = true\r\ninsert_final_newline = true\r\n");

        Settings.Default.Cleaning_RemoveBlankLinesAfterOpeningBrace = true;
        Settings.Default.Cleaning_RemoveBlankLinesBeforeClosingBrace = true;
        Settings.Default.Cleaning_RemoveMultipleConsecutiveBlankLines = true;

        var filePath = Path.Combine(_tempDirectory, "PreprocessorSample.cs");
        var input =
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n#if DEBUG\r\n    public void M()    \r\n    {\r\n    }\r\n#endif\r\n}\r\n";

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.IsTrue(output.Contains("#if DEBUG"), "Headless cleanup must preserve #if directives.");
        Assert.IsTrue(output.Contains("#endif"), "Headless cleanup must preserve #endif directives.");
        Assert.IsTrue(output.Contains("namespace Demo;"), "File-scoped namespace must remain present.");
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_CanInsertFileHeaderAfterUsings_WithoutBreakingFileScopedNamespace()
    {
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// header";
        Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition = 1;
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = 0;

        var filePath = Path.Combine(_tempDirectory, "HeaderSample.cs");
        var input =
            "using System;\r\n\r\nnamespace Demo;\r\n\r\npublic class C\r\n{\r\n}\r\n";

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.IsTrue(output.Contains("using System;\r\n\r\n// header\r\n\r\nnamespace Demo;"), "Header should be inserted after top-level usings without breaking file-scoped namespace.");
        Assert.IsFalse(output.Contains("namespace Demo\r\n{"), "File-scoped namespace must not revert to block-scoped during header insertion.");
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_AppliesEditorConfigUsingSorting_WhenVisualStudioRemoveSortIsDisabled()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"),
            "root = true\r\n\r\n[*.cs]\r\ndotnet_sort_system_directives_first = true\r\ndotnet_separate_import_directive_groups = false\r\n");

        Settings.Default.Cleaning_RunVisualStudioRemoveAndSortUsingStatements = false;

        var filePath = Path.Combine(_tempDirectory, "UsingSample.cs");
        var input =
            "using Zebra;\r\nusing System;\r\nusing Alpha;\r\n\r\nnamespace Demo;\r\n\r\npublic class C { }\r\n";

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.IsTrue(output.StartsWith("using System;\r\nusing Alpha;\r\nusing Zebra;", StringComparison.Ordinal), "Headless cleanup should sort using directives according to .editorconfig-compatible organizer rules.");
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_AlwaysRemovesRegionDirectives_WhilePreservingIfDirectives()
    {
        var filePath = Path.Combine(_tempDirectory, "RegionSample.cs");
        var input =
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n#if DEBUG\r\n#region DebugOnly\r\n    public void M() { }\r\n#endregion\r\n#endif\r\n}\r\n";

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.IsFalse(output.Contains("#region"), "Cleanup should always remove #region directives.");
        Assert.IsFalse(output.Contains("#endregion"), "Cleanup should always remove #endregion directives.");
        Assert.IsTrue(output.Contains("#if DEBUG"), "Cleanup must preserve #if directives.");
        Assert.IsTrue(output.Contains("#endif"), "Cleanup must preserve #endif directives.");
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_NeverRunsAiXmlDoc()
    {
        Settings.Default.Cleaning_AiXmlDocumentationEnabled = true;
        Settings.Default.Cleaning_AiXmlDocumentationEndpointUrl = "https://api.openai.com/v1";
        Settings.Default.Cleaning_AiXmlDocumentationApiKey = "test-key";

        var filePath = Path.Combine(_tempDirectory, "SampleNoXmlDoc.cs");
        var input = "namespace Demo;\r\n\r\npublic class C\r\n{\r\n    public void Method1() { }\r\n}\r\n";

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.IsFalse(output.Contains("/// <summary>"), "AI XML documentation should never run during general cleanup.");
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_StripsBom_WhenRemoveByteOrderMarkIsTrue()
    {
        Settings.Default.Cleaning_RemoveByteOrderMark = true;

        var filePath = Path.Combine(_tempDirectory, "SampleWithBom.cs");
        var input = "\uFEFFnamespace Demo;\r\n\r\npublic class C { }\r\n";

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.IsFalse(output.StartsWith("\uFEFF", StringComparison.Ordinal), "BOM should be stripped when RemoveByteOrderMark is true.");
        Assert.IsTrue(output.StartsWith("namespace Demo;", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_PreservesBom_WhenRemoveByteOrderMarkIsFalse()
    {
        Settings.Default.Cleaning_RemoveByteOrderMark = false;

        var filePath = Path.Combine(_tempDirectory, "SamplePreserveBom.cs");
        var input = "\uFEFFnamespace Demo;\r\n\r\npublic class C { }\r\n";

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.IsTrue(output.StartsWith("\uFEFF", StringComparison.Ordinal), "BOM should be preserved when RemoveByteOrderMark is false.");
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_AppliesPatternMatchingNullChecks_WhenEnabled()
    {
        Settings.Default.Cleaning_ConvertToPatternMatchingNullChecks = true;

        var filePath = Path.Combine(_tempDirectory, "SampleNullChecks.cs");
        var input = "namespace Demo;\r\n\r\npublic class C { public void M(object x) { if (x != null) { } } }\r\n";

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.IsTrue(output.Contains("if (x is not null)"));
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_AppliesStringInterpolation_WhenEnabled()
    {
        Settings.Default.Cleaning_ConvertStringFormatToInterpolation = true;

        var filePath = Path.Combine(_tempDirectory, "SampleStringFormat.cs");
        var input = "namespace Demo;\r\n\r\npublic class C { public string M(string n) { return string.Format(\"Hello {0}\", n); } }\r\n";

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.IsTrue(output.Contains("return $\"Hello {n}\";"));
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_AppliesNameOfOperator_WhenEnabled()
    {
        Settings.Default.Cleaning_ConvertToStringNameOf = true;

        var filePath = Path.Combine(_tempDirectory, "SampleNameOf.cs");
        var input = "using System;\r\nnamespace Demo;\r\n\r\npublic class C { public void M(string p) { throw new ArgumentNullException(\"p\"); } }\r\n";

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.IsTrue(output.Contains("throw new ArgumentNullException(nameof(p));"));
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_AppliesOutVarInlining_WhenEnabled()
    {
        Settings.Default.Cleaning_InlineOutVariableDeclarations = true;

        var filePath = Path.Combine(_tempDirectory, "SampleOutVar.cs");
        var input = "namespace Demo;\r\n\r\npublic class C { public void M(string s) { int res;\r\nif (int.TryParse(s, out res)) { } } }\r\n";

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.IsTrue(output.Contains("if (int.TryParse(s, out var res))"));
    }

    [TestMethod]
    public async Task ApplyHeadlessCSharpTransformations_NeverMovesNamespaceUsings_SoNamespaceRelativeUsingsKeepCompiling()
    {
        // The text pipeline has no semantic model; placing using directives is done by the separate workspace step.
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = true;
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = true;

        var filePath = Path.Combine(_tempDirectory, "SampleRelativeUsings.cs");

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(NamespaceRelativeUsingSource, filePath);

        var namespaceIndex = output.IndexOf("namespace Company.App;", StringComparison.Ordinal);
        Assert.IsTrue(namespaceIndex >= 0, output);
        Assert.IsTrue(output.IndexOf("using Services;", StringComparison.Ordinal) > namespaceIndex, "The using directive must stay inside the namespace:" + Environment.NewLine + output);
        var document = CompilingTestProject.CreateDocument(NamespaceRelativeUsingSource, "namespace Company.App.Services { public class Svc { } }");
        var errors = await CompilingTestProject.GetCompileErrorsAsync(document, output);
        Assert.AreEqual(0, errors.Count, output + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }

    [TestMethod]
    public async Task HeadlessCleanup_OfAClosedFileWithPlacedUsings_InsertsTheFileHeaderAfterTheMovedUsings()
    {
        // The using directive placement rewrites the closed file first; the headless cleanup then reads the placed
        // directives from disk, so a header placed after the usings lands between the moved usings and the namespace.
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = true;
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// header";
        Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition = 1;
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = 0;
        var filePath = Path.Combine(_tempDirectory, "PlacedUsings.cs");
        File.WriteAllText(filePath, NamespaceRelativeUsingSource);

        var placement = await UsingDirectivePlacementLogic.PlaceUsingDirectivesInFileAsync(filePath, async (currentText, direction) =>
        {
            var document = CompilingTestProject.CreateDocument(currentText, "namespace Company.App.Services { public class Svc { } }");
            var result = await UsingDirectivePlacementLogic.PlaceInEveryFlavorAsync(new UsingDirectivePlacementConverter(), direction, new[] { document }, CancellationToken.None);

            return result.Status == UsingDirectivePlacementStatus.Moved
                ? (UsingsMoveOutcome.Moved, result.Text)
                : (UsingsMoveOutcome.LeftInPlace, (string)null);
        });
        var headless = CodeCleanupManager.GetInstance(null).TryRunHeadlessPreCleanupForCSharpCore(filePath);

        Assert.AreEqual(UsingsMoveOutcome.Moved, placement);
        Assert.AreEqual(CodeCleanupManager.HeadlessCleanupResult.Changed, headless.Result);
        var output = File.ReadAllText(filePath);
        var usingIndex = output.IndexOf("using Company.App.Services;", StringComparison.Ordinal);
        var headerIndex = output.IndexOf("// header", StringComparison.Ordinal);
        var namespaceIndex = output.IndexOf("namespace Company.App", StringComparison.Ordinal);
        Assert.IsTrue(usingIndex >= 0, "The using directive must be placed outside the namespace:" + Environment.NewLine + output);
        Assert.IsTrue(headerIndex > usingIndex, "The header must follow the placed using directive:" + Environment.NewLine + output);
        Assert.IsTrue(namespaceIndex > headerIndex, "The header must precede the namespace:" + Environment.NewLine + output);
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_ConvertsToBlockScopedNamespace_WhenEditorConfigRequiresIt_OverPolicyAndUserSetting()
    {
        WriteEditorConfig("csharp_style_namespace_declarations = block_scoped:silent");
        WriteRepositoryPolicy("\"convertToFileScopedNamespace\": true");
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = true;

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n}\r\n", Path.Combine(_tempDirectory, "Sample.cs"));

        StringAssert.Contains(output, "namespace Demo\r\n{\r\n");
        Assert.IsFalse(output.Contains("namespace Demo;"), output);
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_ConvertsToFileScopedNamespace_WhenEditorConfigRequiresIt_OverPolicyAndUserSetting()
    {
        WriteEditorConfig("csharp_style_namespace_declarations = file_scoped:warning");
        WriteRepositoryPolicy("\"convertToFileScopedNamespace\": false");
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = false;

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(
            "namespace Demo\r\n{\r\n    public class C\r\n    {\r\n    }\r\n}\r\n", Path.Combine(_tempDirectory, "Sample.cs"));

        StringAssert.Contains(output, "namespace Demo;");
    }

    [TestMethod]
    [DataRow(false, true, false, DisplayName = "policy off beats user on")]
    [DataRow(null, true, true, DisplayName = "no policy, user on")]
    [DataRow(null, false, false, DisplayName = "no policy, user off")]
    public void ApplyHeadlessCSharpTransformations_IgnoresEditorConfigNamespaceStyleWithNoneSeverity(bool? policy, bool userSetting, bool converted)
    {
        WriteEditorConfig("csharp_style_namespace_declarations = file_scoped:none");
        if (policy.HasValue)
        {
            File.WriteAllText(Path.Combine(_tempDirectory, ".codejanitor"), $"{{ \"cleanup\": {{ \"convertToFileScopedNamespace\": {(policy.Value ? "true" : "false")} }} }}");
        }

        Settings.Default.Cleaning_ConvertToFileScopedNamespace = userSetting;

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(
            "namespace Demo\r\n{\r\n    public class C\r\n    {\r\n    }\r\n}\r\n", Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.AreEqual(converted, output.Contains("namespace Demo;"), output);
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_DoesNotOrganizeUsings_WhenEditorConfigContradictsTheOrganizer_OverPolicy()
    {
        WriteEditorConfig("dotnet_sort_system_directives_first = false");
        WriteRepositoryPolicy("\"organizeUsings\": true");
        Settings.Default.Cleaning_RunVisualStudioRemoveAndSortUsingStatements = false;

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(
            "using Zebra;\r\nusing System;\r\nusing Alpha;\r\n\r\nnamespace Demo;\r\n\r\npublic class C { }\r\n", Path.Combine(_tempDirectory, "Sample.cs"));

        StringAssert.Contains(output, "using Zebra;\r\nusing System;\r\nusing Alpha;");
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_KeepsTrailingWhitespace_WhenEditorConfigDisablesTrimming_OverPolicyAndUserSetting()
    {
        WriteEditorConfig("trim_trailing_whitespace = false");
        WriteRepositoryPolicy("\"removeEndOfLineWhitespace\": true");
        Settings.Default.Cleaning_RemoveEndOfLineWhitespace = true;

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n    public void M()    \r\n    {\r\n    }\r\n}\r\n", Path.Combine(_tempDirectory, "Sample.cs"));

        StringAssert.Contains(output, "public void M()    \r\n");
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_RemovesFinalNewline_WhenEditorConfigInsertFinalNewlineIsFalse()
    {
        WriteEditorConfig("insert_final_newline = false");

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n}\r\n\r\n", Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.IsTrue(output.EndsWith("}", StringComparison.Ordinal), output);
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_ConvertsIndentationToTabs_WhenEditorConfigIndentStyleIsTab()
    {
        WriteEditorConfig("indent_style = tab", "indent_size = 4");

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n    public void M()\r\n    {\r\n        var x = 1;\r\n    }\r\n}\r\n",
            Path.Combine(_tempDirectory, "Sample.cs"));

        StringAssert.Contains(output, "\r\n\tpublic void M()\r\n");
        StringAssert.Contains(output, "\r\n\t\tvar x = 1;\r\n");
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_IndentsTheBlockScopedNamespaceBody_WithTheEditorConfigIndentSize()
    {
        Settings.Default.Cleaning_SealClassesWhenSafe = false;
        WriteEditorConfig("csharp_style_namespace_declarations = block_scoped", "indent_style = space", "indent_size = 2");

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n  public void M()\r\n  {\r\n  }\r\n}\r\n",
            Path.Combine(_tempDirectory, "Sample.cs"));

        StringAssert.Contains(output, "\r\n  public class C\r\n");
        StringAssert.Contains(output, "\r\n    public void M()\r\n");
    }

    [TestMethod]
    public void RequiresEditorCleanupForCSharp_FollowsTheRepositoryPolicyOfTheFile()
    {
        // The Visual Studio settings enable neither Visual Studio command; one repository enables Format Document.
        Settings.Default.Cleaning_RunVisualStudioFormatDocumentCommand = false;
        Settings.Default.Cleaning_RunVisualStudioRemoveAndSortUsingStatements = false;
        var repositoryDirectory = Path.Combine(_tempDirectory, "Repository");
        Directory.CreateDirectory(repositoryDirectory);
        File.WriteAllText(Path.Combine(repositoryDirectory, RepositoryCleanupSettings.PrimaryConfigFileName),
            "{ \"cleanup\": { \"runVisualStudioFormatDocumentCommand\": true } }");

        Assert.IsTrue(CodeCleanupManager.RequiresEditorCleanupForCSharp(Path.Combine(repositoryDirectory, "Sample.cs")));
        Assert.IsFalse(CodeCleanupManager.RequiresEditorCleanupForCSharp(Path.Combine(_tempDirectory, "Sample.cs")));
    }

    [TestMethod]
    public void RequiresEditorCleanupForCSharp_IsFalse_WhenTheRepositoryPolicyDisablesTheVisualStudioCommands()
    {
        // Both Visual Studio commands are enabled by default in the Visual Studio settings.
        WriteRepositoryPolicy("\"runVisualStudioFormatDocumentCommand\": false, \"runVisualStudioRemoveAndSortUsingStatements\": false");

        Assert.IsFalse(CodeCleanupManager.RequiresEditorCleanupForCSharp(Path.Combine(_tempDirectory, "Sample.cs")));
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_InsertsAccessModifiersOnMethods_WhenEditorConfigRequiresThem_OverUserSetting()
    {
        WriteEditorConfig("dotnet_style_require_accessibility_modifiers = for_non_interface_members:silent");
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnMethods = false;

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(MethodWithoutAccessModifierSource, Path.Combine(_tempDirectory, "Sample.cs"));

        StringAssert.Contains(output, "\r\n    private void M()\r\n");
    }

    [TestMethod]
    [DataRow("insertExplicitAccessModifiersOnMethods", nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnMethods), false, MethodWithoutAccessModifierSource, DisplayName = "explicit access modifiers on methods: policy off beats user on")]
    [DataRow("insertBlankLinePaddingBeforeCaseStatements", nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeCaseStatements), false, CaseStatementsSource, DisplayName = "padding before case statements: policy off beats user on")]
    [DataRow("updateSingleLineMethods", nameof(Settings.Cleaning_UpdateSingleLineMethods), true, SingleLineMethodSource, DisplayName = "update single-line methods: policy on beats user off")]
    [DataRow("updateAccessorsToBothBeSingleLineOrMultiLine", nameof(Settings.Cleaning_UpdateAccessorsToBothBeSingleLineOrMultiLine), true, MixedAccessorsSource, DisplayName = "update accessors: policy on beats user off")]
    public void ApplyHeadlessCSharpTransformations_FollowsTheRepositoryPolicyPerKind_OverUserSetting(string policyKey, string settingName, bool policyValue, string input)
    {
        var filePath = Path.Combine(_tempDirectory, "Sample.cs");
        Settings.Default[settingName] = policyValue;
        var expected = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);
        Settings.Default[settingName] = !policyValue;
        Assert.AreNotEqual(expected, CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath), "Control: the setting changes the closed-file result.");

        WriteRepositoryPolicy($"\"{policyKey}\": {(policyValue ? "true" : "false")}");

        Assert.AreEqual(expected, CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath));
    }

    /// <summary>
    /// Writes a root .editorconfig with the specified C# options into the test directory.
    /// </summary>

    private void WriteEditorConfig(params string[] options)
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"),
            "root = true\r\n\r\n[*.cs]\r\n" + string.Join("\r\n", options) + "\r\n");
    }

    /// <summary>
    /// Writes a .codejanitor repository policy with the specified cleanup entries into the test directory.
    /// </summary>

    private void WriteRepositoryPolicy(string cleanupEntries)
    {
        File.WriteAllText(Path.Combine(_tempDirectory, RepositoryCleanupSettings.PrimaryConfigFileName),
            "{ \"cleanup\": { " + cleanupEntries + " } }");
    }

    private const string NamespaceRelativeUsingSource =
        "namespace Company.App\r\n{\r\n    using Services;\r\n\r\n    public class C\r\n    {\r\n        public Svc Service { get; set; }\r\n    }\r\n}\r\n";

    private const string MethodWithoutAccessModifierSource =
        "namespace Demo;\r\n\r\npublic class C\r\n{\r\n    void M()\r\n    {\r\n    }\r\n}\r\n";

    private const string CaseStatementsSource =
        "namespace Demo;\r\n\r\npublic class C\r\n{\r\n    public void M(int value)\r\n    {\r\n        switch (value)\r\n        {\r\n            case 1:\r\n                break;\r\n            case 2:\r\n                break;\r\n        }\r\n    }\r\n}\r\n";

    private const string SingleLineMethodSource =
        "namespace Demo;\r\n\r\npublic class C\r\n{\r\n    public int M() { return 1; }\r\n}\r\n";

    private const string MixedAccessorsSource =
        "namespace Demo;\r\n\r\npublic class C\r\n{\r\n    private int _value;\r\n\r\n    public int Value\r\n    {\r\n        get { return _value; }\r\n        set\r\n        {\r\n            _value = value;\r\n        }\r\n    }\r\n}\r\n";
}
