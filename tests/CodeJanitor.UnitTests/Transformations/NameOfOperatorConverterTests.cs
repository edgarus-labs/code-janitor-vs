using System.Threading.Tasks;
using CodeJanitor.Logic.Transformations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="NameOfOperatorConverter" />.
/// </summary>
[TestClass]
public sealed class NameOfOperatorConverterTests
{
    private NameOfOperatorConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new NameOfOperatorConverter();
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Name_IsNotEmpty()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(_converter.Name));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_ArgumentNullException_ConvertsToNameOf()
    {
        string input = @"
public class C
{
    public void M(string myParam)
    {
        if (myParam == null)
        {
            throw new ArgumentNullException(""myParam"");
        }
    }
}";
        string expected = @"
public class C
{
    public void M(string myParam)
    {
        if (myParam == null)
        {
            throw new ArgumentNullException(nameof(myParam));
        }
    }
}";

        string result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_ArgumentException_ConvertsParameterNameToNameOf()
    {
        string input = @"
public class C
{
    public void M(int count)
    {
        if (count < 0)
        {
            throw new ArgumentException(""Count cannot be negative."", ""count"");
        }
    }
}";
        string expected = @"
public class C
{
    public void M(int count)
    {
        if (count < 0)
        {
            throw new ArgumentException(""Count cannot be negative."", nameof(count));
        }
    }
}";

        string result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_UnknownIdentifier_DoesNotConvert()
    {
        string input = @"
public class C
{
    public void M(int count)
    {
        throw new ArgumentNullException(""nonExistentParam"");
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
    public void Apply_QualifiedArgumentOutOfRangeException_ConvertsConstructorParameter()
    {
        string input = "public class C { public C(int value) { throw new System.ArgumentOutOfRangeException(\"value\"); } }";

        string result = _converter.Apply(input);

        Assert.AreEqual(input.Replace("\"value\"", "nameof(value)"), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_LocalFunctionParameter_ConvertsToNameOf()
    {
        string input = "public class C { public void M() { void Check(string value) { throw new ArgumentNullException(\"value\"); } } }";

        string result = _converter.Apply(input);

        Assert.AreEqual(input.Replace("\"value\"", "nameof(value)"), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_SimpleLambdaParameter_ConvertsToNameOf()
    {
        string input = "using System; public class C { public Action<string> Create() => value => throw new ArgumentException(\"value\"); }";

        string result = _converter.Apply(input);

        Assert.AreEqual(input.Replace("\"value\"", "nameof(value)"), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_ParenthesizedLambdaParameters_ConvertsMatchingArgumentOnly()
    {
        string input = "using System; public class C { public Action<string, int> Create() => (value, count) => throw new ArgumentException(\"value\", \"count\"); }";

        string result = _converter.Apply(input);

        Assert.AreEqual(input.Replace("\"value\"", "nameof(value)").Replace("\"count\"", "nameof(count)"), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_NonTargetExceptionOrInvalidIdentifier_ReturnsOriginal()
    {
        string input = "public class C { public void M(string value) { throw new InvalidOperationException(\"value\"); throw new ArgumentException(\"not a valid identifier\"); } }";

        string result = _converter.Apply(input);

        Assert.AreEqual(input, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_ExceptionWithoutArguments_ReturnsOriginal()
    {
        string input = "public class C { public void M() { throw new ArgumentException(); } }";

        string result = _converter.Apply(input);

        Assert.AreEqual(input, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("  \r\n\t", DisplayName = "whitespace only")]
    [DataRow("class C { void M(string value) { throw new ArgumentException { Source = \"value\" }; } }", DisplayName = "object initializer without argument list")]
    [DataRow("class C { void M() { throw new ArgumentNullException(\"value\"); } }", DisplayName = "method without parameters")]
    [DataRow("class C { Exception E = new ArgumentNullException(\"value\"); }", DisplayName = "field initializer")]
    [DataRow("class C { int _v; int V { set { if (value < 0) throw new ArgumentOutOfRangeException(\"value\"); _v = value; } } }", DisplayName = "setter value is not collected")]
    [DataRow("class C(string name) { void M() { throw new ArgumentNullException(\"name\"); } }", DisplayName = "primary constructor parameter is not collected")]
    [DataRow("class C { System.Action<string> A = delegate (string value) { throw new ArgumentNullException(\"value\"); }; }", DisplayName = "anonymous method parameter is not collected")]
    [DataRow("class C { void M(string value) { throw new ArgumentNullException(\"Value\"); } }", DisplayName = "different casing")]
    [DataRow("class C { void M(string @class) { throw new ArgumentNullException(\"class\"); } }", DisplayName = "verbatim identifier")]
    [DataRow("class C { void M(string value) { throw new ArgumentNullException($\"value\"); } }", DisplayName = "interpolated string")]
    [DataRow("class C { void M(string value) { throw new ArgumentNullException(nameof(value)); } }", DisplayName = "already nameof")]
    [DataRow("class C { void M(string value) { ArgumentNullException e = new(\"value\"); } }", DisplayName = "target-typed new")]
    [DataRow("class C { void M(string value) { throw new NotSupportedException(\"value\"); } }", DisplayName = "other exception type")]
    [DataRow("class C { void M(string value) { throw new MyArgumentException(\"value\"); } }", DisplayName = "name only ends with a target type")]
    [DataRow("class C { void M(string value) { string s = \"value\"; throw new ArgumentNullException(s); } }", DisplayName = "literal outside the constructor call")]
    [DataRow("class C { void M(string value) { throw new ArgumentException(\"value\"u8.ToString()); } }", DisplayName = "UTF-8 literal")]
    public void LiteralThatCannotBecomeNameOf_IsUnchanged(string input)
    {
        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { void M(string value) { throw new global::System.ArgumentNullException(\"value\"); } }", "class C { void M(string value) { throw new global::System.ArgumentNullException(nameof(value)); } }", DisplayName = "global qualified")]
    [DataRow("class C { void M(int value) { throw new System.ComponentModel.InvalidEnumArgumentException(\"value\", value, typeof(int)); } }", "class C { void M(int value) { throw new System.ComponentModel.InvalidEnumArgumentException(nameof(value), value, typeof(int)); } }", DisplayName = "invalid enum argument")]
    [DataRow("class C { void M(string value, string message) { throw new ArgumentException(message, \"value\"); } }", "class C { void M(string value, string message) { throw new ArgumentException(message, nameof(value)); } }", DisplayName = "non-literal message")]
    [DataRow("class C { void M(string value) { throw new ArgumentException(message: \"Bad\", paramName: \"value\"); } }", "class C { void M(string value) { throw new ArgumentException(message: \"Bad\", paramName: nameof(value)); } }", DisplayName = "named arguments")]
    [DataRow("class C { void M(string value) { throw new ArgumentNullException(@\"value\"); } }", "class C { void M(string value) { throw new ArgumentNullException(nameof(value)); } }", DisplayName = "verbatim literal")]
    [DataRow("class C { void M(string value) { throw new ArgumentNullException(\"\"\"value\"\"\"); } }", "class C { void M(string value) { throw new ArgumentNullException(nameof(value)); } }", DisplayName = "raw literal")]
    [DataRow("class C { void M(string outer) { System.Action<string> a = inner => throw new ArgumentException(\"inner\", \"outer\"); } }", "class C { void M(string outer) { System.Action<string> a = inner => throw new ArgumentException(nameof(inner), nameof(outer)); } }", DisplayName = "lambda inside method sees both scopes")]
    [DataRow("class C { void M(string value) { System.Func<string, Exception> f = static x => new ArgumentNullException(\"x\"); } }", "class C { void M(string value) { System.Func<string, Exception> f = static x => new ArgumentNullException(nameof(x)); } }", DisplayName = "static lambda")]
    [DataRow("class C { void M(string value) { throw new ArgumentException(\"value\", new ArgumentNullException(\"value\")); } }", "class C { void M(string value) { throw new ArgumentException(nameof(value), new ArgumentNullException(nameof(value))); } }", DisplayName = "nested exception")]
    [DataRow("record R { public R(string name) { if (name == null) throw new ArgumentNullException(\"name\"); } }", "record R { public R(string name) { if (name == null) throw new ArgumentNullException(nameof(name)); } }", DisplayName = "record constructor")]
    [DataRow("struct S { void M<T>(T item) where T : class { _ = item ?? throw new ArgumentNullException(\"item\"); } }", "struct S { void M<T>(T item) where T : class { _ = item ?? throw new ArgumentNullException(nameof(item)); } }", DisplayName = "generic method in struct")]
    public void LiteralNamingAParameterInScope_BecomesNameOf(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TopLevelStatementsFile_ConvertsOnlyLocalFunctionAndMemberParameters()
    {
        string input =
            "using System;\r\n" +
            "\r\n" +
            "if (args.Length == 0) throw new ArgumentException(\"args\");\r\n" +
            "Check(args[0]);\r\n" +
            "\r\n" +
            "static void Check(string path)\r\n" +
            "{\r\n" +
            "    if (path.Length == 0) throw new ArgumentException(\"path\");\r\n" +
            "}\r\n" +
            "\r\n" +
            "class Guard\r\n" +
            "{\r\n" +
            "    public static void NotNull(object value) { if (value == null) throw new ArgumentNullException(\"value\"); }\r\n" +
            "}\r\n";
        string expected = input
            .Replace("ArgumentException(\"path\")", "ArgumentException(nameof(path))")
            .Replace("ArgumentNullException(\"value\")", "ArgumentNullException(nameof(value))");

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("\r\n", DisplayName = "CRLF")]
    [DataRow("\n", DisplayName = "LF")]
    public void TriviaAroundTheLiteral_IsPreserved(string newLine)
    {
        string input =
            "class C" + newLine +
            "{" + newLine +
            "\t/// <param name=\"value\">The value.</param>" + newLine +
            "\tvoid M(string value)" + newLine +
            "\t{" + newLine +
            "\t\t// \"value\" must not be null" + newLine +
            "\t\tthrow new ArgumentNullException(" + newLine +
            "\t\t\t/* name */ \"value\" /* end */); // trailing" + newLine +
            "\t}" + newLine +
            "}" + newLine;
        string expected = input.Replace("/* name */ \"value\" /* end */", "/* name */ nameof(value) /* end */");

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FileWithSyntaxErrors_ConvertsTheValidCallAndKeepsTheRestVerbatim()
    {
        string input = "class C { void M(string value) { throw new ArgumentNullException(\"value\"); } void N( { int x = ; } }";
        string expected = "class C { void M(string value) { throw new ArgumentNullException(nameof(value)); } void N( { int x = ; } }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ConvertedLiterals_CompileWithoutNewErrors()
    {
        string input =
            "using System;\r\n" +
            "class C\r\n" +
            "{\r\n" +
            "    public C(string name) { if (name == null) throw new ArgumentNullException(\"name\"); }\r\n" +
            "    public void M(int count, string label)\r\n" +
            "    {\r\n" +
            "        if (count < 0) throw new ArgumentOutOfRangeException(\"count\", count, \"Must be positive.\");\r\n" +
            "        Func<string, Exception> make = text => new ArgumentException(\"Bad\", \"text\");\r\n" +
            "        void Local(string inner) { throw new ArgumentException(\"label\", \"inner\"); }\r\n" +
            "        Local(label);\r\n" +
            "    }\r\n" +
            "}\r\n";
        Microsoft.CodeAnalysis.Document document = CompilingTestProject.CreateDocument(input);
        string expected = input
            .Replace("ArgumentNullException(\"name\")", "ArgumentNullException(nameof(name))")
            .Replace("ArgumentOutOfRangeException(\"count\",", "ArgumentOutOfRangeException(nameof(count),")
            .Replace("ArgumentException(\"Bad\", \"text\")", "ArgumentException(\"Bad\", nameof(text))")
            .Replace("ArgumentException(\"label\", \"inner\")", "ArgumentException(nameof(label), nameof(inner))");

        string output = _converter.Apply(input);

        Assert.AreEqual(expected, output);
        Assert.IsEmpty(await CompilingTestProject.GetCompileErrorsAsync(document, output));
    }
}
