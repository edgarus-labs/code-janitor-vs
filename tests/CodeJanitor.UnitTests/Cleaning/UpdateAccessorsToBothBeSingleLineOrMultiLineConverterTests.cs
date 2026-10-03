using System.Text.RegularExpressions;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public sealed class UpdateAccessorsToBothBeSingleLineOrMultiLineConverterTests
{
    private UpdateAccessorsToBothBeSingleLineOrMultiLineConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new UpdateAccessorsToBothBeSingleLineOrMultiLineConverter(EffectiveCleanupSettings.For(null));
        Settings.Default.Cleaning_UpdateAccessorsToBothBeSingleLineOrMultiLine = true;
    }

    [TestCleanup]
    public void TestCleanup() => Settings.Default.Cleaning_UpdateAccessorsToBothBeSingleLineOrMultiLine = false;

    [TestMethod]
    public void SettingDisabled_ReturnsUnchanged()
    {
        Settings.Default.Cleaning_UpdateAccessorsToBothBeSingleLineOrMultiLine = false;
        string source = "public class MyClass { public int MyProp { get { return 0; } set { } } }";
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
    public void ConsistentMultiLineAccessors_ReturnsUnchanged()
    {
        string source = "public class MyClass\r\n{\r\n    public int MyProp\r\n    {\r\n        get\r\n        {\r\n            return 0;\r\n        }\r\n        set\r\n        {\r\n        }\r\n    }\r\n}";
        string result = _converter.Apply(source);
        // Already consistent multi-line, should remain mostly unchanged
        Assert.Contains("public int MyProp", result);
    }

    [TestMethod]
    public void PropertyWithoutAccessorList_ReturnsUnchanged()
    {
        string source = "public class MyClass { public int MyProp => 0; }";
        string result = _converter.Apply(source);
        // Expression-bodied property should remain unchanged
        Assert.Contains("MyProp", result);
    }

    [TestMethod]
    public void NoAccessors_ReturnsUnchanged()
    {
        string source = "public class MyClass { }";
        string result = _converter.Apply(source);
        Assert.AreEqual(source, result);
    }

    [TestMethod]
    public void EventWithSingleAccessor_ReturnsUnchanged()
    {
        string source = "public class MyClass { public event System.EventHandler MyEvent { add { } } }";
        string result = _converter.Apply(source);
        // Events with only add/remove should not be processed if only one present
        Assert.Contains("MyEvent", result);
    }

    [TestMethod]
    public void PropertyWithSingleLineGetterAndMultiLineSetter_CompressesSetter()
    {
        string source = "public class C { private int _value; public int Value { get { return _value; } set\r\n{\r\n_value = value;\r\n} } }";

        string result = _converter.Apply(source);

        Assert.IsTrue(Regex.IsMatch(result, @"set\s*\{\s*_value\s*=\s*value;\s*\}"));
    }

    [TestMethod]
    public void PropertyWithMultiLineGetterAndSingleLineSetter_ExpandsSetter()
    {
        string source = "public class C { private int _value; public int Value { get\r\n{\r\nreturn _value;\r\n} set { _value = value; } } }";

        string result = _converter.Apply(source);

        Assert.IsTrue(Regex.IsMatch(result, @"public\s+int\s+Value\s*\{.*get.*set.*\}", RegexOptions.Singleline));
    }

    [TestMethod]
    public void EventWithInconsistentAccessors_CompressesSecondAccessor()
    {
        string source = "public class C { private System.EventHandler _changed; public event System.EventHandler Changed { add { _changed += value; } remove\r\n{\r\n_changed -= value;\r\n} } }";

        string result = _converter.Apply(source);

        Assert.IsTrue(Regex.IsMatch(result, @"remove\s*\{\s*_changed\s*-=\s*value;\s*\}"));
    }

    [TestMethod]
    public void AbstractPropertyWithoutAccessorBodies_ReturnsUnchanged()
    {
        string source = "public abstract class C { public abstract int Value { get; set; } }";

        string result = _converter.Apply(source);

        Assert.AreEqual(source, result);
    }

    [TestMethod]
    public void ExpressionBodiedAccessors_ReturnUnchanged()
    {
        string source = "public class C { private int _value; public int Value { get => _value; set => _value = value; } }";

        string result = _converter.Apply(source);

        Assert.AreEqual(source, result);
    }

    [TestMethod]
    public void MultiStatementAccessor_IsNotCompressed()
    {
        string source = "public class C { private int _value; public int Value { get { return _value; } set\r\n{\r\n_value = value;\r\nSystem.Console.WriteLine(value);\r\n} } }";

        string result = _converter.Apply(source);

        Assert.Contains("System.Console.WriteLine(value);", result);
        Assert.Contains("set\r\n{", result);
    }

    [TestMethod]
    [DataRow("class C\r\n{\r\n    event System.Action E\r\n    {\r\n        add { }\r\n        remove { }\r\n    }\r\n}\r\n", DisplayName = "event with consistent single-line accessors")]
    [DataRow("class C\r\n{\r\n    event System.Action E\r\n    {\r\n        add\r\n        {\r\n            _e += value;\r\n        }\r\n        remove\r\n        {\r\n            _e -= value;\r\n        }\r\n    }\r\n}\r\n", DisplayName = "event with consistent multi-line accessors")]
    [DataRow("class C\r\n{\r\n    event System.Action E\r\n    {\r\n        add => _e += value;\r\n        remove\r\n        {\r\n            _e -= value;\r\n        }\r\n    }\r\n}\r\n", DisplayName = "event with an expression-bodied accessor")]
    [DataRow("class C\r\n{\r\n    int P\r\n    {\r\n        get => _p;\r\n        set\r\n        {\r\n            _p = value;\r\n        }\r\n    }\r\n}\r\n", DisplayName = "property with an expression-bodied getter")]
    [DataRow("class C\r\n{\r\n    int P { get; init; }\r\n    int Q { get { return 1; } }\r\n}\r\n", DisplayName = "auto property and single accessor")]
    [DataRow("using System;\r\nConsole.WriteLine(new C().P);\r\nclass C\r\n{\r\n    int _p;\r\n    public int P { get { return _p; } set { _p = value; } }\r\n}\r\n", DisplayName = "top-level statements file with consistent accessors")]
    [DataRow("interface I\r\n{\r\n    int P { get; set; }\r\n    event System.Action E;\r\n}\r\n", DisplayName = "interface members")]
    [DataRow("class C\r\n{\r\n#if NEVER_DEFINED\r\n    int P { get { return 1; } set\r\n    {\r\n    } }\r\n#endif\r\n}\r\n", DisplayName = "disabled preprocessor branch")]
    [DataRow("interface I { event System.Action E; }\r\nclass C : I\r\n{\r\n    event System.Action I.E;\r\n}\r\n", DisplayName = "explicit interface event without accessors (syntax error)")]
    public void AccessorsThatAreAlreadyConsistentOrHaveNoBlockBodies_AreUnchanged(string source) => Assert.AreEqual(source, _converter.Apply(source));

    [TestMethod]
    [DataRow(
        "class C\r\n{\r\n    int P\r\n    {\r\n        get { return _p; }\r\n        set\r\n        {\r\n            _p = value;\r\n        }\r\n    }\r\n}\r\n",
        "class C\r\n{\r\n    int P\r\n    {\r\n        get { return _p; }\r\n        set { _p = value; }\r\n    }\r\n}\r\n",
        DisplayName = "multi-line setter compressed")]
    [DataRow(
        "class C\r\n{\r\n    int P\r\n    {\r\n        get\r\n        {\r\n            return _p;\r\n        }\r\n        set { _p = value; }\r\n    }\r\n}\r\n",
        "class C\r\n{\r\n    int P\r\n    {\r\n        get\r\n        {\r\n            return _p;\r\n        }\r\n        set\r\n        {\r\n            _p = value;\r\n        }\r\n    }\r\n}\r\n",
        DisplayName = "single-line setter expanded")]
    [DataRow(
        "class C\n{\n\tint P\n\t{\n\t\tget\n\t\t{\n\t\t\treturn _p;\n\t\t}\n\t\tprivate set { _p = value; /* keep */ }\n\t}\n}\n",
        "class C\n{\n\tint P\n\t{\n\t\tget\n\t\t{\n\t\t\treturn _p;\n\t\t}\n\t\tprivate set\n\t\t{\n\t\t\t_p = value; /* keep */\n\t\t}\n\t}\n}\n",
        DisplayName = "LF tab-indented setter with a comment expanded")]
    [DataRow(
        "class C\n{\n    event System.Action E\n    {\n        add { _e += value; }\n        remove\n        {\n            _e -= value;\n        }\n    }\n}\n",
        "class C\n{\n    event System.Action E\n    {\n        add { _e += value; }\n        remove { _e -= value; }\n    }\n}\n",
        DisplayName = "LF event remove accessor compressed")]
    public void InconsistentAccessors_FollowTheFirstAccessorKeepingIndentationAndLineBreaks(string source, string expected) => Assert.AreEqual(expected, _converter.Apply(source));

    [TestMethod]
    [DataRow("class C\r\n{\r\n    int P\r\n    {\r\n        get { return _p; }\r\n        set\r\n        {\r\n            // store\r\n            _p = value;\r\n        }\r\n    }\r\n}\r\n", DisplayName = "comment line in the body")]
    [DataRow("class C\r\n{\r\n    int P\r\n    {\r\n        get { return _p; }\r\n        set\r\n        {\r\n            _p = value; // store\r\n        }\r\n    }\r\n}\r\n", DisplayName = "trailing comment in the body")]
    [DataRow("class C\r\n{\r\n    int P\r\n    {\r\n        get { return _p; }\r\n        set // store\r\n        {\r\n            _p = value;\r\n        }\r\n    }\r\n}\r\n", DisplayName = "comment after the keyword")]
    [DataRow("class C\r\n{\r\n    int P\r\n    {\r\n        get { return _p; }\r\n        set\r\n        {\r\n            _p = Compute(1,\r\n                2);\r\n        }\r\n    }\r\n}\r\n", DisplayName = "statement spanning two lines")]
    public void AccessorsThatCannotBeCompressedWithoutLosingLayout_AreUnchanged(string source) => Assert.AreEqual(source, _converter.Apply(source));

    [TestMethod]
    public void SingleLineSetterWhoseBodyAlreadyBreaksTheLine_IsNotExpandedIntoABlankLine()
    {
        string source = "class C\r\n{\r\n    int P\r\n    {\r\n        get\r\n        {\r\n            return _p;\r\n        }\r\n        set { _p = value; // keep\r\n        } }\r\n}\r\n";

        string expected = "class C\r\n{\r\n    int P\r\n    {\r\n        get\r\n        {\r\n            return _p;\r\n        }\r\n        set\r\n        {\r\n            _p = value; // keep\r\n        } }\r\n}\r\n";

        Assert.AreEqual(expected, _converter.Apply(source));
    }
}
