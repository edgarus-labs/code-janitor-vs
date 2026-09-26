using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Transformations;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="FileScopedNamespaceConverter" />.
/// Pure transformation tests (no Visual Studio / EnvDTE required).
/// </summary>

[TestClass]
public sealed class FileScopedNamespaceConverterTests
{
    private const string Library = "namespace Company.App.Services { public class Svc { } }\r\n";

    private static readonly SyntaxKind[] s_stringContentTokenKinds =
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
        var input = "namespace A\r\n{\r\n    class C\r\n    {\r\n    }\r\n}\r\n";
        var expected = "namespace A;\r\n\r\nclass C\r\n{\r\n}\r\n";

        Assert.AreEqual(expected, _converter.ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task KeepsNamespaceUsingsInsideFileScopedNamespace_SoRelativeUsingsStillCompile()
    {
        var input = "namespace Company.App\r\n{\r\n    using Services;\r\n\r\n    class C { Svc s; }\r\n}\r\n";
        var expected = "namespace Company.App;\r\n\r\nusing Services;\r\n\r\nclass C { Svc s; }\r\n";

        var result = _converter.ConvertToFileScoped(input);

        Assert.AreEqual(expected, result);
        var errors = await CompilingTestProject.GetCompileErrorsAsync(CompilingTestProject.CreateDocument(input, Library), result);
        Assert.AreEqual(0, errors.Count, string.Join(Environment.NewLine, errors));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertingAfterSemanticUsingMove_YieldsCompilingFileScopedCode()
    {
        var input = "namespace Company.App\r\n{\r\n    using Services;\r\n\r\n    class C { Svc s; }\r\n}\r\n";
        var document = CompilingTestProject.CreateDocument(input, Library);

        var moved = await new UsingDirectivePlacementConverter().MoveUsingsOutsideAsync(document, CancellationToken.None);
        var result = _converter.ConvertToFileScoped(moved.Text);

        Assert.AreEqual("using Company.App.Services;\r\n\r\nnamespace Company.App;\r\n\r\nclass C { Svc s; }\r\n", result);
        var errors = await CompilingTestProject.GetCompileErrorsAsync(document, result);
        Assert.AreEqual(0, errors.Count, string.Join(Environment.NewLine, errors));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void AlreadyFileScoped_ReturnsUnchanged()
    {
        var input = "namespace A;\r\n\r\nclass C\r\n{\r\n}\r\n";

        Assert.AreEqual(input, _converter.ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void MultipleNamespaces_ReturnsUnchanged()
    {
        var input = "namespace A\r\n{\r\n}\r\nnamespace B\r\n{\r\n}\r\n";

        Assert.AreEqual(input, _converter.ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NoNamespace_ReturnsUnchanged()
    {
        var input = "class C\r\n{\r\n}\r\n";

        Assert.AreEqual(input, _converter.ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NestedNamespace_ReturnsUnchanged()
    {
        var input = "namespace A\r\n{\r\n    namespace B\r\n    {\r\n    }\r\n}\r\n";

        Assert.AreEqual(input, _converter.ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void PreservesFileHeaderAndOuterUsings()
    {
        var input = "// file header\r\nusing System;\r\n\r\nnamespace A\r\n{\r\n    class C\r\n    {\r\n    }\r\n}\r\n";
        var expected = "// file header\r\nusing System;\r\n\r\nnamespace A;\r\n\r\nclass C\r\n{\r\n}\r\n";

        Assert.AreEqual(expected, _converter.ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void HasMultipleNamespaces_MultipleTopLevelNamespaces_ReturnsTrue()
    {
        var input = "namespace A\r\n{\r\n}\r\nnamespace B\r\n{\r\n}\r\n";

        Assert.IsTrue(_converter.HasMultipleNamespaces(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void HasMultipleNamespaces_NestedNamespace_ReturnsTrue()
    {
        var input = "namespace A\r\n{\r\n    namespace B\r\n    {\r\n    }\r\n}\r\n";

        Assert.IsTrue(_converter.HasMultipleNamespaces(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void HasMultipleNamespaces_SingleBlockNamespace_ReturnsFalse()
    {
        var input = "namespace A\r\n{\r\n    class C\r\n    {\r\n    }\r\n}\r\n";

        Assert.IsFalse(_converter.HasMultipleNamespaces(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void HasMultipleNamespaces_SingleFileScopedNamespace_ReturnsFalse()
    {
        var input = "namespace A;\r\n\r\nclass C\r\n{\r\n}\r\n";

        Assert.IsFalse(_converter.HasMultipleNamespaces(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NameAndApply_WorkCorrectly()
    {
        var converter = new FileScopedNamespaceConverter();
        Assert.AreEqual("File-Scoped Namespace", converter.Name);

        var input = "namespace A\r\n{\r\n    class C { }\r\n}\r\n";
        var expected = "namespace A;\r\n\r\nclass C { }\r\n";
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
        var tabInput = "namespace A\n{\n\tclass C\n\t{\n\t}\n}\n";
        var expectedTab = "namespace A;\n\nclass C\n{\n}\n";
        Assert.AreEqual(expectedTab, _converter.ConvertToFileScoped(tabInput));

        var emptyInput = "namespace A\n{\n}\n";
        var expectedEmpty = "namespace A;\n";
        Assert.AreEqual(expectedEmpty, _converter.ConvertToFileScoped(emptyInput));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertToFileScoped_MultiLineStringLiteralsAndDisabledText_StayByteIdentical()
    {
        var input =
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
        var expected =
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

        var result = _converter.ConvertToFileScoped(input);

        Assert.AreEqual(expected, result);
        CollectionAssert.AreEqual(GetStringContentTokens(input), GetStringContentTokens(result));
        await AssertCompilesAsync(CompilingTestProject.CreateDocument(input), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void MissingBraceOrMalformed_ReturnsUnchanged()
    {
        var input = "namespace A";
        Assert.AreEqual(input, _converter.ConvertToFileScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void HasMultipleNamespaces_NoNamespace_ReturnsFalse()
    {
        var input = "class C\r\n{\r\n}\r\n";

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
        var input =
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

        var converted = _converter.ConvertToFileScoped(input);

        Assert.AreNotEqual(input, converted, "Expected the converter to change the block-scoped namespace to file-scoped.");
        StringAssert.Contains(converted, "namespace CodeJanitor.Scratch.Bug001;");
        StringAssert.Contains(converted, "public class FileScopedNamespaceSample");
        StringAssert.Contains(converted, "public class SecondClassInSameNamespace");
        StringAssert.Contains(converted, "public int Value { get; set; }");

        // The dedented body should no longer contain the original 4-space class indentation.
        Assert.IsFalse(converted.Contains("\r\n    public class FileScopedNamespaceSample"),
            "Expected class declarations to be dedented by one level after file-scoped conversion.");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertToBlockScoped_WrapsTheBodyInBracesAndIndentsItOneLevel()
    {
        var input = "namespace A;\r\n\r\nclass C\r\n{\r\n    void M() { }\r\n}\r\n";
        var expected = "namespace A\r\n{\r\n    class C\r\n    {\r\n        void M() { }\r\n    }\r\n}\r\n";

        Assert.AreEqual(expected, _converter.ConvertToBlockScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertToBlockScoped_LineFeeds_ArePreserved()
    {
        var input = "namespace A;\n\nclass C\n{\n    int x;\n}\n";
        var expected = "namespace A\n{\n    class C\n    {\n        int x;\n    }\n}\n";

        Assert.AreEqual(expected, _converter.ConvertToBlockScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertToBlockScoped_KeepsFileHeaderAndTopLevelUsings_AndMovesUsingsAfterTheDeclarationIntoTheBlock()
    {
        var input =
            "// <copyright>\r\n// ACME\r\n// </copyright>\r\n\r\nusing System;\r\n\r\nnamespace Company.App;\r\n\r\n" +
            "using Services;\r\n\r\nclass C\r\n{\r\n    Svc s;\r\n    Type t;\r\n}\r\n";
        var expected =
            "// <copyright>\r\n// ACME\r\n// </copyright>\r\n\r\nusing System;\r\n\r\nnamespace Company.App\r\n{\r\n" +
            "    using Services;\r\n\r\n    class C\r\n    {\r\n        Svc s;\r\n        Type t;\r\n    }\r\n}\r\n";

        var result = _converter.ConvertToBlockScoped(input);

        Assert.AreEqual(expected, result);
        await AssertCompilesAsync(CompilingTestProject.CreateDocument(input, Library), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertToBlockScoped_KeepsExternAliasesAndUsingsAboveTheNamespace()
    {
        var input = "extern alias Lib;\n\nusing System;\n\nnamespace A;\n\nclass C\n{\n    Lib::L.X x;\n    Type t;\n}\n";
        var expected = "extern alias Lib;\n\nusing System;\n\nnamespace A\n{\n    class C\n    {\n        Lib::L.X x;\n        Type t;\n    }\n}\n";
        var reference = CompilingTestProject.CreateAliasedReference("AliasedLibrary", "namespace L { public class X { } }\r\n", "Lib");

        var result = _converter.ConvertToBlockScoped(input);

        Assert.AreEqual(expected, result);
        await AssertCompilesAsync(CompilingTestProject.CreateDocument(input, LanguageVersion.Latest, new[] { reference }), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertToBlockScoped_MultiLineStringLiterals_StayByteIdentical()
    {
        var input =
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
        var expected =
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

        var result = _converter.ConvertToBlockScoped(input);

        Assert.AreEqual(expected, result);
        CollectionAssert.AreEqual(GetStringContentTokens(input), GetStringContentTokens(result));
        await AssertCompilesAsync(CompilingTestProject.CreateDocument(input), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertToBlockScoped_IndentsActiveCode_KeepsDisabledTextAndColumnZeroDirectives_AndCompilesInEveryConfiguration()
    {
        var input = "namespace A;\n\nclass C\n{\n#if DEBUG\n    void Debug() { }\n#else\n    void Release() { }\n#endif\n}\n";
        var expected = "namespace A\n{\n    class C\n    {\n#if DEBUG\n    void Debug() { }\n#else\n        void Release() { }\n#endif\n    }\n}\n";

        var result = _converter.ConvertToBlockScoped(input);

        Assert.AreEqual(expected, result);
        await AssertCompilesAsync(CompilingTestProject.CreateDocument(input), result);
        await AssertCompilesAsync(CreateDebugDocument(input), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertToBlockScoped_DirectivesAndCommentsAfterTheLastMember_StayInsideTheBraces()
    {
        var input = "namespace A;\n\nclass C { }\n\n#if DEBUG\nclass D { }\n#endif\n// trailing comment\n";
        var expected = "namespace A\n{\n    class C { }\n\n#if DEBUG\nclass D { }\n#endif\n    // trailing comment\n}\n";

        var result = _converter.ConvertToBlockScoped(input);

        Assert.AreEqual(expected, result);
        await AssertCompilesAsync(CompilingTestProject.CreateDocument(input), result);
        await AssertCompilesAsync(CreateDebugDocument(input), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertToBlockScoped_NestedTypesDocumentationCommentsAndDeclarationComment_CompileAfterConversion()
    {
        var input =
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
        var expected =
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

        var result = _converter.ConvertToBlockScoped(input);

        Assert.AreEqual(expected, result);
        await AssertCompilesAsync(CompilingTestProject.CreateDocument(input), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertToBlockScoped_MultiLineCommentContinuationLines_KeepTheirText()
    {
        var input = "namespace A;\n\n/*\n * Block comment\n */\nclass C { }\n";
        var expected = "namespace A\n{\n    /*\n * Block comment\n */\n    class C { }\n}\n";

        Assert.AreEqual(expected, _converter.ConvertToBlockScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertToBlockScoped_TabIndentedBody_IsIndentedWithATab()
    {
        var input = "namespace A;\n\nclass C\n{\n\tint x;\n}\n";
        var expected = "namespace A\n{\n\tclass C\n\t{\n\t\tint x;\n\t}\n}\n";

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
        var input = "using System;\r\n\r\nnamespace A;\r\n\r\nclass C\r\n{\r\n    void M() { }\r\n}\r\n";

        Assert.AreEqual(input, _converter.ConvertToFileScoped(_converter.ConvertToBlockScoped(input)));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertToFileScoped_KeepsTheDirectiveAndCommentAfterTheClosingBrace_AndCompilesInEveryConfiguration()
    {
        var input = "#if !LEGACY\r\nnamespace A\r\n{\r\n    class C { }\r\n} // namespace A\r\n\r\n#endif\r\n";
        var expected = "#if !LEGACY\r\nnamespace A;\r\n\r\nclass C { }\r\n// namespace A\r\n\r\n#endif\r\n";

        var result = _converter.ConvertToFileScoped(input);

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
        var input = "namespace A;\n\nclass C\n{\n    int x;\n}\n";
        var expected = $"namespace A\n{{\n{level}class C\n{level}{{\n{level}    int x;\n{level}}}\n}}\n";

        Assert.AreEqual(expected, new FileScopedNamespaceConverter(indentWithTabs, indentSize).ConvertToBlockScoped(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertToFileScoped_RemovesOneConfiguredIndentationLevel()
    {
        var input = "namespace A\n{\n  class C\n  {\n    int x;\n  }\n}\n";

        Assert.AreEqual("namespace A;\n\nclass C\n{\n  int x;\n}\n", new FileScopedNamespaceConverter(false, 2).ConvertToFileScoped(input));
    }

    private static Document CreateDebugDocument(string source) =>
        CompilingTestProject.CreateDocument(source, new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: new[] { "DEBUG" }), new MetadataReference[0]);

    private static string[] GetStringContentTokens(string source) =>
        CSharpSyntaxTree.ParseText(source).GetRoot().DescendantTokens()
            .Where(token => s_stringContentTokenKinds.Contains(token.Kind()))
            .Select(token => token.Text)
            .ToArray();

    private static async Task AssertCompilesAsync(Document document, string text)
    {
        var errors = await CompilingTestProject.GetCompileErrorsAsync(document, text);
        Assert.AreEqual(0, errors.Count, string.Join(Environment.NewLine, errors));
    }
}
