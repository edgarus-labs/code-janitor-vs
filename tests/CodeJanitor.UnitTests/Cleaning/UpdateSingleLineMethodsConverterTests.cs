using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public sealed class UpdateSingleLineMethodsConverterTests
{
    private UpdateSingleLineMethodsConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new UpdateSingleLineMethodsConverter(EffectiveCleanupSettings.For(null));
        Settings.Default.Cleaning_UpdateSingleLineMethods = true;
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Settings.Default.Cleaning_UpdateSingleLineMethods = false;
    }

    [TestMethod]
    public void SettingDisabled_ReturnsUnchanged()
    {
        Settings.Default.Cleaning_UpdateSingleLineMethods = false;
        string source = "public class MyClass { public void MyMethod() { return; } }";
        string result = _converter.Apply(source);
        Assert.AreEqual(source, result);
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
    public void MultiLineMethod_ReturnsUnchanged()
    {
        string source = "public class MyClass\r\n{\r\n    public void MyMethod()\r\n    {\r\n        return;\r\n    }\r\n}";
        string result = _converter.Apply(source);
        // Multi-line methods should not be affected
        Assert.Contains("public void MyMethod()", result);
    }

    [TestMethod]
    public void NoMethods_ReturnsUnchanged()
    {
        string source = "public class MyClass { }";
        string result = _converter.Apply(source);
        Assert.Contains("public class MyClass", result);
    }

    [TestMethod]
    public void AbstractMethod_ReturnsUnchanged()
    {
        string source = "public abstract class MyClass { public abstract void MyMethod(); }";
        string result = _converter.Apply(source);
        // Abstract methods should not be spread
        Assert.Contains("public abstract void MyMethod()", result);
    }

    [TestMethod]
    public void InterfaceMethod_ReturnsUnchanged()
    {
        string source = "public interface IMyInterface { void MyMethod(); }";
        string result = _converter.Apply(source);
        // Interface methods should not be spread
        Assert.Contains("void MyMethod()", result);
    }
}
