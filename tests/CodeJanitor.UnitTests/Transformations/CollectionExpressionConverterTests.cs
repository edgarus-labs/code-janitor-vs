using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Transformations;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="CollectionExpressionConverter" />.
/// </summary>
[TestClass]
public sealed class CollectionExpressionConverterTests
{
    private ISourceTransformation _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new CollectionExpressionConverter();
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsEmptyListFieldInitialization()
    {
        string input = "class C { private readonly List<string> _items = new List<string>(); }";
        string expected = "class C { private readonly List<string> _items = []; }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsListWithInitializerElements()
    {
        string input = "class C { void M() { List<string> items = new List<string>() { \"a\", \"b\" }; } }";
        string expected = "class C { void M() { List<string> items = [\"a\", \"b\"]; } }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsLocalListDeclaration()
    {
        string input = "class C { void M() { List<int> x = new List<int>(); } }";
        string expected = "class C { void M() { List<int> x = []; } }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsArrayWithInitializerElements()
    {
        string input = "class C { void M() { int[] a = new int[] { 1, 2, 3 }; } }";
        string expected = "class C { void M() { int[] a = [1, 2, 3]; } }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsExplicitlyEmptyArray()
    {
        string input = "class C { void M() { int[] a = new int[0]; } }";
        string expected = "class C { void M() { int[] a = []; } }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsImplicitArrayCreation()
    {
        string input = "class C { void M() { int[] a = new[] { 1, 2, 3 }; } }";
        string expected = "class C { void M() { int[] a = [1, 2, 3]; } }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsAutoPropertyInitializer()
    {
        string input = "class C { public List<string> Items { get; } = new List<string>(); }";
        string expected = "class C { public List<string> Items { get; } = []; }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsListWithConstructorArguments()
    {
        string input = "class C { void M() { List<string> x = new List<string>(10); } }";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsWhenDeclaredTypeDiffersFromCreatedType()
    {
        string input = "class C { void M() { IList<string> x = new List<string>(); } }";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsSizedArrayWithoutInitializer()
    {
        string input = "class C { void M() { int[] a = new int[5]; } }";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsNonListGenericType()
    {
        string input = "class C { void M() { HashSet<string> x = new HashSet<string>(); } }";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NameAndNullOrEmpty_HandledCorrectly()
    {
        Assert.AreEqual("Collection Expression", _converter.Name);
        Assert.IsNull(_converter.Apply(null));
        Assert.AreEqual(string.Empty, _converter.Apply(string.Empty));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsArrayPropertyInitializer()
    {
        string input = "class C { public int[] Items { get; } = new int[] { 1, 2 }; }";
        string expected = "class C { public int[] Items { get; } = [1, 2]; }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }
}
