using CodeJanitor.Logic.Transformations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="NamespaceFixerConverter" />.
/// </summary>
[TestClass]
public sealed class NamespaceFixerConverterTests
{
    private NamespaceFixerConverter _converter;

    [TestInitialize]
    public void TestInitialize() => _converter = new NamespaceFixerConverter();

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FixNamespace_UpdatesBlockScopedNamespace()
    {
        string input = "namespace Old.Namespace\r\n{\r\n    class C\r\n    {\r\n    }\r\n}\r\n";
        string expected = "namespace New.Namespace\r\n{\r\n    class C\r\n    {\r\n    }\r\n}\r\n";

        Assert.AreEqual(expected, _converter.FixNamespace(input, "New.Namespace"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FixNamespace_UpdatesFileScopedNamespace()
    {
        string input = "namespace Old.Namespace;\r\n\r\nclass C\r\n{\r\n}\r\n";
        string expected = "namespace New.Namespace;\r\n\r\nclass C\r\n{\r\n}\r\n";

        Assert.AreEqual(expected, _converter.FixNamespace(input, "New.Namespace"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FixNamespace_AlreadyMatching_ReturnsUnchanged()
    {
        string input = "namespace New.Namespace;\r\n\r\nclass C\r\n{\r\n}\r\n";

        Assert.AreEqual(input, _converter.FixNamespace(input, "New.Namespace"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FixNamespace_MultipleNamespaces_ReturnsUnchanged()
    {
        string input = "namespace A\r\n{\r\n}\r\nnamespace B\r\n{\r\n}\r\n";

        Assert.AreEqual(input, _converter.FixNamespace(input, "New.Namespace"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FixNamespace_NoNamespace_ReturnsUnchanged()
    {
        string input = "class C\r\n{\r\n}\r\n";

        Assert.AreEqual(input, _converter.FixNamespace(input, "New.Namespace"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   \r\n\t")]
    public void FixNamespace_EmptyOrWhitespaceSource_ReturnsItUnchanged(string input) => Assert.AreEqual(input, _converter.FixNamespace(input, "New.Namespace"));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    public void FixNamespace_EmptyOrWhitespaceExpectedNamespace_ReturnsUnchanged(string expectedNamespace)
    {
        string input = "namespace Old.Namespace;\r\n\r\nclass C\r\n{\r\n}\r\n";

        Assert.AreEqual(input, _converter.FixNamespace(input, expectedNamespace));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FixNamespace_NestedNamespaceInsideTheOnlyTopLevelOne_RenamesOnlyTheTopLevelOne()
    {
        string input = "namespace Old\r\n{\r\n    namespace Inner\r\n    {\r\n        class C { }\r\n    }\r\n}\r\n";
        string expected = "namespace New.Namespace\r\n{\r\n    namespace Inner\r\n    {\r\n        class C { }\r\n    }\r\n}\r\n";

        Assert.AreEqual(expected, _converter.FixNamespace(input, "New.Namespace"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FixNamespace_KeepsCommentsDirectivesUsingsAndLfLineEndingsAroundTheName()
    {
        string input = "// header\n#pragma warning disable CS1591\nusing System;\n\n/* before */ namespace /* inside */ Old.Namespace // after\n{\n\tclass C { string s = \"namespace Old.Namespace\"; }\n}\n";
        string expected = "// header\n#pragma warning disable CS1591\nusing System;\n\n/* before */ namespace /* inside */ New.Namespace // after\n{\n\tclass C { string s = \"namespace Old.Namespace\"; }\n}\n";

        Assert.AreEqual(expected, _converter.FixNamespace(input, "New.Namespace"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FixNamespace_TopLevelStatementsWithoutNamespace_ReturnsUnchanged()
    {
        string input = "using System;\r\n\r\nConsole.WriteLine(\"namespace Old;\");\r\n\r\nstatic void Local() { }\r\n\r\nrecord R(int X);\r\n";

        Assert.AreEqual(input, _converter.FixNamespace(input, "New.Namespace"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FixNamespace_TopLevelStatementsFollowedByANamespace_RenamesTheNamespace()
    {
        string input = "System.Console.WriteLine();\r\n\r\nnamespace Old\r\n{\r\n    record R(int X);\r\n}\r\n";
        string expected = "System.Console.WriteLine();\r\n\r\nnamespace New.Namespace\r\n{\r\n    record R(int X);\r\n}\r\n";

        Assert.AreEqual(expected, _converter.FixNamespace(input, "New.Namespace"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FixNamespace_SyntaxErrors_StillRenamesTheParsedNamespace()
    {
        string input = "namespace Old.Namespace\r\n{\r\n    class C {\r\n";
        string expected = "namespace New.Namespace\r\n{\r\n    class C {\r\n";

        Assert.AreEqual(expected, _converter.FixNamespace(input, "New.Namespace"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FixNamespace_NameDiffersOnlyInCase_IsRenamed()
    {
        string input = "namespace company.app;\n";

        Assert.AreEqual("namespace Company.App;\n", _converter.FixNamespace(input, "Company.App"));
    }
}
