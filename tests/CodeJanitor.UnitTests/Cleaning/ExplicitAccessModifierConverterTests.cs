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
    [DataRow("partial class C { partial int this[int i] { get; } }", DisplayName = "partial indexer")]
    [DataRow("partial class C { partial int Value { get; } }", DisplayName = "partial property")]
    [DataRow("partial class C { partial event System.EventHandler Changed; }", DisplayName = "partial event declaration")]
    [DataRow("partial class C { partial event System.EventHandler Changed { add { } remove { } } }", DisplayName = "partial event implementation")]
    [DataRow("partial class C { partial C(); }", DisplayName = "partial constructor")]
    public void PartialMember_IsNotModified(string source)
    {
        // Both parts of a partial member must declare the same accessibility (CS8799): the other part may be in a file
        // that is not cleaned, or generated.
        Assert.AreEqual(source, _converter.Apply(source));
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
    [DataRow(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnClasses), "internal record struct P;", DisplayName = "classes disabled: the record struct is still a struct")]
    [DataRow(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnStructs), "record struct P;", DisplayName = "structs disabled: the record struct stays as it is")]
    public void RecordStruct_FollowsTheStructSetting(string disabledSetting, string expected)
    {
        Settings.Default[disabledSetting] = false;

        Assert.AreEqual(expected, _converter.Apply("record struct P;"));
    }

    [TestMethod]
    [DataRow(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnClasses), "record R;", DisplayName = "classes disabled: the record class stays as it is")]
    [DataRow(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnStructs), "internal record R;", DisplayName = "structs disabled: the record class is still a class")]
    public void RecordClass_FollowsTheClassSetting(string disabledSetting, string expected)
    {
        Settings.Default[disabledSetting] = false;

        Assert.AreEqual(expected, _converter.Apply("record R;"));
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

    [TestMethod]
    [DataRow(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnClasses), "class C { } record R;", DisplayName = "classes and records")]
    [DataRow(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnStructs), "struct S { } record struct P;", DisplayName = "structs and record structs")]
    [DataRow(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnInterfaces), "interface I { }", DisplayName = "interfaces")]
    [DataRow(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnEnumerations), "enum E { A }", DisplayName = "enumerations")]
    [DataRow(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnDelegates), "delegate void D();", DisplayName = "delegates")]
    [DataRow(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnFields), "public class C { int _x; }", DisplayName = "fields")]
    [DataRow(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnMethods), "public class C { void M() { } C() { } }", DisplayName = "methods and constructors")]
    [DataRow(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnProperties), "public class C { int P { get; set; } int this[int i] => i; }", DisplayName = "properties and indexers")]
    [DataRow(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnEvents), "public class C { event System.Action E; event System.Action F { add { } remove { } } }", DisplayName = "events")]
    public void DisabledKind_IsLeftWithoutModifier(string settingName, string source)
    {
        Settings.Default[settingName] = false;

        Assert.AreEqual(source, _converter.Apply(source));
    }

    [TestMethod]
    public void DeclarationsWithExplicitAccess_AreUnchanged()
    {
        string source =
            "public class C\r\n" +
            "{\r\n" +
            "    private int _a;\r\n" +
            "    protected internal static int _b;\r\n" +
            "    internal C() { }\r\n" +
            "    protected void M() { }\r\n" +
            "    private protected int P { get; set; }\r\n" +
            "    public event System.Action E;\r\n" +
            "    protected event System.Action F { add { } remove { } }\r\n" +
            "    private class N { }\r\n" +
            "    protected struct S { }\r\n" +
            "    internal interface I { }\r\n" +
            "    private enum K { A }\r\n" +
            "    public delegate void D();\r\n" +
            "    private record R;\r\n" +
            "}\r\n" +
            "internal struct T { }\r\n" +
            "public interface J { }\r\n" +
            "internal enum L { B }\r\n" +
            "public delegate int G();\r\n" +
            "public record Q(int X);\r\n";

        Assert.AreEqual(source, _converter.Apply(source));
    }

    [TestMethod]
    [DataRow("partial class C { }", DisplayName = "partial class")]
    [DataRow("partial struct S { }", DisplayName = "partial struct")]
    [DataRow("partial interface I { }", DisplayName = "partial interface")]
    [DataRow("partial record R;", DisplayName = "partial record")]
    [DataRow("public partial class C { partial void M(); }", DisplayName = "partial method")]
    [DataRow("public class C { static C() { } ~C() { } }", DisplayName = "static constructor and finalizer")]
    [DataRow("public class C : I { void I.M() { } int I.P => 1; int I.this[int i] => i; event System.Action I.E { add { } remove { } } }", DisplayName = "explicit interface implementations")]
    [DataRow("public interface I { void M(); int P { get; } int this[int i] { get; } event System.Action E; event System.Action F { add { } remove { } } }", DisplayName = "interface members")]
    [DataRow("public class C { public static C operator +(C a, C b) => a; }", DisplayName = "operators")]
    [DataRow("namespace N { int x; void M() { } int P { get; set; } event System.Action E; event System.Action F { add { } remove { } } }", DisplayName = "members directly in a namespace")]
    [DataRow("namespace N { N() { } }", DisplayName = "constructor-like member directly in a namespace")]
    [DataRow("using System;\r\nConsole.WriteLine(1);\r\nstatic void Local() { }\r\nint value = 2;\r\n", DisplayName = "top-level statements and local functions")]
    [DataRow("public class C { private void M() { void Local() { } int x = 0; } }", DisplayName = "local function and local inside a method body")]
    public void DeclarationThatMustNotReceiveAModifier_IsUnchanged(string source) => Assert.AreEqual(source, _converter.Apply(source));

    [TestMethod]
    [DataRow("static class C { }", "internal static class C { }", DisplayName = "static class")]
    [DataRow("abstract record R;", "internal abstract record R;", DisplayName = "abstract record")]
    [DataRow("readonly struct S { }", "internal readonly struct S { }", DisplayName = "readonly struct")]
    [DataRow("readonly record struct P(int X);", "internal readonly record struct P(int X);", DisplayName = "readonly record struct")]
    [DataRow("unsafe delegate void D(int* p);", "internal unsafe delegate void D(int* p);", DisplayName = "unsafe delegate")]
    [DataRow("public class B { enum E { } } public class D : B { new enum E { } }", "public class B { private enum E { } } public class D : B { private new enum E { } }", DisplayName = "new enum")]
    [DataRow("public class C { static readonly int _x; }", "public class C { private static readonly int _x; }", DisplayName = "static readonly field")]
    [DataRow("public class C { static void M() { } async System.Threading.Tasks.Task N() { } }", "public class C { private static void M() { } private async System.Threading.Tasks.Task N() { } }", DisplayName = "static and async methods")]
    [DataRow("public unsafe class C { unsafe C(int* p) { } }", "public unsafe class C { private unsafe C(int* p) { } }", DisplayName = "unsafe constructor")]
    [DataRow("public class C { static int P { get; } }", "public class C { private static int P { get; } }", DisplayName = "static property")]
    [DataRow("public class C { int this[int i] => i; }", "public class C { private int this[int i] => i; }", DisplayName = "indexer")]
    [DataRow("public struct S { int this[int i] { get { return i; } } }", "public struct S { private int this[int i] { get { return i; } } }", DisplayName = "indexer in a struct")]
    [DataRow("public unsafe class C { unsafe int this[int i] => i; }", "public unsafe class C { private unsafe int this[int i] => i; }", DisplayName = "unsafe indexer")]
    [DataRow("public class C { static event System.Action E; static event System.Action F { add { } remove { } } }", "public class C { private static event System.Action E; private static event System.Action F { add { } remove { } } }", DisplayName = "static events")]
    [DataRow("public class C { interface I { } struct S { } delegate void D(); record struct P; }", "public class C { private interface I { } private struct S { } private delegate void D(); private record struct P; }", DisplayName = "nested type kinds")]
    [DataRow("public struct S { int _x; void M() { } int P => 1; class N { } }", "public struct S { private int _x; private void M() { } private int P => 1; private class N { } }", DisplayName = "struct members")]
    [DataRow("public record R { int _x; R(int x) { _x = x; } enum K { A } }", "public record R { private int _x; private R(int x) { _x = x; } private enum K { A } }", DisplayName = "record members")]
    [DataRow("class G<T> where T : new() { T Make() => new T(); }", "internal class G<T> where T : new() { private T Make() => new T(); }", DisplayName = "generic class with constraints")]
    public void ExistingNonAccessModifiers_FollowTheInsertedAccessModifier(string source, string expected) => Assert.AreEqual(expected, _converter.Apply(source));

    [TestMethod]
    public void TopLevelStatementsFile_ModifiesOnlyTheTypesAfterTheStatements()
    {
        string source =
            "using System;\r\n" +
            "\r\n" +
            "var greeter = new Greeter();\r\n" +
            "Console.WriteLine(greeter.Greet());\r\n" +
            "\r\n" +
            "static string Name() => \"world\";\r\n" +
            "\r\n" +
            "class Greeter\r\n" +
            "{\r\n" +
            "    string Greet() => \"Hello\";\r\n" +
            "}\r\n";
        string expected = source
            .Replace("class Greeter\r\n", "internal class Greeter\r\n")
            .Replace("    string Greet()", "    private string Greet()");

        Assert.AreEqual(expected, _converter.Apply(source));
    }

    [TestMethod]
    [DataRow("\r\n", DisplayName = "CRLF")]
    [DataRow("\n", DisplayName = "LF")]
    public void CommentsAttributesDirectivesAndIndentation_StayInFrontOfTheDeclaration(string newLine)
    {
        string source =
            "namespace N;" + newLine +
            newLine +
            "/// <summary>Documented.</summary>" + newLine +
            "class Documented" + newLine +
            "{" + newLine +
            "\t// A field." + newLine +
            "\t[System.NonSerialized]" + newLine +
            "\tint _x;" + newLine +
            newLine +
            "\t#region Methods" + newLine +
            "\t/// <summary>Does it.</summary>" + newLine +
            "\tstatic void M() { }" + newLine +
            "\t#endregion" + newLine +
            "}" + newLine +
            newLine +
            "[System.Flags]" + newLine +
            "enum Options { None = 0 }" + newLine +
            newLine +
            "#if !NEVER" + newLine +
            "struct Active { }" + newLine +
            "#endif" + newLine +
            newLine +
            "#if NEVER" + newLine +
            "struct Inactive { }" + newLine +
            "#endif" + newLine;
        string expected = source
            .Replace("class Documented", "internal class Documented")
            .Replace("\tint _x;", "\tprivate int _x;")
            .Replace("\tstatic void M()", "\tprivate static void M()")
            .Replace("enum Options", "internal enum Options")
            .Replace("struct Active", "internal struct Active");

        Assert.AreEqual(expected, _converter.Apply(source));
    }

    [TestMethod]
    public void FileWithSyntaxErrors_ModifiesTheValidDeclarationsAndKeepsTheRestVerbatim()
    {
        string source = "class C { int _x; void M( { int y = ; } }";
        string expected = "internal class C { private int _x; private void M( { int y = ; } }";

        Assert.AreEqual(expected, _converter.Apply(source));
    }

    [TestMethod]
    public async System.Threading.Tasks.Task ModifiedDeclarations_CompileWithoutNewErrors()
    {
        string source =
            "using System;\r\n" +
            "namespace Demo\r\n" +
            "{\r\n" +
            "    delegate void Handler(int value);\r\n" +
            "    enum Kind { A, B }\r\n" +
            "    interface IShape { double Area(); }\r\n" +
            "    struct Point { int _x; Point(int x) { _x = x; } static Point Create() => new Point(1); }\r\n" +
            "    class Circle : IShape\r\n" +
            "    {\r\n" +
            "        static int s_count;\r\n" +
            "        double _radius;\r\n" +
            "        event Handler Changed;\r\n" +
            "        event Action Moved { add { } remove { } }\r\n" +
            "        Circle(double radius) { _radius = radius; s_count++; }\r\n" +
            "        static Circle() { }\r\n" +
            "        public double Area() => Math.PI * _radius * _radius;\r\n" +
            "        double IShape_Area() => Area();\r\n" +
            "        Kind Kind { get; set; }\r\n" +
            "        class Nested { }\r\n" +
            "        void Raise() { Changed?.Invoke(s_count); }\r\n" +
            "    }\r\n" +
            "}\r\n";
        Microsoft.CodeAnalysis.Document document = CodeJanitor.UnitTests.Transformations.CompilingTestProject.CreateDocument(source);
        string expected = source
            .Replace("    delegate void Handler", "    internal delegate void Handler")
            .Replace("    enum Kind", "    internal enum Kind")
            .Replace("    interface IShape", "    internal interface IShape")
            .Replace("    struct Point { int _x; Point(int x) { _x = x; } static Point Create()", "    internal struct Point { private int _x; private Point(int x) { _x = x; } private static Point Create()")
            .Replace("    class Circle", "    internal class Circle")
            .Replace("        static int s_count;", "        private static int s_count;")
            .Replace("        double _radius;", "        private double _radius;")
            .Replace("        event Handler Changed;", "        private event Handler Changed;")
            .Replace("        event Action Moved", "        private event Action Moved")
            .Replace("        Circle(double radius)", "        private Circle(double radius)")
            .Replace("        double IShape_Area()", "        private double IShape_Area()")
            .Replace("        Kind Kind { get; set; }", "        private Kind Kind { get; set; }")
            .Replace("        class Nested", "        private class Nested")
            .Replace("        void Raise()", "        private void Raise()");

        string output = _converter.Apply(source);

        Assert.AreEqual(expected, output);
        Assert.IsEmpty(await CodeJanitor.UnitTests.Transformations.CompilingTestProject.GetCompileErrorsAsync(document, output));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("public interface I { enum E { A } }", DisplayName = "enum in interface")]
    [DataRow("public interface I { class N { } }", DisplayName = "class in interface")]
    [DataRow("public interface I { struct S { } }", DisplayName = "struct in interface")]
    [DataRow("public interface I { interface J { } }", DisplayName = "interface in interface")]
    [DataRow("public interface I { record R; }", DisplayName = "record in interface")]
    [DataRow("public interface I { delegate void D(); }", DisplayName = "delegate in interface")]
    [DataRow("public interface I { static int x; }", DisplayName = "field in interface")]
    [DataRow("file class F { }", DisplayName = "file-local class")]
    [DataRow("file struct S { }", DisplayName = "file-local struct")]
    [DataRow("file enum E { A }", DisplayName = "file-local enum")]
    [DataRow("file delegate void D();", DisplayName = "file-local delegate")]
    public void DeclarationWhoseDefaultIsNotPrivateOrInternal_IsUnchanged(string source) => Assert.AreEqual(source, _converter.Apply(source));

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void MembersOfClassNestedInInterface_GetPrivate()
    {
        string source = "public interface I { class N { int _x; } }";

        Assert.AreEqual("public interface I { class N { private int _x; } }", _converter.Apply(source));
    }
}
