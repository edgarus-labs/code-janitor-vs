using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Model.CodeItems;
using CodeJanitor.Properties;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public sealed class InsertBlankLinePaddingLogicTests
{
    private InsertBlankLinePaddingLogic _logic;

    [TestInitialize]
    public void TestInitialize()
    {
        _logic = InsertBlankLinePaddingLogic.GetInstance(null);
        DisableAllSettings();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        DisableAllSettings();
    }

    [DataTestMethod]
    [DataRow(KindCodeItem.Class, "Cleaning_InsertBlankLinePaddingBeforeClasses")]
    [DataRow(KindCodeItem.Delegate, "Cleaning_InsertBlankLinePaddingBeforeDelegates")]
    [DataRow(KindCodeItem.Enum, "Cleaning_InsertBlankLinePaddingBeforeEnumerations")]
    [DataRow(KindCodeItem.Event, "Cleaning_InsertBlankLinePaddingBeforeEvents")]
    [DataRow(KindCodeItem.Field, "Cleaning_InsertBlankLinePaddingBeforeFieldsSingleLine")]
    [DataRow(KindCodeItem.Interface, "Cleaning_InsertBlankLinePaddingBeforeInterfaces")]
    [DataRow(KindCodeItem.Namespace, "Cleaning_InsertBlankLinePaddingBeforeNamespaces")]
    [DataRow(KindCodeItem.Constructor, "Cleaning_InsertBlankLinePaddingBeforeMethods")]
    [DataRow(KindCodeItem.Destructor, "Cleaning_InsertBlankLinePaddingBeforeMethods")]
    [DataRow(KindCodeItem.Method, "Cleaning_InsertBlankLinePaddingBeforeMethods")]
    [DataRow(KindCodeItem.Indexer, "Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine")]
    [DataRow(KindCodeItem.Property, "Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine")]
    [DataRow(KindCodeItem.Region, "Cleaning_InsertBlankLinePaddingBeforeRegionTags")]
    [DataRow(KindCodeItem.Struct, "Cleaning_InsertBlankLinePaddingBeforeStructs")]
    [DataRow(KindCodeItem.Using, "Cleaning_InsertBlankLinePaddingBeforeUsingStatementBlocks")]
    public void ShouldBePrecededByBlankLine_UsesConfiguredSetting(KindCodeItem kind, string settingName)
    {
        SetSetting(settingName, true);

        Assert.IsTrue(_logic.ShouldBePrecededByBlankLine(new TestCodeItem(kind)));
    }

    [DataTestMethod]
    [DataRow(KindCodeItem.Class, "Cleaning_InsertBlankLinePaddingAfterClasses")]
    [DataRow(KindCodeItem.Delegate, "Cleaning_InsertBlankLinePaddingAfterDelegates")]
    [DataRow(KindCodeItem.Enum, "Cleaning_InsertBlankLinePaddingAfterEnumerations")]
    [DataRow(KindCodeItem.Event, "Cleaning_InsertBlankLinePaddingAfterEvents")]
    [DataRow(KindCodeItem.Field, "Cleaning_InsertBlankLinePaddingAfterFieldsSingleLine")]
    [DataRow(KindCodeItem.Interface, "Cleaning_InsertBlankLinePaddingAfterInterfaces")]
    [DataRow(KindCodeItem.Namespace, "Cleaning_InsertBlankLinePaddingAfterNamespaces")]
    [DataRow(KindCodeItem.Constructor, "Cleaning_InsertBlankLinePaddingAfterMethods")]
    [DataRow(KindCodeItem.Destructor, "Cleaning_InsertBlankLinePaddingAfterMethods")]
    [DataRow(KindCodeItem.Method, "Cleaning_InsertBlankLinePaddingAfterMethods")]
    [DataRow(KindCodeItem.Indexer, "Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine")]
    [DataRow(KindCodeItem.Property, "Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine")]
    [DataRow(KindCodeItem.Region, "Cleaning_InsertBlankLinePaddingAfterEndRegionTags")]
    [DataRow(KindCodeItem.Struct, "Cleaning_InsertBlankLinePaddingAfterStructs")]
    [DataRow(KindCodeItem.Using, "Cleaning_InsertBlankLinePaddingAfterUsingStatementBlocks")]
    public void ShouldBeFollowedByBlankLine_UsesConfiguredSetting(KindCodeItem kind, string settingName)
    {
        SetSetting(settingName, true);

        Assert.IsTrue(_logic.ShouldBeFollowedByBlankLine(new TestCodeItem(kind)));
    }

    [TestMethod]
    public void BlankLinePaddingChecks_ReturnFalseForNullAndDisabledSettings()
    {
        Assert.IsFalse(_logic.ShouldBePrecededByBlankLine(null));
        Assert.IsFalse(_logic.ShouldBeFollowedByBlankLine(null));
        Assert.IsFalse(_logic.ShouldBePrecededByBlankLine(new TestCodeItem((KindCodeItem)999)));
        Assert.IsFalse(_logic.ShouldBeFollowedByBlankLine(new TestCodeItem((KindCodeItem)999)));
        Assert.IsFalse(_logic.ShouldBePrecededByBlankLine(new TestCodeItem(KindCodeItem.Class)));
        Assert.IsFalse(_logic.ShouldBeFollowedByBlankLine(new TestCodeItem(KindCodeItem.Class)));
    }

    private static void SetSetting(string settingName, bool value)
    {
        typeof(Settings).GetProperty(settingName).SetValue(Settings.Default, value);
    }

    private static void DisableAllSettings()
    {
        foreach (System.Reflection.PropertyInfo property in typeof(Settings).GetProperties())
        {
            if (property.Name.StartsWith("Cleaning_InsertBlankLinePadding", System.StringComparison.Ordinal) &&
                property.PropertyType == typeof(bool))
            {
                property.SetValue(Settings.Default, false);
            }
        }
    }

    private sealed class TestCodeItem : BaseCodeItem
    {
        private readonly KindCodeItem _kind;

        public TestCodeItem(KindCodeItem kind)
        {
            _kind = kind;
        }

        public override KindCodeItem Kind => _kind;
    }
}
