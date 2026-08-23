using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Cleaning;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public sealed class InsertExplicitAccessModifierLogicTests
{
    [TestMethod]
    public void IsFixedFieldDeclaration_ReturnsTrue_ForUnsafeFixedBuffer()
    {
        Assert.IsTrue(InsertExplicitAccessModifierLogic.IsFixedFieldDeclaration("fixed byte data[8]"));
        Assert.IsTrue(InsertExplicitAccessModifierLogic.IsFixedFieldDeclaration("private unsafe fixed byte data[8]"));
    }

    [TestMethod]
    public void IsFixedFieldDeclaration_ReturnsFalse_ForRegularFields()
    {
        Assert.IsFalse(InsertExplicitAccessModifierLogic.IsFixedFieldDeclaration("byte data;"));
        Assert.IsFalse(InsertExplicitAccessModifierLogic.IsFixedFieldDeclaration("private readonly byte[] data;"));
    }
}
