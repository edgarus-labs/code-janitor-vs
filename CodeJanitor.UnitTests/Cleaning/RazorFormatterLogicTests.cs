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
        var input = "<MyComp A=\"1\" B=\"2\" />";

        var output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(input, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void KeepsMarkupUnchangedRegardlessOfAttributeCount()
    {
        var input = "<MyComp A=\"1\" B=\"2\" C=\"3\" />";

        var output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(input, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void KeepsAuthoredMultiLineTagUnchanged()
    {
        var input = "<link rel=\"icon\"\n  type=\"image/png\"\n  href=\"favicon.png\" />";

        var output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(input, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void FormatsCodeInsideCodeDirective()
    {
        var input = "@code{public void A(){if(true){return;}}}";
        var expected = "@code{\n    public void A()\n    {\n        if (true)\n        {\n            return;\n        }\n    }\n}";

        var output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(expected, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void DoesNotFormatTagsInsideCodeDirective()
    {
        var input = "@code{\n    var xml = \"<MyComp A='1' B='2' C='3' />\";\n}\n<MyComp A=\"1\" B=\"2\" C=\"3\" />";

        var output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(input, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void FormatsCodeInsideIfBlock()
    {
        var input = "@if(true){<Child A=\"1\" B=\"2\" C=\"3\" /> var x=1+2;}";
        var expected = "@if (true)\n{\n    <Child A=\"1\" B=\"2\" C=\"3\" />\n    var x = 1 + 2;\n}";

        var output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(expected, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ProtectsStringMarkupInsideForeachBlock()
    {
        var input = "@foreach(var item in items){var xml=\"<Child A='1' B='2' C='3' />\";}\n<Child A=\"1\" B=\"2\" C=\"3\" />";
        var expected = "@foreach (var item in items)\n{\n    var xml = \"<Child A='1' B='2' C='3' />\";\n}\n<Child A=\"1\" B=\"2\" C=\"3\" />";

        var output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(expected, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void FormatsElseBlockMarkup()
    {
        var input = "@else{<Child A=\"1\" B=\"2\" C=\"3\" />}";
        var expected = "@else\n{\n    <Child A=\"1\" B=\"2\" C=\"3\" />\n}";

        var output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(expected, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void FormatsElseIfHeaderAndCode()
    {
        var input = "@else if(flag&&other){var total=1+2;}";
        var expected = "@else if (flag && other)\n{\n    var total = 1 + 2;\n}";

        var output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(expected, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void FormatsTryCatchFinallyBlocks()
    {
        var input = "@try{var xml=\"<Child A='1' B='2' C='3' />\";}@catch(Exception ex){var total=1+2;}@finally{<Child A=\"1\" B=\"2\" C=\"3\" />}";
        var expected = "@try\n{\n    var xml = \"<Child A='1' B='2' C='3' />\";\n}\n@catch (Exception ex)\n{\n    var total = 1 + 2;\n}\n@finally\n{\n    <Child A=\"1\" B=\"2\" C=\"3\" />\n}";

        var output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(expected, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void KeepsRazorExpressionWithNestedQuotesIntact()
    {
        var input = "<link rel=\"stylesheet\" href=\"@Assets[\"app.css\"]\" />";

        var output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(input, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void KeepsRazorExpressionWithNestedQuotesIntactWithThreeAttributes()
    {
        var input = "<link rel=\"stylesheet\" href=\"@Assets[\"app.css\"]\" type=\"text/css\" />";

        var output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(input, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void KeepsRazorExpressionWithPathAndPrecedingAttributeIntact()
    {
        var input = "<link href=\"@Assets[\"_content/MudBlazor/MudBlazor.min.css\"]\" rel=\"stylesheet\" />";

        var output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(input, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void KeepsBlazorHeadMarkupExactlyAsAuthored()
    {
        var input = "<meta charset=\"utf-8\" />\n"
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

        var output = RazorFormatterLogic.FormatRazorText(input);

        Assert.AreEqual(input, output);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void IsIdempotent()
    {
        var input = "<MyComp A=\"1\" B=\"2\" C=\"3\" />\n@code{public void A(){if(true){return;}}}";

        var once = RazorFormatterLogic.FormatRazorText(input);
        var twice = RazorFormatterLogic.FormatRazorText(once);

        Assert.AreEqual(once, twice);
    }
}
