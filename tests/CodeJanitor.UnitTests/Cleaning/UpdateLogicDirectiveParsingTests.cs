using CodeJanitor.Logic.Cleaning;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public sealed class UpdateLogicDirectiveParsingTests
{
    [TestMethod]
    public void TryParseRegionDirective_AllowsTabSeparator()
    {
        bool parsed = UpdateLogic.TryParseRegionDirective("region\tMy Region", out string name);

        Assert.IsTrue(parsed);
        Assert.AreEqual("My Region", name);
    }

    [TestMethod]
    public void TryParseRegionDirective_AllowsMultipleSpacesAndTrimsName()
    {
        bool parsed = UpdateLogic.TryParseRegionDirective("region    My Region   ", out string name);

        Assert.IsTrue(parsed);
        Assert.AreEqual("My Region", name);
    }

    [TestMethod]
    public void TryParseRegionDirective_RejectsMissingWhitespaceAfterKeyword()
    {
        bool parsed = UpdateLogic.TryParseRegionDirective("regionMyRegion", out _);

        Assert.IsFalse(parsed);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("Region MyRegion")]
    [DataRow("endregion MyRegion")]
    public void TryParseRegionDirective_RejectsNonRegionDirective(string input)
    {
        bool parsed = UpdateLogic.TryParseRegionDirective(input, out string name);

        Assert.IsFalse(parsed);
        Assert.IsNull(name);
    }

    [TestMethod]
    public void TryParseRegionDirective_AllowsKeywordWithoutName()
    {
        bool parsed = UpdateLogic.TryParseRegionDirective("region", out string name);

        Assert.IsTrue(parsed);
        Assert.AreEqual(string.Empty, name);
    }

    [TestMethod]
    public void TryParseRegionDirective_TreatsWhitespaceOnlyNameAsEmpty()
    {
        bool parsed = UpdateLogic.TryParseRegionDirective("region\t   ", out string name);

        Assert.IsTrue(parsed);
        Assert.AreEqual(string.Empty, name);
    }

    [TestMethod]
    public void TryParseEndRegionDirective_ParsesNameWithOriginalWhitespace()
    {
        bool parsed = UpdateLogic.TryParseEndRegionDirective("endregion   My Region", out string name);

        Assert.IsTrue(parsed);
        Assert.AreEqual("   My Region", name);
    }

    [TestMethod]
    public void TryParseEndRegionDirective_CanonicalSuffixMatchesNormalizedName()
    {
        bool parsed = UpdateLogic.TryParseEndRegionDirective("endregion MyRegion", out string name);

        Assert.IsTrue(parsed);
        Assert.AreEqual(UpdateLogic.BuildDirectiveNameSuffix("MyRegion"), name);
    }

    [TestMethod]
    public void TryParseEndRegionDirective_RejectsMissingWhitespaceAfterKeyword()
    {
        bool parsed = UpdateLogic.TryParseEndRegionDirective("endregionMyRegion", out _);

        Assert.IsFalse(parsed);
    }

    [TestMethod]
    public void TryParseEndRegionDirective_AllowsKeywordWithoutName()
    {
        bool parsed = UpdateLogic.TryParseEndRegionDirective("endregion", out string name);

        Assert.IsTrue(parsed);
        Assert.AreEqual(string.Empty, name);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("EndRegion MyRegion")]
    [DataRow("region MyRegion")]
    public void TryParseEndRegionDirective_RejectsNonEndRegionDirective(string input)
    {
        bool parsed = UpdateLogic.TryParseEndRegionDirective(input, out string name);

        Assert.IsFalse(parsed);
        Assert.IsNull(name);
    }

    [TestMethod]
    public void BuildDirectiveNameSuffix_ReturnsEmpty_ForEmptyName()
    {
        Assert.AreEqual(string.Empty, UpdateLogic.BuildDirectiveNameSuffix(string.Empty));
    }

    [TestMethod]
    public void BuildDirectiveNameSuffix_PrependsSingleSpace_ForNonEmptyName()
    {
        Assert.AreEqual(" MyRegion", UpdateLogic.BuildDirectiveNameSuffix("MyRegion"));
    }

    [TestMethod]
    public void BuildDirectiveNameSuffix_ReturnsEmpty_ForWhitespaceOnlyName()
    {
        Assert.AreEqual(string.Empty, UpdateLogic.BuildDirectiveNameSuffix("   \t  "));
    }

    [TestMethod]
    public void BuildDirectiveNameSuffix_TrimsNonEmptyName()
    {
        Assert.AreEqual(" MyRegion", UpdateLogic.BuildDirectiveNameSuffix("  MyRegion  "));
    }
}
