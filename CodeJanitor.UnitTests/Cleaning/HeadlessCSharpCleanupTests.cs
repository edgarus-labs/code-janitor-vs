using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using System;
using System.IO;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public class HeadlessCSharpCleanupTests
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
    public void ApplyHeadlessCSharpTransformations_AlwaysLeavesExactlyOneFinalNewline()
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
    public void ApplyHeadlessCSharpTransformations_DoesNotRunAiXmlDoc_WhenRunDuringCleanupIsFalse()
    {
        Settings.Default.Cleaning_AiXmlDocumentationEnabled = true;
        Settings.Default.Cleaning_AiXmlDocumentationRunDuringCleanup = false;
        Settings.Default.Cleaning_AiXmlDocumentationEndpointUrl = "https://api.openai.com/v1";
        Settings.Default.Cleaning_AiXmlDocumentationApiKey = "test-key";

        var filePath = Path.Combine(_tempDirectory, "SampleNoXmlDoc.cs");
        var input = "namespace Demo;\r\n\r\npublic class C\r\n{\r\n    public void Method1() { }\r\n}\r\n";

        var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(input, filePath);

        Assert.IsFalse(output.Contains("/// <summary>"), "AI XML documentation should not run during cleanup when RunDuringCleanup is false.");
    }
}
