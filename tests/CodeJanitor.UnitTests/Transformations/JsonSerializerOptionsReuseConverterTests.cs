using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Transformations;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="JsonSerializerOptionsReuseConverter" />.
/// </summary>
[TestClass]
public sealed class JsonSerializerOptionsReuseConverterTests
{
    private ISourceTransformation _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new JsonSerializerOptionsReuseConverter();
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsDirectOptionsAllocationInJsonSerializerCall()
    {
        string input = "using System.Text.Json; class C { string M(object value) { return JsonSerializer.Serialize(value, new JsonSerializerOptions()); } }";
        string expected = "using System.Text.Json; class C { string M(object value) { return JsonSerializer.Serialize(value, null); } }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsNamedOptionsArgument()
    {
        string input = "using System.Text.Json; class C { string M(object value) { return JsonSerializer.Serialize(value, options: new JsonSerializerOptions()); } }";
        string expected = "using System.Text.Json; class C { string M(object value) { return JsonSerializer.Serialize(value, options: null); } }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConvertsFullyQualifiedJsonSerializerCall()
    {
        string input = "class C { string M(object value) { return System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions()); } }";
        string expected = "class C { string M(object value) { return System.Text.Json.JsonSerializer.Serialize(value, null); } }";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsConfiguredOptionsInitializer()
    {
        string input = "using System.Text.Json; class C { string M(object value) { return JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }); } }";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsOptionsConstructorWithArguments()
    {
        string input = "using System.Text.Json; class C { string M(object value) { return JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)); } }";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SkipsNonJsonSerializerCalls()
    {
        string input = "class C { void M(Foo f) { f.Serialize(new JsonSerializerOptions()); } }";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NameAndNullOrEmpty_HandledCorrectly()
    {
        Assert.AreEqual("CA1869 JsonSerializerOptions Reuse", _converter.Name);
        Assert.IsNull(_converter.Apply(null));
        Assert.AreEqual(string.Empty, _converter.Apply(string.Empty));
    }
}
