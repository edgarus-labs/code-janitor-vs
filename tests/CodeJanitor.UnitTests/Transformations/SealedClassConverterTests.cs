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
        string input = "internal class Foo { }";
        string expected = "internal sealed class Foo { }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassWithNoAccessModifier_BecomesSealed()
    {
        string input = "class Foo { }";
        string expected = "sealed class Foo { }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassWithDerivedClassInSameFile_StaysUnsealed()
    {
        string input = "internal class Foo { } internal class Bar : Foo { }";
        string expected = "internal class Foo { } internal sealed class Bar : Foo { }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void AlreadySealedClass_Unchanged()
    {
        string input = "internal sealed class Foo { }";

        Assert.AreEqual(input, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void AbstractClass_Unchanged()
    {
        string input = "internal abstract class Foo { }";

        Assert.AreEqual(input, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void StaticClass_Unchanged()
    {
        string input = "internal static class Foo { }";

        Assert.AreEqual(input, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void PartialClass_Unchanged()
    {
        string input = "internal partial class Foo { }";

        Assert.AreEqual(input, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void PublicClass_BecomesSealed()
    {
        string input = "public class Foo { }";
        string expected = "public sealed class Foo { }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void PublicClassWithDerivedClassInSameFile_BaseStaysUnsealed_DerivedBecomesSealed()
    {
        string input = "public class Animal { } public class Dog : Animal { }";
        string expected = "public class Animal { } public sealed class Dog : Animal { }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void PublicRecordClass_BecomesSealed()
    {
        string input = "public record Person(string Name);";
        string expected = "public sealed record Person(string Name);";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void RecordStruct_Unchanged()
    {
        string input = "public record struct Point(int X, int Y);";

        Assert.AreEqual(input, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassImplementingInterface_BecomesSealed()
    {
        string input = "internal class Foo : System.IDisposable { public void Dispose() { } }";
        string expected = "internal sealed class Foo : System.IDisposable { public void Dispose() { } }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NestedClassIsNotSealed_OnlyOuterConsidered()
    {
        string input = "internal class Outer { internal class Inner { } }";
        string expected = "internal sealed class Outer { internal class Inner { } }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NameAndNullOrEmpty_HandledCorrectly()
    {
        SealedClassConverter converter = new SealedClassConverter();
        Assert.AreEqual("Sealed Class", converter.Name);
        Assert.IsNull(converter.Apply(null));
        Assert.AreEqual(string.Empty, converter.Apply(string.Empty));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void QualifiedAndAliasBaseTypes_IdentifiesBaseClass()
    {
        string input = "class Animal { } class Dog : global::Animal { } class Cat : MyNamespace.Animal { }";
        string expected = "class Animal { } sealed class Dog : global::Animal { } sealed class Cat : MyNamespace.Animal { }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FileScopedNamespace_SealsTopLevelClass()
    {
        string input = "namespace N;\r\nclass Foo { }";
        string expected = "namespace N;\r\nsealed class Foo { }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassWithVirtualProperty_StaysUnsealed()
    {
        string input = "public class Foo : SomeBaseType { public virtual OtherType SomeProperty { get; } }";

        Assert.AreEqual(input, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassWithVirtualMethod_StaysUnsealed()
    {
        string input = "public class Foo { public virtual void DoWork() { } }";

        Assert.AreEqual(input, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassWithVirtualIndexer_StaysUnsealed()
    {
        string input = "public class Foo { public virtual int this[int i] => i; }";

        Assert.AreEqual(input, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassWithVirtualEvent_StaysUnsealed()
    {
        string input = "public class Foo { public virtual event System.EventHandler Changed; }";

        Assert.AreEqual(input, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassUsedInGenericConstraintInSameFile_StaysUnsealed()
    {
        string input = "public class Result { } public class Handler<T> where T : Result { }";
        string expected = "public class Result { } public sealed class Handler<T> where T : Result { }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassWithExternalDerivedTypeOrConstraint_StaysUnsealed()
    {
        string input = "public class Result { }";
        string[] disqualified = ["Result"];

        Assert.AreEqual(input, _converter.SealWhenSafe(input, disqualified));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConverterWithConstructorDisqualifiedTypes_WhenInvokedViaParameterlessSealWhenSafe_PreservesDisqualifiedTypes()
    {
        IClassSealingConverter converter = new SealedClassConverter(new[] { "Result" });
        string input = "public class Result { }";

        Assert.AreEqual(input, converter.SealWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ClassUsedInNullableGenericConstraintInSameFile_StaysUnsealed()
    {
        string input = "public class Result { } public class Handler<T> where T : Result? { }";
        string expected = "public class Result { } public sealed class Handler<T> where T : Result? { }";

        Assert.AreEqual(expected, _converter.SealWhenSafe(input));
    }
}
