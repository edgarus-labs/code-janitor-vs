using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public sealed class ExplicitAccessModifierConverterTests
{
    private ExplicitAccessModifierConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new ExplicitAccessModifierConverter(EffectiveCleanupSettings.For(null));
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnClasses = true;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnDelegates = true;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnEnumerations = true;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnEvents = true;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnFields = true;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnInterfaces = true;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnMethods = true;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnProperties = true;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnStructs = true;
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnClasses = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnDelegates = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnEnumerations = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnEvents = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnFields = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnInterfaces = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnMethods = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnProperties = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnStructs = false;
    }

    // ── Top-level types get 'internal' ────────────────────────────────────────

    [TestMethod]
    public void TopLevelClass_WithoutModifier_GetsInternal()
    {
        string source = "namespace N { class Foo { } }";
        string result = _converter.Apply(source);
        Assert.Contains("internal class Foo", result);
    }

    [TestMethod]
    public void TopLevelInterface_WithoutModifier_GetsInternal()
    {
        string source = "namespace N { interface IFoo { } }";
        string result = _converter.Apply(source);
        Assert.Contains("internal interface IFoo", result);
    }

    [TestMethod]
    public void TopLevelEnum_WithoutModifier_GetsInternal()
    {
        string source = "namespace N { enum Color { Red } }";
        string result = _converter.Apply(source);
        Assert.Contains("internal enum Color", result);
    }

    [TestMethod]
    public void TopLevelStruct_WithoutModifier_GetsInternal()
    {
        string source = "namespace N { struct Point { } }";
        string result = _converter.Apply(source);
        Assert.Contains("internal struct Point", result);
    }

    [TestMethod]
    public void TopLevelDelegate_WithoutModifier_GetsInternal()
    {
        string source = "namespace N { delegate void Work(); }";
        string result = _converter.Apply(source);
        Assert.Contains("internal delegate void Work", result);
    }

    // ── Nested types get 'private' ─────────────────────────────────────────────

    [TestMethod]
    public void NestedClass_WithoutModifier_GetsPrivate()
    {
        string source = "class Outer { class Inner { } }";
        string result = _converter.Apply(source);
        Assert.Contains("private class Inner", result);
    }

    [TestMethod]
    public void NestedEnum_WithoutModifier_GetsPrivate()
    {
        string source = "class Outer { enum State { On } }";
        string result = _converter.Apply(source);
        Assert.Contains("private enum State", result);
    }

    // ── Members get 'private' ──────────────────────────────────────────────────

    [TestMethod]
    public void Field_WithoutModifier_GetsPrivate()
    {
        string source = "class Foo { int _x; }";
        string result = _converter.Apply(source);
        Assert.Contains("private int _x", result);
    }

    [TestMethod]
    public void Method_WithoutModifier_GetsPrivate()
    {
        string source = "class Foo { void Bar() { } }";
        string result = _converter.Apply(source);
        Assert.Contains("private void Bar", result);
    }

    [TestMethod]
    public void Property_WithoutModifier_GetsPrivate()
    {
        string source = "class Foo { int Value { get; set; } }";
        string result = _converter.Apply(source);
        Assert.Contains("private int Value", result);
    }

    [TestMethod]
    public void EventField_WithoutModifier_GetsPrivate()
    {
        string source = "class Foo { event System.Action Done; }";
        string result = _converter.Apply(source);
        Assert.Contains("private event System.Action Done", result);
    }

    [TestMethod]
    public void Constructor_WithoutModifier_GetsPrivate()
    {
        string source = "class Foo { Foo() { } }";
        string result = _converter.Apply(source);
        Assert.Contains("private Foo()", result);
    }

    // ── Already-specified modifiers are left alone ─────────────────────────────

    [TestMethod]
    public void PublicClass_IsNotModified()
    {
        string source = "public class Foo { }";
        string result = _converter.Apply(source);
        Assert.AreEqual(source, result);
    }

    [TestMethod]
    public void PublicField_IsNotModified()
    {
        string source = "class Foo { public int X; }";
        string result = _converter.Apply(source);
        Assert.Contains("public int X", result);
        Assert.DoesNotContain("private int X", result);
        Assert.DoesNotContain("internal int X", result);
    }

    // ── Skipped cases ──────────────────────────────────────────────────────────

    [TestMethod]
    public void PartialClass_IsNotModified()
    {
        string source = "partial class Foo { }";
        string result = _converter.Apply(source);
        Assert.AreEqual(source, result);
    }

    [TestMethod]
    public void PartialMethod_IsNotModified()
    {
        string source = "partial class Foo { partial void Bar(); }";
        string result = _converter.Apply(source);
        // partial method should remain unmodified
        Assert.DoesNotContain("private partial void Bar", result);
    }

    [TestMethod]
    public void StaticConstructor_IsNotModified()
    {
        string source = "class Foo { static Foo() { } }";
        string result = _converter.Apply(source);
        // The class itself gets 'internal'; the static ctor must remain modifier-free.
        Assert.DoesNotContain("private Foo()", result);
        Assert.DoesNotContain("public Foo()", result);
        Assert.DoesNotContain("internal Foo()", result);
    }

    [TestMethod]
    public void Destructor_IsNotModified()
    {
        string source = "class Foo { ~Foo() { } }";
        string result = _converter.Apply(source);
        // The class itself gets 'internal'; the destructor must remain modifier-free.
        Assert.DoesNotContain("private ~Foo", result);
        Assert.DoesNotContain("public ~Foo", result);
        Assert.DoesNotContain("internal ~Foo", result);
    }

    [TestMethod]
    public void InterfaceMember_Method_IsNotModified()
    {
        string source = "interface IFoo { void Bar(); }";
        string result = _converter.Apply(source);
        Assert.DoesNotContain("private void Bar", result);
    }

    [TestMethod]
    public void InterfaceMember_Property_IsNotModified()
    {
        string source = "interface IFoo { int Value { get; } }";
        string result = _converter.Apply(source);
        Assert.DoesNotContain("private int Value", result);
    }

    [TestMethod]
    public void ExplicitInterfaceImpl_Method_IsNotModified()
    {
        string source = "class Foo : IFoo { void IFoo.Bar() { } }";
        string result = _converter.Apply(source);
        Assert.DoesNotContain("private void IFoo.Bar", result);
    }

    [TestMethod]
    public void ExplicitInterfaceImpl_Property_IsNotModified()
    {
        string source = "class Foo : IFoo { int IFoo.Value { get; } }";
        string result = _converter.Apply(source);
        Assert.DoesNotContain("private int IFoo.Value", result);
    }

    [TestMethod]
    public void FixedField_IsNotModified()
    {
        string source = "unsafe class Foo { fixed int buf[8]; }";
        string result = _converter.Apply(source);
        Assert.DoesNotContain("private fixed int buf", result);
    }

    // ── Setting disabled ──────────────────────────────────────────────────────

    [TestMethod]
    public void WhenSettingDisabled_FieldIsNotModified()
    {
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnFields = false;
        string source = "class Foo { int _x; }";
        string result = _converter.Apply(source);
        Assert.DoesNotContain("private int _x", result);
        Assert.DoesNotContain("public int _x", result);
        Assert.Contains("int _x", result);
    }

    [TestMethod]
    public void WhenSettingDisabled_ClassIsNotModified()
    {
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnClasses = false;
        string source = "class Foo { }";
        string result = _converter.Apply(source);
        Assert.AreEqual(source, result);
    }

    // ── Indentation trivia preserved ──────────────────────────────────────────

    [TestMethod]
    public void IndentationOfClass_IsPreserved()
    {
        string source = "namespace N\r\n{\r\n    class Foo { }\r\n}";
        string result = _converter.Apply(source);
        Assert.Contains("    internal class Foo", result, result);
    }

    [TestMethod]
    public void IndentationOfField_IsPreserved()
    {
        string source = "class Foo\r\n{\r\n    int _x;\r\n}";
        string result = _converter.Apply(source);
        Assert.Contains("    private int _x", result, result);
    }

    // ── Record support ─────────────────────────────────────────────────────────

    [TestMethod]
    public void TopLevelRecord_WithoutModifier_GetsInternal()
    {
        string source = "namespace N { record Point(int X, int Y); }";
        string result = _converter.Apply(source);
        Assert.Contains("internal record Point", result);
    }

    [TestMethod]
    public void NestedRecord_WithoutModifier_GetsPrivate()
    {
        string source = "class Outer { record Point(int X, int Y); }";
        string result = _converter.Apply(source);
        Assert.Contains("private record Point", result);
    }

    [TestMethod]
    public void CustomEvent_WithoutModifier_GetsPrivate()
    {
        string source = "class Foo { event System.Action Done { add { } remove { } } }";
        string result = _converter.Apply(source);
        Assert.Contains("private event System.Action Done", result);
    }

    [TestMethod]
    public void NameAndNullOrEmpty_HandledCorrectly()
    {
        Assert.AreEqual("Explicit Access Modifiers", _converter.Name);
        Assert.IsNull(_converter.Apply(null));
        Assert.AreEqual(string.Empty, _converter.Apply(string.Empty));
    }

    [TestMethod]
    public void DisabledSettings_LeaveDeclarationsUnmodified()
    {
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnStructs = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnEnumerations = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnInterfaces = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnDelegates = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnMethods = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnProperties = false;
        Settings.Default.Cleaning_InsertExplicitAccessModifiersOnEvents = false;

        string source = "struct S { } enum E { } interface I { } delegate void D(); class C { void M() { } int P { get; } event System.Action Ev; }";
        string result = _converter.Apply(source);
        Assert.DoesNotContain("internal struct", result);
        Assert.DoesNotContain("internal enum", result);
        Assert.DoesNotContain("internal interface", result);
        Assert.DoesNotContain("internal delegate", result);
        Assert.DoesNotContain("private void M", result);
        Assert.DoesNotContain("private int P", result);
        Assert.DoesNotContain("private event", result);
    }

    [TestMethod]
    public void GenericMethodWithAttributeOnTypeParameter_InsertsModifierWithoutCorruption()
    {
        // No explicit access modifier, so PrependModifier must actually run for both methods -
        // this is the code path Bug 2 corrupted (in the separate EnvDTE-based
        // InsertExplicitAccessModifierLogic; see IsGenericMethodDeclaration for that fix).
        // This test guards the pure-Roslyn ExplicitAccessModifierConverter, which builds the
        // new modifier list from the parsed syntax tree and is structurally immune to Bug 2,
        // but must still insert correctly around generic type parameters, attributes on type
        // parameters, where-clauses, and typeof(T) member access without corruption.
        string source = @"public class Service
{
    static T? Find<[SomeAttribute] T>(System.Guid id)
        where T : SomeBaseType =>
        GetAll<T>().FirstOrDefault(item => item.Id == id);

    void Inspect<T>()
    {
        var fields = typeof(T).GetFields();
    }
}";
        string result = _converter.Apply(source);
        Assert.Contains("private static T? Find<[SomeAttribute] T>", result);
        Assert.Contains("private void Inspect<T>", result);
        Assert.Contains("where T : SomeBaseType", result);
        Assert.Contains("typeof(T).GetFields()", result);
        Assert.DoesNotContain("private readonly (", result);
        Assert.DoesNotContain("private SomeBaseType", result);
        Assert.DoesNotContain("private GetFields", result);
    }
}
