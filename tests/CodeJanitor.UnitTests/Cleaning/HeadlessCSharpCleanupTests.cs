using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;
using CodeJanitor.UnitTests.Transformations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
        string filePath = Path.Combine(_tempDirectory, "Preview.cs");
        string source = "namespace Demo;\r\n#region Sample\r\nclass C {}\r\n#endregion\r\n";
        File.WriteAllText(filePath, source);

        SourceTransformationPipeline.PreviewResult preview = CodeCleanupManager.CreateHeadlessCSharpPipeline(source, filePath).Preview(source);

        Assert.AreEqual(CodeCleanupManager.ApplyHeadlessCSharpTransformations(source, filePath), preview.UpdatedSource);
        Assert.AreEqual(source, File.ReadAllText(filePath));
        Assert.IsTrue(preview.HasChanges);
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_PreservesFileScopedNamespace_WhenEditorConfigWhitespaceRulesApply()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"),
            "root = true\r\n\r\n[*.cs]\r\ntrim_trailing_whitespace = true\r\ninsert_final_newline = true\r\n");

        string filePath = Path.Combine(_tempDirectory, "Sample.cs");
        string input =
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n    public void M()    \r\n    {\r\n    }\r\n}\r\n   ";

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.Contains("namespace Demo;", output);
        Assert.DoesNotContain("namespace Demo\r\n{", output, "File-scoped namespace must remain file-scoped.");
        Assert.DoesNotContain("M()    \r\n", output, "Trailing whitespace should be removed by EditorConfig-driven cleanup.");
        Assert.IsTrue(output.EndsWith("\r\n", StringComparison.Ordinal), "Final newline should be inserted by EditorConfig-driven cleanup.");
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_LeavesExactlyOneFinalNewline_UnlessEditorConfigDisablesIt()
    {
        Settings.Default.Cleaning_InsertEndOfFileTrailingNewLine = false;
        Settings.Default.Cleaning_RemoveEndOfFileTrailingNewLine = true;

        string filePath = Path.Combine(_tempDirectory, "FinalNewlineSample.cs");
        string input = "namespace Demo;\r\n\r\npublic class C { }\r\n\r\n";

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.IsTrue(output.EndsWith("\r\n", StringComparison.Ordinal));
        Assert.IsFalse(output.EndsWith("\r\n\r\n", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_UsesEditorConfigIndentStyleSpace_ForTabIndentation()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"),
            "root = true\r\n\r\n[*.cs]\r\nindent_style = space\r\ntab_width = 2\r\n");

        string filePath = Path.Combine(_tempDirectory, "Sample.cs");
        string input =
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n\tpublic void M()\r\n\t{\r\n\t}\r\n}\r\n";

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.Contains("namespace Demo;", output);
        Assert.DoesNotContain("\tpublic void M()", output, "Tab indentation should be expanded to spaces from EditorConfig.");
        Assert.Contains("  public void M()", output);
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_PreservesPreprocessorDirectives_DuringWhitespaceCleanup()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"),
            "root = true\r\n\r\n[*.cs]\r\ntrim_trailing_whitespace = true\r\ninsert_final_newline = true\r\n");

        Settings.Default.Cleaning_RemoveBlankLinesAfterOpeningBrace = true;
        Settings.Default.Cleaning_RemoveBlankLinesBeforeClosingBrace = true;
        Settings.Default.Cleaning_RemoveMultipleConsecutiveBlankLines = true;

        string filePath = Path.Combine(_tempDirectory, "PreprocessorSample.cs");
        string input =
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n#if DEBUG\r\n    public void M()    \r\n    {\r\n    }\r\n#endif\r\n}\r\n";

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.Contains("#if DEBUG", output, "Headless cleanup must preserve #if directives.");
        Assert.Contains("#endif", output, "Headless cleanup must preserve #endif directives.");
        Assert.Contains("namespace Demo;", output, "File-scoped namespace must remain present.");
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_RemovesBlankLinesBetweenDocumentationCommentAndDeclaration()
    {
        string filePath = Path.Combine(_tempDirectory, "DocSample.cs");
        string input =
            "namespace Demo;\r\n\r\n/// <summary>\r\n/// A class.\r\n/// </summary>\r\n\r\npublic class C\r\n{\r\n    /// <summary>\r\n    /// A method.\r\n    /// </summary>\r\n    \r\n\r\n    public void M()\r\n    {\r\n    }\r\n}\r\n";

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.Contains("/// </summary>\r\npublic ", output);
        Assert.Contains("    /// </summary>\r\n    public void M()", output);
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_CanInsertFileHeaderAfterUsings_WithoutBreakingFileScopedNamespace()
    {
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// header";
        Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition = 1;
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = 0;

        string filePath = Path.Combine(_tempDirectory, "HeaderSample.cs");
        string input =
            "using System;\r\n\r\nnamespace Demo;\r\n\r\npublic class C\r\n{\r\n}\r\n";

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.Contains("using System;\r\n\r\n// header\r\n\r\nnamespace Demo;", output, "Header should be inserted after top-level usings without breaking file-scoped namespace.");
        Assert.DoesNotContain("namespace Demo\r\n{", output, "File-scoped namespace must not revert to block-scoped during header insertion.");
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_AppliesEditorConfigUsingSorting_WhenVisualStudioRemoveSortIsDisabled()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"),
            "root = true\r\n\r\n[*.cs]\r\ndotnet_sort_system_directives_first = true\r\ndotnet_separate_import_directive_groups = false\r\n");

        Settings.Default.Cleaning_RunVisualStudioRemoveAndSortUsingStatements = false;

        string filePath = Path.Combine(_tempDirectory, "UsingSample.cs");
        string input =
            "using Zebra;\r\nusing System;\r\nusing Alpha;\r\n\r\nnamespace Demo;\r\n\r\npublic class C { }\r\n";

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.IsTrue(output.StartsWith("using System;\r\nusing Alpha;\r\nusing Zebra;", StringComparison.Ordinal), "Headless cleanup should sort using directives according to .editorconfig-compatible organizer rules.");
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_AlwaysRemovesRegionDirectives_WhilePreservingIfDirectives()
    {
        string filePath = Path.Combine(_tempDirectory, "RegionSample.cs");
        string input =
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n#if DEBUG\r\n#region DebugOnly\r\n    public void M() { }\r\n#endregion\r\n#endif\r\n}\r\n";

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.DoesNotContain("#region", output, "Cleanup should always remove #region directives.");
        Assert.DoesNotContain("#endregion", output, "Cleanup should always remove #endregion directives.");
        Assert.Contains("#if DEBUG", output, "Cleanup must preserve #if directives.");
        Assert.Contains("#endif", output, "Cleanup must preserve #endif directives.");
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_NeverRunsAiXmlDoc()
    {
        Settings.Default.Cleaning_AiXmlDocumentationEnabled = true;
        Settings.Default.Cleaning_AiXmlDocumentationEndpointUrl = "https://api.openai.com/v1";
        Settings.Default.Cleaning_AiXmlDocumentationApiKey = "test-key";

        string filePath = Path.Combine(_tempDirectory, "SampleNoXmlDoc.cs");
        string input = "namespace Demo;\r\n\r\npublic class C\r\n{\r\n    public void Method1() { }\r\n}\r\n";

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.DoesNotContain("/// <summary>", output, "AI XML documentation should never run during general cleanup.");
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_StripsBom_WhenRemoveByteOrderMarkIsTrue()
    {
        Settings.Default.Cleaning_RemoveByteOrderMark = true;

        string filePath = Path.Combine(_tempDirectory, "SampleWithBom.cs");
        string input = "\uFEFFnamespace Demo;\r\n\r\npublic class C { }\r\n";

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.IsFalse(output.StartsWith("\uFEFF", StringComparison.Ordinal), "BOM should be stripped when RemoveByteOrderMark is true.");
        Assert.IsTrue(output.StartsWith("namespace Demo;", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_PreservesBom_WhenRemoveByteOrderMarkIsFalse()
    {
        Settings.Default.Cleaning_RemoveByteOrderMark = false;

        string filePath = Path.Combine(_tempDirectory, "SamplePreserveBom.cs");
        string input = "\uFEFFnamespace Demo;\r\n\r\npublic class C { }\r\n";

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.IsTrue(output.StartsWith("\uFEFF", StringComparison.Ordinal), "BOM should be preserved when RemoveByteOrderMark is false.");
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_LeavesNullChecks_WhenPatternMatchingNullChecksAreEnabled()
    {
        // The conversion needs the semantic model (a user-defined == may give null a meaning of its own), so it runs
        // against the Visual Studio workspace (NullCheckPatternMatchingLogic), not in the headless text pipeline.
        Settings.Default.Cleaning_ConvertToPatternMatchingNullChecks = true;

        string filePath = Path.Combine(_tempDirectory, "SampleNullChecks.cs");
        string input = "namespace Demo;\r\n\r\npublic class C { public void M(object x) { if (x != null) { } } }\r\n";

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.Contains("if (x != null)", output);
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_AppliesStringInterpolation_WhenEnabled()
    {
        Settings.Default.Cleaning_ConvertStringFormatToInterpolation = true;

        string filePath = Path.Combine(_tempDirectory, "SampleStringFormat.cs");
        string input = "namespace Demo;\r\n\r\npublic class C { public string M(string n) { return string.Format(\"Hello {0}\", n); } }\r\n";

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.Contains("return $\"Hello {n}\";", output);
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_AppliesNameOfOperator_WhenEnabled()
    {
        Settings.Default.Cleaning_ConvertToStringNameOf = true;

        string filePath = Path.Combine(_tempDirectory, "SampleNameOf.cs");
        string input = "using System;\r\nnamespace Demo;\r\n\r\npublic class C { public void M(string p) { throw new ArgumentNullException(\"p\"); } }\r\n";

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.Contains("throw new ArgumentNullException(nameof(p));", output);
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_AppliesOutVarInlining_WhenEnabled()
    {
        Settings.Default.Cleaning_InlineOutVariableDeclarations = true;

        string filePath = Path.Combine(_tempDirectory, "SampleOutVar.cs");
        string input = "namespace Demo;\r\n\r\npublic class C { public void M(string s) { int res;\r\nif (int.TryParse(s, out res)) { } } }\r\n";

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.Contains("if (int.TryParse(s, out int res))", output);
    }

    [TestMethod]
    public async Task ApplyHeadlessCSharpTransformations_NeverMovesNamespaceUsings_SoNamespaceRelativeUsingsKeepCompiling()
    {
        // The text pipeline has no semantic model; placing using directives is done by the separate workspace step.
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = true;
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = true;

        string filePath = Path.Combine(_tempDirectory, "SampleRelativeUsings.cs");

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(NamespaceRelativeUsingSource, filePath);

        int namespaceIndex = output.IndexOf("namespace Company.App;", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, namespaceIndex, output);
        Assert.IsGreaterThan(namespaceIndex, output.IndexOf("using Services;", StringComparison.Ordinal), "The using directive must stay inside the namespace:" + Environment.NewLine + output);
        Document document = CompilingTestProject.CreateDocument(NamespaceRelativeUsingSource, "namespace Company.App.Services { public class Svc { } }");
        IReadOnlyList<string> errors = await CompilingTestProject.GetCompileErrorsAsync(document, output);
        Assert.IsEmpty(errors, output + Environment.NewLine + string.Join(Environment.NewLine, errors));
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
        string filePath = Path.Combine(_tempDirectory, "PlacedUsings.cs");
        File.WriteAllText(filePath, NamespaceRelativeUsingSource);

        UsingsMoveOutcome placement = await UsingDirectivePlacementLogic.PlaceUsingDirectivesInFileAsync(filePath, async (currentText, direction) =>
        {
            Document document = CompilingTestProject.CreateDocument(currentText, "namespace Company.App.Services { public class Svc { } }");
            UsingDirectivePlacementResult result = await UsingDirectivePlacementLogic.PlaceInEveryFlavorAsync(new UsingDirectivePlacementConverter(), direction, new[] { document }, CancellationToken.None);

            return result.Status == UsingDirectivePlacementStatus.Moved
                ? (UsingsMoveOutcome.Moved, result.Text)
                : (UsingsMoveOutcome.LeftInPlace, (string)null);
        });
        CodeCleanupManager.HeadlessPreCleanupOutcome headless = CodeCleanupManager.GetInstance(null).TryRunHeadlessPreCleanupForCSharpCore(filePath);

        Assert.AreEqual(UsingsMoveOutcome.Moved, placement);
        Assert.AreEqual(CodeCleanupManager.HeadlessCleanupResult.Changed, headless.Result);
        string output = File.ReadAllText(filePath);
        int usingIndex = output.IndexOf("using Company.App.Services;", StringComparison.Ordinal);
        int headerIndex = output.IndexOf("// header", StringComparison.Ordinal);
        int namespaceIndex = output.IndexOf("namespace Company.App", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, usingIndex, "The using directive must be placed outside the namespace:" + Environment.NewLine + output);
        Assert.IsGreaterThan(usingIndex, headerIndex, "The header must follow the placed using directive:" + Environment.NewLine + output);
        Assert.IsGreaterThan(headerIndex, namespaceIndex, "The header must precede the namespace:" + Environment.NewLine + output);
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_ConvertsToBlockScopedNamespace_WhenEditorConfigRequiresIt_OverPolicyAndUserSetting()
    {
        WriteEditorConfig("csharp_style_namespace_declarations = block_scoped:suggestion");
        WriteRepositoryPolicy("\"convertToFileScopedNamespace\": true");
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = true;

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n}\r\n", Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.Contains("namespace Demo\r\n{\r\n", output);
        Assert.DoesNotContain("namespace Demo;", output, output);
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_ConvertsToFileScopedNamespace_WhenEditorConfigRequiresIt_OverPolicyAndUserSetting()
    {
        WriteEditorConfig("csharp_style_namespace_declarations = file_scoped:warning");
        WriteRepositoryPolicy("\"convertToFileScopedNamespace\": false");
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = false;

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(
            "namespace Demo\r\n{\r\n    public class C\r\n    {\r\n    }\r\n}\r\n", Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.Contains("namespace Demo;", output);
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

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(
            "namespace Demo\r\n{\r\n    public class C\r\n    {\r\n    }\r\n}\r\n", Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.AreEqual(converted, output.Contains("namespace Demo;"), output);
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_DoesNotOrganizeUsings_WhenEditorConfigContradictsTheOrganizer_OverPolicy()
    {
        WriteEditorConfig("dotnet_sort_system_directives_first = false");
        WriteRepositoryPolicy("\"organizeUsings\": true");
        Settings.Default.Cleaning_RunVisualStudioRemoveAndSortUsingStatements = false;

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(
            "using Zebra;\r\nusing System;\r\nusing Alpha;\r\n\r\nnamespace Demo;\r\n\r\npublic class C { }\r\n", Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.Contains("using Zebra;\r\nusing System;\r\nusing Alpha;", output);
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_KeepsTrailingWhitespace_WhenEditorConfigDisablesTrimming_OverPolicyAndUserSetting()
    {
        WriteEditorConfig("trim_trailing_whitespace = false");
        WriteRepositoryPolicy("\"removeEndOfLineWhitespace\": true");
        Settings.Default.Cleaning_RemoveEndOfLineWhitespace = true;

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n    public void M()    \r\n    {\r\n    }\r\n}\r\n", Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.Contains("public void M()    \r\n", output);
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_RemovesFinalNewline_WhenEditorConfigInsertFinalNewlineIsFalse()
    {
        WriteEditorConfig("insert_final_newline = false");

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n}\r\n\r\n", Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.IsTrue(output.EndsWith("}", StringComparison.Ordinal), output);
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_ConvertsIndentationToTabs_WhenEditorConfigIndentStyleIsTab()
    {
        WriteEditorConfig("indent_style = tab", "indent_size = 4");

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n    public void M()\r\n    {\r\n        var x = 1;\r\n    }\r\n}\r\n",
            Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.Contains("\r\n\tpublic void M()\r\n", output);
        Assert.Contains("\r\n\t\tvar x = 1;\r\n", output);
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_IndentsTheBlockScopedNamespaceBody_WithTheEditorConfigIndentSize()
    {
        Settings.Default.Cleaning_SealClassesWhenSafe = false;
        WriteEditorConfig("csharp_style_namespace_declarations = block_scoped", "indent_style = space", "indent_size = 2");

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n  public void M()\r\n  {\r\n  }\r\n}\r\n",
            Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.Contains("\r\n  public class C\r\n", output);
        Assert.Contains("\r\n    public void M()\r\n", output);
    }

    [TestMethod]
    public void RequiresEditorCleanupForCSharp_IsFalse_WhenTheVisualStudioCommandsAreEnabled()
    {
        // Diagnostic cleanup runs the Roslyn equivalents of both commands on closed files.
        Settings.Default.Cleaning_RunVisualStudioFormatDocumentCommand = true;
        Settings.Default.Cleaning_RunVisualStudioRemoveAndSortUsingStatements = true;

        Assert.IsFalse(CodeCleanupManager.RequiresEditorCleanupForCSharp());
    }

    [TestMethod]
    public void ApplyHeadlessCSharpTransformations_InsertsAccessModifiersOnMethods_WhenEditorConfigRequiresThem_OverUserSetting()
    {
        WriteEditorConfig("dotnet_style_require_accessibility_modifiers = for_non_interface_members:suggestion");
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnMethods = false;

        string output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(MethodWithoutAccessModifierSource, Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.Contains("\r\n    private void M()\r\n", output);
    }

    [TestMethod]
    [DataRow("insertExplicitAccessModifiersOnMethods", nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnMethods), false, MethodWithoutAccessModifierSource, DisplayName = "explicit access modifiers on methods: policy off beats user on")]
    [DataRow("insertBlankLinePaddingBeforeCaseStatements", nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeCaseStatements), false, CaseStatementsSource, DisplayName = "padding before case statements: policy off beats user on")]
    [DataRow("updateSingleLineMethods", nameof(Settings.Cleaning_UpdateSingleLineMethods), true, SingleLineMethodSource, DisplayName = "update single-line methods: policy on beats user off")]
    [DataRow("updateAccessorsToBothBeSingleLineOrMultiLine", nameof(Settings.Cleaning_UpdateAccessorsToBothBeSingleLineOrMultiLine), true, MixedAccessorsSource, DisplayName = "update accessors: policy on beats user off")]
    public void ApplyHeadlessCSharpTransformations_FollowsTheRepositoryPolicyPerKind_OverUserSetting(string policyKey, string settingName, bool policyValue, string input)
    {
        string filePath = Path.Combine(_tempDirectory, "Sample.cs");
        Settings.Default[settingName] = policyValue;
        string expected = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);
        Settings.Default[settingName] = !policyValue;
        Assert.AreNotEqual(expected, CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath), "Control: the setting changes the closed-file result.");

        WriteRepositoryPolicy($"\"{policyKey}\": {(policyValue ? "true" : "false")}");

        Assert.AreEqual(expected, CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath));
    }

    /// <summary>
    /// Writes a root .editorconfig with the specified C# options into the test directory.
    /// </summary>
    private void WriteEditorConfig(params string[] options) => File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"),
            "root = true\r\n\r\n[*.cs]\r\n" + string.Join("\r\n", options) + "\r\n");

    /// <summary>
    /// Writes a .codejanitor repository policy with the specified cleanup entries into the test directory.
    /// </summary>
    private void WriteRepositoryPolicy(string cleanupEntries) => File.WriteAllText(Path.Combine(_tempDirectory, RepositoryCleanupSettings.PrimaryConfigFileName),
            "{ \"cleanup\": { " + cleanupEntries + " } }");

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
