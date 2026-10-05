using System.Text;
using CodeJanitor.Logic.Transformations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

[TestClass]
public sealed class RegionDirectiveRemoverTests
{
    private RegionDirectiveRemover _remover;

    [TestInitialize]
    public void TestInitialize() => _remover = new RegionDirectiveRemover();

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Name_ReturnsCorrectName() => Assert.AreEqual("Remove region directives", _remover.Name);

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

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow(
        "using System;\r\n\r\n#region Startup\r\nConsole.WriteLine(\"#region\");\r\n#endregion\r\n\r\nstatic void Local() { }\r\n",
        "using System;\r\n\r\nConsole.WriteLine(\"#region\");\r\n\r\nstatic void Local() { }\r\n",
        DisplayName = "top-level statements file")]
    [DataRow(
        "namespace N;\n\n\t#region Fields\n\tclass C { }\n\t#endregion Fields\n",
        "namespace N;\n\n\tclass C { }\n",
        DisplayName = "tabs and line feeds")]
    [DataRow(
        "class C\r\n{\r\n#if DEBUG\r\n    #region Debug\r\n    void M() { }\r\n    #endregion\r\n#endif\r\n#pragma warning disable CS0169\r\n    int _x;\r\n}\r\n#endregion",
        "class C\r\n{\r\n#if DEBUG\r\n    void M() { }\r\n#endif\r\n#pragma warning disable CS0169\r\n    int _x;\r\n}\r\n",
        DisplayName = "other directives kept, last line without line break")]
    [DataRow(
        "class C\r\n{\r\n    // #region not a directive\r\n    string s = \"#endregion\";\r\n    #regionless\r\n}\r\n",
        "class C\r\n{\r\n    // #region not a directive\r\n    string s = \"#endregion\";\r\n    #regionless\r\n}\r\n",
        DisplayName = "region text that is not a directive line")]
    [DataRow("   \r\n", "   \r\n", DisplayName = "whitespace-only file")]
    public void Apply_RemovesOnlyRegionDirectiveLines(string input, string expected) => Assert.AreEqual(expected, _remover.Apply(input));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C\n{\n    string s = @\"\n#region keep me\n\";\n}\n", DisplayName = "verbatim string")]
    [DataRow("class C\n{\n    string s = \"\"\"\n        #endregion keep me\n        \"\"\";\n}\n", DisplayName = "raw string")]
    [DataRow("class C\n{\n    string s = $@\"\n#region {1}\n\";\n}\n", DisplayName = "interpolated verbatim string")]
    [DataRow("class C\n{\n    /*\n    #region keep me\n    */\n}\n", DisplayName = "multi-line comment")]
    public void Apply_DoesNotRemoveLinesInsideStringLiteralsOrComments(string input) => Assert.AreEqual(input, _remover.Apply(input));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_CarriageReturnOnlyLineEndings_RemovesTheDirectiveLines() => Assert.AreEqual("class C { }\r", _remover.Apply("#region A\rclass C { }\r#endregion\r"));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("#regionX\r\n#region_x\r\n#endregions\r\n#endregion1\r\n# region A\r\n#reg\r\n#\r\n", "#regionX\r\n#region_x\r\n#endregions\r\n#endregion1\r\n# region A\r\n#reg\r\n#\r\n", DisplayName = "keyword continues into a word character or is not a directive")]
    [DataRow("#region\r\n#region\tA\r\n \t#endregion\r\nx", "x", DisplayName = "bare keyword, tab separator, mixed indentation")]
    [DataRow("x\r\n#region", "x\r\n", DisplayName = "directive on the last line without a line break")]
    public void Apply_MatchesOnlyWholeRegionKeywords(string input, string expected) => Assert.AreEqual(expected, _remover.Apply(input));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void RegionLinesInsideVerbatimStringOfInactiveBranch_AreKept()
    {
        string input = "#if DEBUG\r\nconst string T = @\"\r\n#region X\r\nx\r\n#endregion\r\n\";\r\n#endif\r\n";

        Assert.AreEqual(input, _remover.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [Timeout(10000, CooperativeCancellation = true)]
    public void DeeplyNestedInactiveBranches_AreScannedInLinearTimeAndKeepRegionLinesInsideLiterals()
    {
        const int Depth = 20;
        StringBuilder input = new System.Text.StringBuilder();
        for (int i = 0; i < Depth; i++)
        {
            input.Append("#if NEVER_").Append(i).Append("\r\n");
        }

        input.Append("const string T = @\"\r\n#region keep me\r\n\";\r\n#region drop me\r\n");
        for (int i = 0; i < Depth; i++)
        {
            input.Append("#endif\r\n");
        }

        string expected = input.ToString().Replace("#region drop me\r\n", string.Empty);

        Assert.AreEqual(expected, _remover.Apply(input.ToString()));
    }
}
