using CodeJanitor.Logic.Transformations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="StringInterpolationConverter" />.
/// </summary>
[TestClass]
public sealed class StringInterpolationConverterTests
{
    private StringInterpolationConverter _converter;

    [TestInitialize]
    public void TestInitialize() => _converter = new StringInterpolationConverter();

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Name_IsNotEmpty() => Assert.IsFalse(string.IsNullOrWhiteSpace(_converter.Name));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_SimpleStringFormat_ConvertsToInterpolatedString()
    {
        string input = @"
public class C
{
    public string M(string name, int count)
    {
        return string.Format(""Hello {0}, you have {1} messages."", name, count);
    }
}";
        string expected = @"
public class C
{
    public string M(string name, int count)
    {
        return $""Hello {name}, you have {count} messages."";
    }
}";

        string result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_StringFormatWithFormatSpecifier_ConvertsProperly()
    {
        string input = @"
public class C
{
    public string M(double price)
    {
        return string.Format(""Price: {0:C2}"", price);
    }
}";
        string expected = @"
public class C
{
    public string M(double price)
    {
        return $""Price: {price:C2}"";
    }
}";

        string result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_StringFormatWithAlignment_ConvertsProperly()
    {
        string input = @"
public class C
{
    public string M(int id)
    {
        return string.Format(""ID: {0,5}"", id);
    }
}";
        string expected = @"
public class C
{
    public string M(int id)
    {
        return $""ID: {id,5}"";
    }
}";

        string result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_NonLiteralFormatString_Skipped()
    {
        string input = @"
public class C
{
    public string M(string template, int value)
    {
        return string.Format(template, value);
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
    public void Apply_SystemStringFormatAndStringFormat_ConvertsProperly()
    {
        string input = "public class C { public string M(int x) => System.String.Format(\"Val: {0}\", x) + String.Format(\" Other: {0}\", x); }";
        string expected = "public class C { public string M(int x) => $\"Val: {x}\" + $\" Other: {x}\"; }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_FormatStringWithEscapedCharacters_EscapesProperly()
    {
        string input = "public class C { public string M(string s) => string.Format(\"Quote: \\\"{0}\\\"\\r\\nTab:\\t{0}\", s); }";
        string expected = "public class C { public string M(string s) => $\"Quote: \\\"{s}\\\"\\r\\nTab:\\t{s}\"; }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_OutOfBoundsOrNoPlaceholder_Skipped()
    {
        string input1 = "public class C { public string M(int x) => string.Format(\"No placeholder\", x); }";
        Assert.AreEqual(input1, _converter.Apply(input1));

        string input2 = "public class C { public string M(int x) => string.Format(\"Index: {5}\", x); }";
        Assert.AreEqual(input2, _converter.Apply(input2));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_OtherTypeFormatOrSingleArgument_Skipped()
    {
        string input1 = "public class C { public string M(int x) => OtherClass.Format(\"{0}\", x); }";
        Assert.AreEqual(input1, _converter.Apply(input1));

        string input2 = "public class C { public string M(string s) => string.Format(s); }";
        Assert.AreEqual(input2, _converter.Apply(input2));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_ArgumentSpanningLines_Skipped()
    {
        // A line break inside an interpolation hole of a regular interpolated string needs C# 11 (CS8967 before).
        string input = "public class C { public string M(int a, int b) => string.Format(\"{0}\", a +\r\n    b); }";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("  \r\n\t", DisplayName = "whitespace only")]
    [DataRow("using static System.String; class C { string M(int x) => Format(\"{0}\", x); }", DisplayName = "unqualified Format through using static")]
    [DataRow("class C { string M(int x) => string.Concat(\"{0}\", x); }", DisplayName = "other string method")]
    [DataRow("class C { string M(int x) => string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{0}\", x); }", DisplayName = "format provider overload")]
    [DataRow("class C { string M(int x) => string.Format($\"{x}{{0}}\", x); }", DisplayName = "interpolated format string")]
    [DataRow("class C { string M(int x) => string.Format(\"{99999999999}\", x); }", DisplayName = "index that overflows int")]
    [DataRow("class C { string M(int a, int b) => string.Format(\"{0} { 1}\", a, b); }", DisplayName = "placeholder with leading space, invalid for .NET")]
    [DataRow("class C { string M(int a) => string.Format(\"{0} } {x}\", a); }", DisplayName = "unescaped braces next to a placeholder")]
    [DataRow("class C { string M(int x) => string.Format(\"{0}{1}\", x); }", DisplayName = "one placeholder out of range")]
    [DataRow("class C { string M(object[] a) => string.Format(\"{0}{1}\", a); }", DisplayName = "second placeholder out of range with an array argument")]
    [DataRow("class C { string M(int x) => System.Text.StringBuilder.Format(\"{0}\", x); }", DisplayName = "qualified non-string receiver")]
    [DataRow("class C\r\n{\r\n    // string.Format(\"{0}\", x)\r\n    string A = \"string.Format(\\\"{0}\\\", x)\";\r\n}\r\n", DisplayName = "code-like text in comment and literal")]
    public void CallThatCannotBecomeAnInterpolatedString_IsUnchanged(string input) => Assert.AreEqual(input, _converter.Apply(input));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { string M() => string.Format(\"{0}-{0}\", Next()); int Next() => 0; }", DisplayName = "repeated call argument")]
    [DataRow("class C { string M() => string.Format(\"{1}{0}\", A(), B()); int A() => 0; int B() => 0; }", DisplayName = "reordered call arguments")]
    [DataRow("class C { string M() => string.Format(\"{0}\", A(), Log()); int A() => 0; int Log() => 0; }", DisplayName = "unused call argument")]
    [DataRow("class C { string M(int x) => string.Format(\"{1}{0}\", x++, x++); }", DisplayName = "reordered increment")]
    [DataRow("class C { string M(int x) => string.Format(\"{0}{0}\", x++); }", DisplayName = "repeated increment")]
    [DataRow("class C { string M() => string.Format(\"{0} {0}\", System.DateTime.Now); }", DisplayName = "repeated property read that can change between reads")]
    [DataRow("class C { string M(C c) => string.Format(\"{0}-{0}\", c.Name.Length); string Name; }", DisplayName = "repeated member access chain")]
    [DataRow("class C { string M(C c, C d) => string.Format(\"{1} before {0}\", c.Name, d.Name); string Name; }", DisplayName = "reordered member access chains")]
    [DataRow("class C { string M(C c, int b) => string.Format(\"{0}\", b, c.Name); string Name; }", DisplayName = "unused member access chain")]
    [DataRow("class C { string M(int i) => string.Format(\"{1} {0}\", i, i++); }", DisplayName = "variable read moved after its own increment")]
    [DataRow("class C { string M(int i) => string.Format(\"{0} {1} {0}\", i, i++); }", DisplayName = "variable read again after its own increment")]
    [DataRow("class C { string M(int i, int j) => string.Format(\"{1} {0}\", i, i = j); }", DisplayName = "variable read moved after its own assignment")]
    [DataRow("class C { string M(int i) => string.Format(\"{1} {0}\", i, Read(out i)); int Read(out int v) { v = 1; return v; } }", DisplayName = "variable read moved after its own out argument")]
    [DataRow("class C { string M(int x) => string.Format(\"{0}\", x, Log()); int Log() => 0; }", DisplayName = "unused call after a used identifier")]
    [DataRow("class C { string M(int x) => string.Format(/* keep */ \"{0}\", x); }", DisplayName = "comment before the format string")]
    [DataRow("class C { string M(int x) => string.Format(\"{0}\", /* keep */ x); }", DisplayName = "comment before an argument")]
    [DataRow("class C { string M(int x, int y) => string.Format(\"{0}\", x /* keep */, y); }", DisplayName = "comment after an unused argument")]
    [DataRow("class C { string M(int x) => string.Format(\"{0}\", x /* keep */); }", DisplayName = "comment before the closing parenthesis")]
    public void FormatCallWhoseConversionWouldChangeSideEffectsOrDropComments_IsUnchanged(string input) => Assert.AreEqual(input, _converter.Apply(input));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { string M() => string.Format(\"{0}-{0}\", 5); }", "class C { string M() => $\"{5}-{5}\"; }", DisplayName = "repeated literal")]
    [DataRow("class C { string M(int a, int b) => string.Format(\"{0}\", a, b); }", "class C { string M(int a, int b) => $\"{a}\"; }", DisplayName = "unused side-effect-free argument")]
    [DataRow("class C { string M(int a) => string.Format(\"{1}-{0}-{1}\", Next(), a); int Next() => 0; }", "class C { string M(int a) => $\"{a}-{Next()}-{a}\"; int Next() => 0; }", DisplayName = "repeated side-effect-free argument beside a single call")]
    [DataRow("class C { string M(int i, int j) => string.Format(\"{0} {1}\", i, i++); }", "class C { string M(int i, int j) => $\"{i} {i++}\"; }", DisplayName = "variable read before its own increment, in argument order")]
    [DataRow("class C { string M(int i) => string.Format(\"{0} {1}\", i++, i); }", "class C { string M(int i) => $\"{i++} {i}\"; }", DisplayName = "variable read after its own increment, in argument order")]
    [DataRow("class C { string M(int i, int j) => string.Format(\"{1} {0}\", i, j++); }", "class C { string M(int i, int j) => $\"{j++} {i}\"; }", DisplayName = "variable read moved around an unrelated increment")]
    public void FormatCallWhoseEvaluationIsPreserved_BecomesAnInterpolatedString(string input, string expected) => Assert.AreEqual(expected, _converter.Apply(input));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { string M(int a, int b) => string.Format(\"{1} before {0}\", a, b); }", "class C { string M(int a, int b) => $\"{b} before {a}\"; }", DisplayName = "reordered placeholders")]
    [DataRow("class C { string M(int a) => string.Format(\"{0}-{0}\", a); }", "class C { string M(int a) => $\"{a}-{a}\"; }", DisplayName = "repeated placeholder")]
    [DataRow("class C { string M(double a) => string.Format(\"[{0,-10:N2}]\", a); }", "class C { string M(double a) => $\"[{a,-10:N2}]\"; }", DisplayName = "alignment and format")]
    [DataRow("class C { string M(System.DateTime d) => string.Format(\"{0:yyyy-MM-dd HH:mm}\", d); }", "class C { string M(System.DateTime d) => $\"{d:yyyy-MM-dd HH:mm}\"; }", DisplayName = "format with colon and space")]
    [DataRow("class C { string M(int a) => string.Format(\"{{{0}}}\", a); }", "class C { string M(int a) => $\"{{{a}}}\"; }", DisplayName = "placeholder inside escaped braces")]
    [DataRow("class C { string M(int a) => string.Format(@\"Value \"\"{0}\"\"\", a); }", "class C { string M(int a) => $\"Value \\\"{a}\\\"\"; }", DisplayName = "verbatim format with quotes")]
    [DataRow("class C { string M(Item i, int[] a) => string.Format(\"{0} {1} {2} {3}\", i.Name, i?.Name, a[0], (object)i); }", "class C { string M(Item i, int[] a) => $\"{i.Name} {i?.Name} {a[0]} {(object)i}\"; }", DisplayName = "argument shapes")]
    [DataRow("class C { string M(int a) => string.Format(\"{0}\", string.Format(\"<{0}>\", a)); }", "class C { string M(int a) => $\"{$\"<{a}>\"}\"; }", DisplayName = "nested string.Format")]
    [DataRow("class C { string M(int a) => string.Format(\"é {0} ✓\", a); }", "class C { string M(int a) => $\"é {a} ✓\"; }", DisplayName = "non-ASCII text")]
    [DataRow("class C { System.Func<int, string> F = x => string.Format(\"{0}\", x); }", "class C { System.Func<int, string> F = x => $\"{x}\"; }", DisplayName = "inside lambda")]
    [DataRow("record R(int X) { public override string ToString() => string.Format(\"R({0})\", X); }", "record R(int X) { public override string ToString() => $\"R({X})\"; }", DisplayName = "record")]
    [DataRow("class C { string M(int a, int b) => string.Format(\"{0}: {1, 10}\", a, b); }", "class C { string M(int a, int b) => $\"{a}: {b,10}\"; }", DisplayName = "space after the alignment comma")]
    [DataRow("class C { string M(int x) => string.Format(\"{0 }\", x); }", "class C { string M(int x) => $\"{x}\"; }", DisplayName = "placeholder with trailing space")]
    [DataRow("class C { string M(int a) => string.Format(\"[{0:}]\", a); }", "class C { string M(int a) => $\"[{a}]\"; }", DisplayName = "empty format")]
    [DataRow("class C { string M(double a) => string.Format(\"{0 ,5}|{0, -5 :N2}\", a); }", "class C { string M(double a) => $\"{a,5}|{a,-5:N2}\"; }", DisplayName = "spaces around the alignment")]
    public void FormatCallWithLiteralFormat_BecomesAnInterpolatedString(string input, string expected) => Assert.AreEqual(expected, _converter.Apply(input));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TopLevelStatementsFile_ConvertsStatementsLocalFunctionsAndTypes()
    {
        string input =
            "using System;\r\n" +
            "\r\n" +
            "Console.WriteLine(string.Format(\"Hello {0}\", args.Length));\r\n" +
            "\r\n" +
            "static string Describe(int n) => String.Format(\"n={0}\", n);\r\n" +
            "\r\n" +
            "class Printer\r\n" +
            "{\r\n" +
            "    public string Print(int n) => System.String.Format(\"[{0}]\", n);\r\n" +
            "}\r\n";
        string expected = input
            .Replace("string.Format(\"Hello {0}\", args.Length)", "$\"Hello {args.Length}\"")
            .Replace("String.Format(\"n={0}\", n)", "$\"n={n}\"")
            .Replace("System.String.Format(\"[{0}]\", n)", "$\"[{n}]\"");

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("\r\n", DisplayName = "CRLF")]
    [DataRow("\n", DisplayName = "LF")]
    public void TriviaAroundTheCall_IsPreserved(string newLine)
    {
        string input =
            "class C" + newLine +
            "{" + newLine +
            "\t/// <summary>Formats.</summary>" + newLine +
            "\tstring M(int x)" + newLine +
            "\t{" + newLine +
            "\t\t// build it" + newLine +
            "\t\treturn /*a*/ string.Format(\"x={0}\", x) /*b*/; // trailing" + newLine +
            "\t}" + newLine +
            "#if NEVER" + newLine +
            "\tstring N(int x) => string.Format(\"{0}\", x);" + newLine +
            "#endif" + newLine +
            "}" + newLine;
        string expected = input.Replace("/*a*/ string.Format(\"x={0}\", x) /*b*/", "/*a*/ $\"x={x}\" /*b*/");

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FileWithSyntaxErrors_ConvertsTheValidCallAndKeepsTheRestVerbatim()
    {
        string input = "class C { string M(int x) => string.Format(\"{0}\", x); void N( { int y = ; } }";
        string expected = "class C { string M(int x) => $\"{x}\"; void N( { int y = ; } }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { string M(string n) => string.Format(\"C:\\\\temp\\\\{0}\", n); }", DisplayName = "regular literal")]
    [DataRow("class C { string M(string n) => string.Format(@\"C:\\temp\\{0}\", n); }", DisplayName = "verbatim literal")]
    public void BackslashInFormat_IsEscaped(string input)
    {
        string expected = "class C { string M(string n) => $\"C:\\\\temp\\\\{n}\"; }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void BackslashInFormatSpecifier_IsEscaped()
    {
        string input = "class C { string M(System.TimeSpan t) => string.Format(\"{0:hh\\\\:mm}\", t); }";
        string expected = "class C { string M(System.TimeSpan t) => $\"{t:hh\\\\:mm}\"; }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { string M(bool f) => string.Format(\"{0}\", f ? \"y\" : \"n\"); }", "class C { string M(bool f) => $\"{(f ? \"y\" : \"n\")}\"; }", DisplayName = "conditional operator")]
    [DataRow("class C { string M(int x) => string.Format(\"{0}\", global::System.Math.Abs(x)); }", "class C { string M(int x) => $\"{(global::System.Math.Abs(x))}\"; }", DisplayName = "alias-qualified name")]
    public void ArgumentWithTopLevelColon_IsParenthesized(string input, string expected) => Assert.AreEqual(expected, _converter.Apply(input));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { string M(int x) => string.Format(\"{{0}} {0}\", x); }", "class C { string M(int x) => $\"{{0}} {x}\"; }", DisplayName = "escaped braces around a digit")]
    [DataRow("class C { string M(int x) => string.Format(\"{{{0}}}\", x); }", "class C { string M(int x) => $\"{{{x}}}\"; }", DisplayName = "placeholder inside escaped braces")]
    public void EscapedBracesAroundDigit_StayLiteral(string input, string expected) => Assert.AreEqual(expected, _converter.Apply(input));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void OnlyEscapedBraces_IsUnchanged()
    {
        string input = "class C { string M(int x) => string.Format(\"{{0}}\", x); }";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async System.Threading.Tasks.Task ConvertedCalls_CompileWithoutNewErrors()
    {
        string input =
            "using System;\r\n" +
            "class Item { public string Name; }\r\n" +
            "class C\r\n" +
            "{\r\n" +
            "    public string A(Item i, int n) => string.Format(\"{0} has {1,5:N0} items\", i?.Name, n);\r\n" +
            "    public string B(int n) => String.Format(\"{{{0}}} \\\"quoted\\\"\", n);\r\n" +
            "    public string D(int n) => string.Format(\"{0}\", string.Format(\"<{0}>\", n));\r\n" +
            "    public string E(string s) => string.Format(\"{0}!\", \"text\" + s);\r\n" +
            "}\r\n";
        Microsoft.CodeAnalysis.Document document = CompilingTestProject.CreateDocument(input);
        string expected = input
            .Replace("string.Format(\"{0} has {1,5:N0} items\", i?.Name, n)", "$\"{i?.Name} has {n,5:N0} items\"")
            .Replace("String.Format(\"{{{0}}} \\\"quoted\\\"\", n)", "$\"{{{n}}} \\\"quoted\\\"\"")
            .Replace("string.Format(\"{0}\", string.Format(\"<{0}>\", n))", "$\"{$\"<{n}>\"}\"")
            .Replace("string.Format(\"{0}!\", \"text\" + s)", "$\"{\"text\" + s}!\"");

        string output = _converter.Apply(input);

        Assert.AreEqual(expected, output);
        Assert.IsEmpty(await CompilingTestProject.GetCompileErrorsAsync(document, output));
    }
}
