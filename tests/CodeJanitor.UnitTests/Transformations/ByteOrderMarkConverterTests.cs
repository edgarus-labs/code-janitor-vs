using CodeJanitor.Logic.Transformations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="ByteOrderMarkConverter" />.
/// </summary>
[TestClass]
public sealed class ByteOrderMarkConverterTests
{
    private ByteOrderMarkConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new ByteOrderMarkConverter();
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Name_IsNotEmpty()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(_converter.Name));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_WithLeadingBom_StripsBom()
    {
        string input = "\uFEFFusing System;\r\n\r\npublic class C { }\r\n";
        string expected = "using System;\r\n\r\npublic class C { }\r\n";

        string result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_WithoutLeadingBom_ReturnsSameText()
    {
        string input = "using System;\r\n\r\npublic class C { }\r\n";

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
    public void Apply_BomOnly_ReturnsEmptyString()
    {
        string input = "\uFEFF";

        string result = _converter.Apply(input);

        Assert.AreEqual(string.Empty, result);
    }
}
