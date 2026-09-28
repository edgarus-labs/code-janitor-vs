using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;
using CodeJanitor.UnitTests.Transformations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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

    [TestMethod]
    public void Name_DescribesTheCleanup()
    {
        Assert.AreEqual("Update single-line methods", _converter.Name);
    }

    [TestMethod]
    [DataRow("class C\r\n{\r\n    int M() => 1;\r\n}\r\n", DisplayName = "expression-bodied method")]
    [DataRow("class C\r\n{\r\n    void M() { }\r\n}\r\n", DisplayName = "empty body")]
    [DataRow("class C\r\n{\r\n    void M() { Foo(1,\r\n        2); }\r\n}\r\n", DisplayName = "statement spanning two lines")]
    [DataRow("using System;\r\nConsole.WriteLine(Local());\r\nstatic int Local() { return 1; }\r\n", DisplayName = "top-level statements with a single-line local function")]
    [DataRow("class C\r\n{\r\n    void M()\r\n    {\r\n        Run(() => { A(); });\r\n    }\r\n}\r\n", DisplayName = "single-line lambda inside a multi-line method")]
    [DataRow("class C\r\n{\r\n#if NEVER_DEFINED\r\n    void M() { A(); }\r\n#endif\r\n}\r\n", DisplayName = "disabled preprocessor branch")]
    [DataRow("partial class C\r\n{\r\n    partial void M();\r\n}\r\n", DisplayName = "partial method declaration")]
    [DataRow("class C\r\n{\r\n    extern void M();\r\n}\r\n", DisplayName = "extern method")]
    public void MethodsThatAreNotSingleLineBlockBodies_AreUnchanged(string source)
    {
        Assert.AreEqual(source, _converter.Apply(source));
    }

    [TestMethod]
    public async Task SingleLineMethods_AreSpreadSoEveryStatementHasItsOwnLine_AndTheFileStillCompiles()
    {
        string source =
            "class C\r\n{\r\n    int _x;\r\n    int Get() { return _x; }\r\n" +
            "    void Set(int v) { _x = v; Log(v); }\r\n    void Log(int v) { }\r\n}\r\n";

        string result = _converter.Apply(source);

        IReadOnlyList<string> errors = await CompilingTestProject.GetCompileErrorsAsync(CompilingTestProject.CreateDocument(source), result);
        Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors));
        SyntaxTree tree = CSharpSyntaxTree.ParseText(result);
        Dictionary<string, MethodDeclarationSyntax> methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .ToDictionary(method => method.Identifier.Text);
        CollectionAssert.AreEqual(new[] { "return _x;" }, methods["Get"].Body.Statements.Select(s => s.ToString()).ToArray());
        CollectionAssert.AreEqual(new[] { "_x = v;", "Log(v);" }, methods["Set"].Body.Statements.Select(s => s.ToString()).ToArray());
        foreach (string name in new[] { "Get", "Set" })
        {
            BlockSyntax body = methods[name].Body;
            int[] lines = new[] { body.OpenBraceToken.Span }
                .Concat(body.Statements.Select(statement => statement.Span))
                .Concat(new[] { body.CloseBraceToken.Span })
                .Select(span => tree.GetLineSpan(span).StartLinePosition.Line)
                .ToArray();
            Assert.AreEqual(lines.Length, lines.Distinct().Count(), name + " still has several parts on one line:\r\n" + result);
        }

        Assert.Contains("void Log(int v) { }", result);
    }

    [TestMethod]
    [DataRow(
        "class C\r\n{\r\n    int M() { return 1; }\r\n}\r\n",
        "class C\r\n{\r\n    int M()\r\n    {\r\n        return 1;\r\n    }\r\n}\r\n",
        DisplayName = "CRLF file")]
    [DataRow(
        "class C\n{\n    int M() { return 1; }\n    void N() { }\n}\n",
        "class C\n{\n    int M()\n    {\n        return 1;\n    }\n    void N() { }\n}\n",
        DisplayName = "LF file with a following member")]
    [DataRow(
        "class C\r\n{\r\n    void M() { A(); /* why */ }\r\n}\r\n",
        "class C\r\n{\r\n    void M()\r\n    {\r\n        A(); /* why */\r\n    }\r\n}\r\n",
        DisplayName = "comment inside the body")]
    [DataRow(
        "class C\n{\n\tvoid M() { A(); B(); }\n}\n",
        "class C\n{\n\tvoid M()\n\t{\n\t\tA();\n\t\tB();\n\t}\n}\n",
        DisplayName = "tab indentation")]
    [DataRow(
        "namespace N\n{\n    class C\n    {\n        void M()\n        { A(); }\n    }\n}\n",
        "namespace N\n{\n    class C\n    {\n        void M()\n        {\n            A();\n        }\n    }\n}\n",
        DisplayName = "opening brace already on its own line")]
    [DataRow(
        "class C { void M() { A(); } }",
        "class C { void M()\n{\n    A();\n} }",
        DisplayName = "file without line breaks")]
    [DataRow(
        "class C\r\n{\r\n    int M(int a,\r\n          int b) { return a; }\r\n}\r\n",
        "class C\r\n{\r\n    int M(int a,\r\n          int b)\r\n    {\r\n        return a;\r\n    }\r\n}\r\n",
        DisplayName = "parameters wrapped onto a continuation line")]
    [DataRow(
        "class C\r\n{\r\n    T M<T>()\r\n        where T : new() { return new T(); }\r\n}\r\n",
        "class C\r\n{\r\n    T M<T>()\r\n        where T : new()\r\n    {\r\n        return new T();\r\n    }\r\n}\r\n",
        DisplayName = "constraint clause on a continuation line")]
    public void SingleLineMethod_IsSpreadWithTheFileIndentationAndLineBreaks(string source, string expected)
    {
        Assert.AreEqual(expected, _converter.Apply(source));
    }
}
