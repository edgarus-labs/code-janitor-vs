using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Transformations;
using System.Collections.Generic;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="NullCheckPatternMatchingConverter" />.
/// </summary>
[TestClass]
public sealed class NullCheckPatternMatchingConverterTests
{
    private NullCheckPatternMatchingConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new NullCheckPatternMatchingConverter();
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Name_IsNotEmpty()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(_converter.Name));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task Apply_CompilesWhereLambdasMayBecomeExpressionTrees_AndConvertsElsewhere()
    {
        // An 'is' pattern is not allowed in an expression tree (CS8122); only syntax that can never become one is converted.
        string input =
            "using System;\r\nusing System.Linq;\r\nusing System.Linq.Expressions;\r\n\r\n" +
            "class Item { public string Name; }\r\n\r\n" +
            "class C\r\n{\r\n" +
            "    Expression<Func<Item, bool>> tree = item => item.Name != null;\r\n" +
            "    Expression<Func<Item, object>> projection = item => new { Missing = item.Name == null };\r\n" +
            "    IQueryable<Item> Query(IQueryable<Item> items) => items.Where(item => item.Name != null);\r\n" +
            "    IQueryable<Item> Syntax(IQueryable<Item> items) => from item in items where item.Name == null select item;\r\n" +
            "    Func<Item, bool> Block => item => { return item.Name != null; };\r\n" +
            "    bool Method(Item item) => item.Name == null;\r\n" +
            "}\r\n";
        Document document = CompilingTestProject.CreateDocument(
            input,
            LanguageVersion.CSharp9,
            new[] { MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location) });

        string output = _converter.Apply(input);

        IReadOnlyList<string> errors = await CompilingTestProject.GetCompileErrorsAsync(document, output);
        Assert.IsEmpty(errors, output + "\r\n" + string.Join("\r\n", errors));
        Assert.Contains("item => item.Name != null;", output);
        Assert.Contains("Missing = item.Name == null", output);
        Assert.Contains("items.Where(item => item.Name != null)", output);
        Assert.Contains("where item.Name == null", output);
        Assert.Contains("{ return item.Name is not null; }", output);
        Assert.Contains("bool Method(Item item) => item.Name is null;", output);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_NotEqualsNull_ConvertsToIsNotNull()
    {
        string input = @"
public class C
{
    public void M(object x)
    {
        if (x != null)
        {
            DoWork();
        }
    }
}";
        string expected = @"
public class C
{
    public void M(object x)
    {
        if (x is not null)
        {
            DoWork();
        }
    }
}";

        string result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_EqualsEqualsNull_ConvertsToIsNull()
    {
        string input = @"
public class C
{
    public void M(object x)
    {
        if (x == null)
        {
            return;
        }
    }
}";
        string expected = @"
public class C
{
    public void M(object x)
    {
        if (x is null)
        {
            return;
        }
    }
}";

        string result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_ReversedNullChecks_ConvertsProperly()
    {
        string input = @"
public class C
{
    public void M(object a, object b)
    {
        if (null != a && null == b)
        {
            DoWork();
        }
    }
}";
        string expected = @"
public class C
{
    public void M(object a, object b)
    {
        if (a is not null && b is null)
        {
            DoWork();
        }
    }
}";

        string result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_TernaryAndReturnExpressions_ConvertsProperly()
    {
        string input = @"
public class C
{
    public bool Check(object x, object y)
    {
        var flag = x != null ? true : false;
        return y == null;
    }
}";
        string expected = @"
public class C
{
    public bool Check(object x, object y)
    {
        var flag = x is not null ? true : false;
        return y is null;
    }
}";

        string result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_NullOrEmpty_ReturnsOriginal()
    {
        Assert.IsNull(_converter.Apply(null));
        Assert.AreEqual(string.Empty, _converter.Apply(string.Empty));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_EqualsNullInsideExpressionBodiedLambda_LeavesUnchanged()
    {
        string input = @"
using System.Collections.Generic;
using System.Linq;

class C
{
    void M(IEnumerable<string> items)
    {
        var found = items.Any(x => x == null);
    }
}";

        string actual = _converter.Apply(input);

        // Deliberately conservative: this lambda has no semantic model to confirm its delegate type,
        // so it could still be bound to Expression<Func<T, bool>> (e.g. IQueryable .Where/.Any), which
        // would fail to compile with CS8122 if rewritten to `is null`.
        Assert.Contains("x == null", actual);
        Assert.DoesNotContain("x is null", actual);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_NotEqualsNullInsideExpressionBodiedLambda_LeavesUnchanged()
    {
        string input = @"
using System.Collections.Generic;
using System.Linq;

class C
{
    void M(IEnumerable<string> items)
    {
        var found = items.Any(x => x != null);
    }
}";

        string actual = _converter.Apply(input);

        Assert.Contains("x != null", actual);
        Assert.DoesNotContain("is not null", actual);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_EqualsNullInsideBlockBodiedLambda_ConvertsToIsNull()
    {
        string input = @"
using System.Collections.Generic;
using System.Linq;

class C
{
    void M(IEnumerable<string> items)
    {
        var found = items.Any(x =>
        {
            return x == null;
        });
    }
}";

        string actual = _converter.Apply(input);

        // A block-bodied lambda can never be compiled to an expression tree (CS0834), so this is
        // always safe to convert.
        Assert.Contains("x is null", actual);
        Assert.DoesNotContain("== null", actual);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_EqualsNullInsideAsyncLambda_ConvertsToIsNull()
    {
        string input = @"
using System;
using System.Threading.Tasks;

class C
{
    void M()
    {
        Func<string, Task<bool>> f = async x => await Task.FromResult(x == null);
    }
}";

        string actual = _converter.Apply(input);

        // An async lambda can never be compiled to an expression tree (CS1989), so this is always
        // safe to convert.
        Assert.Contains("x is null", actual);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_EqualsNullInsideAnonymousMethod_ConvertsToIsNull()
    {
        string input = @"
using System;

class C
{
    void M()
    {
        Func<string, bool> f = delegate (string x)
        {
            return x == null;
        };
    }
}";

        string actual = _converter.Apply(input);

        // An anonymous method can never be compiled to an expression tree (CS1946), so this is
        // always safe to convert.
        Assert.Contains("x is null", actual);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_EqualsNullInQueryWhereClause_LeavesUnchanged()
    {
        string input = @"
using System.Collections.Generic;
using System.Linq;

class C
{
    void M(IEnumerable<string> items)
    {
        var result = from x in items where x == null select x;
    }
}";

        string actual = _converter.Apply(input);

        // Deliberately conservative: a query clause's condition cannot rule out an IQueryable
        // source being translated to an expression tree, so it is never rewritten.
        Assert.Contains("x == null", actual);
        Assert.DoesNotContain("x is null", actual);
    }
}
