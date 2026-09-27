using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Transformations;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="NamespaceFixerConverter" />.
/// </summary>
[TestClass]
public sealed class NamespaceFixerConverterTests
{
    private NamespaceFixerConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new NamespaceFixerConverter();
    }

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
}
