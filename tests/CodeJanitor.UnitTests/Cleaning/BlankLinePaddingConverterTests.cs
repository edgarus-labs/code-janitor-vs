using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public sealed class BlankLinePaddingConverterTests
{
    private BlankLinePaddingConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new BlankLinePaddingConverter(EffectiveCleanupSettings.For(null));
        DisableAllSettings();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        DisableAllSettings();
    }

    [TestMethod]
    public void DoesNotSeparateDocumentationCommentFromItsMember()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = true;

        string source = "class C\r\n{\r\n    int _x;\r\n    /// <summary>\r\n    /// Does a thing.\r\n    /// </summary>\r\n    void M() { }\r\n}\r\n";

        string result = _converter.Apply(source);

        Assert.DoesNotContain("/// </summary>\r\n\r\n    void M()", result, "A blank line was inserted between the doc comment and the method.");
        Assert.Contains("int _x;\r\n\r\n    /// <summary>", result);
    }

    private static void DisableAllSettings()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeDelegates = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterDelegates = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEnumerations = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterEnumerations = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEvents = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterEvents = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeFieldsSingleLine = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterFieldsSingleLine = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeFieldsMultiLine = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterFieldsMultiLine = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeInterfaces = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterInterfaces = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterMethods = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeNamespaces = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterNamespaces = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforePropertiesMultiLine = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterPropertiesMultiLine = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeRegionTags = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterRegionTags = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEndRegionTags = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterEndRegionTags = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeStructs = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterStructs = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeUsingStatementBlocks = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterUsingStatementBlocks = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeCaseStatements = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeSingleLineComments = false;
    }

    [TestMethod]
    public void AllSettingsDisabled_ReturnsUnchanged()
    {
        string source = "public class Foo { public void Bar() { } }";
        string result = _converter.Apply(source);
        Assert.AreEqual(source, result);
    }

    [TestMethod]
    public void EmptySource_ReturnsUnchanged()
    {
        string result = _converter.Apply("");
        Assert.AreEqual("", result);
    }

    [TestMethod]
    public void NullSource_ReturnsUnchanged()
    {
        Assert.IsNull(_converter.Apply(null));
    }

    [TestMethod]
    public void BeforeMethod_InsertsBlankLine()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = true;
        string source = "public class Foo\r\n{\r\n    private int _x;\r\n    public void Bar() { }\r\n}\r\n";
        string result = _converter.Apply(source);
        Assert.Contains("_x;\r\n\r\n    public void Bar()", result, result);
    }

    [TestMethod]
    public void BeforeMethod_AlreadyHasBlankLine_DoesNotDouble()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = true;
        string source = "public class Foo\r\n{\r\n    private int _x;\r\n\r\n    public void Bar() { }\r\n}\r\n";
        string result = _converter.Apply(source);
        // Should not add another blank line
        Assert.DoesNotContain("_x;\r\n\r\n\r\n    public void Bar()", result, result);
    }

    [TestMethod]
    public void AfterMethod_InsertsBlankLine()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterMethods = true;
        string source = "public class Foo\r\n{\r\n    public void Bar() { }\r\n    private int _x;\r\n}\r\n";
        string result = _converter.Apply(source);
        Assert.Contains("{ }\r\n\r\n    private int _x;", result, result);
    }

    [TestMethod]
    public void BeforeClass_InsertsBlankLine()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = true;
        string source = "namespace MyNs\r\n{\r\n    public class Foo { }\r\n    public class Bar { }\r\n}\r\n";
        string result = _converter.Apply(source);
        Assert.Contains("Foo { }\r\n\r\n    public class Bar", result, result);
    }

    [TestMethod]
    public void BeforeClass_AdjacentToOpenBrace_SkipsInsertion()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = true;
        // Class at start of namespace body (right after {)
        string source = "namespace MyNs\r\n{\r\n    public class Foo { }\r\n}\r\n";
        string result = _converter.Apply(source);
        // Should not insert before Foo (adjacent to opening brace of namespace)
        Assert.DoesNotContain("{\r\n\r\n    public class Foo", result, result);
    }

    [TestMethod]
    public void BeforeMethod_AdjacentToOpenBrace_SkipsInsertion()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = true;
        string source = "public class Foo\r\n{\r\n    public void Bar() { }\r\n}\r\n";
        string result = _converter.Apply(source);
        // Method is first in class (after {), should not insert blank
        Assert.DoesNotContain("{\r\n\r\n    public void Bar()", result, result);
    }

    [TestMethod]
    public void BeforeProperty_SingleLine_InsertsBlankLine()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine = true;
        string source = "public class Foo\r\n{\r\n    private int _x;\r\n    public int X { get; set; }\r\n}\r\n";
        string result = _converter.Apply(source);
        Assert.Contains("_x;\r\n\r\n    public int X", result, result);
    }

    [TestMethod]
    public void BeforeEnum_InsertsBlankLine()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEnumerations = true;
        string source = "public class Foo { }\r\npublic enum Bar { A, B }\r\n";
        string result = _converter.Apply(source);
        Assert.Contains("{ }\r\n\r\npublic enum Bar", result, result);
    }

    [TestMethod]
    public void BeforeStruct_InsertsBlankLine()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeStructs = true;
        string source = "public class Foo { }\r\npublic struct Bar { }\r\n";
        string result = _converter.Apply(source);
        Assert.Contains("{ }\r\n\r\npublic struct Bar", result, result);
    }

    [TestMethod]
    public void BeforeInterface_InsertsBlankLine()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeInterfaces = true;
        string source = "public class Foo { }\r\npublic interface IBar { }\r\n";
        string result = _converter.Apply(source);
        Assert.Contains("{ }\r\n\r\npublic interface IBar", result, result);
    }

    [TestMethod]
    public void BeforeRegionTag_InsertsBlankLine()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeRegionTags = true;
        string source = "public class Foo\r\n{\r\n    private int _x;\r\n    #region Methods\r\n    public void Bar() { }\r\n    #endregion\r\n}\r\n";
        string result = _converter.Apply(source);
        Assert.Contains("_x;\r\n\r\n    #region Methods", result, result);
    }

    [TestMethod]
    public void AfterEndRegionTag_InsertsBlankLine()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterEndRegionTags = true;
        string source = "public class Foo\r\n{\r\n    #region Fields\r\n    private int _x;\r\n    #endregion\r\n    public void Bar() { }\r\n}\r\n";
        string result = _converter.Apply(source);
        Assert.Contains("#endregion\r\n\r\n    public void Bar()", result, result);
    }

    [TestMethod]
    public void PreservesNewlineStyle_Unix()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = true;
        string source = "public class Foo\n{\n    private int _x;\n    public void Bar() { }\n}\n";
        string result = _converter.Apply(source);
        Assert.Contains("_x;\n\n    public void Bar()", result, result);
    }

    [TestMethod]
    public void Name_DescribesTheCleanup()
    {
        Assert.AreEqual("Insert blank line padding", _converter.Name);
    }

    [TestMethod]
    [DataRow("BeforeClasses", "enum E { A }\nclass C { }", "enum E { A }\n\nclass C { }")]
    [DataRow("AfterClasses", "class C { }\nenum E { A }", "class C { }\n\nenum E { A }")]
    [DataRow("BeforeDelegates", "class C { }\ndelegate void D();", "class C { }\n\ndelegate void D();")]
    [DataRow("AfterDelegates", "delegate void D();\nclass C { }", "delegate void D();\n\nclass C { }")]
    [DataRow("BeforeEnumerations", "class C { }\nenum E { A }", "class C { }\n\nenum E { A }")]
    [DataRow("AfterEnumerations", "enum E { A }\nclass C { }", "enum E { A }\n\nclass C { }")]
    [DataRow("BeforeEvents", "class C\n{\n    int a;\n    event System.Action E;\n}", "class C\n{\n    int a;\n\n    event System.Action E;\n}")]
    [DataRow("AfterEvents", "class C\n{\n    event System.Action E { add { } remove { } }\n    int a;\n}", "class C\n{\n    event System.Action E { add { } remove { } }\n\n    int a;\n}")]
    [DataRow("BeforeFieldsSingleLine", "class C\n{\n    void M() { }\n    int a;\n}", "class C\n{\n    void M() { }\n\n    int a;\n}")]
    [DataRow("AfterFieldsSingleLine", "class C\n{\n    int a;\n    void M() { }\n}", "class C\n{\n    int a;\n\n    void M() { }\n}")]
    [DataRow("BeforeFieldsMultiLine", "class C\n{\n    void M() { }\n    int[] a =\n        { 1 };\n}", "class C\n{\n    void M() { }\n\n    int[] a =\n        { 1 };\n}")]
    [DataRow("AfterFieldsMultiLine", "class C\n{\n    int[] a =\n        { 1 };\n    void M() { }\n}", "class C\n{\n    int[] a =\n        { 1 };\n\n    void M() { }\n}")]
    [DataRow("BeforeInterfaces", "class C { }\ninterface I { }", "class C { }\n\ninterface I { }")]
    [DataRow("AfterInterfaces", "interface I { }\nclass C { }", "interface I { }\n\nclass C { }")]
    [DataRow("BeforeMethods", "class C\n{\n    int a;\n    void M() { }\n}", "class C\n{\n    int a;\n\n    void M() { }\n}")]
    [DataRow("AfterMethods", "class C\n{\n    void M() { }\n    int a;\n}", "class C\n{\n    void M() { }\n\n    int a;\n}")]
    [DataRow("BeforeNamespaces", "using System;\nnamespace N { }", "using System;\n\nnamespace N { }")]
    [DataRow("AfterNamespaces", "namespace N { }\nnamespace M { }", "namespace N { }\n\nnamespace M { }")]
    [DataRow("BeforePropertiesSingleLine", "class C\n{\n    int a;\n    int P { get; }\n}", "class C\n{\n    int a;\n\n    int P { get; }\n}")]
    [DataRow("AfterPropertiesSingleLine", "class C\n{\n    int P { get; }\n    int a;\n}", "class C\n{\n    int P { get; }\n\n    int a;\n}")]
    [DataRow("BeforePropertiesMultiLine", "class C\n{\n    int a;\n    int P\n    {\n        get { return 1; }\n    }\n}", "class C\n{\n    int a;\n\n    int P\n    {\n        get { return 1; }\n    }\n}")]
    [DataRow("AfterPropertiesMultiLine", "class C\n{\n    int P\n    {\n        get { return 1; }\n    }\n    int a;\n}", "class C\n{\n    int P\n    {\n        get { return 1; }\n    }\n\n    int a;\n}")]
    [DataRow("BeforeRegionTags", "class C\n{\n    int a;\n    #region R\n    int b;\n    #endregion\n    int c;\n}", "class C\n{\n    int a;\n\n    #region R\n    int b;\n    #endregion\n    int c;\n}")]
    [DataRow("AfterRegionTags", "class C\n{\n    int a;\n    #region R\n    int b;\n    #endregion\n    int c;\n}", "class C\n{\n    int a;\n    #region R\n\n    int b;\n    #endregion\n    int c;\n}")]
    [DataRow("BeforeEndRegionTags", "class C\n{\n    int a;\n    #region R\n    int b;\n    #endregion\n    int c;\n}", "class C\n{\n    int a;\n    #region R\n    int b;\n\n    #endregion\n    int c;\n}")]
    [DataRow("AfterEndRegionTags", "class C\n{\n    int a;\n    #region R\n    int b;\n    #endregion\n    int c;\n}", "class C\n{\n    int a;\n    #region R\n    int b;\n    #endregion\n\n    int c;\n}")]
    [DataRow("BeforeStructs", "class C { }\nstruct S { }", "class C { }\n\nstruct S { }")]
    [DataRow("AfterStructs", "struct S { }\nclass C { }", "struct S { }\n\nclass C { }")]
    [DataRow("BeforeUsingStatementBlocks", "// header\nusing System;\nclass C { }", "// header\n\nusing System;\nclass C { }")]
    [DataRow("AfterUsingStatementBlocks", "using System;\nclass C { }", "using System;\n\nclass C { }")]
    [DataRow("BeforeCaseStatements", "switch (x)\n{\n    case 1:\n        break;\n    default:\n        return;\n}", "switch (x)\n{\n    case 1:\n        break;\n\n    default:\n        return;\n}")]
    [DataRow("BeforeSingleLineComments", "int a = 1;\n// note\nint b = 2;", "int a = 1;\n\n// note\nint b = 2;")]
    public void EachSetting_OnItsOwn_PadsOnlyItsConstruct(string setting, string source, string expected)
    {
        Settings.Default["Cleaning_InsertBlankLinePadding" + setting] = true;

        Assert.AreEqual(expected, _converter.Apply(source));
    }

    [TestMethod]
    public void TopLevelStatementsFile_PadsTypesAfterTheStatements_AndLeavesStatementsAndLocalFunctionsAlone()
    {
        EnableAllBeforeSettings();
        string source =
            "using System;\r\n" +
            "\r\n" +
            "Console.WriteLine(\"hi\");\r\n" +
            "static int Local() => 1;\r\n" +
            "Console.WriteLine(Local());\r\n" +
            "class Foo\r\n" +
            "{\r\n" +
            "    int _a;\r\n" +
            "    void M() { }\r\n" +
            "}\r\n" +
            "record R(int X);\r\n";
        string expected =
            "using System;\r\n" +
            "\r\n" +
            "Console.WriteLine(\"hi\");\r\n" +
            "static int Local() => 1;\r\n" +
            "Console.WriteLine(Local());\r\n" +
            "\r\n" +
            "class Foo\r\n" +
            "{\r\n" +
            "    int _a;\r\n" +
            "\r\n" +
            "    void M() { }\r\n" +
            "}\r\n" +
            "\r\n" +
            "record R(int X);\r\n";

        Assert.AreEqual(expected, _converter.Apply(source));
    }

    [TestMethod]
    public void FileScopedNamespace_BeforePaddingSeparatesItFromTheUsings()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeNamespaces = true;
        string source = "using System;\r\nnamespace N;\r\n\r\nclass C { }\r\n";

        Assert.AreEqual("using System;\r\n\r\nnamespace N;\r\n\r\nclass C { }\r\n", _converter.Apply(source));
    }

    [TestMethod]
    public void MultiLineDocumentationComment_PaddingGoesAboveTheComment()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = true;
        string source = "int x = 1;\n/** <summary>C</summary> */\nclass C { }\n";

        Assert.AreEqual("int x = 1;\n\n/** <summary>C</summary> */\nclass C { }\n", _converter.Apply(source));
    }

    [TestMethod]
    public void Attributes_PaddingGoesAboveTheAttributeList()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = true;
        string source = "class C\n{\n    int _x;\n    [System.Obsolete]\n    void M() { }\n}\n";

        Assert.AreEqual("class C\n{\n    int _x;\n\n    [System.Obsolete]\n    void M() { }\n}\n", _converter.Apply(source));
    }

    [TestMethod]
    public void BeforeAndAfterBetweenTwoMembers_InsertsASingleBlankLine()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterMethods = true;
        string source = "class C\n{\n    void A() { }\n    void B() { }\n}\n";

        Assert.AreEqual("class C\n{\n    void A() { }\n\n    void B() { }\n}\n", _converter.Apply(source));
    }

    [TestMethod]
    [DataRow("class C\n{\n    void M() { }\n}\n", DisplayName = "closing brace")]
    [DataRow("class C\n{\n    void M() { }\n} // end of C\n", DisplayName = "closing brace followed by a comment")]
    [DataRow("class C\n{\n    void M() { }\n};\n", DisplayName = "closing brace followed by a semicolon")]
    public void AfterPadding_IsNotInsertedBeforeAClosingBrace(string source)
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterMethods = true;

        Assert.AreEqual(source, _converter.Apply(source));
    }

    [TestMethod]
    public void BeforePadding_IsNotInsertedAfterALineEndingWithAnOpeningBrace()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeFieldsSingleLine = true;
        string source = "class C {\n    int a;\n}\n";

        Assert.AreEqual(source, _converter.Apply(source));
    }

    [TestMethod]
    public void CarriageReturnOnlyLineBreaks_ArePreserved()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = true;
        string source = "class C\r{\r    int _x;\r    void M() { }\r}\r";

        Assert.AreEqual("class C\r{\r    int _x;\r\r    void M() { }\r}\r", _converter.Apply(source));
    }

    [TestMethod]
    public void TabIndentation_IsPreserved()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = true;
        string source = "class C\r\n{\r\n\tint a;\r\n\tvoid M() { }\r\n}\r\n";

        Assert.AreEqual("class C\r\n{\r\n\tint a;\r\n\r\n\tvoid M() { }\r\n}\r\n", _converter.Apply(source));
    }

    [TestMethod]
    public void DeclarationsInsideDisabledPreprocessorBranches_AreNotPadded()
    {
        EnableAllBeforeSettings();
        string source = "class C\r\n{\r\n    int _x;\r\n#if NEVER_DEFINED\r\n    void M() { }\r\n#endif\r\n}\r\n";

        Assert.AreEqual(source, _converter.Apply(source));
    }

    [TestMethod]
    [DataRow("var s = @\"\r\nclass X { }\r\n#region R\r\n\";\r\n", DisplayName = "verbatim string")]
    [DataRow("var s = \"\"\"\r\n    class X { }\r\n    #region R\r\n    \"\"\";\r\n", DisplayName = "raw string")]
    [DataRow("var s = $\"\"\"\r\n    void M() { }\r\n    {1}\r\n    \"\"\";\r\n", DisplayName = "interpolated raw string")]
    [DataRow("/*\r\nint a;\r\nvoid M() { }\r\n*/\r\n", DisplayName = "multi-line comment")]
    public void CodeLikeTextInsideLiteralsAndComments_IsNotPadded(string source)
    {
        EnableAllBeforeSettings();
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeSingleLineComments = false;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeCaseStatements = false;

        Assert.AreEqual(source, _converter.Apply(source));
    }

    [TestMethod]
    [DataRow("", DisplayName = "empty")]
    [DataRow("   \r\n\t\r\n", DisplayName = "whitespace only")]
    [DataRow("class C { }", DisplayName = "single line")]
    public void TrivialFiles_AreUnchanged(string source)
    {
        EnableAllBeforeSettings();
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = true;

        Assert.AreEqual(source, _converter.Apply(source));
    }

    [TestMethod]
    public void FileWithSyntaxErrors_OnlyAddsBlankLines_AndLosesNoText()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = true;
        string source = "class C\r\n{\r\n    int a\r\n    void M( { }\r\n";

        string result = _converter.Apply(source);

        Assert.AreEqual(source, result.Replace("\r\n\r\n", "\r\n"));
    }

    [TestMethod]
    [DataRow(true, false, DisplayName = "all before settings")]
    [DataRow(false, true, DisplayName = "all after settings")]
    [DataRow(true, true, DisplayName = "all before and after settings")]
    public void CombinedSettings_ArePaddedIdempotently(bool before, bool after)
    {
        if (before)
        {
            EnableAllBeforeSettings();
        }

        if (after)
        {
            EnableAllAfterSettings();
        }

        string source =
            "// header\r\nusing System;\r\nusing System.Text;\r\nnamespace N\r\n{\r\n    #region Fields\r\n    class C\r\n    {\r\n" +
            "        int a;\r\n        // note\r\n        int b;\r\n        int P { get; }\r\n        void M(int x)\r\n        {\r\n" +
            "            switch (x)\r\n            {\r\n                case 1:\r\n                    break;\r\n" +
            "                default:\r\n                    return;\r\n            }\r\n        }\r\n        event System.Action E;\r\n" +
            "#if true\r\n        int c;\r\n#else\r\n        int d;\r\n#endif\r\n    }\r\n    #endregion\r\n    struct S { }\r\n" +
            "    enum E2 { A }\r\n    delegate void D();\r\n    interface I { }\r\n}\r\n";

        string once = _converter.Apply(source);
        string twice = _converter.Apply(once);

        Assert.AreNotEqual(source, once, "The scenario has to be padded at all.");
        Assert.AreEqual(once, twice, "A second run changed the output:\r\n" + once);
    }

    [TestMethod]
    [DataRow("BeforeMethods", "class C\n{\n    int a;\n#if true\n    void M() { }\n#endif\n}\n", "class C\n{\n    int a;\n#if true\n    void M() { }\n#endif\n}\n", DisplayName = "declaration directly below #if")]
    [DataRow("BeforeMethods", "class C\n{\n    int a;\n#if false\n    int b;\n#else\n    void M() { }\n#endif\n}\n", "class C\n{\n    int a;\n#if false\n    int b;\n#else\n    void M() { }\n#endif\n}\n", DisplayName = "declaration directly below #else")]
    [DataRow("BeforeSingleLineComments", "class C\n{\n#if true\n    // c\n    int a;\n#endif\n}\n", "class C\n{\n#if true\n    // c\n    int a;\n#endif\n}\n", DisplayName = "comment directly below #if")]
    [DataRow("AfterMethods", "class C\n{\n#if true\n    void M() { }\n#endif\n    int a;\n}\n", "class C\n{\n#if true\n    void M() { }\n#endif\n    int a;\n}\n", DisplayName = "no blank line between a declaration and the #endif below it")]
    [DataRow("AfterMethods", "class C\n{\n#if true\n    void M() { }\n#else\n    int b;\n#endif\n}\n", "class C\n{\n#if true\n    void M() { }\n#else\n    int b;\n#endif\n}\n", DisplayName = "no blank line between a declaration and the #else below it")]
    [DataRow("BeforeMethods", "class C\n{\n#if true\n    int a;\n#endif\n    void M() { }\n}\n", "class C\n{\n#if true\n    int a;\n#endif\n\n    void M() { }\n}\n", DisplayName = "declaration directly below #endif is still padded")]
    public void ConditionalDirectives_AreNotSeparatedFromTheCodeTheyEnclose(string setting, string source, string expected)
    {
        Settings.Default["Cleaning_InsertBlankLinePadding" + setting] = true;

        Assert.AreEqual(expected, _converter.Apply(source));
    }

    [TestMethod]
    public void ConstructorsDestructorsOperatorsAndConversions_UseTheMethodSettings()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = true;
        string source =
            "class C\n{\n    int a;\n    C() { }\n    int b;\n    ~C() { }\n    int c;\n" +
            "    public static C operator +(C x, C y) => x;\n    int d;\n    public static implicit operator int(C x) => 0;\n}\n";
        string expected =
            "class C\n{\n    int a;\n\n    C() { }\n    int b;\n\n    ~C() { }\n    int c;\n\n" +
            "    public static C operator +(C x, C y) => x;\n    int d;\n\n    public static implicit operator int(C x) => 0;\n}\n";

        Assert.AreEqual(expected, _converter.Apply(source));
    }

    [TestMethod]
    public void IndexersAndEventFields_UseThePropertyAndEventSettings()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEvents = true;
        string source = "class C\n{\n    int a;\n    int this[int i] => i;\n    int b;\n    event System.Action E;\n}\n";

        Assert.AreEqual("class C\n{\n    int a;\n\n    int this[int i] => i;\n    int b;\n\n    event System.Action E;\n}\n", _converter.Apply(source));
    }

    [TestMethod]
    public void GenericPartialAsyncMembers_ArePadded()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = true;
        string source =
            "partial class C<T> where T : class\n{\n    int a;\n" +
            "    async System.Threading.Tasks.Task<T> M<U>() where U : T => await Get<T>();\n}\n";
        string expected =
            "partial class C<T> where T : class\n{\n    int a;\n\n" +
            "    async System.Threading.Tasks.Task<T> M<U>() where U : T => await Get<T>();\n}\n";

        Assert.AreEqual(expected, _converter.Apply(source));
    }

    [TestMethod]
    public void UsingDirectives_ProgramFileWithGlobalStaticAndAliasUsings_IsPaddedAsOneBlock()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeUsingStatementBlocks = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterUsingStatementBlocks = true;
        string source = "// header\r\nglobal using System;\r\nusing static System.Math;\r\nusing X = System.String;\r\nConsole.WriteLine(Abs(-1));\r\n";
        string expected = "// header\r\n\r\nglobal using System;\r\nusing static System.Math;\r\nusing X = System.String;\r\n\r\nConsole.WriteLine(Abs(-1));\r\n";

        Assert.AreEqual(expected, _converter.Apply(source));
    }

    [TestMethod]
    public void UsingDirectives_InsideANamespace_ArePaddedSeparatelyFromTheOuterBlock()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeUsingStatementBlocks = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterUsingStatementBlocks = true;
        string source = "using System;\nnamespace N\n{\n    using A;\n    using B;\n    class C { }\n}\n";

        Assert.AreEqual("using System;\n\nnamespace N\n{\n    using A;\n    using B;\n\n    class C { }\n}\n", _converter.Apply(source));
    }

    [TestMethod]
    public void UsingDirectives_SeparatedByABlankLine_DoNotGetASecondBlankLine()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeUsingStatementBlocks = true;
        string source = "namespace N\n{\n    using A;\n\n    using B;\n}\n";

        Assert.AreEqual(source, _converter.Apply(source));
    }

    [TestMethod]
    public void UsingDirectives_OnTheLastLineWithoutLineBreak_AreUnchanged()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterUsingStatementBlocks = true;

        Assert.AreEqual("using System;", _converter.Apply("using System;"));
    }

    [TestMethod]
    public void UsingStatements_AreNotUsingDirectiveBlocks()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeUsingStatementBlocks = true;
        string source = "class C\n{\n    void M()\n    {\n        Foo();\n        using (var x = Get()) { }\n    }\n}\n";

        Assert.AreEqual(source, _converter.Apply(source));
    }

    [TestMethod]
    public void RegionTags_AtTheFileEdges_AreOnlyPaddedInward()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeRegionTags = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterRegionTags = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEndRegionTags = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterEndRegionTags = true;
        string source = "#region A\nclass C { }\n#endregion";

        Assert.AreEqual("#region A\n\nclass C { }\n\n#endregion", _converter.Apply(source));
    }

    [TestMethod]
    public void EndRegionTag_OnTheFirstLine_IsNotPaddedAbove()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEndRegionTags = true;
        string source = "#endregion\nclass C { }\n";

        Assert.AreEqual(source, _converter.Apply(source));
    }

    [TestMethod]
    public void CaseStatements_CrLfSwitchWithReturnValues_IsPadded()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeCaseStatements = true;
        string source = "switch (x)\r\n{\r\n    case 1:\r\n        return 5;\r\n    case 2:\r\n        break;\r\n    default:\r\n        return;\r\n}\r\n";
        string expected = "switch (x)\r\n{\r\n    case 1:\r\n        return 5;\r\n\r\n    case 2:\r\n        break;\r\n\r\n    default:\r\n        return;\r\n}\r\n";

        Assert.AreEqual(expected, _converter.Apply(source));
    }

    [TestMethod]
    public void CaseStatements_AlreadyPaddedOrFallingThrough_AreUnchanged()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeCaseStatements = true;
        string source = "switch (x)\n{\n    case 1:\n    case 2:\n        break;\n\n    default:\n        break;\n}\n";

        Assert.AreEqual(source, _converter.Apply(source));
    }

    [TestMethod]
    [DataRow("int a;\n// first\n// second\n", "int a;\n\n// first\n// second\n", DisplayName = "only the first of consecutive comments")]
    [DataRow("void M()\n{\n    // first line of the body\n}\n", "void M()\n{\n    // first line of the body\n}\n", DisplayName = "not after an opening brace")]
    [DataRow("/// <summary>\n// c\n", "/// <summary>\n// c\n", DisplayName = "not after a documentation comment")]
    [DataRow("int a;\n/// <summary>x</summary>\nvoid M() { }\n", "int a;\n/// <summary>x</summary>\nvoid M() { }\n", DisplayName = "documentation comments are not single-line comments")]
    [DataRow("int a;\n\n// c\n", "int a;\n\n// c\n", DisplayName = "already padded")]
    [DataRow("// c\nint a;\n", "// c\nint a;\n", DisplayName = "comment on the first line")]
    public void SingleLineComments_LineFeedFile(string source, string expected)
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeSingleLineComments = true;

        Assert.AreEqual(expected, _converter.Apply(source));
    }

    private static void EnableAllAfterSettings()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterDelegates = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterEnumerations = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterEvents = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterFieldsSingleLine = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterFieldsMultiLine = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterInterfaces = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterMethods = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterNamespaces = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterPropertiesMultiLine = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterRegionTags = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterEndRegionTags = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterStructs = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterUsingStatementBlocks = true;
    }

    private static void EnableAllBeforeSettings()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeDelegates = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEnumerations = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEvents = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeFieldsSingleLine = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeFieldsMultiLine = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeInterfaces = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeNamespaces = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforePropertiesMultiLine = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeRegionTags = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEndRegionTags = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeStructs = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeUsingStatementBlocks = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeCaseStatements = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeSingleLineComments = true;
    }

    [TestMethod]
    public void SingleLineComments_CrLfFile_InsertsACompleteCrLfBlankLine()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeSingleLineComments = true;

        Assert.AreEqual("int a = 1;\r\n\r\n// note\r\nint b = 2;\r\n", _converter.Apply("int a = 1;\r\n// note\r\nint b = 2;\r\n"));
    }

    [TestMethod]
    [DataRow("BeforeSingleLineComments", "class C\n{\n    string s = @\"a\n// not a comment\n\";\n}\n", DisplayName = "comment marker in a verbatim string")]
    [DataRow("BeforeSingleLineComments", "var s = \"\"\"\n    a\n    // not a comment\n    \"\"\";\n", DisplayName = "comment marker in a raw string")]
    [DataRow("BeforeSingleLineComments", "/*\n text\n// inner\n*/\n", DisplayName = "comment marker in a block comment")]
    [DataRow("BeforeSingleLineComments", "int x; // first part\n// continued\n", DisplayName = "continuation of a trailing comment")]
    [DataRow("BeforeCaseStatements", "var s = @\"\n    break;\n    case 1:\n\";\n", DisplayName = "case label in a verbatim string")]
    public void TextThatIsNotARealCommentOrCaseLabel_IsNotPadded(string setting, string source)
    {
        Settings.Default["Cleaning_InsertBlankLinePadding" + setting] = true;

        Assert.AreEqual(source, _converter.Apply(source));
    }

    [TestMethod]
    [DataRow("BeforeMethods", "class C\n{\n    int _x;\n    // Explains M\n    void M() { }\n}\n", "class C\n{\n    int _x;\n\n    // Explains M\n    void M() { }\n}\n", DisplayName = "single-line comment above a method")]
    [DataRow("BeforeMethods", "class C\n{\n    int _x;\n    /* Explains M */\n    [System.Obsolete]\n    void M() { }\n}\n", "class C\n{\n    int _x;\n\n    /* Explains M */\n    [System.Obsolete]\n    void M() { }\n}\n", DisplayName = "block comment above an attributed method")]
    [DataRow("BeforeUsingStatementBlocks", "using A;\n// the B block\nusing B;\n", "using A;\n\n// the B block\nusing B;\n", DisplayName = "comment above a using block")]
    public void BeforePadding_GoesAboveTheCommentAttachedToTheDeclaration(string setting, string source, string expected)
    {
        Settings.Default["Cleaning_InsertBlankLinePadding" + setting] = true;

        Assert.AreEqual(expected, _converter.Apply(source));
    }

    [TestMethod]
    [DataRow("AfterMethods", "class C\n{\n    void A() { }\n\n    void B() { }\n}\n", "class C\n{\n    void A() { }\n\n    void B() { }\n}\n", DisplayName = "methods already separated")]
    [DataRow("AfterUsingStatementBlocks", "namespace N\n{\n    using A;\n\n    using B;\n    class C { }\n}\n", "namespace N\n{\n    using A;\n\n    using B;\n\n    class C { }\n}\n", DisplayName = "using blocks already separated")]
    [DataRow("AfterClasses", "using System;\nConsole.WriteLine(1);\nclass Foo { }\n", "using System;\nConsole.WriteLine(1);\nclass Foo { }\n", DisplayName = "last class of a top-level statements file")]
    [DataRow("AfterNamespaces", "namespace N;\n\nclass C { }\n", "namespace N;\n\nclass C { }\n", DisplayName = "file-scoped namespace")]
    public void AfterPadding_IsNotAddedWhereABlankLineOrTheEndOfTheFileFollows(string setting, string source, string expected)
    {
        Settings.Default["Cleaning_InsertBlankLinePadding" + setting] = true;

        Assert.AreEqual(expected, _converter.Apply(source));
    }

    [TestMethod]
    public void MixedLineEndings_PaddingLandsOnTheDeclarationLine()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = true;
        string source = "class C\n{\r\n    int _x;\r\n    void M() { }\r\n    int _y;\r\n    int _z;\r\n}\r\n";

        Assert.AreEqual("class C\n{\r\n    int _x;\r\n\r\n    void M() { }\r\n    int _y;\r\n    int _z;\r\n}\r\n", _converter.Apply(source));
    }

    [TestMethod]
    public void AfterSingleLineField_FollowedByVerbatimStringLines_DoesNotChangeTheString()
    {
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterFieldsSingleLine = true;
        string source = "class C\r\n{\r\n    int a; string b = @\"x\r\ny\";\r\n}\r\n";

        Assert.AreEqual(source, _converter.Apply(source));
    }
}
