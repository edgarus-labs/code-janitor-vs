using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Transformations;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="SpaceToTabConverter" /> (<c>indent_style = tab</c>): leading indentation spaces become
/// tabs, while lines starting inside multi-line string literals, disabled text or multi-line comments keep their text.
/// </summary>

[TestClass]
public sealed class SpaceToTabConverterTests
{
    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FullIndentationLevels_BecomeTabs()
    {
        var input = "class C\n{\n    void M()\n    {\n        int x;\n    }\n}\n";
        var expected = "class C\n{\n\tvoid M()\n\t{\n\t\tint x;\n\t}\n}\n";

        Assert.AreEqual(expected, new SpaceToTabConverter(4).Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void RemainderSpaces_AreKeptAfterTheTabs_AndMixedIndentationIsMeasuredByTabStops()
    {
        var input = "class C\n{\n    int x =\n          1;\n  \tint y;\n\t  int z;\n}\n";
        var expected = "class C\n{\n\tint x =\n\t\t  1;\n\tint y;\n\t  int z;\n}\n";

        Assert.AreEqual(expected, new SpaceToTabConverter(4).Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TabSize_DecidesHowManySpacesMakeATab()
    {
        var input = "class C\n{\n  int x;\n     int y;\n}\n";
        var expected = "class C\n{\n\tint x;\n\t\t int y;\n}\n";

        Assert.AreEqual(expected, new SpaceToTabConverter(2).Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void CrLfLineBreaks_ArePreserved()
    {
        var input = "class C\r\n{\r\n    int x;\r\n}\r\n";
        var expected = "class C\r\n{\r\n\tint x;\r\n}\r\n";

        Assert.AreEqual(expected, new SpaceToTabConverter(4).Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void LinesInsideMultiLineStringLiterals_AreUntouched()
    {
        var input =
            "class C\r\n" +
            "{\r\n" +
            "    string Verbatim = @\"first\r\n" +
            "        second\";\r\n" +
            "\r\n" +
            "    string Raw = \"\"\"\r\n" +
            "        raw\r\n" +
            "            indented\r\n" +
            "        \"\"\";\r\n" +
            "\r\n" +
            "    string InterpolatedRaw(int x) => $\"\"\"\r\n" +
            "        {x} raw\r\n" +
            "        \"\"\";\r\n" +
            "}\r\n";
        var expected =
            "class C\r\n" +
            "{\r\n" +
            "\tstring Verbatim = @\"first\r\n" +
            "        second\";\r\n" +
            "\r\n" +
            "\tstring Raw = \"\"\"\r\n" +
            "        raw\r\n" +
            "            indented\r\n" +
            "        \"\"\";\r\n" +
            "\r\n" +
            "\tstring InterpolatedRaw(int x) => $\"\"\"\r\n" +
            "        {x} raw\r\n" +
            "        \"\"\";\r\n" +
            "}\r\n";

        var result = new SpaceToTabConverter(4).Convert(input);

        Assert.AreEqual(expected, result);
        CollectionAssert.AreEqual(GetStringValues(input), GetStringValues(result));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void DisabledTextAndMultiLineCommentContinuationLines_AreUntouched()
    {
        var input = "class C\n{\n#if NEVER\n    int disabled;\n#endif\n    /*\n     * comment\n     */\n    int x;\n}\n";
        var expected = "class C\n{\n#if NEVER\n    int disabled;\n#endif\n\t/*\n     * comment\n     */\n\tint x;\n}\n";

        Assert.AreEqual(expected, new SpaceToTabConverter(4).Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SpacesAfterTheIndentationAndOnBlankLines_AreUntouched()
    {
        var input = "class C\n{\n    int  x = 1;    // aligned\n        \n    /// <summary>\n    /// Doc.\n    /// </summary>\n    int y;\n}\n";
        var expected = "class C\n{\n\tint  x = 1;    // aligned\n        \n\t/// <summary>\n\t/// Doc.\n\t/// </summary>\n\tint y;\n}\n";

        Assert.AreEqual(expected, new SpaceToTabConverter(4).Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TabIndentedSource_ReturnsTheSameInstance()
    {
        var input = "class C\n{\n\tint x;\n\t  int y;\n}\n";

        Assert.AreSame(input, new SpaceToTabConverter(4).Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NullOrEmpty_ReturnsInput()
    {
        var converter = new SpaceToTabConverter(4);

        Assert.IsNull(converter.Convert(null));
        Assert.AreEqual(string.Empty, converter.Convert(string.Empty));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TabSizeBelowOne_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SpaceToTabConverter(0));
    }

    private static string[] GetStringValues(string source) =>
        CSharpSyntaxTree.ParseText(source).GetRoot().DescendantTokens()
            .Where(token => token.IsKind(SyntaxKind.StringLiteralToken)
                || token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken)
                || token.IsKind(SyntaxKind.InterpolatedStringTextToken)
                || token.IsKind(SyntaxKind.InterpolatedRawStringEndToken))
            .Select(token => token.Text)
            .ToArray();
}
