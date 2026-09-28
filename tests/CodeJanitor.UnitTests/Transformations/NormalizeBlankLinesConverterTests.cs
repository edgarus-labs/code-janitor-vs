using CodeJanitor.Logic.Transformations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

[TestClass]
public sealed class NormalizeBlankLinesConverterTests
{
    private NormalizeBlankLinesConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new NormalizeBlankLinesConverter();
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NoBlankLines_Unchanged()
    {
        string input = "class C\n{\n    void M() { }\n}";
        Assert.AreEqual(input, _converter.Normalize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SingleBlankLine_Unchanged()
    {
        string input = "class C\n{\n    void A() { }\n\n    void B() { }\n}";
        Assert.AreEqual(input, _converter.Normalize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TwoBlankLines_CollapsedToOne()
    {
        string input = "class C\n{\n    void A() { }\n\n\n    void B() { }\n}";
        string expected = "class C\n{\n    void A() { }\n\n    void B() { }\n}";
        Assert.AreEqual(expected, _converter.Normalize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ThreeBlankLines_CollapsedToOne()
    {
        string input = "void A() { }\n\n\n\nvoid B() { }";
        string expected = "void A() { }\n\nvoid B() { }";
        Assert.AreEqual(expected, _converter.Normalize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void CrlfTwoBlankLines_CollapsedToOne()
    {
        string input = "void A() { }\r\n\r\n\r\nvoid B() { }";
        string expected = "void A() { }\r\n\r\nvoid B() { }";
        Assert.AreEqual(expected, _converter.Normalize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void MultipleRunsOfBlankLines_AllCollapsed()
    {
        string input = "A\n\n\n\nB\n\n\nC\n\n\n\n\nD";
        string expected = "A\n\nB\n\nC\n\nD";
        Assert.AreEqual(expected, _converter.Normalize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void BlankLinesWithWhitespace_CollapsedCorrectly()
    {
        // Intermediate blank lines that contain only whitespace (e.g. indented editors) are collapsed.
        string input = "A\n\n   \n\nB";
        string expected = "A\n\nB";
        Assert.AreEqual(expected, _converter.Normalize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void EmptyString_Unchanged()
    {
        Assert.AreEqual(string.Empty, _converter.Normalize(string.Empty));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NullString_ReturnsNull()
    {
        Assert.IsNull(_converter.Normalize(null));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ImplementsISourceTransformation()
    {
        Assert.AreEqual("Normalize blank lines", _converter.Name);
        string input = "A\n\n\nB";
        string expected = "A\n\nB";
        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TopLevelStatementsFile_RunsBetweenUsingsStatementsAndTypesCollapsed()
    {
        string input = "using System;\r\n\r\n\r\n\r\nConsole.WriteLine(1);\r\n\r\n  \r\n\r\nclass C { }\r\n";

        Assert.AreEqual("using System;\r\n\r\nConsole.WriteLine(1);\r\n\r\nclass C { }\r\n", _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void BlankLinesAtTheEndOfTheFile_CollapsedToOne()
    {
        Assert.AreEqual("class C { }\n\n", _converter.Apply("class C { }\n\n\n\n"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void MixedLineBreaksInARun_UseCrLf()
    {
        Assert.AreEqual("a\r\n\r\nb", _converter.Apply("a\r\n\n\r\nb"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("var s = @\"a\r\n\r\n\r\nb\";\r\n", DisplayName = "verbatim string")]
    [DataRow("var s = \"\"\"\r\n    a\r\n\r\n\r\n    b\r\n    \"\"\";\r\n", DisplayName = "raw string")]
    [DataRow("var s = $@\"{x}\n\n  \n{y}\";\n", DisplayName = "interpolated verbatim string")]
    public void BlankLinesInsideMultiLineStringLiterals_Preserved(string input)
    {
        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("\r\n\r\n\r\nclass C { }", "\r\nclass C { }", DisplayName = "CRLF")]
    [DataRow("\n  \n\t\nclass C { }\n", "\nclass C { }\n", DisplayName = "LF with whitespace-only lines")]
    [DataRow("\nclass C { }", "\nclass C { }", DisplayName = "single blank line kept")]
    public void BlankLinesAtTheStartOfTheFile_CollapsedToOne(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void BlankLinesInsideVerbatimStringOfInactiveBranch_AreKept()
    {
        string input = "#if DEBUG\nconst string S = @\"a\n\n\n\nb\";\n#endif\n";

        Assert.AreEqual(input, _converter.Normalize(input));
    }
}
