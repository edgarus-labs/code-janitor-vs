using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;

namespace CodeJanitor.UnitTests.Cleaning
{
    [TestClass]
    public class BlankLinePaddingConverterTests
    {
        private BlankLinePaddingConverter _converter;

        [TestInitialize]
        public void TestInitialize()
        {
            _converter = new BlankLinePaddingConverter();
        }

        [TestMethod]
        public void BlankLinePaddingDisabled_ReturnsUnchanged()
        {
            // Clear all blank line padding settings.
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterMethods = false;

            var source = @"public class MyClass
{
    public void MyMethod()
    {
    }
}";
            var result = _converter.Apply(source);
            Assert.AreEqual(source, result);
        }

        [TestMethod]
        public void PaddingBeforeClass_Enabled_InsertsBlankLine()
        {
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = true;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterMethods = false;

            var source = @"using System;
public class MyClass
{
}";
            var result = _converter.Apply(source);
            Assert.IsTrue(result.Contains(";\r\n\r\npublic class MyClass"));
        }

        [TestMethod]
        public void PaddingAfterClass_Enabled_InsertsBlankLine()
        {
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = true;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterMethods = false;

            var source = @"public class MyClass
{
}
public class AnotherClass
{
}";
            var result = _converter.Apply(source);
            Assert.IsTrue(result.Contains("}\r\n\r\npublic class AnotherClass"));
        }

        [TestMethod]
        public void PaddingBeforeMethod_Enabled_InsertsBlankLine()
        {
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = true;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterMethods = false;

            var source = @"public class MyClass
{
    public void FirstMethod() { }
    public void SecondMethod() { }
}";
            var result = _converter.Apply(source);
            Assert.IsTrue(result.Contains("{ }\r\n\r\npublic void SecondMethod"));
        }

        [TestMethod]
        public void PaddingAfterMethod_Enabled_InsertsBlankLine()
        {
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterMethods = true;

            var source = @"public class MyClass
{
    public void FirstMethod() { }
    public void SecondMethod() { }
}";
            var result = _converter.Apply(source);
            Assert.IsTrue(result.Contains("{ }\r\n\r\npublic void SecondMethod"));
        }

        [TestMethod]
        public void PaddingBeforeNamespace_Enabled_InsertsBlankLine()
        {
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeNamespaces = true;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterNamespaces = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = false;

            var source = @"using System;
namespace MyNamespace
{
}";
            var result = _converter.Apply(source);
            Assert.IsTrue(result.Contains(";\r\n\r\nnamespace MyNamespace"));
        }

        [TestMethod]
        public void PaddingAfterNamespace_Enabled_InsertsBlankLine()
        {
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeNamespaces = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterNamespaces = true;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = false;

            var source = @"namespace MyNamespace
{
}
namespace AnotherNamespace
{
}";
            var result = _converter.Apply(source);
            Assert.IsTrue(result.Contains("}\r\n\r\nnamespace AnotherNamespace"));
        }

        [TestMethod]
        public void PaddingBeforeProperty_SingleLine_Enabled_InsertsBlankLine()
        {
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine = true;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforePropertiesMultiLine = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterPropertiesMultiLine = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = false;

            var source = @"public class MyClass
{
    public string FirstProperty { get; set; }
    public string SecondProperty { get; set; }
}";
            var result = _converter.Apply(source);
            Assert.IsTrue(result.Contains("{ get; set; }\r\n\r\npublic string SecondProperty"));
        }

        [TestMethod]
        public void PaddingBeforeProperty_MultiLine_Enabled_InsertsBlankLine()
        {
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforePropertiesMultiLine = true;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterPropertiesMultiLine = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = false;

            var source = @"public class MyClass
{
    public string FirstProperty
    {
        get { return ""value""; }
    }
    public string SecondProperty { get; set; }
}";
            var result = _converter.Apply(source);
            Assert.IsTrue(result.Contains("}\r\n\r\npublic string SecondProperty"));
        }

        [TestMethod]
        public void PaddingBeforeField_SingleLine_Enabled_InsertsBlankLine()
        {
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeFieldsSingleLine = true;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterFieldsSingleLine = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeFieldsMultiLine = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterFieldsMultiLine = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = false;

            var source = @"public class MyClass
{
    private string _firstField;
    private string _secondField;
}";
            var result = _converter.Apply(source);
            Assert.IsTrue(result.Contains(";\r\n\r\nprivate string _secondField"));
        }

        [TestMethod]
        public void PaddingBeforeField_MultiLine_Enabled_InsertsBlankLine()
        {
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeFieldsSingleLine = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeFieldsMultiLine = true;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterFieldsSingleLine = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterFieldsMultiLine = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = false;

            var source = @"public class MyClass
{
    private string _firstField =
        ""value"";
    private string _secondField;
}";
            var result = _converter.Apply(source);
            Assert.IsTrue(result.Contains(";\r\n\r\nprivate string _secondField"));
        }

        [TestMethod]
        public void PaddingBeforeEvent_Enabled_InsertsBlankLine()
        {
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEvents = true;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterEvents = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = false;

            var source = @"public class MyClass
{
    public event System.EventHandler FirstEvent;
    public event System.EventHandler SecondEvent;
}";
            var result = _converter.Apply(source);
            Assert.IsTrue(result.Contains(";\r\n\r\npublic event System.EventHandler SecondEvent"));
        }

        [TestMethod]
        public void PaddingBeforeStruct_Enabled_InsertsBlankLine()
        {
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeStructs = true;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterStructs = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = false;

            var source = @"using System;
public struct MyStruct
{
}";
            var result = _converter.Apply(source);
            Assert.IsTrue(result.Contains(";\r\n\r\npublic struct MyStruct"));
        }

        [TestMethod]
        public void PaddingBeforeInterface_Enabled_InsertsBlankLine()
        {
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeInterfaces = true;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterInterfaces = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = false;

            var source = @"using System;
public interface IMyInterface
{
}";
            var result = _converter.Apply(source);
            Assert.IsTrue(result.Contains(";\r\n\r\npublic interface IMyInterface"));
        }

        [TestMethod]
        public void PaddingBeforeEnum_Enabled_InsertsBlankLine()
        {
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEnumerations = true;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterEnumerations = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = false;

            var source = @"using System;
public enum MyEnum
{
}";
            var result = _converter.Apply(source);
            Assert.IsTrue(result.Contains(";\r\n\r\npublic enum MyEnum"));
        }

        [TestMethod]
        public void PaddingBeforeDelegate_Enabled_InsertsBlankLine()
        {
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeDelegates = true;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterDelegates = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = false;

            var source = @"using System;
public delegate void MyDelegate();";
            var result = _converter.Apply(source);
            Assert.IsTrue(result.Contains(";\r\n\r\npublic delegate void MyDelegate"));
        }

        [TestMethod]
        public void Record_PaddingBeforeClasses_Applied()
        {
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = true;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = false;
            Settings.Default.Cleaning_InsertBlankLinePaddingAfterMethods = false;

            var source = @"using System;
public record MyRecord(string Name);";
            var result = _converter.Apply(source);
            Assert.IsTrue(result.Contains(";\r\n\r\npublic record MyRecord"));
        }

        [TestMethod]
        public void EmptySource_ReturnsUnchanged()
        {
            var source = "";
            var result = _converter.Apply(source);
            Assert.AreEqual(source, result);
        }

        [TestMethod]
        public void NullSource_ReturnsUnchanged()
        {
            var result = _converter.Apply(null);
            Assert.IsNull(result);
        }
    }
}
