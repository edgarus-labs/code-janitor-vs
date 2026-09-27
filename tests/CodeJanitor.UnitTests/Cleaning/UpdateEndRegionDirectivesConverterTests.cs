using CodeJanitor.Logic.Transformations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public sealed class UpdateEndRegionDirectivesConverterTests
{
    private UpdateEndRegionDirectivesConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new UpdateEndRegionDirectivesConverter();
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
    public void NoRegions_ReturnsUnchanged()
    {
        string source = "public class MyClass\r\n{\r\n}\r\n";
        string result = _converter.Apply(source);
        Assert.AreEqual(source, result);
    }

    [TestMethod]
    public void SingleRegionWithoutName_UpdatesEndregionCorrectly()
    {
        string source = "public class MyClass\r\n{\r\n    #region\r\n    public void MyMethod() { }\r\n    #endregion\r\n}\r\n";
        string result = _converter.Apply(source);
        // Empty region name should result in just "#endregion"
        Assert.Contains("#endregion\r\n", result);
    }

    [TestMethod]
    public void SingleRegionWithName_UpdatesEndregionToMatchRegionName()
    {
        string source = "public class MyClass\r\n{\r\n    #region MyRegion\r\n    public void MyMethod() { }\r\n    #endregion WrongName\r\n}\r\n";
        string result = _converter.Apply(source);
        Assert.Contains("#endregion MyRegion", result);
    }

    [TestMethod]
    public void MultipleRegions_UpdatesAllEndregionsCorrectly()
    {
        string source = "public class MyClass\r\n{\r\n    #region Fields\r\n    private int _field;\r\n    #endregion\r\n\r\n    #region Methods\r\n    public void MyMethod() { }\r\n    #endregion\r\n}\r\n";
        string result = _converter.Apply(source);
        Assert.Contains("#endregion Fields", result);
        Assert.Contains("#endregion Methods", result);
    }

    [TestMethod]
    public void NestedRegions_UpdatesAllEndregionsInStack()
    {
        string source = "#region Outer\r\n#region Inner\r\npublic class MyClass { }\r\n#endregion\r\n#endregion\r\n";
        string result = _converter.Apply(source);
        Assert.Contains("#endregion Inner", result);
        Assert.Contains("#endregion Outer", result);
    }

    [TestMethod]
    public void RegionWithWhitespaceNormalization_NormalizesWhitespace()
    {
        string source = "#region   MyRegion   \r\ncode\r\n#endregion\r\n";
        string result = _converter.Apply(source);
        // The region name is trimmed, so we should have "#endregion MyRegion"
        Assert.Contains("#endregion MyRegion", result);
    }

    [TestMethod]
    public void PreservesIndentation()
    {
        string source = "public class MyClass\r\n{\r\n        #region Fields\r\n        private int _field;\r\n        #endregion OldName\r\n}\r\n";
        string result = _converter.Apply(source);
        // Should preserve the 8-space indentation
        Assert.Contains("        #endregion Fields", result);
    }

    [TestMethod]
    public void MismatchedRegions_KeepsLineAsIs()
    {
        // More endregions than regions - the extra endregion should be kept as-is
        string source = "#region Fields\r\n#endregion WrongName1\r\n#endregion WrongName2\r\n";
        string result = _converter.Apply(source);
        // First endregion should be updated, second should be kept
        Assert.Contains("#endregion Fields", result);
        Assert.Contains("#endregion WrongName2", result);
    }
}
