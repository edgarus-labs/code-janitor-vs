using CodeJanitor.Logic.Transformations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="TabToSpaceConverter" />. Verifies that indentation/whitespace tabs
/// are expanded to spaces while tabs inside string/char literals and comments are preserved
/// (headless-Roslyn cleanup block, BL-018, C#-only per scope).
/// </summary>
[TestClass]
public sealed class TabToSpaceConverterTests
{
    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void IndentationTab_ExpandedToFourSpacesByDefault()
    {
        TabToSpaceConverter converter = new TabToSpaceConverter();
        string input = "class C\n{\n\tint x;\n}\n";
        string expected = "class C\n{\n    int x;\n}\n";

        Assert.AreEqual(expected, converter.Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void IndentationTab_ExpandedToCustomTabSize()
    {
        TabToSpaceConverter converter = new TabToSpaceConverter(2);
        string input = "class C\n{\n\tint x;\n}\n";
        string expected = "class C\n{\n  int x;\n}\n";

        Assert.AreEqual(expected, converter.Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TabInsideStringLiteral_Preserved()
    {
        TabToSpaceConverter converter = new TabToSpaceConverter();
        string input = "class C\n{\n\tstring s = \"a\tb\";\n}\n";
        string expected = "class C\n{\n    string s = \"a\tb\";\n}\n";

        Assert.AreEqual(expected, converter.Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TabInsideVerbatimString_Preserved()
    {
        TabToSpaceConverter converter = new TabToSpaceConverter();
        string input = "class C\n{\n\tstring s = @\"a\tb\";\n}\n";
        string expected = "class C\n{\n    string s = @\"a\tb\";\n}\n";

        Assert.AreEqual(expected, converter.Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NoTabs_Unchanged()
    {
        TabToSpaceConverter converter = new TabToSpaceConverter();
        string input = "class C\n{\n    int x;\n}\n";

        Assert.AreEqual(input, converter.Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void EmptySource_Unchanged()
    {
        TabToSpaceConverter converter = new TabToSpaceConverter();

        Assert.AreEqual(string.Empty, converter.Convert(string.Empty));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NullSource_ReturnsNull()
    {
        TabToSpaceConverter converter = new TabToSpaceConverter();

        Assert.IsNull(converter.Convert(null));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ImplementsSourceTransformation_ApplyMatchesConvert()
    {
        ISourceTransformation transformation = new TabToSpaceConverter();
        string input = "class C\n{\n\tint x;\n}\n";
        string expected = "class C\n{\n    int x;\n}\n";

        Assert.AreEqual(expected, transformation.Apply(input));
        Assert.AreEqual("Convert tabs to spaces", transformation.Name);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Constructor_InvalidTabSize_ThrowsArgumentOutOfRangeException()
    {
        try
        {
            _ = new TabToSpaceConverter(0);
            Assert.Fail("Expected ArgumentOutOfRangeException was not thrown.");
        }
        catch (System.ArgumentOutOfRangeException)
        {
            // Expected
        }
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TabSize_ReportsTheConfiguredWidth()
    {
        Assert.AreEqual(4, new TabToSpaceConverter().TabSize);
        Assert.AreEqual(2, new TabToSpaceConverter(2).TabSize);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TopLevelStatementsFile_TabsInCommentsAndStringLiteralsAreKept()
    {
        string input =
            "using System;\n\nConsole.WriteLine(\"a\tb\");\nstatic void Local()\n{\n" +
            "\t// comment\twith tab\n\tvar s = @\"x\ty\";\n\tvar i = $\"{s}\t!\";\n\tvar r = \"\"\"\n\t\traw\ttab\n\t\t\"\"\";\n}\n";
        string expected =
            "using System;\n\nConsole.WriteLine(\"a\tb\");\nstatic void Local()\n{\n" +
            "    // comment\twith tab\n    var s = @\"x\ty\";\n    var i = $\"{s}\t!\";\n    var r = \"\"\"\n\t\traw\ttab\n\t\t\"\"\";\n}\n";

        Assert.AreEqual(expected, new TabToSpaceConverter().Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void DirectivesSingleLineDocumentationCommentsAndTrailingTabs_AreExpanded()
    {
        string input = "class C\r\n{\r\n\t#region R\r\n\t/// <summary>Doc</summary>\r\n\tint x;\t// trailing\r\n\t#endregion\r\n}\r\n";
        string expected = "class C\r\n{\r\n    #region R\r\n    /// <summary>Doc</summary>\r\n    int x;    // trailing\r\n    #endregion\r\n}\r\n";

        Assert.AreEqual(expected, new TabToSpaceConverter().Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void DisabledPreprocessorText_IsKept()
    {
        string input = "#if NEVER_DEFINED\n\tint x;\n#endif\n";

        Assert.AreEqual(input, new TabToSpaceConverter().Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FileWithSyntaxErrors_IndentationIsStillExpanded() => Assert.AreEqual("class C {\r\n    void M( {\r\n", new TabToSpaceConverter().Convert("class C {\r\n\tvoid M( {\r\n"));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TabIndentationOfDocumentationCommentContinuationLines_IsExpanded()
    {
        string input = "class C\r\n{\r\n\t/// <summary>\r\n\t/// Doc\there\r\n\t/// </summary>\r\n\tint x;\r\n}\r\n";
        string expected = "class C\r\n{\r\n    /// <summary>\r\n    /// Doc\there\r\n    /// </summary>\r\n    int x;\r\n}\r\n";

        Assert.AreEqual(expected, new TabToSpaceConverter().Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TabIndentationOfMultiLineDocumentationCommentContinuationLines_IsExpanded()
    {
        string input = "class C\n{\n\t/**\n\t * <summary>Doc</summary>\n\t */\n\tint x;\n}\n";
        string expected = "class C\n{\n    /**\n     * <summary>Doc</summary>\n     */\n    int x;\n}\n";

        Assert.AreEqual(expected, new TabToSpaceConverter().Convert(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow(4, "class C\n{\n\tint x;\n}\n", DisplayName = "indentation, default tab size")]
    [DataRow(2, "class C\n{\n\t\tint x;\n}\n", DisplayName = "nested indentation, custom tab size")]
    [DataRow(4, "class C\r\n{\r\n\t#region R\r\n\t/// <summary>Doc</summary>\r\n\tint x;\t// trailing\r\n\t#endregion\r\n}\r\n", DisplayName = "directives, documentation comments and trailing tabs")]
    [DataRow(4, "class C\n{\n\tstring s = \"a\tb\";\n\tstring v = @\"x\ty\";\n}\n", DisplayName = "literals keep their tabs")]
    [DataRow(4, "class C\n{\n\t/**\n\t * <summary>Doc</summary>\n\t */\n\tint x;\n}\n", DisplayName = "multi-line documentation comment")]
    public void Convert_IsIdempotent(int tabSize, string input)
    {
        TabToSpaceConverter converter = new TabToSpaceConverter(tabSize);
        string once = converter.Convert(input);

        Assert.AreNotEqual(input, once, "The scenario has to change the input at all.");
        Assert.AreEqual(once, converter.Convert(once));
    }
}
