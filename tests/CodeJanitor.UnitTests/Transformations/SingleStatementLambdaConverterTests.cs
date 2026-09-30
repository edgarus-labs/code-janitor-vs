using CodeJanitor.Logic.Transformations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="SingleStatementLambdaConverter" />.
/// </summary>
[TestClass]
public sealed class SingleStatementLambdaConverterTests
{
    private ISourceTransformation _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new SingleStatementLambdaConverter();
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SimplifiesParenthesizedLambdaWithReturnStatement()
    {
        string input = "using System.Text.Json; class C { Func<object,string> f = source => { return JsonSerializer.Serialize(source, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }); }; }";
        string expected = "using System.Text.Json; class C { Func<object,string> f = source => JsonSerializer.Serialize(source, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }); }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SimplifiesLambdaWithSingleExpressionStatement()
    {
        string input = "class C { Action a = () => { DoWork(); }; void DoWork(){} }";
        string expected = "class C { Action a = () => DoWork(); void DoWork(){} }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsLambdaWithMultipleStatements()
    {
        string input = "class C { Func<int,int> f = x => { Log(x); return x + 1; }; void Log(int _){} }";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsLambdaWithBareReturn()
    {
        string input = "class C { Action a = () => { return; }; }";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SimplifiesAnonymousDelegateWithReturnStatement()
    {
        string input = "using System; class C { Func<int, int> f = delegate(int x) { return x + 1; }; }";
        string expected = "using System; class C { Func<int, int> f = (int x) => x + 1; }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SimplifiesParameterlessAnonymousDelegateWithExpressionStatement()
    {
        string input = "using System; class C { Action a = delegate { DoWork(); }; void DoWork(){} }";
        string expected = "using System; class C { Action a = () => DoWork(); void DoWork(){} }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NameAndNullOrEmpty_HandledCorrectly()
    {
        Assert.AreEqual("Single Statement Lambda", _converter.Name);
        Assert.IsNull(_converter.Apply(null));
        Assert.AreEqual(string.Empty, _converter.Apply(string.Empty));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SimplifiesAsyncAnonymousDelegate()
    {
        string input = "using System; using System.Threading.Tasks; class C { Func<Task<int>> f = async delegate { return await Task.FromResult(1); }; }";
        string expected = "using System; using System.Threading.Tasks; class C { Func<Task<int>> f = async () => await Task.FromResult(1); }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void AnonymousDelegateWithMultipleStatements_NotSimplified()
    {
        string input = "using System; class C { Action a = delegate { Log(); DoWork(); }; void Log(){} void DoWork(){} }";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { Action a = delegate /* keep */ { Foo(); }; static void Foo() { } }", DisplayName = "comment after the delegate keyword")]
    [DataRow("class C { Action<int> a = delegate /* keep */ (int x) { Foo(); }; static void Foo() { } }", DisplayName = "comment after the delegate keyword before parameters")]
    public void AnonymousMethodWithCommentInItsHeader_IsUnchanged(string input)
    {
        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { Action a = static delegate { Foo(); }; static void Foo() { } }", "class C { Action a = static () => Foo(); static void Foo() { } }", DisplayName = "static anonymous method")]
    [DataRow("class C { Func<int, int> f = static delegate (int x) { return x; }; }", "class C { Func<int, int> f = static (int x) => x; }", DisplayName = "static anonymous method with parameters")]
    [DataRow("class C { Func<Task> f = static async delegate { await Task.Delay(1); }; }", "class C { Func<Task> f = static async () => await Task.Delay(1); }", DisplayName = "static async anonymous method")]
    [DataRow("class C { Func<Task> f = async static delegate { await Task.Delay(1); }; }", "class C { Func<Task> f = async static () => await Task.Delay(1); }", DisplayName = "async static anonymous method")]
    public void StaticAnonymousMethod_KeepsItsModifiers(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow(
        "class C\r\n{\r\n    Func<int, int> f = delegate (int x)\r\n    {\r\n        return x;\r\n    };\r\n}",
        "class C\r\n{\r\n    Func<int, int> f = (int x) =>\r\n    x;\r\n}",
        DisplayName = "with parameters")]
    [DataRow(
        "class C\r\n{\r\n    Action a = delegate\r\n    {\r\n        Foo();\r\n    };\r\n    static void Foo() { }\r\n}",
        "class C\r\n{\r\n    Action a = () =>\r\n    Foo();\r\n    static void Foo() { }\r\n}",
        DisplayName = "without parameters")]
    public void AllmanAnonymousMethod_KeepsTheArrowOnTheHeaderLine(string input, string expected)
    {
        string result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
        Assert.AreEqual(expected, _converter.Apply(result), "The conversion must be idempotent.");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("  \r\n\t", DisplayName = "whitespace only")]
    [DataRow("class C { Func<int, int> f = x => x + 1; Action a = () => DoWork(); }", DisplayName = "already expression-bodied")]
    [DataRow("class C { Action a = () => { }; Action b = delegate { }; }", DisplayName = "empty block")]
    [DataRow("class C { Action a = () => { throw new Exception(); }; }", DisplayName = "throw statement")]
    [DataRow("class C { Action<int> a = x => { if (x > 0) DoWork(); }; }", DisplayName = "if statement")]
    [DataRow("class C { Action a = () => { int y = 1; }; }", DisplayName = "local declaration")]
    [DataRow("class C { Action a = () => { { DoWork(); } }; }", DisplayName = "nested block only")]
    [DataRow("class C { Action a = delegate { return; }; }", DisplayName = "anonymous method with bare return")]
    [DataRow("class C { void M() { DoWork(); } int P { get { return 1; } } }", DisplayName = "method and accessor blocks")]
    [DataRow("class C { void M() { void L() { DoWork(); } } }", DisplayName = "local function block")]
    [DataRow("class C { string S = \"() => { return 1; }\"; }", DisplayName = "code-like text in literal")]
    public void BodyThatIsNotASingleExpression_IsUnchanged(string input)
    {
        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { int _total; Action<int> a = x => { _total = x; }; }", "class C { int _total; Action<int> a = x => _total = x; }", DisplayName = "assignment statement")]
    [DataRow("class C { int _n; Action a = () => { _n++; }; }", "class C { int _n; Action a = () => _n++; }", DisplayName = "increment statement")]
    [DataRow("class C { Func<int, int, int> f = (int a, int b) => { return a * b; }; }", "class C { Func<int, int, int> f = (int a, int b) => a * b; }", DisplayName = "explicitly typed parameters")]
    [DataRow("class C { Func<int, int, int> f = (_, _) => { return 0; }; }", "class C { Func<int, int, int> f = (_, _) => 0; }", DisplayName = "discard parameters")]
    [DataRow("class C { Func<int, int> f = static x => { return x; }; }", "class C { Func<int, int> f = static x => x; }", DisplayName = "static lambda")]
    [DataRow("class C { Func<Task> f = async () => { await Task.Delay(1); }; }", "class C { Func<Task> f = async () => await Task.Delay(1); }", DisplayName = "async parenthesized lambda")]
    [DataRow("class C { Func<int, Task<int>> f = async x => { return await Task.FromResult(x); }; }", "class C { Func<int, Task<int>> f = async x => await Task.FromResult(x); }", DisplayName = "async simple lambda")]
    [DataRow("class C { Func<int, Func<int, int>> f = x => { return y => { return x + y; }; }; }", "class C { Func<int, Func<int, int>> f = x => y => x + y; }", DisplayName = "nested lambdas")]
    [DataRow("class C { Func<int, int, int> f = delegate (int a, int b) { return a - b; }; }", "class C { Func<int, int, int> f = (int a, int b) => a - b; }", DisplayName = "anonymous method with parameters")]
    [DataRow("class C { Func<int, string> f = x => { return $\"{x} => {{ }}\"; }; }", "class C { Func<int, string> f = x => $\"{x} => {{ }}\"; }", DisplayName = "interpolated string with braces")]
    [DataRow("class C { Func<object, bool> f = o => { return o is string { Length: > 0 }; }; }", "class C { Func<object, bool> f = o => o is string { Length: > 0 }; }", DisplayName = "property pattern")]
    public void SingleExpressionBody_BecomesExpressionBodied(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TopLevelStatementsFile_SimplifiesLambdasWithAFixedTargetType()
    {
        string input =
            "using System;\r\n" +
            "\r\n" +
            "Func<int, int> twice = x => { return x * 2; };\r\n" +
            "Console.WriteLine(Apply(3, n => { return n + 1; }));\r\n" +
            "\r\n" +
            "static int Apply(int value, Func<int, int> f) => f(value);\r\n" +
            "\r\n" +
            "class Handlers\r\n" +
            "{\r\n" +
            "    public Action Log = () => { Console.WriteLine(); };\r\n" +
            "}\r\n";
        // The lambda passed to Apply is left alone: an argument's target type depends on overload resolution.
        string expected = input
            .Replace("x => { return x * 2; }", "x => x * 2")
            .Replace("() => { Console.WriteLine(); }", "() => Console.WriteLine()");

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("\r\n", DisplayName = "CRLF")]
    [DataRow("\n", DisplayName = "LF")]
    public void TriviaOutsideTheLambda_IsPreserved(string newLine)
    {
        string input =
            "class C" + newLine +
            "{" + newLine +
            "\t/// <summary>Doubles.</summary>" + newLine +
            "\tFunc<int, int> f = /*a*/ x => { return x * 2; } /*b*/; // trailing" + newLine +
            "#if NEVER" + newLine +
            "\tFunc<int, int> g = x => { return x; };" + newLine +
            "#endif" + newLine +
            "}" + newLine;
        string expected = input.Replace("/*a*/ x => { return x * 2; } /*b*/", "/*a*/ x => x * 2 /*b*/");

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FileWithSyntaxErrors_SimplifiesTheValidLambdaAndKeepsTheRestVerbatim()
    {
        string input = "class C { Func<int, int> f = x => { return x; }; void N( { int y = ; } }";
        string expected = "class C { Func<int, int> f = x => x; void N( { int y = ; } }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async System.Threading.Tasks.Task SimplifiedLambdas_CompileWithoutNewErrors()
    {
        string input =
            "using System;\r\n" +
            "using System.Threading.Tasks;\r\n" +
            "class C\r\n" +
            "{\r\n" +
            "    private int _count;\r\n" +
            "    public Func<int, int> Twice = x => { return x * 2; };\r\n" +
            "    public Func<int, Task<int>> Async = async x => { return await Task.FromResult(x); };\r\n" +
            "    public Func<int, int, int> Sub = delegate (int a, int b) { return a - b; };\r\n" +
            "    public Func<int> One = delegate { return 1; };\r\n" +
            "    public EventHandler Handler = delegate { GC.Collect(); };\r\n" +
            "    public Task Run() => Task.Run(() => { Twice(1); });\r\n" +
            "    public Action Increment() => () => { _count++; };\r\n" +
            "    public Action<int> Set() => value => { _count = value; };\r\n" +
            "}\r\n";
        Microsoft.CodeAnalysis.Document document = CompilingTestProject.CreateDocument(input);
        string expected = input
            .Replace("x => { return x * 2; }", "x => x * 2")
            .Replace("async x => { return await Task.FromResult(x); }", "async x => await Task.FromResult(x)")
            .Replace("delegate (int a, int b) { return a - b; }", "(int a, int b) => a - b")
            .Replace("delegate { return 1; }", "() => 1")
            .Replace("() => { _count++; }", "() => _count++")
            .Replace("value => { _count = value; }", "value => _count = value");

        string output = _converter.Apply(input);

        Assert.AreEqual(expected, output);
        Assert.IsEmpty(await CompilingTestProject.GetCompileErrorsAsync(document, output));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { System.EventHandler h = delegate { System.Console.WriteLine(); }; }", DisplayName = "delegate type with parameters")]
    [DataRow("class C { void M(Button b) { b.Click += delegate { Log(); }; } }", DisplayName = "event subscription")]
    [DataRow("class C { System.Func<int, int> f = delegate { return 1; }; }", DisplayName = "Func with a parameter")]
    [DataRow("class C { MyHandler h = delegate { Log(); }; }", DisplayName = "unknown delegate type")]
    public void ParameterlessAnonymousMethodWhoseDelegateMayTakeParameters_IsUnchanged(string input)
    {
        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { System.Action a = delegate { Log(); }; }", "class C { System.Action a = () => Log(); }", DisplayName = "qualified Action")]
    [DataRow("class C { void M() { Func<int> f = delegate { return 1; }; } }", "class C { void M() { Func<int> f = () => 1; } }", DisplayName = "local Func without parameters")]
    public void ParameterlessAnonymousMethodForAParameterlessDelegate_BecomesLambda(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("using System.Threading.Tasks; class C { void M() { Task.Run(() => { F(); }); } }", DisplayName = "overload may switch from Action to Func<Task>")]
    [DataRow("class C { void M(System.Linq.IQueryable<int> q) { var r = q.Where(x => { return x > 1; }); } }", DisplayName = "overload may switch to an expression tree")]
    [DataRow("class C { object M() => new Holder(x => { return x; }); }", DisplayName = "constructor argument")]
    [DataRow("class C { void M() { Run(() => () => { return 1; }); } }", DisplayName = "lambda inside a lambda argument")]
    [DataRow("class C { System.Collections.Generic.List<System.Action> L = new System.Collections.Generic.List<System.Action> { () => { F(); } }; }", DisplayName = "collection initializer element")]
    public void LambdaWhoseTargetTypeDependsOnOverloadResolution_IsUnchanged(string input)
    {
        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { void M() { var a = () => { Foo(); }; } int Foo() => 0; }", DisplayName = "var takes the lambda's natural type")]
    [DataRow("class C { object o = () => { Foo(); }; static int Foo() => 0; }", DisplayName = "object target")]
    [DataRow("class C { System.Delegate d = (int x) => { Foo(x); }; static int Foo(int x) => x; }", DisplayName = "Delegate target")]
    [DataRow("class C { object _o; void M() { _o = () => { Foo(); }; } int Foo() => 0; }", DisplayName = "assignment to a member whose type is not visible")]
    [DataRow("class C { System.Action a = true ? () => { Foo(); } : null; static int Foo() => 0; }", DisplayName = "conditional operator branch")]
    [DataRow("class C { object M() { return () => { Foo(); }; } static int Foo() => 0; }", DisplayName = "return from a method returning object")]
    public void LambdaWithoutAKnownDelegateTargetType_IsUnchanged(string input)
    {
        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { Action P { get; } = () => { F(); }; }", "class C { Action P { get; } = () => F(); }", DisplayName = "property initializer")]
    [DataRow("class C { Action P => () => { F(); }; }", "class C { Action P => () => F(); }", DisplayName = "expression-bodied property")]
    [DataRow("class C { Action P { get => () => { F(); }; } }", "class C { Action P { get => () => F(); } }", DisplayName = "expression-bodied getter")]
    [DataRow("class C { Action M() { return () => { F(); }; } }", "class C { Action M() { return () => F(); } }", DisplayName = "return statement")]
    [DataRow("class C { object o = (System.Action)(() => { F(); }); }", "class C { object o = (System.Action)(() => F()); }", DisplayName = "cast")]
    [DataRow("class C { System.Linq.Expressions.Expression<Func<int, int>> e = x => { return x; }; }", "class C { System.Linq.Expressions.Expression<Func<int, int>> e = x => x; }", DisplayName = "expression tree")]
    [DataRow("class C { Predicate<int>? p = x => { return x > 0; }; }", "class C { Predicate<int>? p = x => x > 0; }", DisplayName = "nullable Predicate")]
    public void LambdaWithAKnownDelegateTargetType_BecomesExpressionBodied(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { Func<int, int> f = x => { // keep\n return x; }; }", DisplayName = "comment after the open brace")]
    [DataRow("class C { Func<int, int> f = x => { return /* why */ x; }; }", DisplayName = "comment after return")]
    [DataRow("class C { Func<int, int> f = x => { return x; /* why */ }; }", DisplayName = "comment before the close brace")]
    [DataRow("class C { Action a = () => {\n#if !NEVER\n Log();\n#endif\n}; }", DisplayName = "preprocessor directives")]
    public void BodyWithCommentsOrDirectives_IsUnchanged(string input)
    {
        Assert.AreEqual(input, _converter.Apply(input));
    }
}
