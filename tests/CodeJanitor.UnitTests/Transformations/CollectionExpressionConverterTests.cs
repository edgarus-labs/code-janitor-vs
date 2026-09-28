using System.Threading.Tasks;
using CodeJanitor.Logic.Transformations;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="CollectionExpressionConverter" />.
/// </summary>
[TestClass]
public sealed class CollectionExpressionConverterTests
{
    private ISourceTransformation _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new CollectionExpressionConverter();
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsEmptyListFieldInitialization()
    {
        string input = "class C { private readonly List<string> _items = new List<string>(); }";
        string expected = "class C { private readonly List<string> _items = []; }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsListWithInitializerElements()
    {
        string input = "class C { void M() { List<string> items = new List<string>() { \"a\", \"b\" }; } }";
        string expected = "class C { void M() { List<string> items = [\"a\", \"b\"]; } }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsLocalListDeclaration()
    {
        string input = "class C { void M() { List<int> x = new List<int>(); } }";
        string expected = "class C { void M() { List<int> x = []; } }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsArrayWithInitializerElements()
    {
        string input = "class C { void M() { int[] a = new int[] { 1, 2, 3 }; } }";
        string expected = "class C { void M() { int[] a = [1, 2, 3]; } }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsExplicitlyEmptyArray()
    {
        string input = "class C { void M() { int[] a = new int[0]; } }";
        string expected = "class C { void M() { int[] a = []; } }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsImplicitArrayCreation()
    {
        string input = "class C { void M() { int[] a = new[] { 1, 2, 3 }; } }";
        string expected = "class C { void M() { int[] a = [1, 2, 3]; } }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsAutoPropertyInitializer()
    {
        string input = "class C { public List<string> Items { get; } = new List<string>(); }";
        string expected = "class C { public List<string> Items { get; } = []; }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsListWithConstructorArguments()
    {
        string input = "class C { void M() { List<string> x = new List<string>(10); } }";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsWhenDeclaredTypeDiffersFromCreatedType()
    {
        string input = "class C { void M() { IList<string> x = new List<string>(); } }";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsSizedArrayWithoutInitializer()
    {
        string input = "class C { void M() { int[] a = new int[5]; } }";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsNonListGenericType()
    {
        string input = "class C { void M() { HashSet<string> x = new HashSet<string>(); } }";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NameAndNullOrEmpty_HandledCorrectly()
    {
        Assert.AreEqual("Collection Expression", _converter.Name);
        Assert.IsNull(_converter.Apply(null));
        Assert.AreEqual(string.Empty, _converter.Apply(string.Empty));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsArrayPropertyInitializer()
    {
        string input = "class C { public int[] Items { get; } = new int[] { 1, 2 }; }";
        string expected = "class C { public int[] Items { get; } = [1, 2]; }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("  \r\n\t", DisplayName = "whitespace only")]
    [DataRow("class C { void M() { List<int> x; int[] a; } }", DisplayName = "declarations without initializer")]
    [DataRow("class C { public List<int> Items { get; set; } public int[] Values => new int[] { 1 }; }", DisplayName = "properties without initializer")]
    [DataRow("class C { public List<int> Items { get; } = Create(); }", DisplayName = "property initialized by an invocation")]
    [DataRow("class C { void M() { List<int> x = Create(); int[] y = null; } }", DisplayName = "other initializer kinds")]
    [DataRow("class C { void M() { List<int> x = new(); } }", DisplayName = "target-typed new")]
    [DataRow("class C { void M() { var x = new List<int>(); var a = new[] { 1 }; var b = new int[] { 1 }; } }", DisplayName = "var declarations")]
    [DataRow("class C { void M() { object a = new[] { 1, 2 }; } }", DisplayName = "implicit array assigned to a non-array type")]
    [DataRow("class C { void M() { IEnumerable<int> a = new int[] { 1 }; } }", DisplayName = "array assigned to a non-array type")]
    [DataRow("class C { void M() { object[] a = new string[] { \"a\" }; } }", DisplayName = "covariant array")]
    [DataRow("class C { void M() { int[] a = new int[n]; int[] b = new int[1]; } }", DisplayName = "non-zero or non-constant size")]
    [DataRow("class C { void M() { ArrayList x = new ArrayList(); } }", DisplayName = "non-generic type")]
    [DataRow("class C { void M() { Dictionary<int, int> x = new Dictionary<int, int>(); } }", DisplayName = "other generic type")]
    [DataRow("class C { void M() { System.Collections.Generic.List<int> x = new System.Collections.Generic.List<int>(); } }", DisplayName = "qualified list type")]
    [DataRow("class C { void M() { List<int>? x = new List<int>(); } }", DisplayName = "nullable declared type")]
    [DataRow("class C { void M(IEnumerable<int> source) { List<int> x = new List<int>(source) { 1 }; } }", DisplayName = "constructor argument with initializer")]
    public void InitializerThatCannotBecomeACollectionExpression_IsUnchanged(string input)
    {
        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { List<int> a = new List<int>(), b = new List<int> { 1 }; }", "class C { List<int> a = [], b = [1]; }", DisplayName = "several declarators")]
    [DataRow("class C { List<int> a = new List<int> { 1, 2 }; }", "class C { List<int> a = [1, 2]; }", DisplayName = "initializer without parentheses")]
    [DataRow("class C { List<int> a = new List<int>() { }; }", "class C { List<int> a = []; }", DisplayName = "empty initializer")]
    [DataRow("class C { List<List<int>> a = new List<List<int>> { new List<int> { 1 } }; }", "class C { List<List<int>> a = [new List<int> { 1 }]; }", DisplayName = "nested list element stays an element")]
    [DataRow("class C { int[][] a = new int[][] { new[] { 1 }, new int[0] }; }", "class C { int[][] a = [new[] { 1 }, new int[0]]; }", DisplayName = "jagged array")]
    [DataRow("class C { string[] a = new string[0x0]; }", "class C { string[] a = []; }", DisplayName = "hexadecimal zero size")]
    [DataRow("class C { static readonly (int, string)[] a = new (int, string)[] { (1, \"a\") }; }", "class C { static readonly (int, string)[] a = [(1, \"a\")]; }", DisplayName = "tuple elements")]
    [DataRow("class C { void M() { System.Action a = () => { List<int> x = new List<int>(); }; } }", "class C { void M() { System.Action a = () => { List<int> x = []; }; } }", DisplayName = "inside lambda")]
    [DataRow("class C { async Task M() { List<Task> x = new List<Task> { Task.Delay(1) }; await Task.WhenAll(x); } }", "class C { async Task M() { List<Task> x = [Task.Delay(1)]; await Task.WhenAll(x); } }", DisplayName = "async method")]
    [DataRow("record R { public List<int> Items { get; init; } = new List<int>(); }", "record R { public List<int> Items { get; init; } = []; }", DisplayName = "record init property")]
    [DataRow("struct S { public S() { } int[] _a = new int[] { 1 }; }", "struct S { public S() { } int[] _a = [1]; }", DisplayName = "struct field")]
    [DataRow("class C<T> where T : struct { List<T> _items = new List<T>(); }", "class C<T> where T : struct { List<T> _items = []; }", DisplayName = "generic element type")]
    [DataRow("class C { string[] a = new string[] { \"new List<int>()\", @\"x\", $\"{1}\" }; }", "class C { string[] a = [\"new List<int>()\", @\"x\", $\"{1}\"]; }", DisplayName = "string literal elements")]
    public void ConvertibleInitializer_BecomesACollectionExpression(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TopLevelStatementsFile_ConvertsStatementsLocalFunctionsAndTypes()
    {
        string input =
            "using System.Collections.Generic;\r\n" +
            "\r\n" +
            "List<string> names = new List<string> { \"a\" };\r\n" +
            "int[] numbers = new[] { 1, 2 };\r\n" +
            "Print(names);\r\n" +
            "\r\n" +
            "static void Print(List<string> values)\r\n" +
            "{\r\n" +
            "    string[] copy = new string[0];\r\n" +
            "}\r\n" +
            "\r\n" +
            "class Registry\r\n" +
            "{\r\n" +
            "    public List<int> Ids { get; } = new List<int>();\r\n" +
            "}\r\n";
        string expected =
            "using System.Collections.Generic;\r\n" +
            "\r\n" +
            "List<string> names = [\"a\"];\r\n" +
            "int[] numbers = [1, 2];\r\n" +
            "Print(names);\r\n" +
            "\r\n" +
            "static void Print(List<string> values)\r\n" +
            "{\r\n" +
            "    string[] copy = [];\r\n" +
            "}\r\n" +
            "\r\n" +
            "class Registry\r\n" +
            "{\r\n" +
            "    public List<int> Ids { get; } = [];\r\n" +
            "}\r\n";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("\r\n", DisplayName = "CRLF")]
    [DataRow("\n", DisplayName = "LF")]
    public void TriviaAroundTheInitializer_IsPreserved(string newLine)
    {
        string input =
            "namespace N;" + newLine +
            "class C" + newLine +
            "{" + newLine +
            "\t/// <summary>Items.</summary>" + newLine +
            "\t[Obsolete]" + newLine +
            "\tprivate List<int> _items = /* start */ new List<int>() /* end */; // trailing" + newLine +
            "#if DEBUG" + newLine +
            "\tprivate List<int> _inactive = new List<int>();" + newLine +
            "#endif" + newLine +
            "}" + newLine;
        string expected = input.Replace("/* start */ new List<int>() /* end */", "/* start */ [] /* end */");

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FileWithSyntaxErrors_ConvertsTheValidDeclarationAndKeepsTheRestVerbatim()
    {
        string input = "class C { List<int> _a = new List<int>(); void M( { int x = ; } }";
        string expected = "class C { List<int> _a = []; void M( { int x = ; } }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertedInitializers_CompileWithoutNewErrors()
    {
        string input =
            "using System.Collections.Generic;\r\n" +
            "class C<T>\r\n" +
            "{\r\n" +
            "    private readonly List<T> _items = new List<T>();\r\n" +
            "    private static readonly string[] Names = new string[] { \"a\", \"b\" };\r\n" +
            "    public int[] Empty { get; } = new int[0];\r\n" +
            "    public List<int> Numbers { get; } = new List<int> { 1, 2 };\r\n" +
            "    public int Count()\r\n" +
            "    {\r\n" +
            "        int[] local = new[] { 1, 2, 3 };\r\n" +
            "        List<string> words = new List<string>() { Names[0] };\r\n" +
            "        return local.Length + words.Count + _items.Count + Empty.Length + Numbers.Count;\r\n" +
            "    }\r\n" +
            "}\r\n";
        Document document = CompilingTestProject.CreateDocument(input);
        string expected = input
            .Replace("_items = new List<T>();", "_items = [];")
            .Replace("Names = new string[] { \"a\", \"b\" };", "Names = [\"a\", \"b\"];")
            .Replace("Empty { get; } = new int[0];", "Empty { get; } = [];")
            .Replace("Numbers { get; } = new List<int> { 1, 2 };", "Numbers { get; } = [1, 2];")
            .Replace("local = new[] { 1, 2, 3 };", "local = [1, 2, 3];")
            .Replace("words = new List<string>() { Names[0] };", "words = [Names[0]];");

        string result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
        Assert.IsEmpty(await CompilingTestProject.GetCompileErrorsAsync(document, result));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { int[,] a = new int[,] { { 1, 2 }, { 3, 4 } }; }", DisplayName = "rectangular array with initializer")]
    [DataRow("class C { int[,] a = new int[0,0]; }", DisplayName = "empty rectangular array")]
    [DataRow("class C { int[,] a = new[,] { { 1 } }; }", DisplayName = "implicit rectangular array")]
    [DataRow("class C { int[,][] a = new int[0,0][]; }", DisplayName = "rectangular array of arrays")]
    [DataRow("class C { int[][] a = new int[0][,]; }", DisplayName = "different inner rank")]
    public void RectangularArray_IsUnchanged(string input)
    {
        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow(
        "class C\n{\n    List<int> a = new List<int>\n    {\n        1, // one\n        2\n    };\n}\n",
        "class C\n{\n    List<int> a = [\n        1, // one\n        2\n    ];\n}\n",
        DisplayName = "list with a trailing comment")]
    [DataRow(
        "class C\r\n{\r\n    int[] a = new int[] { /* first */ 1, 2 /* last */ };\r\n}\r\n",
        "class C\r\n{\r\n    int[] a = [ /* first */ 1, 2 /* last */ ];\r\n}\r\n",
        DisplayName = "array with block comments")]
    [DataRow(
        "class C\n{\n    int[] a = new[]\n    {\n        1,\n#if DEBUG\n        2,\n#endif\n    };\n}\n",
        "class C\n{\n    int[] a = [\n        1,\n#if DEBUG\n        2,\n#endif\n    ];\n}\n",
        DisplayName = "preprocessor branch in the initializer")]
    public void CommentsAndDirectivesInsideTheInitializer_AreKept(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task JaggedArraysAndCommentedInitializers_CompileWithoutNewErrors()
    {
        string input =
            "using System.Collections.Generic;\n" +
            "class C\n" +
            "{\n" +
            "    int[][] a = new int[0][];\n" +
            "    int[,] b = new int[,] { { 1 } };\n" +
            "    List<int> c = new List<int>\n" +
            "    {\n" +
            "        1, // one\n" +
            "        2,\n" +
            "    };\n" +
            "    int[] d = new[] { /* x */ 1 };\n" +
            "    int Count() => a.Length + b.Length + c.Count + d.Length;\n" +
            "}\n";
        Document document = CompilingTestProject.CreateDocument(input);
        string expected = input
            .Replace("a = new int[0][];", "a = [];")
            .Replace("c = new List<int>\n    {\n", "c = [\n")
            .Replace("        2,\n    };", "        2,\n    ];")
            .Replace("d = new[] { /* x */ 1 };", "d = [ /* x */ 1 ];");

        string result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
        Assert.IsEmpty(await CompilingTestProject.GetCompileErrorsAsync(document, result));
    }
}
