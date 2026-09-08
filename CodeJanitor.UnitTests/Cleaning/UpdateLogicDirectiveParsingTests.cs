using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Cleaning;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public sealed class UpdateLogicDirectiveParsingTests
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

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("Region MyRegion")]
    [DataRow("endregion MyRegion")]
    public void TryParseRegionDirective_RejectsNonRegionDirective(string input)
    {
        var parsed = UpdateLogic.TryParseRegionDirective(input, out var name);

        Assert.IsFalse(parsed);
        Assert.IsNull(name);
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
    public void TryParseEndRegionDirective_AllowsKeywordWithoutName()
    {
        var parsed = UpdateLogic.TryParseEndRegionDirective("endregion", out var name);

        Assert.IsTrue(parsed);
        Assert.AreEqual(string.Empty, name);
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("EndRegion MyRegion")]
    [DataRow("region MyRegion")]
    public void TryParseEndRegionDirective_RejectsNonEndRegionDirective(string input)
    {
        var parsed = UpdateLogic.TryParseEndRegionDirective(input, out var name);

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
