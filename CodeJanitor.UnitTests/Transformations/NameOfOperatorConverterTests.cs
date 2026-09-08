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
        var input = @"
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
        var expected = @"
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

        var result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_ArgumentException_ConvertsParameterNameToNameOf()
    {
        var input = @"
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
        var expected = @"
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

        var result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_UnknownIdentifier_DoesNotConvert()
    {
        var input = @"
public class C
{
    public void M(int count)
    {
        throw new ArgumentNullException(""nonExistentParam"");
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
    public void Apply_QualifiedArgumentOutOfRangeException_ConvertsConstructorParameter()
    {
        var input = "public class C { public C(int value) { throw new System.ArgumentOutOfRangeException(\"value\"); } }";

        var result = _converter.Apply(input);

        Assert.AreEqual(input.Replace("\"value\"", "nameof(value)"), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_LocalFunctionParameter_ConvertsToNameOf()
    {
        var input = "public class C { public void M() { void Check(string value) { throw new ArgumentNullException(\"value\"); } } }";

        var result = _converter.Apply(input);

        Assert.AreEqual(input.Replace("\"value\"", "nameof(value)"), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_SimpleLambdaParameter_ConvertsToNameOf()
    {
        var input = "using System; public class C { public Action<string> Create() => value => throw new ArgumentException(\"value\"); }";

        var result = _converter.Apply(input);

        Assert.AreEqual(input.Replace("\"value\"", "nameof(value)"), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_ParenthesizedLambdaParameters_ConvertsMatchingArgumentOnly()
    {
        var input = "using System; public class C { public Action<string, int> Create() => (value, count) => throw new ArgumentException(\"value\", \"count\"); }";

        var result = _converter.Apply(input);

        Assert.AreEqual(input.Replace("\"value\"", "nameof(value)").Replace("\"count\"", "nameof(count)"), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_NonTargetExceptionOrInvalidIdentifier_ReturnsOriginal()
    {
        var input = "public class C { public void M(string value) { throw new InvalidOperationException(\"value\"); throw new ArgumentException(\"not a valid identifier\"); } }";

        var result = _converter.Apply(input);

        Assert.AreEqual(input, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_ExceptionWithoutArguments_ReturnsOriginal()
    {
        var input = "public class C { public void M() { throw new ArgumentException(); } }";

        var result = _converter.Apply(input);

        Assert.AreEqual(input, result);
    }
}
