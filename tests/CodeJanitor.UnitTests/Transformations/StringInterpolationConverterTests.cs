using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Transformations;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="StringInterpolationConverter" />.
/// </summary>
[TestClass]
public sealed class StringInterpolationConverterTests
{
    private StringInterpolationConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new StringInterpolationConverter();
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Name_IsNotEmpty()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(_converter.Name));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_SimpleStringFormat_ConvertsToInterpolatedString()
    {
        var input = @"
public class C
{
    public string M(string name, int count)
    {
        return string.Format(""Hello {0}, you have {1} messages."", name, count);
    }
}";
        var expected = @"
public class C
{
    public string M(string name, int count)
    {
        return $""Hello {name}, you have {count} messages."";
    }
}";

        var result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_StringFormatWithFormatSpecifier_ConvertsProperly()
    {
        var input = @"
public class C
{
    public string M(double price)
    {
        return string.Format(""Price: {0:C2}"", price);
    }
}";
        var expected = @"
public class C
{
    public string M(double price)
    {
        return $""Price: {price:C2}"";
    }
}";

        var result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_StringFormatWithAlignment_ConvertsProperly()
    {
        var input = @"
public class C
{
    public string M(int id)
    {
        return string.Format(""ID: {0,5}"", id);
    }
}";
        var expected = @"
public class C
{
    public string M(int id)
    {
        return $""ID: {id,5}"";
    }
}";

        var result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_NonLiteralFormatString_Skipped()
    {
        var input = @"
public class C
{
    public string M(string template, int value)
    {
        return string.Format(template, value);
    }
}";

        var result = _converter.Apply(input);

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
        var input = "public class C { public string M(int x) => System.String.Format(\"Val: {0}\", x) + String.Format(\" Other: {0}\", x); }";
        var expected = "public class C { public string M(int x) => $\"Val: {x}\" + $\" Other: {x}\"; }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_FormatStringWithEscapedCharacters_EscapesProperly()
    {
        var input = "public class C { public string M(string s) => string.Format(\"Quote: \\\"{0}\\\"\\r\\nTab:\\t{0}\", s); }";
        var expected = "public class C { public string M(string s) => $\"Quote: \\\"{s}\\\"\\r\\nTab:\\t{s}\"; }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_OutOfBoundsOrNoPlaceholder_Skipped()
    {
        var input1 = "public class C { public string M(int x) => string.Format(\"No placeholder\", x); }";
        Assert.AreEqual(input1, _converter.Apply(input1));

        var input2 = "public class C { public string M(int x) => string.Format(\"Index: {5}\", x); }";
        Assert.AreEqual(input2, _converter.Apply(input2));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_OtherTypeFormatOrSingleArgument_Skipped()
    {
        var input1 = "public class C { public string M(int x) => OtherClass.Format(\"{0}\", x); }";
        Assert.AreEqual(input1, _converter.Apply(input1));

        var input2 = "public class C { public string M(string s) => string.Format(s); }";
        Assert.AreEqual(input2, _converter.Apply(input2));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_ArgumentSpanningLines_Skipped()
    {
        // A line break inside an interpolation hole of a regular interpolated string needs C# 11 (CS8967 before).
        var input = "public class C { public string M(int a, int b) => string.Format(\"{0}\", a +\r\n    b); }";

        Assert.AreEqual(input, _converter.Apply(input));
    }
}
