using CodeJanitor.Logic.Transformations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="VarWhenApparentConverter" />.
/// Policy (ADR-0007): use var only when the right-hand side explicitly indicates the type
/// (object creation, cast, array creation) and the declared type textually matches; method
/// invocations and literals keep the explicit type.
/// </summary>
[TestClass]
public sealed class VarWhenApparentConverterTests
{
    private ITypeStyleConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new VarWhenApparentConverter();
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsObjectCreationWithMatchingType()
    {
        string input = "class C { void M() { Foo x = new Foo(); } }";
        string expected = "class C { void M() { var x = new Foo(); } }";

        Assert.AreEqual(expected, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsMethodInvocation()
    {
        string input = "class C { void M() { Foo x = GetFoo(); } }";

        Assert.AreEqual(input, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsLiteral()
    {
        string input = "class C { void M() { int x = 5; } }";

        Assert.AreEqual(input, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsWhenDeclaredTypeDiffersFromCreatedType()
    {
        string input = "class C { void M() { IFoo x = new Foo(); } }";

        Assert.AreEqual(input, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsCastWithMatchingType()
    {
        string input = "class C { void M(object o) { Foo x = (Foo)o; } }";
        string expected = "class C { void M(object o) { var x = (Foo)o; } }";

        Assert.AreEqual(expected, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsArrayCreationWithMatchingElementType()
    {
        string input = "class C { void M() { int[] a = new int[3]; } }";
        string expected = "class C { void M() { var a = new int[3]; } }";

        Assert.AreEqual(expected, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsAlreadyVar()
    {
        string input = "class C { void M() { var x = new Foo(); } }";

        Assert.AreEqual(input, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsFieldDeclaration()
    {
        string input = "class C { private Foo _x = new Foo(); }";

        Assert.AreEqual(input, _converter.UseVarWhenApparent(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void PreservesUnrelatedCode()
    {
        string input = "class C { void M() { Foo x = new Foo(); int y = 5; var z = GetFoo(); } }";
        string expected = "class C { void M() { var x = new Foo(); int y = 5; var z = GetFoo(); } }";

        Assert.AreEqual(expected, _converter.UseVarWhenApparent(input));
    }
}
