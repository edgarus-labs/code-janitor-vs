using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Transformations;

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
}
