using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Cleaning;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public class UpdateLogicDirectiveParsingTests
{
    [TestMethod]
    public void TryParseRegionDirective_AllowsTabSeparator()
    {
        var parsed = UpdateLogic.TryParseRegionDirective("region\tMy Region", out var name);

        Assert.IsTrue(parsed);
        Assert.AreEqual("My Region", name);
    }

    [TestMethod]
    public void TryParseRegionDirective_AllowsMultipleSpacesAndTrimsName()
    {
        var parsed = UpdateLogic.TryParseRegionDirective("region    My Region   ", out var name);

        Assert.IsTrue(parsed);
        Assert.AreEqual("My Region", name);
    }

    [TestMethod]
    public void TryParseRegionDirective_RejectsMissingWhitespaceAfterKeyword()
    {
        var parsed = UpdateLogic.TryParseRegionDirective("regionMyRegion", out _);

        Assert.IsFalse(parsed);
    }

    [TestMethod]
    public void TryParseRegionDirective_AllowsKeywordWithoutName()
    {
        var parsed = UpdateLogic.TryParseRegionDirective("region", out var name);

        Assert.IsTrue(parsed);
        Assert.AreEqual(string.Empty, name);
    }

    [TestMethod]
    public void TryParseRegionDirective_TreatsWhitespaceOnlyNameAsEmpty()
    {
        var parsed = UpdateLogic.TryParseRegionDirective("region\t   ", out var name);

        Assert.IsTrue(parsed);
        Assert.AreEqual(string.Empty, name);
    }

    [TestMethod]
    public void TryParseEndRegionDirective_ParsesNameWithOriginalWhitespace()
    {
        var parsed = UpdateLogic.TryParseEndRegionDirective("endregion   My Region", out var name);

        Assert.IsTrue(parsed);
        Assert.AreEqual("   My Region", name);
    }

    [TestMethod]
    public void TryParseEndRegionDirective_CanonicalSuffixMatchesNormalizedName()
    {
        var parsed = UpdateLogic.TryParseEndRegionDirective("endregion MyRegion", out var name);

        Assert.IsTrue(parsed);
        Assert.AreEqual(UpdateLogic.BuildDirectiveNameSuffix("MyRegion"), name);
    }

    [TestMethod]
    public void TryParseEndRegionDirective_RejectsMissingWhitespaceAfterKeyword()
    {
        var parsed = UpdateLogic.TryParseEndRegionDirective("endregionMyRegion", out _);

        Assert.IsFalse(parsed);
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
