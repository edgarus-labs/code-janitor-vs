using CodeJanitor.Logic.Transformations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="EnsureFinalNewlineConverter" />.
/// </summary>
[TestClass]
public sealed class EnsureFinalNewlineConverterTests
{
    private EnsureFinalNewlineConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new EnsureFinalNewlineConverter();
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void MissingFinalNewline_Added()
    {
        string input = "class C\n{\n}";
        string expected = "class C\n{\n}\n";

        Assert.AreEqual(expected, _converter.Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ExistingFinalNewline_Unchanged()
    {
        string input = "class C\n{\n}\n";

        Assert.AreEqual(input, _converter.Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void CrlfFileMissingFinalNewline_GetsCrlf()
    {
        string input = "class C\r\n{\r\n}";
        string expected = "class C\r\n{\r\n}\r\n";

        Assert.AreEqual(expected, _converter.Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ExistingCrlfFinalNewline_Unchanged()
    {
        string input = "class C\r\n{\r\n}\r\n";

        Assert.AreEqual(input, _converter.Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void MultipleTrailingNewlines_CollapsedToOne()
    {
        string input = "class C\n{\n}\n\n\n";
        string expected = "class C\n{\n}\n";

        Assert.AreEqual(expected, _converter.Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void MultipleTrailingCrlfNewlines_CollapsedToOneCrlf()
    {
        string input = "class C\r\n{\r\n}\r\n\r\n";
        string expected = "class C\r\n{\r\n}\r\n";

        Assert.AreEqual(expected, _converter.Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void EmptySource_Unchanged()
    {
        Assert.AreEqual(string.Empty, _converter.Convert(string.Empty));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NullSource_ReturnsNull()
    {
        Assert.IsNull(_converter.Convert(null));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ImplementsSourceTransformation()
    {
        ISourceTransformation transformation = new EnsureFinalNewlineConverter();

        Assert.AreEqual("class C\n{\n}\n", transformation.Apply("class C\n{\n}"));
        Assert.AreEqual("Ensure final newline", transformation.Name);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("Console.WriteLine();\r\n// end", "Console.WriteLine();\r\n// end\r\n", DisplayName = "top-level statements file ending with a comment")]
    [DataRow("#region R\r\n#endregion", "#region R\r\n#endregion\r\n", DisplayName = "file ending with a directive")]
    [DataRow("\r\n\r\n", "\r\n", DisplayName = "file of blank lines")]
    [DataRow("a\nb\r\n\n", "a\nb\r\n", DisplayName = "mixed line breaks use CRLF")]
    public void FinalLineBreak_IsExactlyOne(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { }\r", "class C { }\r", DisplayName = "CR-only file already ending with a line break")]
    [DataRow("class C\r{ }", "class C\r{ }\r", DisplayName = "CR-only file missing the final line break")]
    [DataRow("class C { }\r\r\r", "class C { }\r", DisplayName = "CR-only file with several final line breaks")]
    public void CarriageReturnOnlyFile_KeepsItsLineBreakStyle(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C\n{\n}", DisplayName = "missing final line break")]
    [DataRow("class C\r\n{\r\n}\r\n\r\n\r\n", DisplayName = "several CRLF line breaks")]
    [DataRow("a\nb\r\n\n", DisplayName = "mixed line breaks")]
    [DataRow("class C { }\r\r\r", DisplayName = "CR-only file with several final line breaks")]
    [DataRow("#region R\r\n#endregion", DisplayName = "file ending with a directive")]
    [DataRow("\r\n\r\n", DisplayName = "file of blank lines")]
    public void Apply_IsIdempotent(string input)
    {
        string once = _converter.Convert(input);

        Assert.AreNotEqual(input, once, "The scenario has to change the input at all.");
        Assert.AreEqual(once, _converter.Convert(once));
    }
}
