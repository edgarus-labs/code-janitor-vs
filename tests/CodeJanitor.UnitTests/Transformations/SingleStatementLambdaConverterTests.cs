using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Transformations;

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
}
