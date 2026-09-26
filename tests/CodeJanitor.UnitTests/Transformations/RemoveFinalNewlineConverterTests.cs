using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Transformations;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="RemoveFinalNewlineConverter" /> (<c>insert_final_newline = false</c>).
/// </summary>

[TestClass]
public sealed class RemoveFinalNewlineConverterTests
{
    private RemoveFinalNewlineConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new RemoveFinalNewlineConverter();
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C\r\n{\r\n}\r\n", "class C\r\n{\r\n}", DisplayName = "CRLF")]
    [DataRow("class C\n{\n}\n", "class C\n{\n}", DisplayName = "LF")]
    [DataRow("class C\r\n{\r\n}\r\n\r\n\n", "class C\r\n{\r\n}", DisplayName = "several line breaks")]
    [DataRow("class C {}\n  \n\t\n", "class C {}", DisplayName = "blank lines with whitespace")]
    [DataRow("class C {}  \n", "class C {}  ", DisplayName = "whitespace of the last line is kept")]
    [DataRow("// comment\r\n", "// comment", DisplayName = "comment on the last line")]
    public void FinalLineBreaks_AreRemoved(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NoFinalLineBreak_ReturnsTheSameInstance()
    {
        var input = "class C\r\n{\r\n}";

        Assert.AreSame(input, _converter.Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NullOrEmpty_ReturnsInput()
    {
        Assert.IsNull(_converter.Convert(null));
        Assert.AreEqual(string.Empty, _converter.Convert(string.Empty));
    }
}
