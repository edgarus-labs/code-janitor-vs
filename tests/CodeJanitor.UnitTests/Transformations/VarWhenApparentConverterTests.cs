using System.Threading.Tasks;
using CodeJanitor.Logic.Transformations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="VarWhenApparentConverter" />.
/// Policy (ADR-0007): use var only when the right-hand side explicitly indicates the type
/// (object creation, cast, array creation) and the declared type textually matches; method
/// invocations and literals keep the explicit type.
/// </summary>
[TestClass]
public sealed class VarWhenApparentConverterTests
{
    private ITypeStyleConverter _converter;

    [TestInitialize]
    public void TestInitialize() => _converter = new VarWhenApparentConverter();

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsObjectCreationWithMatchingType()
    {
        string input = "class C { void M() { Foo x = new Foo(); } }";
        string expected = "class C { void M() { var x = new Foo(); } }";

        Assert.AreEqual(expected, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsMethodInvocation()
    {
        string input = "class C { void M() { Foo x = GetFoo(); } }";

        Assert.AreEqual(input, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsLiteral()
    {
        string input = "class C { void M() { int x = 5; } }";

        Assert.AreEqual(input, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsWhenDeclaredTypeDiffersFromCreatedType()
    {
        string input = "class C { void M() { IFoo x = new Foo(); } }";

        Assert.AreEqual(input, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsCastWithMatchingType()
    {
        string input = "class C { void M(object o) { Foo x = (Foo)o; } }";
        string expected = "class C { void M(object o) { var x = (Foo)o; } }";

        Assert.AreEqual(expected, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsArrayCreationWithMatchingElementType()
    {
        string input = "class C { void M() { int[] a = new int[3]; } }";
        string expected = "class C { void M() { var a = new int[3]; } }";

        Assert.AreEqual(expected, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsAlreadyVar()
    {
        string input = "class C { void M() { var x = new Foo(); } }";

        Assert.AreEqual(input, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsFieldDeclaration()
    {
        string input = "class C { private Foo _x = new Foo(); }";

        Assert.AreEqual(input, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void PreservesUnrelatedCode()
    {
        string input = "class C { void M() { Foo x = new Foo(); int y = 5; var z = GetFoo(); } }";
        string expected = "class C { void M() { var x = new Foo(); int y = 5; var z = GetFoo(); } }";

        Assert.AreEqual(expected, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NameAndApply_UseTheVarWhenApparentRewrite()
    {
        ISourceTransformation transformation = new VarWhenApparentConverter();

        Assert.AreEqual("Var When Apparent", transformation.Name);
        Assert.AreEqual("class C { void M() { var x = new Foo(); } }", transformation.Apply("class C { void M() { Foo x = new Foo(); } }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow(null, DisplayName = "null")]
    [DataRow("", DisplayName = "empty")]
    [DataRow("   \r\n\t", DisplayName = "whitespace only")]
    public void EmptyOrWhitespaceSource_IsReturnedUnchanged(string input) => Assert.AreEqual(input, _converter.UseVarWhenApparent(input));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { void M() { Foo a = new Foo(), b = new Foo(); } }", DisplayName = "several declarators")]
    [DataRow("class C { void M() { Foo x; x = new Foo(); } }", DisplayName = "no initializer")]
    [DataRow("class C { void M() { Foo x = new(); } }", DisplayName = "target-typed new")]
    [DataRow("class C { void M() { Foo? x = new Foo(); } }", DisplayName = "nullable declared type")]
    [DataRow("class C { void M() { List<int> x = new List<int, int>(); } }", DisplayName = "different type arguments")]
    [DataRow("class C { void M() { object[] a = new[] { 1 }; } }", DisplayName = "implicit array creation")]
    [DataRow("class C { void M() { IList<int> a = new int[3]; } }", DisplayName = "array assigned to non-array type")]
    [DataRow("class C { void M() { object[] a = new string[3]; } }", DisplayName = "covariant array")]
    [DataRow("class C { void M(object o) { IFoo x = (Foo)o; } }", DisplayName = "cast to a different type")]
    [DataRow("class C { void M(object o) { Foo x = o as Foo; } }", DisplayName = "as expression")]
    [DataRow("class C { void M(int[] a) { ref int x = ref a[0]; } }", DisplayName = "ref local")]
    [DataRow("class C { void M() { Span<int> s = stackalloc int[3]; } }", DisplayName = "stackalloc")]
    [DataRow("class C { void M() { Func<int> f = () => 1; } }", DisplayName = "lambda")]
    [DataRow("class C { void M() { for (Foo x = new Foo(); x != null; x = null) { } } }", DisplayName = "for initializer")]
    [DataRow("class C { void M() { using (Foo x = new Foo()) { } } }", DisplayName = "using statement")]
    [DataRow("class C { Foo P { get; } = new Foo(); }", DisplayName = "property initializer")]
    public void DeclarationWhoseTypeIsNotApparent_IsUnchanged(string input) => Assert.AreEqual(input, _converter.UseVarWhenApparent(input));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { void M() { List<int> x = new List<int> { 1, 2 }; } }", "class C { void M() { var x = new List<int> { 1, 2 }; } }", DisplayName = "collection initializer")]
    [DataRow("class C { void M() { Dictionary<string, List<int>> x = new Dictionary<string, List<int>>(); } }", "class C { void M() { var x = new Dictionary<string, List<int>>(); } }", DisplayName = "nested generic")]
    [DataRow("class C { void M() { System.Text.StringBuilder x = new System.Text.StringBuilder(); } }", "class C { void M() { var x = new System.Text.StringBuilder(); } }", DisplayName = "qualified name")]
    [DataRow("class C { void M() { int[,] a = new int[2, 3]; } }", "class C { void M() { var a = new int[2, 3]; } }", DisplayName = "multi-dimensional array")]
    [DataRow("class C { void M() { string[] a = new string[] { \"a\" }; } }", "class C { void M() { var a = new string[] { \"a\" }; } }", DisplayName = "array with initializer")]
    [DataRow("class C { void M(object o) { int? x = (int?)o; } }", "class C { void M(object o) { var x = (int?)o; } }", DisplayName = "cast to nullable value type")]
    [DataRow("class C { void M(object o) { (int, string) t = ((int, string))o; } }", "class C { void M(object o) { var t = ((int, string))o; } }", DisplayName = "cast to tuple")]
    [DataRow("class C { void M() { using Foo x = new Foo(); } }", "class C { void M() { using var x = new Foo(); } }", DisplayName = "using declaration")]
    [DataRow("class C { async Task M() { await using Foo x = new Foo(); } }", "class C { async Task M() { await using var x = new Foo(); } }", DisplayName = "await using declaration")]
    [DataRow("class C { void M() { Action a = () => { Foo x = new Foo(); }; } }", "class C { void M() { Action a = () => { var x = new Foo(); }; } }", DisplayName = "inside lambda")]
    [DataRow("class C { void M() { void L() { Foo x = new Foo(); } } }", "class C { void M() { void L() { var x = new Foo(); } } }", DisplayName = "inside local function")]
    [DataRow("record R { void M() { R x = new R(); } }", "record R { void M() { var x = new R(); } }", DisplayName = "inside record")]
    [DataRow("class C<T> where T : new() { void M() { T x = new T(); } }", "class C<T> where T : new() { void M() { var x = new T(); } }", DisplayName = "generic type parameter")]
    public void DeclarationWithApparentType_BecomesVar(string input, string expected) => Assert.AreEqual(expected, _converter.UseVarWhenApparent(input));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TopLevelStatementsFile_ConvertsStatementsAndTypeMembersAfterThem()
    {
        string input =
            "using System.Text;\r\n" +
            "\r\n" +
            "StringBuilder builder = new StringBuilder();\r\n" +
            "int count = 3;\r\n" +
            "Print(builder);\r\n" +
            "\r\n" +
            "static void Print(StringBuilder b)\r\n" +
            "{\r\n" +
            "    Helper h = new Helper();\r\n" +
            "}\r\n" +
            "\r\n" +
            "class Helper\r\n" +
            "{\r\n" +
            "    void M() { Helper other = (Helper)null; }\r\n" +
            "}\r\n";
        string expected =
            "using System.Text;\r\n" +
            "\r\n" +
            "var builder = new StringBuilder();\r\n" +
            "int count = 3;\r\n" +
            "Print(builder);\r\n" +
            "\r\n" +
            "static void Print(StringBuilder b)\r\n" +
            "{\r\n" +
            "    var h = new Helper();\r\n" +
            "}\r\n" +
            "\r\n" +
            "class Helper\r\n" +
            "{\r\n" +
            "    void M() { var other = (Helper)null; }\r\n" +
            "}\r\n";

        Assert.AreEqual(expected, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("\r\n", DisplayName = "CRLF")]
    [DataRow("\n", DisplayName = "LF")]
    public void TriviaAroundTheDeclaredType_IsPreserved(string newLine)
    {
        string input =
            "namespace N;" + newLine +
            "class C" + newLine +
            "{" + newLine +
            "    void M()" + newLine +
            "    {" + newLine +
            "        // create it" + newLine +
            "\tFoo /* kept */ x = new Foo(); // trailing" + newLine +
            "    }" + newLine +
            "}" + newLine;
        string expected = input.Replace("\tFoo /* kept */ x", "\tvar /* kept */ x");

        Assert.AreEqual(expected, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void PreprocessorBranchesAndRegions_KeepTheirDirectives()
    {
        string input =
            "class C\r\n" +
            "{\r\n" +
            "    #region Body\r\n" +
            "    void M()\r\n" +
            "    {\r\n" +
            "#if DEBUG\r\n" +
            "        Foo inactive = new Foo();\r\n" +
            "#else\r\n" +
            "        Foo active = new Foo();\r\n" +
            "#endif\r\n" +
            "    }\r\n" +
            "    #endregion\r\n" +
            "}\r\n";
        string expected = input.Replace("Foo active", "var active");

        Assert.AreEqual(expected, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void CodeLikeTextInsideLiteralsAndComments_IsUnchanged()
    {
        string input =
            "class C\r\n" +
            "{\r\n" +
            "    // Foo x = new Foo();\r\n" +
            "    /// <summary>Foo x = new Foo();</summary>\r\n" +
            "    string a = \"Foo x = new Foo();\";\r\n" +
            "    string b = @\"Foo x = new Foo();\";\r\n" +
            "    string c = \"\"\"\r\n" +
            "        Foo x = new Foo();\r\n" +
            "        \"\"\";\r\n" +
            "    string d = $\"{1} Foo x = new Foo();\";\r\n" +
            "}\r\n";

        Assert.AreEqual(input, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FileWithSyntaxErrors_ConvertsOnlyTheApparentDeclarationAndKeepsTheRestVerbatim()
    {
        string input = "class C { void M() { Foo x = new Foo(); int y = ; } void N( }";
        string expected = "class C { void M() { var x = new Foo(); int y = ; } void N( }";

        Assert.AreEqual(expected, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertedDeclarations_CompileWithoutNewErrors()
    {
        string input =
            "using System;\r\n" +
            "using System.Collections.Generic;\r\n" +
            "sealed class Foo : IDisposable { public void Dispose() { } }\r\n" +
            "class C\r\n" +
            "{\r\n" +
            "    void M(object o)\r\n" +
            "    {\r\n" +
            "        Foo a = new Foo();\r\n" +
            "        List<int> b = new List<int> { 1 };\r\n" +
            "        int[] c = new int[] { 1 };\r\n" +
            "        string d = (string)o;\r\n" +
            "        using Foo e = new Foo();\r\n" +
            "        a = null;\r\n" +
            "        b.Add(2);\r\n" +
            "        c[0] = 2;\r\n" +
            "    }\r\n" +
            "}\r\n";
        Microsoft.CodeAnalysis.Document document = CompilingTestProject.CreateDocument(input);
        string expected = input
            .Replace("Foo a =", "var a =")
            .Replace("List<int> b =", "var b =")
            .Replace("int[] c =", "var c =")
            .Replace("string d =", "var d =")
            .Replace("using Foo e =", "using var e =");

        string result = _converter.UseVarWhenApparent(input);

        Assert.AreEqual(expected, result);
        Assert.IsEmpty(await CompilingTestProject.GetCompileErrorsAsync(document, result));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { void M() { const string s = (string)\"a\"; } }", DisplayName = "const local with a cast")]
    [DataRow("class C { void M() { object[] a = new object[1][]; a[0] = 1; } }", DisplayName = "jagged array created for a one-dimensional array")]
    [DataRow("class C { void M() { int[][] a = new int[1][,]; } }", DisplayName = "different inner rank")]
    [DataRow("class C { void M() { int[,] a = new int[1][]; } }", DisplayName = "different rank")]
    public void DeclarationWhereVarChangesTheMeaning_IsUnchanged(string input) => Assert.AreEqual(input, _converter.UseVarWhenApparent(input));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task JaggedArrayOfTheSameShape_BecomesVarAndCompiles()
    {
        string input = "class C { void M() { int[][,] a = new int[1][,]; a[0] = new int[1, 1]; } }";
        Microsoft.CodeAnalysis.Document document = CompilingTestProject.CreateDocument(input);

        string result = _converter.UseVarWhenApparent(input);

        Assert.AreEqual("class C { void M() { var a = new int[1][,]; a[0] = new int[1, 1]; } }", result);
        Assert.IsEmpty(await CompilingTestProject.GetCompileErrorsAsync(document, result));
    }
}
