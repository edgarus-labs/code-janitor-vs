using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Logic.Cleaning.Diagnostics;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;
using CodeJanitor.UnitTests.Cleaning.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning;

/// <summary>
/// End-to-end tests of the C# cleanup of one file, step after step in production order:
/// <list type="number">
/// <item>the semantic steps (class sealing, null check conversion),</item>
/// <item>the text steps (<see cref="CodeCleanupManager.TryRunHeadlessPreCleanupForCSharpCore" />),</item>
/// <item>for a closed file the Roslyn equivalents of "Remove and Sort Usings" and "Format Document",</item>
/// <item>the Roslyn diagnostic cleanup (analyzers and code fixes) with the effective settings of the file.</item>
/// </list>
/// An open editor cannot be driven headlessly: its text steps run against the editor buffer (DTE) and "Remove and Sort
/// Usings" and "Format Document" are Visual Studio commands. The open-file runs therefore apply the same text steps to
/// the text, skip the two commands and run the diagnostic cleanup the way it runs on an open document.
/// </summary>
[TestClass]
public sealed class FullPipelineCleanupTests
{
    private const string SampleInput =
        "namespace Sample.Core\r\n{\r\n    using System;\r\n    using System.Collections.Generic;\r\n    using System.Linq;\r\n\r\n" +
        "    class Widget\r\n    {\r\n        private int _count;\r\n        private int[] _values = new int[] { 1, 2, 3 };\r\n\r\n" +
        "        string Describe(string text)\r\n        {\r\n            int parsed;\r\n            if (int.TryParse(text, out parsed))\r\n            {\r\n                _count = parsed;\r\n            }\r\n\r\n" +
        "            Dictionary<string, int> map = new Dictionary<string, int>();\r\n            Func<int, int> twice = x => { return x * 2; };\r\n\r\n" +
        "            return map.Count.ToString() + twice(_count) + _values.Length;\r\n        }\r\n    }\r\n}\r\n";

    private const string SampleOutputEditor =
        "using System;\r\nusing System.Collections.Generic;\r\nusing System.Linq;\r\n\r\nnamespace Sample.Core;\r\n\r\n" +
        "internal sealed class Widget\r\n{\r\n    private int _count;\r\n    private readonly int[] _values = [1, 2, 3];\r\n\r\n" +
        "    private string Describe(string text)\r\n    {\r\n        if (int.TryParse(text, out int parsed))\r\n        {\r\n            _count = parsed;\r\n        }\r\n\r\n" +
        "        var map = new Dictionary<string, int>();\r\n        Func<int, int> twice = x => x * 2;\r\n\r\n" +
        "        return map.Count.ToString() + twice(_count) + _values.Length;\r\n    }\r\n}\r\n";

    private const string UsingsInsideBlockNamespace =
        "namespace Sample.Core\r\n{\r\n    using System;\r\n    using System.Collections.Generic;\r\n\r\n    class Widget\r\n    {\r\n        List<string> Names(Action done) => new List<string>();\r\n    }\r\n}\r\n";

    private const string UsingsAfterFileScopedNamespace =
        "namespace Sample.Core;\r\n\r\nusing System;\r\nusing System.Collections.Generic;\r\n\r\nclass Widget\r\n{\r\n    List<string> Names(Action done) => new List<string>();\r\n}\r\n";

    private const string RegionsAndConditionals =
        "using System;\r\n#if DEBUG\r\nusing System.Diagnostics;\r\n#endif\r\n\r\nnamespace Demo\r\n{\r\n    #region Types\r\n    public class Probe\r\n    {\r\n        #region Fields\r\n        private int _x = 1;\r\n        #endregion\r\n\r\n" +
        "        #if DEBUG\r\n        public void Trace() { Debug.WriteLine(_x); }\r\n        #endif\r\n    }\r\n    #endregion\r\n}\r\n";

    private const string Attributes =
        "using System;\r\n\r\nnamespace Demo\r\n{\r\n    [Serializable]\r\n\r\n\r\n    [Obsolete(\"old\")]\r\n    public class Probe\r\n    {\r\n        [ThreadStatic]\r\n\r\n        private static int _x;\r\n    }\r\n}\r\n";

    private const string NestedNamespaces =
        "namespace Outer\r\n{\r\n    namespace Inner\r\n    {\r\n        using System;\r\n\r\n        class Probe\r\n        {\r\n            Action _callback;\r\n        }\r\n    }\r\n}\r\n";

    private const string TopLevelStatements =
        "using System;\r\n\r\nint parsed;\r\nif (int.TryParse(\"1\", out parsed))\r\n{\r\n    Console.WriteLine(parsed);\r\n}\r\n";

    private const string AlreadyClean =
        "using System;\r\n\r\nnamespace Demo;\r\n\r\ninternal sealed class Probe\r\n{\r\n    private readonly int _x = 1;\r\n\r\n    public int Get()\r\n    {\r\n        Func<int, int> twice = x => x * 2;\r\n\r\n        return twice(_x);\r\n    }\r\n}\r\n";

    private const string SyntaxErrors =
        "namespace Broken\r\n{\r\n    using System;\r\n\r\n    class Probe\r\n    {\r\n        Action _callback;\r\n\r\n        void Run()\r\n        {\r\n            int parsed\r\n            int.TryParse(\"1\", out parsed);\r\n        }\r\n    }\r\n}\r\n";

    private string _directoryName;
    private string _directory;

    [TestInitialize]
    public void TestInitialize()
    {
        Settings.Default.Reset();
        Settings.Default.Cleaning_AiXmlDocumentationEnabled = false;

        _directoryName = Guid.NewGuid().ToString("N");
        _directory = DiagnosticCleanupTestWorkspace.GetPath(_directoryName);
        Directory.CreateDirectory(_directory);
        WritePolicy(string.Empty);
        WriteEditorConfig("end_of_line = crlf");
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Settings.Default.Reset();
        Directory.Delete(_directory, true);
    }

    public static IEnumerable<object[]> PipelineModes()
    {
        yield return new object[] { false };
        yield return new object[] { true };
    }

    public static string PipelineModeName(System.Reflection.MethodInfo method, object[] data) => (bool)data[0] ? "closed file" : "open file";

    public static IEnumerable<object[]> NamespaceStepCombinations()
    {
        foreach (string input in new[] { UsingsInsideBlockNamespace, UsingsAfterFileScopedNamespace })
        {
            for (int mask = 0; mask < 32; mask++)
            {
                yield return new object[] { input, (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0, (mask & 16) != 0 };
            }
        }
    }

    public static string NamespaceStepCombinationName(System.Reflection.MethodInfo method, object[] data)
    {
        string input = (string)data[0] == UsingsInsideBlockNamespace ? "block namespace" : "file-scoped namespace";
        string steps = string.Join(
            " + ",
            new[] { "move usings", "file-scoped namespace", "multiple blank lines", "file header", "remove and sort usings" }
                .Where((_, index) => (bool)data[index + 1]));

        return $"{input}: {(steps.Length == 0 ? "no Roslyn step" : steps)}";
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task ClosedFile_EverySemanticTextAndRoslynStepEnabled_CleansTheSampleInOnePass()
    {
        EnableEveryRoslynStep();
        Settings.Default.Cleaning_SealClassesWhenSafe = true;
        Settings.Default.Cleaning_RunVisualStudioRemoveAndSortUsingStatements = true;

        string output = await CleanClosedFileAsync("Widget.cs", SampleInput);

        Assert.AreEqual(SampleOutputEditor.Replace("using System.Linq;\r\n", string.Empty), output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task OpenFile_EverySemanticTextAndRoslynStepEnabled_CleansTheSampleInOnePass()
    {
        EnableEveryRoslynStep();
        Settings.Default.Cleaning_SealClassesWhenSafe = true;

        string output = await CleanOpenFileAsync("Widget.cs", SampleInput);

        Assert.AreEqual(SampleOutputEditor, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DynamicData(nameof(PipelineModes), DynamicDataDisplayName = nameof(PipelineModeName))]
    public async Task UsingsInsideBlockNamespace_MovedOutsideAndFileScoped_StartsWithTheFirstUsing(bool closedFile)
    {
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = true;
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = true;

        string output = await CleanAsync(closedFile, "Widget.cs", UsingsInsideBlockNamespace);

        Assert.AreEqual(
            "using System;\r\nusing System.Collections.Generic;\r\n\r\nnamespace Sample.Core;\r\n\r\ninternal class Widget\r\n{\r\n    private List<string> Names(Action done) => new List<string>();\r\n}\r\n",
            output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DynamicData(nameof(NamespaceStepCombinations), DynamicDataDisplayName = nameof(NamespaceStepCombinationName))]
    public async Task NamespaceAndUsingSteps_AnyCombination_ProduceAFileWithoutLeadingBlankLineThatTheSecondRunLeavesUnchanged(
        string input,
        bool moveUsingsOutside,
        bool fileScopedNamespace,
        bool removeMultipleBlankLines,
        bool fileHeader,
        bool removeAndSortUsings)
    {
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = moveUsingsOutside;
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = fileScopedNamespace;
        Settings.Default.Cleaning_RemoveMultipleConsecutiveBlankLines = removeMultipleBlankLines;
        Settings.Default.Cleaning_RunVisualStudioRemoveAndSortUsingStatements = removeAndSortUsings;
        if (fileHeader)
        {
            Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// Copyright Sample";
        }

        string filePath = WriteFile("Widget.cs", input);
        string first = await RunClosedAsync(filePath);
        string second = await RunClosedAsync(filePath);

        bool inputIsFileScoped = input == UsingsAfterFileScopedNamespace;
        string expectedStart = fileHeader ? "// Copyright Sample\r\n" : moveUsingsOutside ? "using " : "namespace Sample.Core";
        Assert.IsTrue(first.StartsWith(expectedStart, StringComparison.Ordinal), "The file must start with its first line of content, not with a blank line.\r\n" + first);
        Assert.IsTrue(first.EndsWith("}\r\n", StringComparison.Ordinal) && !first.EndsWith("\r\n\r\n", StringComparison.Ordinal), "The file ends with exactly one final newline.");
        Assert.IsTrue(!removeMultipleBlankLines || !first.Contains("\r\n\r\n\r\n"), "No two consecutive blank lines remain.\r\n" + first);
        Assert.AreEqual(fileScopedNamespace || inputIsFileScoped, first.Contains("namespace Sample.Core;"));
        Assert.AreEqual(moveUsingsOutside, first.IndexOf("using System;", StringComparison.Ordinal) < first.IndexOf("namespace Sample.Core", StringComparison.Ordinal));
        Assert.AreEqual(0, CountSyntaxErrors(first));
        Assert.AreEqual(first, second, "A second cleanup must not change the file.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(SampleInput, DisplayName = "sample with every Roslyn step")]
    [DataRow(RegionsAndConditionals, DisplayName = "regions and conditional compilation")]
    [DataRow(Attributes, DisplayName = "attributes followed by blank lines")]
    [DataRow(NestedNamespaces, DisplayName = "nested namespaces")]
    [DataRow(TopLevelStatements, DisplayName = "top-level statements")]
    [DataRow(AlreadyClean, DisplayName = "already clean file")]
    [DataRow(SyntaxErrors, DisplayName = "file with syntax errors")]
    [DataRow(UsingsAfterFileScopedNamespace, DisplayName = "usings after a file-scoped namespace")]
    public async Task ClosedFile_EveryRoslynStepEnabled_TheSecondRunLeavesTheFileUnchanged(string input)
    {
        EnableEveryRoslynStep();
        Settings.Default.Cleaning_SealClassesWhenSafe = true;
        string filePath = WriteFile("Probe.cs", input);

        string first = await RunClosedAsync(filePath);
        string second = await RunClosedAsync(filePath);

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(SampleInput, DisplayName = "sample with every Roslyn step")]
    [DataRow(RegionsAndConditionals, DisplayName = "regions and conditional compilation")]
    [DataRow(Attributes, DisplayName = "attributes followed by blank lines")]
    [DataRow(NestedNamespaces, DisplayName = "nested namespaces")]
    [DataRow(TopLevelStatements, DisplayName = "top-level statements")]
    [DataRow(AlreadyClean, DisplayName = "already clean file")]
    [DataRow(SyntaxErrors, DisplayName = "file with syntax errors")]
    [DataRow(UsingsAfterFileScopedNamespace, DisplayName = "usings after a file-scoped namespace")]
    public async Task OpenFile_EveryRoslynStepEnabled_TheSecondRunLeavesTheTextUnchanged(string input)
    {
        EnableEveryRoslynStep();
        Settings.Default.Cleaning_SealClassesWhenSafe = true;

        string first = await CleanOpenFileAsync("Probe.cs", input);
        string second = await CleanOpenFileAsync("Probe.cs", first);

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task ClosedFile_AlreadyCleanFile_IsLeftByteIdentical()
    {
        EnableEveryRoslynStep();
        Settings.Default.Cleaning_SealClassesWhenSafe = true;
        string filePath = WriteFile("Probe.cs", AlreadyClean);
        byte[] before = File.ReadAllBytes(filePath);

        await RunClosedAsync(filePath);

        Assert.AreSequenceEqual(before, File.ReadAllBytes(filePath));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task ClosedFile_SyntaxErrors_CleanupAddsNoFurtherError()
    {
        EnableEveryRoslynStep();
        string filePath = WriteFile("Broken.cs", SyntaxErrors);
        int errorsBefore = CountSyntaxErrors(SyntaxErrors);

        string output = await RunClosedAsync(filePath);

        Assert.AreEqual(1, errorsBefore);
        Assert.AreEqual(errorsBefore, CountSyntaxErrors(output));
        Assert.StartsWith("using System;\r\n\r\nnamespace Broken;\r\n", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("class Probe\n{\n    int Get()\n    {\n        return 1;\n    }\n}\n", "crlf", "\r\n", DisplayName = "LF source, end_of_line = crlf")]
    [DataRow("class Probe\r\n{\r\n    int Get()\r\n    {\r\n        return 1;\r\n    }\r\n}\r\n", "lf", "\n", DisplayName = "CRLF source, end_of_line = lf")]
    public async Task ClosedFile_EndOfLineInEditorConfig_IsTheOnlyLineEndingOfTheCleanedFile(string input, string endOfLine, string expectedLineEnding)
    {
        EnableEveryRoslynStep();
        WriteEditorConfig($"end_of_line = {endOfLine}");

        string output = await CleanClosedFileAsync("Probe.cs", input);

        Assert.AreEqual(
            "internal class Probe\n{\n    private int Get()\n    {\n        return 1;\n    }\n}\n".Replace("\n", expectedLineEnding),
            output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("lf", "\n")]
    [DataRow("crlf", "\r\n")]
    public async Task OpenFile_EndOfLineInEditorConfig_IsTheOnlyLineEndingOfTheCleanedText(string endOfLine, string expectedLineEnding)
    {
        EnableEveryRoslynStep();
        WriteEditorConfig($"end_of_line = {endOfLine}");

        string output = await CleanOpenFileAsync("Probe.cs", UsingsInsideBlockNamespace.Replace("\r\n", "\n"));

        string withoutLineEndings = output.Replace(expectedLineEnding, string.Empty);
        Assert.AreEqual(-1, withoutLineEndings.IndexOfAny(new[] { '\r', '\n' }), "Only the configured line ending is used.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(true, true, false, DisplayName = "BOM removed when Remove Byte Order Mark is on")]
    [DataRow(true, false, false, DisplayName = "BOM-less file stays BOM-less when Remove Byte Order Mark is on")]
    [DataRow(false, true, true, DisplayName = "BOM kept when Remove Byte Order Mark is off")]
    [DataRow(false, false, false, DisplayName = "BOM-less file stays BOM-less when Remove Byte Order Mark is off")]
    public async Task ClosedFile_ByteOrderMark_IsRemovedOrKeptAsConfiguredWhileTheRoslynStepsRewriteTheFile(bool removeByteOrderMark, bool fileHasBom, bool expectBom)
    {
        EnableEveryRoslynStep();
        Settings.Default.Cleaning_RemoveByteOrderMark = removeByteOrderMark;
        string filePath = WriteFile("Widget.cs", UsingsInsideBlockNamespace, new UTF8Encoding(fileHasBom));

        await RunClosedAsync(filePath);

        byte[] bytes = File.ReadAllBytes(filePath);
        Assert.AreEqual(expectBom, bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.StartsWith("using System;\r\n", Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("", "}\r\n", DisplayName = "final newline added")]
    [DataRow("insert_final_newline = true", "}\r\n", DisplayName = "final newline enforced")]
    [DataRow("insert_final_newline = false", "}", DisplayName = "final newline removed")]
    public async Task ClosedFile_FinalNewline_FollowsTheEditorConfig(string option, string expectedEnd)
    {
        EnableEveryRoslynStep();
        WriteEditorConfig("end_of_line = crlf", option);

        string output = await CleanClosedFileAsync("Widget.cs", UsingsInsideBlockNamespace.TrimEnd('\r', '\n') + "\r\n\r\n\r\n");

        Assert.IsTrue(output.EndsWith(expectedEnd, StringComparison.Ordinal) && !output.EndsWith(expectedEnd + "\r\n", StringComparison.Ordinal), output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task ClosedFile_EditorConfigBlockScopedNamespace_BeatsTheEnabledUserSetting()
    {
        EnableEveryRoslynStep();
        WriteEditorConfig("end_of_line = crlf", "csharp_style_namespace_declarations = block_scoped:suggestion");

        string output = await CleanClosedFileAsync("Widget.cs", UsingsInsideBlockNamespace);

        Assert.AreEqual(
            "using System;\r\nusing System.Collections.Generic;\r\n\r\nnamespace Sample.Core\r\n{\r\n    internal class Widget\r\n    {\r\n        private List<string> Names(Action done) => new List<string>();\r\n    }\r\n}\r\n",
            output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task ClosedFile_PolicyFileScopedNamespace_BeatsTheDisabledUserSetting()
    {
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = false;
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = true;
        WritePolicy("\"convertToFileScopedNamespace\": true");

        string output = await CleanClosedFileAsync("Widget.cs", UsingsInsideBlockNamespace);

        Assert.StartsWith("using System;\r\nusing System.Collections.Generic;\r\n\r\nnamespace Sample.Core;\r\n", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task ClosedFile_PolicyDisablingVar_BeatsTheEnabledUserSetting()
    {
        Settings.Default.Cleaning_ConvertToVarWhenApparent = true;
        WritePolicy("\"convertToVarWhenApparent\": false");

        string output = await CleanClosedFileAsync("Widget.cs", SampleInput);

        Assert.Contains("Dictionary<string, int> map = new Dictionary<string, int>();", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task ClosedFile_EditorConfigInsideNamespacePlacement_BeatsThePolicyThatMovesUsingsOutside()
    {
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = false;
        WritePolicy("\"moveUsingsOutsideNamespace\": true");
        WriteEditorConfig("end_of_line = crlf", "csharp_using_directive_placement = inside_namespace:suggestion");

        string output = await CleanClosedFileAsync("Widget.cs", UsingsInsideBlockNamespace);

        Assert.StartsWith("namespace Sample.Core\r\n{\r\n    using System;\r\n    using System.Collections.Generic;\r\n\r\n", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task ClosedFile_PolicyKeepsRegionsWhileTheOtherTextStepsStillRun()
    {
        EnableEveryRoslynStep();
        WritePolicy("\"removeRegions\": false");

        string output = await CleanClosedFileAsync("Probe.cs", RegionsAndConditionals);

        Assert.Contains("#region Types", output);
        Assert.Contains("#endregion", output);
        Assert.StartsWith("using System;\r\n", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task ClosedFile_RegionsAreRemovedAndConditionalCompilationIsKept()
    {
        EnableEveryRoslynStep();

        string output = await CleanClosedFileAsync("Probe.cs", RegionsAndConditionals);

        Assert.DoesNotContain("#region", output);
        Assert.DoesNotContain("#endregion", output);
        Assert.Contains("#if DEBUG\r\nusing System.Diagnostics;\r\n#endif", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(SampleInput, DisplayName = "sample with every Roslyn step")]
    [DataRow(RegionsAndConditionals, DisplayName = "regions and conditional compilation")]
    [DataRow(Attributes, DisplayName = "attributes followed by blank lines")]
    [DataRow(NestedNamespaces, DisplayName = "nested namespaces")]
    [DataRow(TopLevelStatements, DisplayName = "top-level statements")]
    [DataRow(AlreadyClean, DisplayName = "already clean file")]
    [DataRow(SyntaxErrors, DisplayName = "file with syntax errors")]
    [DataRow(UsingsInsideBlockNamespace, DisplayName = "usings inside a block-scoped namespace")]
    [DataRow(UsingsAfterFileScopedNamespace, DisplayName = "usings after a file-scoped namespace")]
    public async Task ClosedFileAndOpenFile_WithoutTheVisualStudioCommands_ProduceTheSameText(string input)
    {
        EnableEveryRoslynStep();
        Settings.Default.Cleaning_SealClassesWhenSafe = true;
        Settings.Default.Cleaning_RunVisualStudioFormatDocumentCommand = false;
        Settings.Default.Cleaning_RunVisualStudioRemoveAndSortUsingStatements = false;

        string closed = await CleanClosedFileAsync("Closed.cs", input);
        string open = await CleanOpenFileAsync("Open.cs", input);

        Assert.AreEqual(closed, open);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task DiagnosticCleanup_MovingUsingsAboveAFileScopedNamespace_LeavesNoBlankLineAtTheStart()
    {
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = true;
        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(Path.Combine(_directory, "Widget.cs"));

        string output = await ApplyRoslynStepsAsync(Path.Combine(_directory, "Widget.cs"), UsingsAfterFileScopedNamespace, settings, closedFile: false);

        Assert.StartsWith("using System;\r\nusing System.Collections.Generic;\r\n", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task DiagnosticCleanup_FileThatStartedWithBlankLines_IsNotStrippedWhenAFixMovesTheUsings()
    {
        Settings.Default.Cleaning_MoveUsingsOutsideNamespace = true;
        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(Path.Combine(_directory, "Widget.cs"));

        string output = await ApplyRoslynStepsAsync(Path.Combine(_directory, "Widget.cs"), "\r\n\r\n" + UsingsAfterFileScopedNamespace, settings, closedFile: false);

        Assert.StartsWith("\r\nusing System;\r\n", output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task ClosedFile_StepsDisabled_LeaveTheSemanticsOfTheSampleUntouched()
    {
        foreach (string settingName in RoslynStepSettings())
        {
            Settings.Default[settingName] = false;
        }

        Settings.Default.Cleaning_RunVisualStudioFormatDocumentCommand = false;
        Settings.Default.Cleaning_RunVisualStudioRemoveAndSortUsingStatements = false;

        string output = await CleanClosedFileAsync("Widget.cs", SampleInput);

        Assert.Contains("    using System;", output, "Usings stay inside the namespace.");
        Assert.Contains("namespace Sample.Core\r\n{", output);
        Assert.Contains("int parsed;", output);
        Assert.Contains("Dictionary<string, int> map = new Dictionary<string, int>();", output);
        Assert.Contains("x => { return x * 2; }", output);
        Assert.Contains("new int[] { 1, 2, 3 }", output);
        Assert.DoesNotContain("sealed", output);
        Assert.DoesNotContain("readonly", output);
    }

    private static int CountSyntaxErrors(string text)
        => CSharpSyntaxTree.ParseText(text).GetDiagnostics().Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    private static string[] RoslynStepSettings() =>
    [
        nameof(Settings.Cleaning_ConvertToCollectionExpressions),
        nameof(Settings.Cleaning_ConvertToFileScopedNamespace),
        nameof(Settings.Cleaning_ConvertToVarWhenApparent),
        nameof(Settings.Cleaning_InlineOutVariableDeclarations),
        nameof(Settings.Cleaning_InsertExplicitAccessModifiers),
        nameof(Settings.Cleaning_MakeFieldsReadonlyWhenSafe),
        nameof(Settings.Cleaning_MoveUsingsOutsideNamespace),
        nameof(Settings.Cleaning_RemoveMultipleConsecutiveBlankLines),
        nameof(Settings.Cleaning_SimplifySingleStatementLambdas),
    ];

    private static void EnableEveryRoslynStep()
    {
        foreach (string settingName in RoslynStepSettings())
        {
            Settings.Default[settingName] = true;
        }
    }

    private void WriteEditorConfig(params string[] csharpOptions)
        => File.WriteAllText(
            Path.Combine(_directory, ".editorconfig"),
            "root = true\r\n\r\n[*.cs]\r\n" + string.Concat(csharpOptions.Where(option => option.Length > 0).Select(option => option + "\r\n")));

    private void WritePolicy(string cleanupEntries)
        => File.WriteAllText(Path.Combine(_directory, RepositoryCleanupSettings.PrimaryConfigFileName), "{ \"cleanup\": { " + cleanupEntries + " } }");

    private string WriteFile(string fileName, string text, Encoding encoding = null)
    {
        string filePath = Path.Combine(_directory, fileName);
        File.WriteAllText(filePath, text, encoding ?? new UTF8Encoding(false));

        return filePath;
    }

    private Task<string> CleanAsync(bool closedFile, string fileName, string input)
        => closedFile ? CleanClosedFileAsync(fileName, input) : CleanOpenFileAsync(fileName, input);

    private Task<string> CleanClosedFileAsync(string fileName, string input) => RunClosedAsync(WriteFile(fileName, input));

    /// <summary>
    /// Cleans a closed file on disk the way the cleanup of a closed file does and returns the text read back from disk.
    /// </summary>
    private async Task<string> RunClosedAsync(string filePath)
    {
        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(filePath);

        string onDisk = VisualStudioRoslynWorkspace.ReadFileText(filePath);
        string afterSemanticSteps = await ApplySemanticStepsAsync(filePath, onDisk, settings);
        if (afterSemanticSteps != onDisk)
        {
            Assert.AreEqual(ClosedFileWriteResult.Written, VisualStudioRoslynWorkspace.TryWriteClosedFileText(filePath, onDisk, afterSemanticSteps));
        }

        CodeCleanupManager.GetInstance(null).TryRunHeadlessPreCleanupForCSharpCore(filePath);

        string headlessText = VisualStudioRoslynWorkspace.ReadFileText(filePath);
        string cleaned = await ApplyRoslynStepsAsync(filePath, headlessText, settings, closedFile: true);
        if (cleaned != headlessText)
        {
            Assert.AreEqual(ClosedFileWriteResult.Written, VisualStudioRoslynWorkspace.TryWriteClosedFileText(filePath, headlessText, cleaned));
        }

        return VisualStudioRoslynWorkspace.ReadFileText(filePath);
    }

    /// <summary>
    /// Cleans the text of an open file: the same semantic and text steps, then the diagnostic cleanup of an open document.
    /// </summary>
    private async Task<string> CleanOpenFileAsync(string fileName, string input)
    {
        string filePath = Path.Combine(_directory, fileName);
        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(filePath);

        string afterSemanticSteps = await ApplySemanticStepsAsync(filePath, input, settings);
        string afterTextSteps = CodeCleanupManager.ApplyHeadlessCSharpTransformations(afterSemanticSteps, filePath);

        return await ApplyRoslynStepsAsync(filePath, afterTextSteps, settings, closedFile: false);
    }

    private async Task<string> ApplySemanticStepsAsync(string filePath, string text, EffectiveCleanupSettings settings)
    {
        if (settings.GetBoolean(nameof(Settings.Cleaning_SealClassesWhenSafe)))
        {
            using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
            Document document = CreateDocument(workspace, filePath, text);
            text = await new ClassSealingConverter().SealWhenSafeAsync(new[] { document }, CancellationToken.None);
        }

        if (settings.GetBoolean(nameof(Settings.Cleaning_ConvertToPatternMatchingNullChecks)))
        {
            using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
            Document document = CreateDocument(workspace, filePath, text);
            text = await new NullCheckPatternMatchingConverter().ConvertAsync(new[] { document }, CancellationToken.None);
        }

        return text;
    }

    private async Task<string> ApplyRoslynStepsAsync(string filePath, string text, EffectiveCleanupSettings settings, bool closedFile)
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        Document document = CreateDocument(workspace, filePath, text);

        document = await RoslynDocumentCleanup.ApplyAsync(
            document,
            closedFile && settings.RunsRemoveAndSortUsings(isAutoSaveContext: false),
            closedFile && settings.GetBoolean(nameof(Settings.Cleaning_RunVisualStudioFormatDocumentCommand)),
            Array.Empty<string>(),
            CancellationToken.None);
        DiagnosticCleanupOptions options = new DiagnosticCleanupOptions(
            (DiagnosticCleanupCategory[])Enum.GetValues(typeof(DiagnosticCleanupCategory)),
            analyzerConfigOverrides: settings.AnalyzerConfigOverrides,
            usingDirectiveSorting: settings.GetUsingDirectiveSortingAfterFixes(isAutoSaveContext: false, isClosedFile: closedFile));
        DiagnosticCleanupResult result = await new DiagnosticCleanupEngine(new CodeFixProviderCatalog()).CleanupAsync(document, options, CancellationToken.None);

        return await DiagnosticCleanupTestWorkspace.GetTextAsync(result.HasChanges ? result.ChangedSolution : document.Project.Solution, document.Id);
    }

    private Document CreateDocument(DiagnosticCleanupTestWorkspace workspace, string filePath, string text)
    {
        workspace.AddEditorConfig(_directoryName, File.ReadAllText(Path.Combine(_directory, ".editorconfig")));
        DocumentId documentId = workspace.AddDocument(_directoryName + "/" + Path.GetFileName(filePath), text);

        return workspace.CreateSolution().GetDocument(documentId);
    }
}
