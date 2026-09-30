using CodeJanitor.Logic.Transformations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="OutVarInliningConverter" />.
/// </summary>
[TestClass]
public sealed class OutVarInliningConverterTests
{
    private OutVarInliningConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new OutVarInliningConverter();
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Name_IsNotEmpty()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(_converter.Name));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_UninitializedDeclarationBeforeIfTryParse_InlinesOutVar()
    {
        string input = @"
public class C
{
    public void M(string s)
    {
        int result;
        if (int.TryParse(s, out result))
        {
            DoWork(result);
        }
    }
}";
        string expected = @"
public class C
{
    public void M(string s)
    {
        if (int.TryParse(s, out int result))
        {
            DoWork(result);
        }
    }
}";

        string result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_InitializedDeclaration_DoesNotInlined()
    {
        string input = @"
public class C
{
    public void M(string s)
    {
        int result = 0;
        if (int.TryParse(s, out result))
        {
            DoWork(result);
        }
    }
}";

        string result = _converter.Apply(input);

        Assert.AreEqual(input, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_NullOrEmpty_ReturnsOriginal()
    {
        Assert.IsNull(_converter.Apply(null));
        Assert.AreEqual(string.Empty, _converter.Apply(string.Empty));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("  \r\n\t", DisplayName = "whitespace only")]
    [DataRow("class C { void M() { int a, b; F(out a); } }", DisplayName = "several declarators")]
    [DataRow("class C { void M() { int x; G(x); F(out x); } }", DisplayName = "out argument not in the next statement")]
    [DataRow("class C { void M() { int x; F(out y); } }", DisplayName = "out argument of another variable")]
    [DataRow("class C { void M() { int x; F(ref x); } }", DisplayName = "ref argument")]
    [DataRow("class C { void M() { int x; F(in x); } }", DisplayName = "in argument")]
    [DataRow("class C { void M() { int x; bool ok = Check(nameof(x)) && F(out x); } }", DisplayName = "used earlier in the same statement")]
    [DataRow("class C { void M() { int x; } }", DisplayName = "declaration is the last statement")]
    [DataRow("class C { void M(C other) { int x; F(out other.x); } }", DisplayName = "out argument is a member access")]
    [DataRow("class C { void M(int k) { switch (k) { case 1: int x; F(out x); break; } } }", DisplayName = "switch section statements")]
    [DataRow("int x;\r\nint.TryParse(args[0], out x);\r\n", DisplayName = "top-level statements")]
    public void DeclarationThatCannotBeInlined_IsUnchanged(string input)
    {
        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { void M() { int a; F(out a); int b; F(out b); } }", "class C { void M() { F(out int a); F(out int b); } }", DisplayName = "consecutive pairs")]
    [DataRow("class C { void M() { int a; int b; F(out a, out b); } }", "class C { void M() { F(out int a, out int b); } }", DisplayName = "several declarations before the call")]
    [DataRow("class C { void M(System.Collections.Generic.Dictionary<string, string> d) { string s; d.TryGetValue(\"k\", out s); } }", "class C { void M(System.Collections.Generic.Dictionary<string, string> d) { d.TryGetValue(\"k\", out string s); } }", DisplayName = "expression statement")]
    [DataRow("class C { int M(string t) { int n; return int.TryParse(t, out n) ? n : 0; } }", "class C { int M(string t) { return int.TryParse(t, out int n) ? n : 0; } }", DisplayName = "return statement")]
    [DataRow("class C { void M(string t) { int n; bool ok = int.TryParse(t, out n); Use(n); } }", "class C { void M(string t) { bool ok = int.TryParse(t, out int n); Use(n); } }", DisplayName = "local declaration with later use")]
    [DataRow("class C { void M() { { int x; F(out x); } } }", "class C { void M() { { F(out int x); } } }", DisplayName = "nested block")]
    [DataRow("class C { System.Action A = () => { int x; F(out x); }; }", "class C { System.Action A = () => { F(out int x); }; }", DisplayName = "block lambda")]
    [DataRow("class C { void M() { void L() { int x; F(out x); } } }", "class C { void M() { void L() { F(out int x); } } }", DisplayName = "local function")]
    [DataRow("struct S { void M<T>() where T : new() { T x; F(out x); } }", "struct S { void M<T>() where T : new() { F(out T x); } }", DisplayName = "generic method in struct")]
    [DataRow("record R { R(string t) { int n; int.TryParse(t, out n); } }", "record R { R(string t) { int.TryParse(t, out int n); } }", DisplayName = "record constructor")]
    [DataRow("class C { async System.Threading.Tasks.Task M() { await System.Threading.Tasks.Task.Yield(); int n; F(out n); } }", "class C { async System.Threading.Tasks.Task M() { await System.Threading.Tasks.Task.Yield(); F(out int n); } }", DisplayName = "async method")]
    [DataRow("class C { void M(string s) { System.DayOfWeek e; System.Enum.TryParse(s, out e); } }", "class C { void M(string s) { System.Enum.TryParse(s, out System.DayOfWeek e); } }", DisplayName = "declared type that cannot be inferred from the call")]
    [DataRow("class C { void M() { dynamic d; F(out d); d.Foo(); } }", "class C { void M() { F(out dynamic d); d.Foo(); } }", DisplayName = "dynamic keeps its declared type")]
    [DataRow("class C { void M() { string? s; F(out s); } }", "class C { void M() { F(out string? s); } }", DisplayName = "nullable reference type")]
    [DataRow("class C { void M() { int[] a; F(out a); } }", "class C { void M() { F(out int[] a); } }", DisplayName = "array type")]
    public void DeclarationFollowedByOutArgument_IsInlined(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TopLevelStatementsFile_InlinesOnlyInsideBlocks()
    {
        string input =
            "int top;\r\n" +
            "int.TryParse(args[0], out top);\r\n" +
            "\r\n" +
            "static int Parse(string text)\r\n" +
            "{\r\n" +
            "    int value;\r\n" +
            "    int.TryParse(text, out value);\r\n" +
            "    return value;\r\n" +
            "}\r\n" +
            "\r\n" +
            "class Parser\r\n" +
            "{\r\n" +
            "    public int Parse(string text) { int value; int.TryParse(text, out value); return value; }\r\n" +
            "}\r\n";
        string expected =
            "int top;\r\n" +
            "int.TryParse(args[0], out top);\r\n" +
            "\r\n" +
            "static int Parse(string text)\r\n" +
            "{\r\n" +
            "    int.TryParse(text, out int value);\r\n" +
            "    return value;\r\n" +
            "}\r\n" +
            "\r\n" +
            "class Parser\r\n" +
            "{\r\n" +
            "    public int Parse(string text) { int.TryParse(text, out int value); return value; }\r\n" +
            "}\r\n";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("\r\n", DisplayName = "CRLF")]
    [DataRow("\n", DisplayName = "LF")]
    public void LeadingCommentsAndIndentationOfTheDeclaration_MoveToTheCall(string newLine)
    {
        string input =
            "class C" + newLine +
            "{" + newLine +
            "\tvoid M(string s)" + newLine +
            "\t{" + newLine +
            "\t\t// parse the input" + newLine +
            "\t\tint result;" + newLine +
            "\t\tint.TryParse(s, out result); // trailing" + newLine +
            "\t}" + newLine +
            "}" + newLine;
        string expected =
            "class C" + newLine +
            "{" + newLine +
            "\tvoid M(string s)" + newLine +
            "\t{" + newLine +
            "\t\t// parse the input" + newLine +
            "\t\tint.TryParse(s, out int result); // trailing" + newLine +
            "\t}" + newLine +
            "}" + newLine;

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FileWithSyntaxErrors_InlinesTheValidPairAndKeepsTheRestVerbatim()
    {
        string input = "class C { void M(string s) { int n; int.TryParse(s, out n); } void N( { int y = ; } }";
        string expected = "class C { void M(string s) { int.TryParse(s, out int n); } void N( { int y = ; } }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async System.Threading.Tasks.Task InlinedDeclarations_CompileWithoutNewErrors()
    {
        string input =
            "using System.Collections.Generic;\r\n" +
            "class C\r\n" +
            "{\r\n" +
            "    public int M(string text, Dictionary<string, string> map)\r\n" +
            "    {\r\n" +
            "        int number;\r\n" +
            "        if (!int.TryParse(text, out number))\r\n" +
            "        {\r\n" +
            "            return -1;\r\n" +
            "        }\r\n" +
            "        string value;\r\n" +
            "        map.TryGetValue(text, out value);\r\n" +
            "        return number + value.Length;\r\n" +
            "    }\r\n" +
            "}\r\n";
        Microsoft.CodeAnalysis.Document document = CompilingTestProject.CreateDocument(input);
        string expected = input
            .Replace("        int number;\r\n", string.Empty)
            .Replace("out number", "out int number")
            .Replace("        string value;\r\n", string.Empty)
            .Replace("out value", "out string value");

        string output = _converter.Apply(input);

        Assert.AreEqual(expected, output);
        Assert.IsEmpty(await CompilingTestProject.GetCompileErrorsAsync(document, output));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { void M(bool c) { int x; if (c) { F(out x); } else { x = 0; } U(x); } }", DisplayName = "if block")]
    [DataRow("class C { void M() { int x; while (F(out x)) { } U(x); } }", DisplayName = "while condition")]
    [DataRow("class C { void M() { int x; do { } while (F(out x)); U(x); } }", DisplayName = "do condition")]
    [DataRow("class C { void M() { int x; for (; F(out x);) { } U(x); } }", DisplayName = "for condition")]
    [DataRow("class C { void M() { int x; foreach (var a in G(out x)) { } U(x); } }", DisplayName = "foreach expression")]
    [DataRow("class C { void M() { int x; using (G(out x)) { } U(x); } }", DisplayName = "using statement")]
    [DataRow("class C { void M() { int x; lock (G(out x)) { } U(x); } }", DisplayName = "lock statement")]
    [DataRow("class C { void M() { int x; R(() => F(out x)); U(x); } }", DisplayName = "lambda")]
    [DataRow("class C { void M() { int x; R(delegate { F(out x); }); U(x); } }", DisplayName = "anonymous method")]
    [DataRow("class C { void M(int k) { int x; var y = k switch { 1 => F(out x), _ => 0 }; U(x); } }", DisplayName = "switch expression arm")]
    [DataRow("class C { void M(int[] a) { int x; var q = from i in a where F(out x) select i; U(x); } }", DisplayName = "query clause")]
    [DataRow("class C { void M() { int x;\n#if !A\n F(out x);\n#else\n x = 0;\n#endif\n U(x); } }", DisplayName = "call after a preprocessor directive")]
    public void OutArgumentInNestedScope_IsNotInlined(string input)
    {
        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { void M() { int x; // result\n F(out x); } }", "class C { void M() { // result\n F(out int x); } }", DisplayName = "one line")]
    [DataRow("class C\n{\n    void M()\n    {\n        int x; // result\n        F(out x);\n    }\n}\n", "class C\n{\n    void M()\n    {\n        // result\n        F(out int x);\n    }\n}\n", DisplayName = "trailing comment of the declaration")]
    [DataRow("class C\r\n{\r\n    void M()\r\n    {\r\n        int x;\r\n        // parse\r\n        F(out x);\r\n    }\r\n}\r\n", "class C\r\n{\r\n    void M()\r\n    {\r\n        // parse\r\n        F(out int x);\r\n    }\r\n}\r\n", DisplayName = "leading comment of the call")]
    [DataRow("class C\n{\n    void M()\n    {\n        int x; /* a */\n        /* b */\n        F(out x);\n    }\n}\n", "class C\n{\n    void M()\n    {\n        /* a */\n        /* b */\n        F(out int x);\n    }\n}\n", DisplayName = "both")]
    [DataRow("class C\n{\n    void M()\n    {\n        int x;\n\n        // parse\n        F(out x);\n    }\n}\n", "class C\n{\n    void M()\n    {\n\n        // parse\n        F(out int x);\n    }\n}\n", DisplayName = "blank line before the leading comment of the call")]
    public void CommentsBetweenDeclarationAndCall_AreKept(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async System.Threading.Tasks.Task OutArgumentsInScopesThatLeakToTheBlock_AreInlinedAndCompile()
    {
        string input =
            "class C\n" +
            "{\n" +
            "    static bool F(out int v) { v = 1; return true; }\n" +
            "    int M(bool c)\n" +
            "    {\n" +
            "        int a; // first\n" +
            "        if (F(out a) && c) { }\n" +
            "        int b;\n" +
            "        int s = F(out b) ? b : 0;\n" +
            "        int d;\n" +
            "        if (c) { F(out d); } else { d = 0; }\n" +
            "        return a + b + s + d;\n" +
            "    }\n" +
            "}\n";
        Microsoft.CodeAnalysis.Document document = CompilingTestProject.CreateDocument(input);
        string expected = input
            .Replace("        int a; // first\n        if (F(out a) && c)", "        // first\n        if (F(out int a) && c)")
            .Replace("        int b;\n        int s = F(out b)", "        int s = F(out int b)");

        string output = _converter.Apply(input);

        Assert.AreEqual(expected, output);
        Assert.IsEmpty(await CompilingTestProject.GetCompileErrorsAsync(document, output));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async System.Threading.Tasks.Task OutArgumentsWhoseTypeTheCallCannotInfer_StillCompile()
    {
        string input =
            "using System;\n" +
            "class C\n" +
            "{\n" +
            "    static void F(out int v) { v = 1; }\n" +
            "    static void F(out long v) { v = 1; }\n" +
            "    DayOfWeek M(string s)\n" +
            "    {\n" +
            "        DayOfWeek day;\n" +
            "        Enum.TryParse(s, out day);\n" +
            "        int n;\n" +
            "        F(out n);\n" +
            "        int a;\n" +
            "        F(out a);\n" +
            "        int b;\n" +
            "        F(out b);\n" +
            "        Console.WriteLine(n + a + b);\n" +
            "        return day;\n" +
            "    }\n" +
            "}\n";
        Microsoft.CodeAnalysis.Document document = CompilingTestProject.CreateDocument(input);
        string expected = input
            .Replace("        DayOfWeek day;\n        Enum.TryParse(s, out day);", "        Enum.TryParse(s, out DayOfWeek day);")
            .Replace("        int n;\n        F(out n);", "        F(out int n);")
            .Replace("        int a;\n        F(out a);\n        int b;\n        F(out b);", "        F(out int a);\n        F(out int b);");

        string output = _converter.Apply(input);

        Assert.AreEqual(expected, output);
        Assert.IsEmpty(await CompilingTestProject.GetCompileErrorsAsync(document, output));
    }
}
