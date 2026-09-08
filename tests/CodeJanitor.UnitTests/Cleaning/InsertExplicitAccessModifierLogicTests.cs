using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Model.CodeItems;
using CodeJanitor.Properties;
using System.Linq;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public sealed class InsertExplicitAccessModifierLogicTests
{
    [TestMethod]
    public void GetInstance_ReturnsSingletonInstance()
    {
        var instance1 = InsertExplicitAccessModifierLogic.GetInstance();
        var instance2 = InsertExplicitAccessModifierLogic.GetInstance();

        Assert.IsNotNull(instance1);
        Assert.AreSame(instance1, instance2);
    }

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
        Assert.IsFalse(InsertExplicitAccessModifierLogic.IsFixedFieldDeclaration(null));
        Assert.IsFalse(InsertExplicitAccessModifierLogic.IsFixedFieldDeclaration(string.Empty));
    }

    [TestMethod]
    public void InsertExplicitAccessModifiers_WhenSettingsDisabled_DoesNotThrow()
    {
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnClasses = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnDelegates = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnEnumerations = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnEvents = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnFields = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnInterfaces = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnMethods = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnProperties = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnStructs = false;

        var logic = InsertExplicitAccessModifierLogic.GetInstance();

        logic.InsertExplicitAccessModifiersOnClasses(Enumerable.Empty<CodeItemClass>());
        logic.InsertExplicitAccessModifiersOnDelegates(Enumerable.Empty<CodeItemDelegate>());
        logic.InsertExplicitAccessModifiersOnEnumerations(Enumerable.Empty<CodeItemEnum>());
        logic.InsertExplicitAccessModifiersOnEvents(Enumerable.Empty<CodeItemEvent>());
        logic.InsertExplicitAccessModifiersOnFields(Enumerable.Empty<CodeItemField>());
        logic.InsertExplicitAccessModifiersOnInterfaces(Enumerable.Empty<CodeItemInterface>());
        logic.InsertExplicitAccessModifiersOnMethods(Enumerable.Empty<CodeItemMethod>());
        logic.InsertExplicitAccessModifiersOnProperties(Enumerable.Empty<CodeItemProperty>());
        logic.InsertExplicitAccessModifiersOnStructs(Enumerable.Empty<CodeItemStruct>());
    }
}
