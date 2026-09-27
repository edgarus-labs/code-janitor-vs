using CodeJanitor.Logic.Transformations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

[TestClass]
public sealed class RegionDirectiveRemoverTests
{
    private RegionDirectiveRemover _remover;

    [TestInitialize]
    public void TestInitialize()
    {
        _remover = new RegionDirectiveRemover();
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Name_ReturnsCorrectName()
    {
        Assert.AreEqual("Remove region directives", _remover.Name);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NullOrEmpty_ReturnsOriginal()
    {
        Assert.IsNull(_remover.Apply(null));
        Assert.AreEqual(string.Empty, _remover.Apply(string.Empty));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void RemovesRegionsAndEndRegions_PreservesCode()
    {
        string input = @"#region MyRegion
public class C
{
    public void M() { }
}
";
        string expected = @"public class C
{
    public void M() { }
}
";
        string result = _remover.Apply(input);
        Assert.AreEqual(expected, result);
    }
}
