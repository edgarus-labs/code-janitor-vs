using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;

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
        var source = "// comment\r\npublic class MyClass { }";
        var result = _converter.Apply(source);
        Assert.AreEqual(source, result);
    }

    [TestMethod]
    public void EmptySource_ReturnsUnchanged()
    {
        var source = "";
        var result = _converter.Apply(source);
        Assert.AreEqual(source, result);
    }

    [TestMethod]
    public void NullSource_ReturnsUnchanged()
    {
        var result = _converter.Apply(null);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void NoComments_ReturnsUnchanged()
    {
        var source = "public class MyClass { }";
        var result = _converter.Apply(source);
        Assert.AreEqual(source, result);
    }

    [TestMethod]
    public void SingleLineComment_NormalizesSpacing()
    {
        var source = "//  comment with extra spaces\r\npublic class MyClass { }";
        var result = _converter.Apply(source);
        // Should normalize to single space after //
        Assert.IsTrue(result.Contains("// comment with extra spaces"));
    }

    [TestMethod]
    public void SingleLineCommentWithoutSpace_AddsSpace()
    {
        var source = "//comment\r\npublic class MyClass { }";
        var result = _converter.Apply(source);
        // Should add space after //
        Assert.IsTrue(result.Contains("// comment"));
    }

    [TestMethod]
    public void IndentedComment_PreservesIndentation()
    {
        var source = "    // indented comment\r\npublic class MyClass { }";
        var result = _converter.Apply(source);
        // Should preserve indentation
        Assert.IsTrue(result.Contains("    // indented comment"));
    }

    [TestMethod]
    public void EmptyComment_NormalizesCorrectly()
    {
        var source = "//\r\npublic class MyClass { }";
        var result = _converter.Apply(source);
        Assert.IsTrue(result.Contains("//"));
    }

    [TestMethod]
    public void MultiLineComment_PreservesStructure()
    {
        var source = "/* comment */\r\npublic class MyClass { }";
        var result = _converter.Apply(source);
        Assert.IsTrue(result.Contains("/* comment */"));
    }

    [TestMethod]
    public void MultipleComments_FormatsAll()
    {
        var source = "//  comment1\r\n//  comment2\r\npublic class MyClass { }";
        var result = _converter.Apply(source);
        // Both should be normalized
        Assert.IsTrue(result.Contains("// comment1"));
        Assert.IsTrue(result.Contains("// comment2"));
    }

    [TestMethod]
    public void PreservesNewlineStyle()
    {
        var source = "// comment\n// another";
        var result = _converter.Apply(source);
        // Should preserve \n style
        Assert.IsTrue(result.Contains("\n"));
        Assert.IsFalse(result.Contains("\r\n"));
    }

    [TestMethod]
    public void Name_ReturnsCorrectName()
    {
        Assert.AreEqual("Format comments", _converter.Name);
    }

    [TestMethod]
    public void MultiLineComment_WithAsteriskContinuationLines_AlignsWithBaseIndentation()
    {
        var source = "    /*\r\n    * line 1\r\n    * line 2\r\n    */\r\n    public class MyClass { }";
        var result = _converter.Apply(source);

        Assert.IsTrue(result.Contains("    * line 1"));
        Assert.IsTrue(result.Contains("    * line 2"));
    }

    [TestMethod]
    public void MultiLineComment_WithoutAsteriskContinuationLines_PreservesLines()
    {
        var source = "    /*\r\n    content line 1\r\n    content line 2\r\n    */\r\n    public class MyClass { }";
        var result = _converter.Apply(source);

        Assert.IsTrue(result.Contains("    content line 1"));
        Assert.IsTrue(result.Contains("    content line 2"));
    }

    [TestMethod]
    public void PreservesCarriageReturnNewlineStyle()
    {
        var source = "// comment 1\r// comment 2";
        var result = _converter.Apply(source);

        Assert.IsTrue(result.Contains("\r"));
    }
}
