using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Cleaning;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public sealed class RazorFormatterLogicTests
{
    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void KeepsInlineWhenTwoAttributes()
    {
        string input = "<MyComp A=\"1\" B=\"2\" />";

        string output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(input, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void KeepsMarkupUnchangedRegardlessOfAttributeCount()
    {
        string input = "<MyComp A=\"1\" B=\"2\" C=\"3\" />";

        string output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(input, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void KeepsAuthoredMultiLineTagUnchanged()
    {
        string input = "<link rel=\"icon\"\n  type=\"image/png\"\n  href=\"favicon.png\" />";

        string output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(input, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void FormatsCodeInsideCodeDirective()
    {
        string input = "@code{public void A(){if(true){return;}}}";
        string expected = "@code{\n    public void A()\n    {\n        if (true)\n        {\n            return;\n        }\n    }\n}";

        string output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(expected, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void DoesNotFormatTagsInsideCodeDirective()
    {
        string input = "@code{\n    var xml = \"<MyComp A='1' B='2' C='3' />\";\n}\n<MyComp A=\"1\" B=\"2\" C=\"3\" />";

        string output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(input, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void FormatsCodeInsideIfBlock()
    {
        string input = "@if(true){<Child A=\"1\" B=\"2\" C=\"3\" /> var x=1+2;}";
        string expected = "@if (true)\n{\n    <Child A=\"1\" B=\"2\" C=\"3\" />\n    var x = 1 + 2;\n}";

        string output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(expected, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ProtectsStringMarkupInsideForeachBlock()
    {
        string input = "@foreach(var item in items){var xml=\"<Child A='1' B='2' C='3' />\";}\n<Child A=\"1\" B=\"2\" C=\"3\" />";
        string expected = "@foreach (var item in items)\n{\n    var xml = \"<Child A='1' B='2' C='3' />\";\n}\n<Child A=\"1\" B=\"2\" C=\"3\" />";

        string output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(expected, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void FormatsElseBlockMarkup()
    {
        string input = "@else{<Child A=\"1\" B=\"2\" C=\"3\" />}";
        string expected = "@else\n{\n    <Child A=\"1\" B=\"2\" C=\"3\" />\n}";

        string output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(expected, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void FormatsElseIfHeaderAndCode()
    {
        string input = "@else if(flag&&other){var total=1+2;}";
        string expected = "@else if (flag && other)\n{\n    var total = 1 + 2;\n}";

        string output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(expected, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void FormatsTryCatchFinallyBlocks()
    {
        string input = "@try{var xml=\"<Child A='1' B='2' C='3' />\";}@catch(Exception ex){var total=1+2;}@finally{<Child A=\"1\" B=\"2\" C=\"3\" />}";
        string expected = "@try\n{\n    var xml = \"<Child A='1' B='2' C='3' />\";\n}\n@catch (Exception ex)\n{\n    var total = 1 + 2;\n}\n@finally\n{\n    <Child A=\"1\" B=\"2\" C=\"3\" />\n}";

        string output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(expected, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void KeepsRazorExpressionWithNestedQuotesIntact()
    {
        string input = "<link rel=\"stylesheet\" href=\"@Assets[\"app.css\"]\" />";

        string output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(input, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void KeepsRazorExpressionWithNestedQuotesIntactWithThreeAttributes()
    {
        string input = "<link rel=\"stylesheet\" href=\"@Assets[\"app.css\"]\" type=\"text/css\" />";

        string output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(input, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void KeepsRazorExpressionWithPathAndPrecedingAttributeIntact()
    {
        string input = "<link href=\"@Assets[\"_content/MudBlazor/MudBlazor.min.css\"]\" rel=\"stylesheet\" />";

        string output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(input, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void KeepsBlazorHeadMarkupExactlyAsAuthored()
    {
        string input = "<meta charset=\"utf-8\" />\n"
            + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />\n"
            + "<base href=\"/\" />\n"
            + "<ResourcePreloader />\n"
            + "<link rel=\"stylesheet\" href=\"@Assets[\"app.css\"]\" />\n"
            + "<link rel=\"stylesheet\"\n      href=\"@Assets[\"Jade.Web.styles.css\"]\" />\n"
            + "<ImportMap />\n"
            + "<link rel=\"icon\"\n      type=\"image/png\"\n      href=\"favicon.png\" />\n"
            + "<link href=\"https://fonts.googleapis.com/css?family=Roboto:300,400,500,700&display=swap\" rel=\"stylesheet\" />\n"
            + "<link href=\"@Assets[\"_content/MudBlazor/MudBlazor.min.css\"]\" rel=\"stylesheet\" />\n"
            + "<HeadOutlet />";

        string output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(input, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void IsIdempotent()
    {
        string input = "<MyComp A=\"1\" B=\"2\" C=\"3\" />\n@code{public void A(){if(true){return;}}}";

        string once = RazorFormatterLogic.FormatRazorText(input);
        string twice = RazorFormatterLogic.FormatRazorText(once);

        Assert.AreEqual(once, twice);
    }
}
