using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Transformations;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="SealedClassConverter" />.
/// Policy (ADR-0007): sealing is opt-in and API-changing (CA1852-style), so scope is limited
/// to top-level classes that are not public/protected (i.e. cannot be inherited outside the
/// assembly), not already sealed/abstract/static/partial, and provably not derived from by any
/// other type declared in the same file. This is a single-file heuristic: it cannot see
/// derived types declared in other files of the same assembly, so it remains an explicit
/// opt-in setting.
/// </summary>

[TestClass]
public sealed class SealedClassConverterTests
{
    private IClassSealingConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new SealedClassConverter();
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void InternalClassWithNoDerivedTypeInFile_BecomesSealed()
    {
        var input = "internal class Foo { }";
        var expected = "internal sealed class Foo { }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassWithNoAccessModifier_BecomesSealed()
    {
        var input = "class Foo { }";
        var expected = "sealed class Foo { }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassWithDerivedClassInSameFile_StaysUnsealed()
    {
        var input = "internal class Foo { } internal class Bar : Foo { }";
        var expected = "internal class Foo { } internal sealed class Bar : Foo { }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void AlreadySealedClass_Unchanged()
    {
        var input = "internal sealed class Foo { }";

        Assert.AreEqual(input, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void AbstractClass_Unchanged()
    {
        var input = "internal abstract class Foo { }";

        Assert.AreEqual(input, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void StaticClass_Unchanged()
    {
        var input = "internal static class Foo { }";

        Assert.AreEqual(input, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void PartialClass_Unchanged()
    {
        var input = "internal partial class Foo { }";

        Assert.AreEqual(input, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void PublicClass_BecomesSealed()
    {
        var input = "public class Foo { }";
        var expected = "public sealed class Foo { }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void PublicClassWithDerivedClassInSameFile_BaseStaysUnsealed_DerivedBecomesSealed()
    {
        var input = "public class Animal { } public class Dog : Animal { }";
        var expected = "public class Animal { } public sealed class Dog : Animal { }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void PublicRecordClass_BecomesSealed()
    {
        var input = "public record Person(string Name);";
        var expected = "public sealed record Person(string Name);";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void RecordStruct_Unchanged()
    {
        var input = "public record struct Point(int X, int Y);";

        Assert.AreEqual(input, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassImplementingInterface_BecomesSealed()
    {
        var input = "internal class Foo : System.IDisposable { public void Dispose() { } }";
        var expected = "internal sealed class Foo : System.IDisposable { public void Dispose() { } }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NestedClassIsNotSealed_OnlyOuterConsidered()
    {
        var input = "internal class Outer { internal class Inner { } }";
        var expected = "internal sealed class Outer { internal class Inner { } }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NameAndNullOrEmpty_HandledCorrectly()
    {
        var converter = new SealedClassConverter();
        Assert.AreEqual("Sealed Class", converter.Name);
        Assert.IsNull(converter.Apply(null));
        Assert.AreEqual(string.Empty, converter.Apply(string.Empty));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void QualifiedAndAliasBaseTypes_IdentifiesBaseClass()
    {
        var input = "class Animal { } class Dog : global::Animal { } class Cat : MyNamespace.Animal { }";
        var expected = "class Animal { } sealed class Dog : global::Animal { } sealed class Cat : MyNamespace.Animal { }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FileScopedNamespace_SealsTopLevelClass()
    {
        var input = "namespace N;\r\nclass Foo { }";
        var expected = "namespace N;\r\nsealed class Foo { }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassWithVirtualProperty_StaysUnsealed()
    {
        var input = "public class Foo : SomeBaseType { public virtual OtherType SomeProperty { get; } }";

        Assert.AreEqual(input, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassWithVirtualMethod_StaysUnsealed()
    {
        var input = "public class Foo { public virtual void DoWork() { } }";

        Assert.AreEqual(input, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassWithVirtualIndexer_StaysUnsealed()
    {
        var input = "public class Foo { public virtual int this[int i] => i; }";

        Assert.AreEqual(input, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassWithVirtualEvent_StaysUnsealed()
    {
        var input = "public class Foo { public virtual event System.EventHandler Changed; }";

        Assert.AreEqual(input, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassUsedInGenericConstraintInSameFile_StaysUnsealed()
    {
        var input = "public class Result { } public class Handler<T> where T : Result { }";
        var expected = "public class Result { } public sealed class Handler<T> where T : Result { }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassWithExternalDerivedTypeOrConstraint_StaysUnsealed()
    {
        var input = "public class Result { }";
        var disqualified = new[] { "Result" };

        Assert.AreEqual(input, _converter.SealWhenSafe(input, disqualified));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConverterWithConstructorDisqualifiedTypes_WhenInvokedViaParameterlessSealWhenSafe_PreservesDisqualifiedTypes()
    {
        IClassSealingConverter converter = new SealedClassConverter(new[] { "Result" });
        var input = "public class Result { }";

        Assert.AreEqual(input, converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassUsedInNullableGenericConstraintInSameFile_StaysUnsealed()
    {
        var input = "public class Result { } public class Handler<T> where T : Result? { }";
        var expected = "public class Result { } public sealed class Handler<T> where T : Result? { }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }
}
