using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Transformations;

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
    public void Apply_NotEqualsNull_ConvertsToIsNotNull()
    {
        var input = @"
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
        var expected = @"
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

        var result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_EqualsEqualsNull_ConvertsToIsNull()
    {
        var input = @"
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
        var expected = @"
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

        var result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_ReversedNullChecks_ConvertsProperly()
    {
        var input = @"
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
        var expected = @"
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

        var result = _converter.Apply(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_TernaryAndReturnExpressions_ConvertsProperly()
    {
        var input = @"
public class C
{
    public bool Check(object x, object y)
    {
        var flag = x != null ? true : false;
        return y == null;
    }
}";
        var expected = @"
public class C
{
    public bool Check(object x, object y)
    {
        var flag = x is not null ? true : false;
        return y is null;
    }
}";

        var result = _converter.Apply(input);

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
        var input = @"
using System.Collections.Generic;
using System.Linq;

class C
{
    void M(IEnumerable<string> items)
    {
        var found = items.Any(x => x == null);
    }
}";

        var actual = _converter.Apply(input);

        // Deliberately conservative: this lambda has no semantic model to confirm its delegate type,
        // so it could still be bound to Expression<Func<T, bool>> (e.g. IQueryable .Where/.Any), which
        // would fail to compile with CS8122 if rewritten to `is null`.
        StringAssert.Contains(actual, "x == null");
        Assert.IsFalse(actual.Contains("x is null"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_NotEqualsNullInsideExpressionBodiedLambda_LeavesUnchanged()
    {
        var input = @"
using System.Collections.Generic;
using System.Linq;

class C
{
    void M(IEnumerable<string> items)
    {
        var found = items.Any(x => x != null);
    }
}";

        var actual = _converter.Apply(input);

        StringAssert.Contains(actual, "x != null");
        Assert.IsFalse(actual.Contains("is not null"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_EqualsNullInsideBlockBodiedLambda_ConvertsToIsNull()
    {
        var input = @"
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

        var actual = _converter.Apply(input);

        // A block-bodied lambda can never be compiled to an expression tree (CS0834), so this is
        // always safe to convert.
        StringAssert.Contains(actual, "x is null");
        Assert.IsFalse(actual.Contains("== null"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_EqualsNullInsideAsyncLambda_ConvertsToIsNull()
    {
        var input = @"
using System;
using System.Threading.Tasks;

class C
{
    void M()
    {
        Func<string, Task<bool>> f = async x => await Task.FromResult(x == null);
    }
}";

        var actual = _converter.Apply(input);

        // An async lambda can never be compiled to an expression tree (CS1989), so this is always
        // safe to convert.
        StringAssert.Contains(actual, "x is null");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_EqualsNullInsideAnonymousMethod_ConvertsToIsNull()
    {
        var input = @"
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

        var actual = _converter.Apply(input);

        // An anonymous method can never be compiled to an expression tree (CS1946), so this is
        // always safe to convert.
        StringAssert.Contains(actual, "x is null");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Apply_EqualsNullInQueryWhereClause_LeavesUnchanged()
    {
        var input = @"
using System.Collections.Generic;
using System.Linq;

class C
{
    void M(IEnumerable<string> items)
    {
        var result = from x in items where x == null select x;
    }
}";

        var actual = _converter.Apply(input);

        // Deliberately conservative: a query clause's condition cannot rule out an IQueryable
        // source being translated to an expression tree, so it is never rewritten.
        StringAssert.Contains(actual, "x == null");
        Assert.IsFalse(actual.Contains("x is null"));
    }
}
