using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeJanitor.Logic.Transformations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="FileScopedNamespaceConverter" />.
/// Pure transformation tests (no Visual Studio / EnvDTE required).
/// </summary>
[TestClass]
public sealed class FileScopedNamespaceConverterTests
{
    private const string Library = "namespace Company.App.Services { public class Svc { } }\r\n";

    private static readonly SyntaxKind[] StringContentTokenKinds =
    {
        SyntaxKind.StringLiteralToken,
        SyntaxKind.MultiLineRawStringLiteralToken,
        SyntaxKind.InterpolatedStringTextToken,
        SyntaxKind.InterpolatedStringEndToken,
        SyntaxKind.InterpolatedRawStringEndToken,
    };

    private INamespaceScopeConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new FileScopedNamespaceConverter();
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsSingleBlockNamespaceToFileScoped()
    {
        string input = "namespace A\r\n{\r\n    class C\r\n    {\r\n    }\r\n}\r\n";
        string expected = "namespace A;\r\n\r\nclass C\r\n{\r\n}\r\n";

        Assert.AreEqual(expected, _converter.ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task KeepsNamespaceUsingsInsideFileScopedNamespace_SoRelativeUsingsStillCompile()
    {
        string input = "namespace Company.App\r\n{\r\n    using Services;\r\n\r\n    class C { Svc s; }\r\n}\r\n";
        string expected = "namespace Company.App;\r\n\r\nusing Services;\r\n\r\nclass C { Svc s; }\r\n";

        string result = _converter.ConvertToFileScoped(input);

        Assert.AreEqual(expected, result);
        IReadOnlyList<string> errors = await CompilingTestProject.GetCompileErrorsAsync(CompilingTestProject.CreateDocument(input, Library), result);
        Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertingAfterSemanticUsingMove_YieldsCompilingFileScopedCode()
    {
        string input = "namespace Company.App\r\n{\r\n    using Services;\r\n\r\n    class C { Svc s; }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, Library);

        UsingDirectivePlacementResult moved = await new UsingDirectivePlacementConverter().MoveUsingsOutsideAsync(document, CancellationToken.None);
        string result = _converter.ConvertToFileScoped(moved.Text);

        Assert.AreEqual("using Company.App.Services;\r\n\r\nnamespace Company.App;\r\n\r\nclass C { Svc s; }\r\n", result);
        IReadOnlyList<string> errors = await CompilingTestProject.GetCompileErrorsAsync(document, result);
        Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void AlreadyFileScoped_ReturnsUnchanged()
    {
        string input = "namespace A;\r\n\r\nclass C\r\n{\r\n}\r\n";

        Assert.AreEqual(input, _converter.ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void MultipleNamespaces_ReturnsUnchanged()
    {
        string input = "namespace A\r\n{\r\n}\r\nnamespace B\r\n{\r\n}\r\n";

        Assert.AreEqual(input, _converter.ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NoNamespace_ReturnsUnchanged()
    {
        string input = "class C\r\n{\r\n}\r\n";

        Assert.AreEqual(input, _converter.ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NestedNamespace_ReturnsUnchanged()
    {
        string input = "namespace A\r\n{\r\n    namespace B\r\n    {\r\n    }\r\n}\r\n";

        Assert.AreEqual(input, _converter.ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void PreservesFileHeaderAndOuterUsings()
    {
        string input = "// file header\r\nusing System;\r\n\r\nnamespace A\r\n{\r\n    class C\r\n    {\r\n    }\r\n}\r\n";
        string expected = "// file header\r\nusing System;\r\n\r\nnamespace A;\r\n\r\nclass C\r\n{\r\n}\r\n";

        Assert.AreEqual(expected, _converter.ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void HasMultipleNamespaces_MultipleTopLevelNamespaces_ReturnsTrue()
    {
        string input = "namespace A\r\n{\r\n}\r\nnamespace B\r\n{\r\n}\r\n";

        Assert.IsTrue(_converter.HasMultipleNamespaces(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void HasMultipleNamespaces_NestedNamespace_ReturnsTrue()
    {
        string input = "namespace A\r\n{\r\n    namespace B\r\n    {\r\n    }\r\n}\r\n";

        Assert.IsTrue(_converter.HasMultipleNamespaces(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void HasMultipleNamespaces_SingleBlockNamespace_ReturnsFalse()
    {
        string input = "namespace A\r\n{\r\n    class C\r\n    {\r\n    }\r\n}\r\n";

        Assert.IsFalse(_converter.HasMultipleNamespaces(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void HasMultipleNamespaces_SingleFileScopedNamespace_ReturnsFalse()
    {
        string input = "namespace A;\r\n\r\nclass C\r\n{\r\n}\r\n";

        Assert.IsFalse(_converter.HasMultipleNamespaces(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NameAndApply_WorkCorrectly()
    {
        FileScopedNamespaceConverter converter = new FileScopedNamespaceConverter();
        Assert.AreEqual("File-Scoped Namespace", converter.Name);

        string input = "namespace A\r\n{\r\n    class C { }\r\n}\r\n";
        string expected = "namespace A;\r\n\r\nclass C { }\r\n";
        Assert.AreEqual(expected, converter.Apply(input));

        Assert.IsNull(converter.Apply(null));
        Assert.AreEqual(string.Empty, converter.Apply(string.Empty));
        Assert.IsFalse(converter.HasMultipleNamespaces(null));
        Assert.IsFalse(converter.HasMultipleNamespaces(string.Empty));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Dedent_HandlesTabsAndEmptyBody()
    {
        string tabInput = "namespace A\n{\n\tclass C\n\t{\n\t}\n}\n";
        string expectedTab = "namespace A;\n\nclass C\n{\n}\n";
        Assert.AreEqual(expectedTab, _converter.ConvertToFileScoped(tabInput));

        string emptyInput = "namespace A\n{\n}\n";
        string expectedEmpty = "namespace A;\n";
        Assert.AreEqual(expectedEmpty, _converter.ConvertToFileScoped(emptyInput));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertToFileScoped_MultiLineStringLiteralsAndDisabledText_StayByteIdentical()
    {
        string input =
            "namespace A\r\n" +
            "{\r\n" +
            "    class C\r\n" +
            "    {\r\n" +
            "        const string Verbatim = @\"first\r\n" +
            "    second\r\n" +
            "        third\";\r\n" +
            "\r\n" +
            "        string Raw = \"\"\"\r\n" +
            "            raw\r\n" +
            "              indented\r\n" +
            "            \"\"\";\r\n" +
            "\r\n" +
            "        string InterpolatedRaw(int x) => $\"\"\"\r\n" +
            "            {x} raw\r\n" +
            "            \"\"\";\r\n" +
            "#if NEVER\r\n" +
            "        void Disabled() { }\r\n" +
            "#endif\r\n" +
            "    }\r\n" +
            "}\r\n";
        string expected =
            "namespace A;\r\n" +
            "\r\n" +
            "class C\r\n" +
            "{\r\n" +
            "    const string Verbatim = @\"first\r\n" +
            "    second\r\n" +
            "        third\";\r\n" +
            "\r\n" +
            "    string Raw = \"\"\"\r\n" +
            "            raw\r\n" +
            "              indented\r\n" +
            "            \"\"\";\r\n" +
            "\r\n" +
            "    string InterpolatedRaw(int x) => $\"\"\"\r\n" +
            "            {x} raw\r\n" +
            "            \"\"\";\r\n" +
            "#if NEVER\r\n" +
            "        void Disabled() { }\r\n" +
            "#endif\r\n" +
            "}\r\n";

        string result = _converter.ConvertToFileScoped(input);

        Assert.AreEqual(expected, result);
        Assert.AreSequenceEqual(GetStringContentTokens(input), GetStringContentTokens(result));
        await AssertCompilesAsync(CompilingTestProject.CreateDocument(input), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void MissingBraceOrMalformed_ReturnsUnchanged()
    {
        string input = "namespace A";
        Assert.AreEqual(input, _converter.ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void HasMultipleNamespaces_NoNamespace_ReturnsFalse()
    {
        string input = "class C\r\n{\r\n}\r\n";

        Assert.IsFalse(_converter.HasMultipleNamespaces(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsRealWorldReproFile_MultipleClassesFieldsPropertiesAndMethods()
    {
        // Mirrors the actual manual-test repro file used while diagnosing BUG-001
        // (scratch/BUG-001-repro, later copied into a playground console app), after a
        // Cleanup pass had already reorganized it: a single block-scoped namespace
        // containing two classes, with fields, properties, and methods (including a
        // private helper and local variable declarations).
        string input =
            "namespace CodeJanitor.Scratch.Bug001\r\n" +
            "{\r\n" +
            "    /// <summary>\r\n" +
            "    /// Repro for BUG-001: running CodeJanitor Cleanup/Reorganize on a file with a\r\n" +
            "    /// file-scoped namespace (C# 10+) should NOT remove or corrupt the\r\n" +
            "    /// \"namespace CodeJanitor.Scratch.Bug001;\" declaration line above.\r\n" +
            "    /// </summary>\r\n" +
            "\r\n" +
            "    public class FileScopedNamespaceSample\r\n" +
            "    {\r\n" +
            "        private List<string> _items = new List<string>();\r\n" +
            "\r\n" +
            "        public string Name { get; set; }\r\n" +
            "\r\n" +
            "        public void DoWork()\r\n" +
            "        {\r\n" +
            "            Console.WriteLine(\"hello\");\r\n" +
            "            var list = new List<string>();\r\n" +
            "            list.Add(\"a\");\r\n" +
            "        }\r\n" +
            "\r\n" +
            "        private void PrivateHelper()\r\n" +
            "        {\r\n" +
            "            int x = 5;\r\n" +
            "\r\n" +
            "            Console.WriteLine(x);\r\n" +
            "        }\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    public class SecondClassInSameNamespace\r\n" +
            "    {\r\n" +
            "        public int Value { get; set; }\r\n" +
            "    }\r\n" +
            "}\r\n";

        Assert.IsFalse(_converter.HasMultipleNamespaces(input), "Repro file has exactly one namespace and should not be flagged as multiple.");

        string converted = _converter.ConvertToFileScoped(input);

        Assert.AreNotEqual(input, converted, "Expected the converter to change the block-scoped namespace to file-scoped.");
        Assert.Contains("namespace CodeJanitor.Scratch.Bug001;", converted);
        Assert.Contains("public class FileScopedNamespaceSample", converted);
        Assert.Contains("public class SecondClassInSameNamespace", converted);
        Assert.Contains("public int Value { get; set; }", converted);

        // The dedented body should no longer contain the original 4-space class indentation.
        Assert.DoesNotContain("\r\n    public class FileScopedNamespaceSample", converted,
            "Expected class declarations to be dedented by one level after file-scoped conversion.");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertToBlockScoped_WrapsTheBodyInBracesAndIndentsItOneLevel()
    {
        string input = "namespace A;\r\n\r\nclass C\r\n{\r\n    void M() { }\r\n}\r\n";
        string expected = "namespace A\r\n{\r\n    class C\r\n    {\r\n        void M() { }\r\n    }\r\n}\r\n";

        Assert.AreEqual(expected, _converter.ConvertToBlockScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertToBlockScoped_LineFeeds_ArePreserved()
    {
        string input = "namespace A;\n\nclass C\n{\n    int x;\n}\n";
        string expected = "namespace A\n{\n    class C\n    {\n        int x;\n    }\n}\n";

        Assert.AreEqual(expected, _converter.ConvertToBlockScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertToBlockScoped_KeepsFileHeaderAndTopLevelUsings_AndMovesUsingsAfterTheDeclarationIntoTheBlock()
    {
        string input =
            "// <copyright>\r\n// ACME\r\n// </copyright>\r\n\r\nusing System;\r\n\r\nnamespace Company.App;\r\n\r\n" +
            "using Services;\r\n\r\nclass C\r\n{\r\n    Svc s;\r\n    Type t;\r\n}\r\n";
        string expected =
            "// <copyright>\r\n// ACME\r\n// </copyright>\r\n\r\nusing System;\r\n\r\nnamespace Company.App\r\n{\r\n" +
            "    using Services;\r\n\r\n    class C\r\n    {\r\n        Svc s;\r\n        Type t;\r\n    }\r\n}\r\n";

        string result = _converter.ConvertToBlockScoped(input);

        Assert.AreEqual(expected, result);
        await AssertCompilesAsync(CompilingTestProject.CreateDocument(input, Library), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertToBlockScoped_KeepsExternAliasesAndUsingsAboveTheNamespace()
    {
        string input = "extern alias Lib;\n\nusing System;\n\nnamespace A;\n\nclass C\n{\n    Lib::L.X x;\n    Type t;\n}\n";
        string expected = "extern alias Lib;\n\nusing System;\n\nnamespace A\n{\n    class C\n    {\n        Lib::L.X x;\n        Type t;\n    }\n}\n";
        MetadataReference reference = CompilingTestProject.CreateAliasedReference("AliasedLibrary", "namespace L { public class X { } }\r\n", "Lib");

        string result = _converter.ConvertToBlockScoped(input);

        Assert.AreEqual(expected, result);
        await AssertCompilesAsync(CompilingTestProject.CreateDocument(input, LanguageVersion.Latest, new[] { reference }), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertToBlockScoped_MultiLineStringLiterals_StayByteIdentical()
    {
        string input =
            "namespace A;\r\n" +
            "\r\n" +
            "class C\r\n" +
            "{\r\n" +
            "    const string Verbatim = @\"first\r\n" +
            "  second\r\n" +
            "third\";\r\n" +
            "\r\n" +
            "    string Raw = \"\"\"\r\n" +
            "        raw line\r\n" +
            "          indented\r\n" +
            "        \"\"\";\r\n" +
            "\r\n" +
            "    string Interpolated(int x) => $@\"value\r\n" +
            "{x} end\r\n" +
            "    tail\r\n" +
            "\";\r\n" +
            "\r\n" +
            "    string InterpolatedRaw(int x) => $\"\"\"\r\n" +
            "        {x} raw\r\n" +
            "          more\r\n" +
            "        \"\"\";\r\n" +
            "\r\n" +
            "    string Hole(int x) => $@\"a{\r\n" +
            "        x\r\n" +
            "    }b\";\r\n" +
            "}\r\n";
        string expected =
            "namespace A\r\n" +
            "{\r\n" +
            "    class C\r\n" +
            "    {\r\n" +
            "        const string Verbatim = @\"first\r\n" +
            "  second\r\n" +
            "third\";\r\n" +
            "\r\n" +
            "        string Raw = \"\"\"\r\n" +
            "        raw line\r\n" +
            "          indented\r\n" +
            "        \"\"\";\r\n" +
            "\r\n" +
            "        string Interpolated(int x) => $@\"value\r\n" +
            "{x} end\r\n" +
            "    tail\r\n" +
            "\";\r\n" +
            "\r\n" +
            "        string InterpolatedRaw(int x) => $\"\"\"\r\n" +
            "        {x} raw\r\n" +
            "          more\r\n" +
            "        \"\"\";\r\n" +
            "\r\n" +
            "        string Hole(int x) => $@\"a{\r\n" +
            "            x\r\n" +
            "        }b\";\r\n" +
            "    }\r\n" +
            "}\r\n";

        string result = _converter.ConvertToBlockScoped(input);

        Assert.AreEqual(expected, result);
        Assert.AreSequenceEqual(GetStringContentTokens(input), GetStringContentTokens(result));
        await AssertCompilesAsync(CompilingTestProject.CreateDocument(input), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertToBlockScoped_IndentsActiveCode_KeepsDisabledTextAndColumnZeroDirectives_AndCompilesInEveryConfiguration()
    {
        string input = "namespace A;\n\nclass C\n{\n#if DEBUG\n    void Debug() { }\n#else\n    void Release() { }\n#endif\n}\n";
        string expected = "namespace A\n{\n    class C\n    {\n#if DEBUG\n    void Debug() { }\n#else\n        void Release() { }\n#endif\n    }\n}\n";

        string result = _converter.ConvertToBlockScoped(input);

        Assert.AreEqual(expected, result);
        await AssertCompilesAsync(CompilingTestProject.CreateDocument(input), result);
        await AssertCompilesAsync(CreateDebugDocument(input), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertToBlockScoped_DirectivesAndCommentsAfterTheLastMember_StayInsideTheBraces()
    {
        string input = "namespace A;\n\nclass C { }\n\n#if DEBUG\nclass D { }\n#endif\n// trailing comment\n";
        string expected = "namespace A\n{\n    class C { }\n\n#if DEBUG\nclass D { }\n#endif\n    // trailing comment\n}\n";

        string result = _converter.ConvertToBlockScoped(input);

        Assert.AreEqual(expected, result);
        await AssertCompilesAsync(CompilingTestProject.CreateDocument(input), result);
        await AssertCompilesAsync(CreateDebugDocument(input), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertToBlockScoped_NestedTypesDocumentationCommentsAndDeclarationComment_CompileAfterConversion()
    {
        string input =
            "namespace A.B; // the namespace\r\n" +
            "\r\n" +
            "/// <summary>\r\n" +
            "/// Outer type.\r\n" +
            "/// </summary>\r\n" +
            "[System.Serializable]\r\n" +
            "public class Outer\r\n" +
            "{\r\n" +
            "    public class Inner\r\n" +
            "    {\r\n" +
            "        public enum Kind\r\n" +
            "        {\r\n" +
            "            One,\r\n" +
            "            Two,\r\n" +
            "        }\r\n" +
            "    }\r\n" +
            "}\r\n" +
            "\r\n" +
            "public interface IThing { Outer.Inner.Kind Kind { get; } }\r\n";
        string expected =
            "namespace A.B // the namespace\r\n" +
            "{\r\n" +
            "    /// <summary>\r\n" +
            "    /// Outer type.\r\n" +
            "    /// </summary>\r\n" +
            "    [System.Serializable]\r\n" +
            "    public class Outer\r\n" +
            "    {\r\n" +
            "        public class Inner\r\n" +
            "        {\r\n" +
            "            public enum Kind\r\n" +
            "            {\r\n" +
            "                One,\r\n" +
            "                Two,\r\n" +
            "            }\r\n" +
            "        }\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    public interface IThing { Outer.Inner.Kind Kind { get; } }\r\n" +
            "}\r\n";

        string result = _converter.ConvertToBlockScoped(input);

        Assert.AreEqual(expected, result);
        await AssertCompilesAsync(CompilingTestProject.CreateDocument(input), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertToBlockScoped_MultiLineCommentContinuationLines_KeepTheirText()
    {
        string input = "namespace A;\n\n/*\n * Block comment\n */\nclass C { }\n";
        string expected = "namespace A\n{\n    /*\n * Block comment\n */\n    class C { }\n}\n";

        Assert.AreEqual(expected, _converter.ConvertToBlockScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertToBlockScoped_TabIndentedBody_IsIndentedWithATab()
    {
        string input = "namespace A;\n\nclass C\n{\n\tint x;\n}\n";
        string expected = "namespace A\n{\n\tclass C\n\t{\n\t\tint x;\n\t}\n}\n";

        Assert.AreEqual(expected, _converter.ConvertToBlockScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("namespace A;\n\nclass C { }", "namespace A\n{\n    class C { }\n}", DisplayName = "no final line break")]
    [DataRow("namespace A;\n", "namespace A\n{\n}\n", DisplayName = "empty body")]
    [DataRow("namespace A;", "namespace A\r\n{\r\n}", DisplayName = "single line without line break")]
    [DataRow("namespace A; class C { }\n", "namespace A\n{\n    class C { }\n}\n", DisplayName = "member on the declaration line")]
    [DataRow("namespace A;\n\nclass C { }\n\n\n", "namespace A\n{\n    class C { }\n}\n", DisplayName = "trailing blank lines")]
    public void ConvertToBlockScoped_LineBreakEdgeCases(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.ConvertToBlockScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C\n{\n}\n", DisplayName = "no namespace")]
    [DataRow("namespace A\n{\n    class C { }\n}\n", DisplayName = "block-scoped namespace")]
    [DataRow("namespace A;\nnamespace B;\n", DisplayName = "two file-scoped namespaces")]
    [DataRow("namespace A;\n\nnamespace B\n{\n}\n", DisplayName = "file-scoped and block-scoped namespaces")]
    [DataRow("namespace A;\n\nclass C {\n", DisplayName = "syntax error")]
    [DataRow("#if true\nnamespace A;\n\nclass C { }\n#endif\n", DisplayName = "namespace inside #if")]
    public void ConvertToBlockScoped_NotExactlyOneConvertibleFileScopedNamespace_ReturnsUnchanged(string input)
    {
        Assert.AreEqual(input, _converter.ConvertToBlockScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertToBlockScoped_NullOrEmpty_ReturnsInput()
    {
        Assert.IsNull(_converter.ConvertToBlockScoped(null));
        Assert.AreEqual(string.Empty, _converter.ConvertToBlockScoped(string.Empty));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertToBlockScoped_ThenToFileScoped_RoundTrips()
    {
        string input = "using System;\r\n\r\nnamespace A;\r\n\r\nclass C\r\n{\r\n    void M() { }\r\n}\r\n";

        Assert.AreEqual(input, _converter.ConvertToFileScoped(_converter.ConvertToBlockScoped(input)));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertToFileScoped_KeepsTheDirectiveAndCommentAfterTheClosingBrace_AndCompilesInEveryConfiguration()
    {
        string input = "#if !LEGACY\r\nnamespace A\r\n{\r\n    class C { }\r\n} // namespace A\r\n\r\n#endif\r\n";
        string expected = "#if !LEGACY\r\nnamespace A;\r\n\r\nclass C { }\r\n// namespace A\r\n\r\n#endif\r\n";

        string result = _converter.ConvertToFileScoped(input);

        Assert.AreEqual(expected, result);
        await AssertCompilesAsync(CompilingTestProject.CreateDocument(input), result);
        await AssertCompilesAsync(
            CompilingTestProject.CreateDocument(input, new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: new[] { "LEGACY" }), new MetadataReference[0]),
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow(
        "#region File\r\nnamespace A\r\n{\r\n    class C { }\r\n}\r\n#endregion\r\n",
        "#region File\r\nnamespace A;\r\n\r\nclass C { }\r\n#endregion\r\n",
        DisplayName = "#endregion after the namespace")]
    [DataRow(
        "namespace A // the namespace\r\n{ // body\r\n    class C { }\r\n};\r\n/* end of file */\r\n",
        "namespace A; // the namespace\r\n// body\r\n\r\nclass C { }\r\n/* end of file */\r\n",
        DisplayName = "comments around the braces")]
    [DataRow(
        "#if DEBUG\r\nusing System.Diagnostics;\r\n#endif\r\nnamespace A\r\n{\r\n    class C { }\r\n}\r\n",
        "#if DEBUG\r\nusing System.Diagnostics;\r\n#endif\r\nnamespace A;\r\n\r\nclass C { }\r\n",
        DisplayName = "disabled using directive before the namespace")]
    public void ConvertToFileScoped_KeepsTheContentAroundTheBraces(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("namespace A\r\n{\r\n    class C { }\r\n}\r\nclass B { }\r\n", DisplayName = "type after the namespace")]
    [DataRow("class B { }\r\nnamespace A\r\n{\r\n    class C { }\r\n}\r\n", DisplayName = "type before the namespace")]
    [DataRow("namespace A\r\n{\r\n#if X\r\n    class C { }\r\n#else\r\n    class D { }\r\n}\r\n#endif\r\n", DisplayName = "#if block around the closing brace")]
    [DataRow("namespace A\r\n{\r\n    class C { }\r\n}\r\n#if X\r\nclass Extra { }\r\n#endif\r\n", DisplayName = "disabled text after the namespace")]
    [DataRow("namespace A\r\n#if X\r\n#endif\r\n{\r\n    class C { }\r\n}\r\n", DisplayName = "directive between the name and the body")]
    [DataRow("#if DEBUG\r\nclass Helper { }\r\n#endif\r\nnamespace A\r\n{\r\n    class C { }\r\n}\r\n", DisplayName = "type disabled before the namespace")]
    [DataRow("#if DEBUG\r\nnamespace M { }\r\n#endif\r\nnamespace A\r\n{\r\n    class C { }\r\n}\r\n", DisplayName = "namespace disabled before the namespace")]
    [DataRow("#if DEBUG\r\nclass Helper { }\r\n#endif\r\nusing System;\r\n\r\nnamespace A\r\n{\r\n    class C { }\r\n}\r\n", DisplayName = "type disabled before the using directives")]
    public void ConvertToFileScoped_LeavesTheFileUnchanged_WhenAFileScopedNamespaceCannotKeepItsContent(string input)
    {
        Assert.AreEqual(input, _converter.ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow(true, 4, "\t", DisplayName = "tabs")]
    [DataRow(false, 2, "  ", DisplayName = "two spaces")]
    public void ConvertToBlockScoped_IndentsWithTheConfiguredIndentation(bool indentWithTabs, int indentSize, string level)
    {
        string input = "namespace A;\n\nclass C\n{\n    int x;\n}\n";
        string expected = $"namespace A\n{{\n{level}class C\n{level}{{\n{level}    int x;\n{level}}}\n}}\n";

        Assert.AreEqual(expected, new FileScopedNamespaceConverter(indentWithTabs, indentSize).ConvertToBlockScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertToFileScoped_RemovesOneConfiguredIndentationLevel()
    {
        string input = "namespace A\n{\n  class C\n  {\n    int x;\n  }\n}\n";

        Assert.AreEqual("namespace A;\n\nclass C\n{\n  int x;\n}\n", new FileScopedNamespaceConverter(false, 2).ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow(0)]
    [DataRow(-4)]
    public void Constructor_NonPositiveIndentSize_Throws(int indentSize)
    {
        ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new FileScopedNamespaceConverter(null, indentSize));

        Assert.AreEqual("indentSize", exception.ParamName);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("   \r\n\t\r\n", DisplayName = "whitespace-only file")]
    [DataRow("namespace A\r\n{\r\n    class C { }\r\n", DisplayName = "missing closing brace")]
    [DataRow("namespace A\r\n{\r\n    class C { }\r\n}\r\n#pragma warning disable CS0168\r\n;\r\n", DisplayName = "directive before the semicolon after the closing brace")]
    [DataRow("#if true\r\nnamespace A\r\n{\r\n#else\r\nnamespace B\r\n{\r\n#endif\r\n    class C { }\r\n}\r\n", DisplayName = "#else in the body continues an #if before the namespace")]
    [DataRow("#if true\r\nnamespace A\r\n{\r\n#endif\r\n    class C { }\r\n}\r\n", DisplayName = "#endif in the body closes an #if before the namespace")]
    [DataRow("namespace A\r\n{\r\n    class C { }\r\n#if X\r\n    class D { }\r\n}\r\n", DisplayName = "#if in the body without #endif")]
    [DataRow("using System;\r\n\r\nConsole.WriteLine(\"namespace X { }\");\r\n\r\nstatic int Add(int a, int b) => a + b;\r\n\r\nnamespace A\r\n{\r\n    class C { }\r\n}\r\n", DisplayName = "top-level statements before the namespace")]
    [DataRow("using System;\r\n\r\nConsole.WriteLine(\"hello\");\r\n\r\nclass C { }\r\n", DisplayName = "top-level statements without namespace")]
    [DataRow("namespace A.B\r\n{\r\n}\r\nnamespace A.C\r\n{\r\n}\r\n", DisplayName = "two namespaces")]
    public void ConvertToFileScoped_NotConvertible_ReturnsUnchanged(string input)
    {
        Assert.AreEqual(input, _converter.ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("   \r\n\t\r\n", DisplayName = "whitespace-only file")]
    [DataRow("using System;\r\n\r\nConsole.WriteLine(\"namespace A;\");\r\n", DisplayName = "top-level statements without namespace")]
    [DataRow("#if DEBUG\r\nnamespace A;\r\n#else\r\nnamespace B;\r\n#endif\r\n\r\nclass C { }\r\n", DisplayName = "namespace between #if and #else")]
    public void ConvertToBlockScoped_NotConvertible_ReturnsUnchanged(string input)
    {
        Assert.AreEqual(input, _converter.ConvertToBlockScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("#if X\n#endif\nnamespace A;\n\nclass C { }\n", "#if X\n#endif\nnamespace A\n{\n    class C { }\n}\n", DisplayName = "closed #if block before the namespace")]
    [DataRow("namespace A;\n   \n\t\nclass C { }\n", "namespace A\n{\n    class C { }\n}\n", DisplayName = "leading whitespace-only lines")]
    [DataRow("namespace A;\r\rclass C { }\r", "namespace A\r{\r    class C { }\r}\r", DisplayName = "carriage-return line breaks")]
    [DataRow("namespace A;\r", "namespace A\r{\r}\r", DisplayName = "single carriage return at the end")]
    [DataRow("namespace A; // the namespace\n\nclass C { }\n", "namespace A // the namespace\n{\n    class C { }\n}\n", DisplayName = "comment after the semicolon")]
    public void ConvertToBlockScoped_LineAndDirectiveEdgeCases(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.ConvertToBlockScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertToFileScoped_LinesIndentedLessThanOneLevel_KeepTheirText()
    {
        string input = "namespace A\n{\n    class C\n    {\n  // odd\n    }\n}\n";

        Assert.AreEqual("namespace A;\n\nclass C\n{\n  // odd\n}\n", _converter.ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertToFileScoped_RichBody_IsDedentedAndCompiles_AndRoundTrips()
    {
        string input =
            "// <copyright file=\"Target.cs\">header</copyright>\r\n" +
            "using System;\r\n" +
            "using System.Threading.Tasks;\r\n" +
            "\r\n" +
            "namespace Company.App\r\n" +
            "{\r\n" +
            "    #region Types\r\n" +
            "    /// <summary>A record.</summary>\r\n" +
            "    public record Person(string Name);\r\n" +
            "\r\n" +
            "    public readonly record struct Point(int X, int Y);\r\n" +
            "\r\n" +
            "    [Obsolete(\"namespace X { }\")]\r\n" +
            "    public partial class Repo<T> where T : class, new()\r\n" +
            "    {\r\n" +
            "#pragma warning disable CS0169\r\n" +
            "        private T _item;\r\n" +
            "#pragma warning restore CS0169\r\n" +
            "\r\n" +
            "        public string Describe(int x) => $\"{x}: }} namespace {{\";\r\n" +
            "\r\n" +
            "        public async Task<int> RunAsync()\r\n" +
            "        {\r\n" +
            "            Func<int, Task<int>> twice = async value => { await Task.Yield(); return value * 2; };\r\n" +
            "            int Local(int y) => y + 1;\r\n" +
            "            return Local(await twice(1));\r\n" +
            "        }\r\n" +
            "\r\n" +
            "        public class Nested { }\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    public partial class Repo<T>\r\n" +
            "    {\r\n" +
            "    }\r\n" +
            "    #endregion\r\n" +
            "}\r\n";
        string expected =
            "// <copyright file=\"Target.cs\">header</copyright>\r\n" +
            "using System;\r\n" +
            "using System.Threading.Tasks;\r\n" +
            "\r\n" +
            "namespace Company.App;\r\n" +
            "\r\n" +
            "#region Types\r\n" +
            "/// <summary>A record.</summary>\r\n" +
            "public record Person(string Name);\r\n" +
            "\r\n" +
            "public readonly record struct Point(int X, int Y);\r\n" +
            "\r\n" +
            "[Obsolete(\"namespace X { }\")]\r\n" +
            "public partial class Repo<T> where T : class, new()\r\n" +
            "{\r\n" +
            "#pragma warning disable CS0169\r\n" +
            "    private T _item;\r\n" +
            "#pragma warning restore CS0169\r\n" +
            "\r\n" +
            "    public string Describe(int x) => $\"{x}: }} namespace {{\";\r\n" +
            "\r\n" +
            "    public async Task<int> RunAsync()\r\n" +
            "    {\r\n" +
            "        Func<int, Task<int>> twice = async value => { await Task.Yield(); return value * 2; };\r\n" +
            "        int Local(int y) => y + 1;\r\n" +
            "        return Local(await twice(1));\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    public class Nested { }\r\n" +
            "}\r\n" +
            "\r\n" +
            "public partial class Repo<T>\r\n" +
            "{\r\n" +
            "}\r\n" +
            "#endregion\r\n";
        const string IsExternalInit = "namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }\r\n";

        string result = _converter.ConvertToFileScoped(input);

        Assert.AreEqual(expected, result);
        await AssertCompilesAsync(CompilingTestProject.CreateDocument(input, IsExternalInit), result);
        Assert.AreEqual(
            input.Replace("    #region Types\r\n", "#region Types\r\n").Replace("    #endregion\r\n", "#endregion\r\n"),
            _converter.ConvertToBlockScoped(result));
    }

    private static Document CreateDebugDocument(string source)
        => CompilingTestProject.CreateDocument(source, new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: new[] { "DEBUG" }), new MetadataReference[0]);

    private static string[] GetStringContentTokens(string source)
        => CSharpSyntaxTree.ParseText(source).GetRoot().DescendantTokens()
            .Where(token => StringContentTokenKinds.Contains(token.Kind()))
            .Select(token => token.Text)
            .ToArray();

    private static async Task AssertCompilesAsync(Document document, string text)
    {
        IReadOnlyList<string> errors = await CompilingTestProject.GetCompileErrorsAsync(document, text);
        Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors));
    }
}
