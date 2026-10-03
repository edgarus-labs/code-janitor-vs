using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public sealed class CommentFormatConverterTests
{
    private CommentFormatConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new CommentFormatConverter();
        Settings.Default.Formatting_CommentRunDuringCleanup = true;
    }

    [TestCleanup]
    public void TestCleanup() => Settings.Default.Formatting_CommentRunDuringCleanup = false;

    [TestMethod]
    public void SettingDisabled_ReturnsUnchanged()
    {
        Settings.Default.Formatting_CommentRunDuringCleanup = false;
        string source = "// comment\r\npublic class MyClass { }";
        string result = _converter.Apply(source);
        Assert.AreEqual(source, result);
    }

    [TestMethod]
    public void EmptySource_ReturnsUnchanged()
    {
        string source = "";
        string result = _converter.Apply(source);
        Assert.AreEqual(source, result);
    }

    [TestMethod]
    public void NullSource_ReturnsUnchanged()
    {
        string result = _converter.Apply(null);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void NoComments_ReturnsUnchanged()
    {
        string source = "public class MyClass { }";
        string result = _converter.Apply(source);
        Assert.AreEqual(source, result);
    }

    [TestMethod]
    public void SingleLineComment_NormalizesSpacing()
    {
        string source = "//  comment with extra spaces\r\npublic class MyClass { }";
        string result = _converter.Apply(source);
        // Should normalize to single space after //
        Assert.Contains("// comment with extra spaces", result);
    }

    [TestMethod]
    public void SingleLineCommentWithoutSpace_AddsSpace()
    {
        string source = "//comment\r\npublic class MyClass { }";
        string result = _converter.Apply(source);
        // Should add space after //
        Assert.Contains("// comment", result);
    }

    [TestMethod]
    public void IndentedComment_PreservesIndentation()
    {
        string source = "    // indented comment\r\npublic class MyClass { }";
        string result = _converter.Apply(source);
        // Should preserve indentation
        Assert.Contains("    // indented comment", result);
    }

    [TestMethod]
    public void EmptyComment_NormalizesCorrectly()
    {
        string source = "//\r\npublic class MyClass { }";
        string result = _converter.Apply(source);
        Assert.Contains("//", result);
    }

    [TestMethod]
    public void MultiLineComment_PreservesStructure()
    {
        string source = "/* comment */\r\npublic class MyClass { }";
        string result = _converter.Apply(source);
        Assert.Contains("/* comment */", result);
    }

    [TestMethod]
    public void MultipleComments_FormatsAll()
    {
        string source = "//  comment1\r\n//  comment2\r\npublic class MyClass { }";
        string result = _converter.Apply(source);
        // Both should be normalized
        Assert.Contains("// comment1", result);
        Assert.Contains("// comment2", result);
    }

    [TestMethod]
    public void PreservesNewlineStyle()
    {
        string source = "// comment\n// another";
        string result = _converter.Apply(source);
        // Should preserve \n style
        Assert.Contains("\n", result);
        Assert.DoesNotContain("\r\n", result);
    }

    [TestMethod]
    public void Name_ReturnsCorrectName() => Assert.AreEqual("Format comments", _converter.Name);

    [TestMethod]
    public void MultiLineComment_WithAsteriskContinuationLines_AlignsWithBaseIndentation()
    {
        string source = "    /*\r\n    * line 1\r\n    * line 2\r\n    */\r\n    public class MyClass { }";
        string result = _converter.Apply(source);

        Assert.Contains("    * line 1", result);
        Assert.Contains("    * line 2", result);
    }

    [TestMethod]
    public void MultiLineComment_WithoutAsteriskContinuationLines_PreservesLines()
    {
        string source = "    /*\r\n    content line 1\r\n    content line 2\r\n    */\r\n    public class MyClass { }";
        string result = _converter.Apply(source);

        Assert.Contains("    content line 1", result);
        Assert.Contains("    content line 2", result);
    }

    [TestMethod]
    public void PreservesCarriageReturnNewlineStyle()
    {
        string source = "// comment 1\r// comment 2";
        string result = _converter.Apply(source);

        Assert.Contains("\r", result);
    }

    [TestMethod]
    [DataRow("//Entry\nConsole.WriteLine(\"hi\");\n", "// Entry\nConsole.WriteLine(\"hi\");\n", DisplayName = "LF")]
    [DataRow("//Entry\r\nConsole.WriteLine(\"hi\");\r\n", "// Entry\r\nConsole.WriteLine(\"hi\");\r\n", DisplayName = "CRLF")]
    [DataRow("//Entry\rConsole.WriteLine(\"hi\");\r", "// Entry\rConsole.WriteLine(\"hi\");\r", DisplayName = "CR")]
    public void LineBreakStyleAndFinalLineBreak_ArePreservedExactly(string source, string expected) => Assert.AreEqual(expected, _converter.Apply(source));

    [TestMethod]
    public void TopLevelStatementsFile_AlreadyFormatted_IsUnchanged()
    {
        string source =
            "using System;\r\n" +
            "\r\n" +
            "// Entry point\r\n" +
            "Console.WriteLine(\"hi\"); // trailing\r\n" +
            "\r\n" +
            "static void Local()\r\n" +
            "{\r\n" +
            "    // body\r\n" +
            "}\r\n" +
            "\r\n" +
            "class Helper { }\r\n";

        Assert.AreEqual(source, _converter.Apply(source));
    }

    [TestMethod]
    public void TopLevelStatementsFile_FormatsWholeLineCommentsOnly_WithoutAddingOrRemovingLines()
    {
        string source = "using System;\r\n//Entry point\r\nConsole.WriteLine(\"hi\"); //trailing\r\n    //   indented\r\n";
        string expected = "using System;\r\n// Entry point\r\nConsole.WriteLine(\"hi\"); //trailing\r\n    // indented\r\n";

        Assert.AreEqual(expected, _converter.Apply(source));
    }

    [TestMethod]
    [DataRow("var url = \"http://example.com\";\r\n", DisplayName = "URL in a string")]
    [DataRow("var s = \"//not a comment\";\r\n", DisplayName = "comment marker in a string")]
    [DataRow("#region  Fields\r\nint x;\r\n#endregion\r\n", DisplayName = "region directives")]
    [DataRow("#if DEBUG\r\nint x;\r\n#endif\r\n", DisplayName = "conditional directives")]
    [DataRow("   \r\n\t\r\n", DisplayName = "whitespace only")]
    [DataRow("class C { int x; /* inline */ }\r\n", DisplayName = "inline block comment")]
    public void LinesWithoutWholeLineComments_AreUnchanged(string source) => Assert.AreEqual(source, _converter.Apply(source));

    [TestMethod]
    public void SingleLineCommentMarkersInsideABlockComment_AreKeptVerbatim()
    {
        string source = "/*\r\n//keep as is\r\n*/\r\n//after\r\n";

        Assert.AreEqual("/*\r\n//keep as is\r\n*/\r\n// after\r\n", _converter.Apply(source));
    }

    [TestMethod]
    public void BlockCommentClosedOnItsFirstLine_DoesNotSwallowTheFollowingLines()
    {
        string source = "/* a */\r\n//b\r\n";

        Assert.AreEqual("/* a */\r\n// b\r\n", _converter.Apply(source));
    }

    [TestMethod]
    public void BlockCommentClosingLine_IsKeptAsIs_AndLaterCommentsAreFormatted()
    {
        string source = "/* a\r\n   * b */ int x;\r\n//c\r\n";

        Assert.AreEqual("/* a\r\n   * b */ int x;\r\n// c\r\n", _converter.Apply(source));
    }

    [TestMethod]
    public void BlockCommentContinuationStars_AreAlignedUnderTheOpeningStar()
    {
        string source = "    /*\r\n    * line 1\r\n      * line 2\r\n    text\r\n    */\r\n";

        Assert.AreEqual("    /*\r\n     * line 1\r\n     * line 2\r\n    text\r\n    */\r\n", _converter.Apply(source));
    }

    [TestMethod]
    [DataRow("\t//\tcomment", "\t// comment", DisplayName = "tabs")]
    [DataRow("//   ", "//", DisplayName = "empty comment with trailing spaces")]
    [DataRow("    //", "    //", DisplayName = "indented empty comment")]
    public void CommentSpacing_IsNormalized(string source, string expected) => Assert.AreEqual(expected, _converter.Apply(source));

    [TestMethod]
    public void FileWithSyntaxErrors_OnlyCommentLinesChange()
    {
        string source = "class C {\r\n//broken\r\n    void M( {\r\n";

        Assert.AreEqual("class C {\r\n// broken\r\n    void M( {\r\n", _converter.Apply(source));
    }

    [TestMethod]
    public void Apply_IsIdempotent()
    {
        string source = "//a\r\n    /*\r\n    * b\r\n    */\r\nclass C { } //c\r\n";
        string once = _converter.Apply(source);

        Assert.AreEqual(once, _converter.Apply(once));
    }

    [TestMethod]
    [DataRow("/// <summary>\r\n/// Does X.\r\n/// </summary>\r\nclass C { }\r\n", DisplayName = "type documentation")]
    [DataRow("class C\r\n{\r\n    ///<summary>Does X.</summary>\r\n    void M() { }\r\n}\r\n", DisplayName = "member documentation without a space")]
    public void XmlDocumentationComments_AreUnchanged(string source) => Assert.AreEqual(source, _converter.Apply(source));

    [TestMethod]
    [DataRow("var s = @\"\r\n//x\r\n\";\r\n", DisplayName = "verbatim string")]
    [DataRow("var s = \"\"\"\r\n    //x\r\n    \"\"\";\r\n", DisplayName = "raw string")]
    [DataRow("var s = @\"\r\n/*\r\n  * x\r\n*/\";\r\n", DisplayName = "block comment markers in a verbatim string")]
    public void CommentMarkersInsideMultiLineStrings_AreUnchanged(string source) => Assert.AreEqual(source, _converter.Apply(source));

    [TestMethod]
    [DataRow("// a\r\nint x;\nint y;\r\n", "// a\r\nint x;\nint y;\r\n", DisplayName = "already formatted")]
    [DataRow("//a\nint x;\r\n//b\r\n", "// a\nint x;\r\n// b\r\n", DisplayName = "formatted comments")]
    public void MixedLineEndings_ArePreservedLineByLine(string source, string expected) => Assert.AreEqual(expected, _converter.Apply(source));

    [TestMethod]
    [DataRow("////<summary>\r\n", DisplayName = "commented-out documentation comment")]
    [DataRow("    //// old code\r\n", DisplayName = "four slashes")]
    public void CommentsStartingWithMoreThanTwoSlashes_AreUnchanged(string source) => Assert.AreEqual(source, _converter.Apply(source));

    [TestMethod]
    public void BlockCommentOpenedAfterCode_ContentIsUnchanged()
    {
        string source = "int x; /* start\r\n//inner\r\n  * star\r\n*/\r\n";

        Assert.AreEqual(source, _converter.Apply(source));
    }

    [TestMethod]
    [DataRow("#if DEBUG\r\n//a\r\n    //b\r\n#endif\r\n", "#if DEBUG\r\n// a\r\n    // b\r\n#endif\r\n", DisplayName = "single-line comments")]
    [DataRow("#if DEBUG\r\n    /*\r\n    * a\r\n    */\r\n#endif\r\n", "#if DEBUG\r\n    /*\r\n     * a\r\n    */\r\n#endif\r\n", DisplayName = "block comment")]
    [DataRow("#if DEBUG\r\n//a\r\n#else\r\n//b\r\n#endif\r\n", "#if DEBUG\r\n// a\r\n#else\r\n// b\r\n#endif\r\n", DisplayName = "disabled and active branch")]
    [DataRow("#if DEBUG\r\nvar s = \"//x\";\r\nint y; //c\r\n//d\r\n#endif\r\n", "#if DEBUG\r\nvar s = \"//x\";\r\nint y; //c\r\n// d\r\n#endif\r\n", DisplayName = "string and trailing comment left alone")]
    [DataRow("#if DEBUG\r\nvar s = @\"\r\n//x\r\n\";\r\n#endif\r\n", "#if DEBUG\r\nvar s = @\"\r\n//x\r\n\";\r\n#endif\r\n", DisplayName = "verbatim string left alone")]
    public void CommentsInsideAnInactivePreprocessorBranch_AreFormatted(string source, string expected) => Assert.AreEqual(expected, _converter.Apply(source));
}
