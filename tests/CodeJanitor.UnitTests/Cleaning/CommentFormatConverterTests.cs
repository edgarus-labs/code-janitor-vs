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
    public void TestCleanup()
    {
        Settings.Default.Formatting_CommentRunDuringCleanup = false;
    }

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
    public void Name_ReturnsCorrectName()
    {
        Assert.AreEqual("Format comments", _converter.Name);
    }

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
}
