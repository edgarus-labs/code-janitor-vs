using CodeJanitor.Logic.Transformations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="ReadonlyFieldConverter" />.
/// Policy (ADR-0007): add readonly only when provably safe without a full solution-wide
/// semantic analysis. Scope: private, non-partial, non-const fields whose only writes occur
/// in the declaring class's own constructor (instance fields) or static constructor / field
/// initializer (static fields), including ref/out use within that constructor (legal per the
/// C# spec). Any write elsewhere (regular methods, accessors, local functions, nested
/// lambdas, compound assignment) disqualifies the field. Public/internal/protected fields are
/// always left unchanged since external assignment cannot be ruled out syntactically.
/// </summary>
[TestClass]
public sealed class ReadonlyFieldConverterTests
{
    private IFieldMutabilityConverter _converter;

    [TestInitialize]
    public void TestInitialize()
    {
        _converter = new ReadonlyFieldConverter();
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FieldAssignedOnlyInConstructor_BecomesReadonly()
    {
        string input = "class C { private int _x; public C() { _x = 1; } }";
        string expected = "class C { private readonly int _x; public C() { _x = 1; } }";

        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FieldWithInitializerOnlyNeverWrittenElsewhere_BecomesReadonly()
    {
        string input = "class C { private int _x = 5; void M() { var y = _x; } }";
        string expected = "class C { private readonly int _x = 5; void M() { var y = _x; } }";

        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FieldAssignedInRegularMethod_StaysMutable()
    {
        string input = "class C { private int _x; void M() { _x = 1; } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FieldPassedAsRefArgumentInConstructor_StaysMutable()
    {
        string input = "class C { private int _x; public C() { Helper(ref _x); } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FieldPassedAsOutArgumentInConstructor_StaysMutable()
    {
        string input = "class C { private int _x; public C() { Helper(out _x); } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ObjectFieldMemberPassedAsRefArgumentInMethod_StaysMutable()
    {
        string input = "class C { private Point _pt; void M() { Helper(ref _pt.X); } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ObjectFieldMemberPassedAsOutArgumentInMethod_StaysMutable()
    {
        string input = "class C { private Point _pt; void M() { int.TryParse(\"1\", out _pt.X); } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ObjectFieldMemberPassedAsRefArgumentInConstructor_StaysMutable()
    {
        string input = "class C { private Point _pt; public C() { Helper(ref _pt.X); } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ObjectFieldMemberPassedAsOutArgumentInConstructor_StaysMutable()
    {
        string input = "class C { private Point _pt; public C() { Helper(out _pt.X); } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void DeepObjectFieldMemberPassedAsRefArgument_StaysMutable()
    {
        string input = "class C { private Nested _n; void M() { Helper(ref this._n.Deep.Value); } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ObjectFieldMemberAssignedInMethod_StaysMutable()
    {
        string input = "class C { private Point _pt; void M() { _pt.X = 10; } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ObjectFieldMemberIncrementedInMethod_StaysMutable()
    {
        string input = "class C { private Point _pt; void M() { _pt.X++; } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ObjectFieldElementPassedAsRef_StaysMutable()
    {
        string input = "class C { private int[] _arr; void M() { Helper(ref _arr[0]); } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FieldPassedAsRefArgumentInMethod_StaysMutable()
    {
        string input = "class C { private int _x; void M() { Helper(ref _x); } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FieldPassedAsRefArgumentThroughAnotherInstance_StaysMutable()
    {
        string input = "class C { private int _x; void M(C other) { Helper(ref other._x); } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void StaticFieldPassedAsRefArgumentThroughTypeName_StaysMutable()
    {
        string input = "class C { private static int _x; static void M() { Helper(ref C._x); } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FieldAssignedThroughAnotherInstance_StaysMutable()
    {
        string input = "class C { private int _x; void M(C other) { other._x = 1; } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FieldReturnedByRefProperty_StaysMutable()
    {
        string input = "class C { private int _x; public ref int X => ref _x; }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FieldReturnedByRefMethod_StaysMutable()
    {
        string input = "class C { private int _x; public ref int M() { return ref _x; } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FieldWithCompoundAssignmentInMethod_StaysMutable()
    {
        string input = "class C { private int _x; void M() { _x += 1; } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FieldAlreadyReadonly_Unchanged()
    {
        string input = "class C { private readonly int _x; public C() { _x = 1; } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ConstField_Unchanged()
    {
        string input = "class C { private const int _x = 1; }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void StaticFieldAssignedInStaticConstructor_BecomesReadonly()
    {
        string input = "class C { private static int _x; static C() { _x = 1; } }";
        string expected = "class C { private static readonly int _x; static C() { _x = 1; } }";

        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FieldAssignedInsideLambdaInsideConstructor_StaysMutable()
    {
        // '_a' is assigned directly in the constructor, so it is safely readonly. '_x' is only
        // ever written from inside the lambda body, which could run after construction, so it
        // must stay mutable.
        string input = "class C { private System.Action _a; private int _x; public C() { _a = () => { _x = 1; }; } }";
        string expected = "class C { private readonly System.Action _a; private int _x; public C() { _a = () => { _x = 1; }; } }";

        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void PartialClass_FieldsUnchanged()
    {
        string input = "partial class C { private int _x; public C() { _x = 1; } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void PublicField_StaysMutable()
    {
        string input = "class C { public int X; public C() { X = 1; } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void MultiVariableFieldDeclaration_Unchanged()
    {
        string input = "class C { private int _x, _y; public C() { _x = 1; _y = 2; } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NameAndApply_WorkCorrectly()
    {
        string input = "class C { private int _x; public C() { _x = 1; } }";
        string expected = "class C { private readonly int _x; public C() { _x = 1; } }";

        ReadonlyFieldConverter converter = new ReadonlyFieldConverter();
        Assert.AreEqual("Readonly Field", converter.Name);
        Assert.AreEqual(expected, converter.Apply(input));
        Assert.IsNull(converter.Apply(null));
        Assert.AreEqual(string.Empty, converter.Apply(string.Empty));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void VolatileAndInternalAndProtectedFields_Unchanged()
    {
        string input1 = "class C { private volatile int _x; public C() { _x = 1; } }";
        Assert.AreEqual(input1, _converter.AddReadonlyWhenSafe(input1));

        string input2 = "class C { internal int _x; public C() { _x = 1; } }";
        Assert.AreEqual(input2, _converter.AddReadonlyWhenSafe(input2));

        string input3 = "class C { protected int _x; public C() { _x = 1; } }";
        Assert.AreEqual(input3, _converter.AddReadonlyWhenSafe(input3));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void UnaryIncrementsAndDecrements_StayMutable()
    {
        string input1 = "class C { private int _x; void M() { _x--; } }";
        Assert.AreEqual(input1, _converter.AddReadonlyWhenSafe(input1));

        string input2 = "class C { private int _x; void M() { ++_x; } }";
        Assert.AreEqual(input2, _converter.AddReadonlyWhenSafe(input2));

        string input3 = "class C { private int _x; void M() { --_x; } }";
        Assert.AreEqual(input3, _converter.AddReadonlyWhenSafe(input3));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ParenthesizedAssignmentAndConditionalAccess_HandledCorrectly()
    {
        string input = "class C { private int _x; public C() { (_x) = 1; } }";
        string expected = "class C { private readonly int _x; public C() { (_x) = 1; } }";
        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void MismatchedConstructorStaticness_StaysMutable()
    {
        // Static field assigned in instance constructor -> stays mutable
        string input1 = "class C { private static int _x; public C() { _x = 1; } }";
        Assert.AreEqual(input1, _converter.AddReadonlyWhenSafe(input1));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void WritesInDestructorLocalFunctionOrProperty_StaysMutable()
    {
        string input1 = "class C { private int _x; ~C() { _x = 0; } }";
        Assert.AreEqual(input1, _converter.AddReadonlyWhenSafe(input1));

        string input2 = "class C { private int _x; public C() { void Init() { _x = 1; } Init(); } }";
        Assert.AreEqual(input2, _converter.AddReadonlyWhenSafe(input2));

        string input3 = "class C { private int _x; public int X { get => _x; set => _x = value; } }";
        Assert.AreEqual(input3, _converter.AddReadonlyWhenSafe(input3));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FieldMutatedViaInterlockedInNestedType_StaysMutable()
    {
        string input = @"class Fleet
{
    private int _active;

    private class NestedHelper
    {
        public void Increment(Fleet fleet)
        {
            System.Threading.Interlocked.Increment(ref fleet._active);
        }
    }
}";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FieldMutatedViaInterlockedDecrementInNestedType_StaysMutable()
    {
        string input = @"class Fleet
{
    private int _active;

    private class NestedHelper
    {
        public void Decrement(Fleet fleet)
        {
            System.Threading.Interlocked.Decrement(ref fleet._active);
        }
    }
}";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FieldAssignedInNestedTypeMethod_StaysMutable()
    {
        string input = @"class Fleet
{
    private int _active;

    private class NestedHelper
    {
        public void Set(Fleet fleet)
        {
            fleet._active = 10;
        }
    }
}";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FieldAddressOfTakenInUnsafeContext_StaysMutable()
    {
        string input = @"unsafe class C
{
    private int _x;

    public void M()
    {
        int* p = &_x;
    }
}";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FieldAddressOfTakenThroughInstanceInUnsafeContext_StaysMutable()
    {
        string input = @"unsafe class C
{
    private int _x;

    public void M(C other)
    {
        int* p = &other._x;
    }
}";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow(null, DisplayName = "null")]
    [DataRow("", DisplayName = "empty")]
    [DataRow("  \r\n\t\r\n", DisplayName = "whitespace only")]
    [DataRow("class C { }", DisplayName = "no fields")]
    [DataRow("enum E { A, B }", DisplayName = "enum only")]
    public void SourceWithoutCandidateFields_IsReturnedUnchanged(string input)
    {
        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void RefAndOutArgumentsOfOtherVariables_DoNotBlockTheField()
    {
        string input = "class C { private int _x; private int _y; void M(int[] a) { H(ref _y); H(ref a[0]); T(out var z); T(out int w); ref int r = ref a[0]; } void H(ref int v) { } void T(out int v) { v = _x; } }";
        string expected = "class C { private readonly int _x; private int _y; void M(int[] a) { H(ref _y); H(ref a[0]); T(out var z); T(out int w); ref int r = ref a[0]; } void H(ref int v) { } void T(out int v) { v = _x; } }";

        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void InArgument_DoesNotBlockTheField()
    {
        string input = "struct S { private long _x; void M() { H(in _x); } static void H(in long v) { } }";
        string expected = "struct S { private readonly long _x; void M() { H(in _x); } static void H(in long v) { } }";

        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void ReadOnlyRefLocal_KeepsTheFieldMutable()
    {
        string input = "class C { private int _x; void M() { ref readonly int r = ref _x; } }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NonWritingOperators_DoNotBlockTheField()
    {
        string input = "class C { private int _x; private bool _b; private string _s; private int[] _a; int M() => -_x + ~_x + (!_b ? 1 : 0) + _s!.Length + _a![0] + +_x; }";
        string expected = "class C { private readonly int _x; private readonly bool _b; private readonly string _s; private readonly int[] _a; int M() => -_x + ~_x + (!_b ? 1 : 0) + _s!.Length + _a![0] + +_x; }";

        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void WritesToOtherTargets_DoNotBlockTheField()
    {
        string input = "unsafe class C { private int _x; int this[int i] { get => i; set { } } void M(Holder o, int* p) { o.Value = _x; o.Inner.Value = 1; o.Items[0] = 2; this[0] = 3; *p = 4; o?.Value = 5; } }";
        string expected = "unsafe class C { private readonly int _x; int this[int i] { get => i; set { } } void M(Holder o, int* p) { o.Value = _x; o.Inner.Value = 1; o.Items[0] = 2; this[0] = 3; *p = 4; o?.Value = 5; } }";

        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NullConditionalIncrementOfAnotherObject_DoesNotBlockTheField()
    {
        // Not valid C# (CS0131), but the parser produces a conditional access as the increment operand.
        string input = "class C { private Holder _h; void M(Holder o) { o?.Value++; } }";
        string expected = "class C { private readonly Holder _h; void M(Holder o) { o?.Value++; } }";

        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { private Holder _h; void M() { _h.Items[0] = 1; } }", DisplayName = "element of a member")]
    [DataRow("class C { private Holder _h; void M() { (_h).Value = 1; } }", DisplayName = "parenthesized receiver")]
    [DataRow("class C { private Holder _h; void M() { this._h.Value++; } }", DisplayName = "increment through this")]
    [DataRow("class C { private Holder _h; void M() { _h?.Value++; } }", DisplayName = "null-conditional increment (not valid C#, still parsed)")]
    [DataRow("class C { private Holder _h; void M() { --_h.Value; } }", DisplayName = "prefix decrement of a member")]
    [DataRow("class C { private int _x; void M() { _x ??= 1; } }", DisplayName = "null-coalescing assignment")]
    [DataRow("class C { private int _x; void M() { _x <<= 1; } }", DisplayName = "shift assignment")]
    public void SubMemberOrCompoundWriteOutsideTheConstructor_KeepsTheFieldMutable(string input)
    {
        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { private int _x; int P => _x++; }", DisplayName = "expression-bodied property")]
    [DataRow("class C { private int _x; int this[int i] => _x = i; }", DisplayName = "expression-bodied indexer")]
    [DataRow("class C { private int _x; public int X { init => _x = value; } }", DisplayName = "init accessor")]
    [DataRow("class C { private int _x; public event System.Action E { add { _x++; } remove { } } }", DisplayName = "event accessor")]
    [DataRow("class C { private int _x; public static C operator +(C a, C b) { a._x = 1; return a; } }", DisplayName = "operator")]
    [DataRow("class C { private int _x; public static explicit operator int(C c) { c._x = 2; return 0; } }", DisplayName = "conversion operator")]
    [DataRow("class C { private int _x; C() { System.Action a = delegate { _x = 1; }; } }", DisplayName = "anonymous method in constructor")]
    [DataRow("class C { private int _x; C() { System.Action a = () => _x = 1; } }", DisplayName = "expression lambda in constructor")]
    [DataRow("class O { private int _x; class N { N(O o) { o._x = 1; } } }", DisplayName = "constructor of a nested type")]
    [DataRow("class C { private int _x; async System.Threading.Tasks.Task M() { await System.Threading.Tasks.Task.Yield(); _x = 1; } }", DisplayName = "async method")]
    public void WriteOutsideAnOwnConstructor_KeepsTheFieldMutable(string input)
    {
        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { private System.Func<int> _f; private int _x; C() { _x = 1; _f = () => _x; } }", "class C { private readonly System.Func<int> _f; private readonly int _x; C() { _x = 1; _f = () => _x; } }", DisplayName = "lambda capturing the field")]
    [DataRow("class C { private int _x; C() => _x = 1; }", "class C { private readonly int _x; C() => _x = 1; }", DisplayName = "expression-bodied constructor")]
    [DataRow("class C { private int _x; C(int x) { this._x = x; } C() : this(0) { _x += 1; } }", "class C { private readonly int _x; C(int x) { this._x = x; } C() : this(0) { _x += 1; } }", DisplayName = "compound assignment in constructor")]
    [DataRow("class C { private System.EventHandler _h; C(System.EventHandler h) { _h += h; } }", "class C { private readonly System.EventHandler _h; C(System.EventHandler h) { _h += h; } }", DisplayName = "delegate combine in constructor")]
    [DataRow("class C { private static int _count; static C() { _count++; } }", "class C { private static readonly int _count; static C() { _count++; } }", DisplayName = "increment in static constructor")]
    [DataRow("struct S { private int _x; public S(int x) { _x = x; } public int X => _x; }", "struct S { private readonly int _x; public S(int x) { _x = x; } public int X => _x; }", DisplayName = "struct")]
    [DataRow("record R { private int _x; public R(int x) { _x = x; } }", "record R { private readonly int _x; public R(int x) { _x = x; } }", DisplayName = "record")]
    [DataRow("record struct P(int X) { private int _y = X; }", "record struct P(int X) { private readonly int _y = X; }", DisplayName = "record struct with primary constructor")]
    [DataRow("class C(int x) { private int _x = x; public int X => _x; }", "class C(int x) { private readonly int _x = x; public int X => _x; }", DisplayName = "class with primary constructor")]
    [DataRow("class C<T> where T : class, new() { private T _value = new T(); public T Value => _value; }", "class C<T> where T : class, new() { private readonly T _value = new T(); public T Value => _value; }", DisplayName = "generic with constraints")]
    [DataRow("class C { private string? _name; C(string? name) { _name = name; } }", "class C { private readonly string? _name; C(string? name) { _name = name; } }", DisplayName = "nullable annotation")]
    [DataRow("interface I { private static int s; static int M() => s; }", "interface I { private static readonly int s; static int M() => s; }", DisplayName = "static field of an interface")]
    [DataRow("class C { private new int _x; }", "class C { private new readonly int _x; }", DisplayName = "new modifier")]
    public void FieldWrittenOnlyDuringConstruction_BecomesReadonly(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { private protected int _x; }", DisplayName = "private protected")]
    [DataRow("class C { protected internal int _x; }", DisplayName = "protected internal")]
    [DataRow("class C { private static volatile int _x; }", DisplayName = "static volatile")]
    [DataRow("class C { private static readonly int _x; }", DisplayName = "static readonly")]
    [DataRow("partial class C { private int _x; }", DisplayName = "partial class")]
    [DataRow("partial struct S { private int _x; }", DisplayName = "partial struct")]
    [DataRow("partial record R { private int _x; }", DisplayName = "partial record")]
    [DataRow("class O { partial class N { private int _x; } }", DisplayName = "partial nested class")]
    public void FieldOutOfScope_IsUnchanged(string input)
    {
        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NonPartialNestedTypeOfAPartialType_IsConverted()
    {
        string input = "partial class O { private int _outer; class N { private int _inner; } }";
        string expected = "partial class O { private int _outer; class N { private readonly int _inner; } }";

        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TopLevelStatementsFile_ConvertsTheFieldsOfTheTypesAfterTheStatements()
    {
        string input =
            "using System;\r\n" +
            "\r\n" +
            "var counter = new Counter(1);\r\n" +
            "int local = 0;\r\n" +
            "local = 2;\r\n" +
            "Console.WriteLine(counter.Next() + local);\r\n" +
            "\r\n" +
            "static int Twice(int value) => value * 2;\r\n" +
            "\r\n" +
            "class Counter\r\n" +
            "{\r\n" +
            "    private int _start;\r\n" +
            "    private int _current;\r\n" +
            "\r\n" +
            "    public Counter(int start) { _start = start; }\r\n" +
            "\r\n" +
            "    public int Next() => _current++ + _start;\r\n" +
            "}\r\n";
        string expected = input.Replace("private int _start;", "private readonly int _start;");

        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("\r\n", DisplayName = "CRLF")]
    [DataRow("\n", DisplayName = "LF")]
    public void DocCommentsAttributesAndRegionsAroundTheField_ArePreserved(string newLine)
    {
        string input =
            "namespace N" + newLine +
            "{" + newLine +
            "\tclass C" + newLine +
            "\t{" + newLine +
            "\t\t#region Fields" + newLine +
            "\t\t/// <summary>The name.</summary>" + newLine +
            "\t\t[System.NonSerialized]" + newLine +
            "\t\tprivate /* why */ string _name = \"_name = 1\"; // trailing" + newLine +
            "\t\t#endregion" + newLine +
            "\t\tprivate static string _text = @\"_text = 2\";" + newLine +
            "\t}" + newLine +
            "}" + newLine;
        string expected = input
            .Replace("private /* why */ string _name", "private /* why */ readonly string _name")
            .Replace("private static string _text", "private static readonly string _text");

        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FileWithSyntaxErrors_ConvertsTheUnwrittenFieldAndKeepsTheRestVerbatim()
    {
        string input = "class C { private int _x; private int _y; void M( { _y = ; } }";
        string expected = "class C { private readonly int _x; private int _y; void M( { _y = ; } }";

        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async System.Threading.Tasks.Task ConvertedFields_CompileWithoutNewErrors()
    {
        string input =
            "using System;\r\n" +
            "using System.Collections.Generic;\r\n" +
            "class Holder { public int Value; }\r\n" +
            "sealed class C<T> where T : class\r\n" +
            "{\r\n" +
            "    private readonly List<T> _items = new List<T>();\r\n" +
            "    private Func<int> _reader;\r\n" +
            "    private Holder _holder = new Holder();\r\n" +
            "    private static int s_instances;\r\n" +
            "    private int _id;\r\n" +
            "    private T? _last;\r\n" +
            "    static C() { s_instances = 0; }\r\n" +
            "    public C(int id)\r\n" +
            "    {\r\n" +
            "        _id = id;\r\n" +
            "        _reader = () => _id;\r\n" +
            "    }\r\n" +
            "    public void Add(T item) { _items.Add(item); _holder.Value++; _last = item; }\r\n" +
            "    public int Read() => _reader() + s_instances + _holder.Value;\r\n" +
            "}\r\n";
        Microsoft.CodeAnalysis.Document document = CompilingTestProject.CreateDocument(input);
        string expected = input
            .Replace("private Func<int> _reader;", "private readonly Func<int> _reader;")
            .Replace("private static int s_instances;", "private static readonly int s_instances;")
            .Replace("private int _id;", "private readonly int _id;");

        string result = _converter.AddReadonlyWhenSafe(input);

        Assert.AreEqual(expected, result);
        Assert.IsEmpty(await CompilingTestProject.GetCompileErrorsAsync(document, result));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { private int _x; private int _y; void M() { (_x, _y) = (1, 2); } }", DisplayName = "tuple deconstruction")]
    [DataRow("class C { private int _x; void M() { (_x, var y) = (1, 2); } }", DisplayName = "deconstruction mixed with a declaration")]
    [DataRow("class C { private int _x; private int _y; void M() { ((this._x, _y), var z) = ((1, 2), 3); } }", DisplayName = "nested deconstruction through this")]
    [DataRow("class C { private int _x; private int _y; void M((int, int)[] a) { foreach ((_x, _y) in a) { } } }", DisplayName = "foreach deconstruction")]
    public void DeconstructionWriteOutsideTheConstructor_KeepsFieldsMutable(string input)
    {
        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { private int _x; C(C other) { other._x = 5; } }", DisplayName = "member access on another instance")]
    [DataRow("class C { private int _x; C() { var c = new C { _x = 1 }; } }", DisplayName = "object initializer")]
    [DataRow("record R { private int _x; R(R other) { var r = other with { _x = 1 }; } }", DisplayName = "with expression")]
    [DataRow("class C { private int _x; C(C other) { other._x++; } }", DisplayName = "increment on another instance")]
    [DataRow("class C { private int _x; C(C other) { (other._x, var y) = (1, 2); } }", DisplayName = "deconstruction into another instance")]
    [DataRow("struct S { private Holder _h; S(S other) { other._h.Value = 1; } }", DisplayName = "sub-member of another instance")]
    [DataRow("class C { private static int s_x; static C() { D.s_x = 1; } }", DisplayName = "static field of another type")]
    public void WriteToAnotherInstanceInConstructor_KeepsFieldMutable(string input)
    {
        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { private int _x; private int _y; C() { (_x, this._y) = (1, 2); } }", "class C { private readonly int _x; private readonly int _y; C() { (_x, this._y) = (1, 2); } }", DisplayName = "deconstruction in the constructor")]
    [DataRow("class C { private static int s_x; static C() { C.s_x = 1; } }", "class C { private static readonly int s_x; static C() { C.s_x = 1; } }", DisplayName = "static field through the own type name")]
    public void WriteThroughThisInConstructor_BecomesReadonly(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("struct P { public int X; } class C { private P _p; C() { _p.X = 1; } }", DisplayName = "assignment to a member of a struct in the file")]
    [DataRow("struct P { public int X; } class C { private P _p; C() { _p.X += 1; } }", DisplayName = "compound assignment to a member of a struct in the file")]
    [DataRow("struct P { public int Count; } class C { private P _p; C() { _p.Count++; } }", DisplayName = "postfix increment of a member of a struct in the file")]
    [DataRow("struct P { public int Count; } class C { private P _p; C() { --this._p.Count; } }", DisplayName = "prefix decrement of a member through this")]
    [DataRow("struct P { public int X; } class C { private P _p; C() { (_p.X, var y) = (1, 2); } }", DisplayName = "deconstruction into a member of a struct")]
    [DataRow("class C { private Point _p; C() { _p.X = 1; } }", DisplayName = "member of a type declared elsewhere")]
    [DataRow("class C<T> { private T _t; C() { _t.Value = 1; } }", DisplayName = "member of a type parameter")]
    public void SubMemberWriteInConstructorOfPossiblyMutableStructField_KeepsTheFieldMutable(string input)
    {
        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class H { public int X; } class C { private H _h; C() { _h.X = 1; } }", "class H { public int X; } class C { private readonly H _h; C() { _h.X = 1; } }", DisplayName = "member of a class in the file")]
    [DataRow("class C { private int[] _a; C() { _a[0] = 1; } }", "class C { private readonly int[] _a; C() { _a[0] = 1; } }", DisplayName = "element of an array")]
    [DataRow("using System.Collections.Generic; class C { private List<int> _l; C() { _l[0] = 1; } }", "using System.Collections.Generic; class C { private readonly List<int> _l; C() { _l[0] = 1; } }", DisplayName = "element of a well-known class")]
    [DataRow("struct P { public int X; } class C { private P _p; C() { _p = new P(); } }", "struct P { public int X; } class C { private readonly P _p; C() { _p = new P(); } }", DisplayName = "direct assignment of a struct field")]
    public void WriteThroughNonStructFieldInConstructor_BecomesReadonly(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async System.Threading.Tasks.Task StructFieldWithSubMemberWriteInConstructor_CompilesWithoutNewErrors()
    {
        string input =
            "struct P { public int X; public int Count; }\r\n" +
            "class Holder { public int Value; }\r\n" +
            "class C\r\n" +
            "{\r\n" +
            "    private P _p;\r\n" +
            "    private Holder _h = new Holder();\r\n" +
            "    public C() { _p.X = 1; _p.X += 1; _p.Count++; _h.Value = 2; }\r\n" +
            "    public int Sum() => _p.X + _p.Count + _h.Value;\r\n" +
            "}\r\n";
        Microsoft.CodeAnalysis.Document document = CompilingTestProject.CreateDocument(input);

        string result = _converter.AddReadonlyWhenSafe(input);

        Assert.AreEqual(input.Replace("private Holder _h", "private readonly Holder _h"), result);
        Assert.IsEmpty(await CompilingTestProject.GetCompileErrorsAsync(document, result));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { private int _x; void M() {\n#if DEBUG\n _x = 5;\n#endif\n} }", DisplayName = "#if DEBUG")]
    [DataRow("class C { private int _x; void M() {\n#if !RELEASE\n#else\n _x++;\n#endif\n} }", DisplayName = "#else branch")]
    [DataRow("#define A\nclass C { private int _x; void M() {\n#if A\n#else\n M2(ref _x);\n#endif\n} }", DisplayName = "branch disabled by #define")]
    public void WriteInsideAnInactivePreprocessorBranch_KeepsFieldMutable(string input)
    {
        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void InactivePreprocessorBranchWithoutTheField_DoesNotBlockIt()
    {
        string input = "class C { private int _x; int M() {\n#if DEBUG\n int _xy = 1; _xy++;\n#endif\n return _x; } }";
        string expected = "class C { private readonly int _x; int M() {\n#if DEBUG\n int _xy = 1; _xy++;\n#endif\n return _x; } }";

        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C\n{\n    int _x;\n}\n", "class C\n{\n    readonly int _x;\n}\n", DisplayName = "no modifiers")]
    [DataRow("class C\r\n{\r\n    [System.NonSerialized]\r\n    int _x;\r\n}\r\n", "class C\r\n{\r\n    [System.NonSerialized]\r\n    readonly int _x;\r\n}\r\n", DisplayName = "attribute")]
    [DataRow("class C\n{\n    /// <summary>X.</summary>\n    int _x;\n}\n", "class C\n{\n    /// <summary>X.</summary>\n    readonly int _x;\n}\n", DisplayName = "doc comment")]
    public void FieldWithoutModifiers_KeepsItsLayout(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("using System.Collections.Generic; public class C { private List<int>.Enumerator _e = new List<int> { 1, 2 }.GetEnumerator(); public bool Next() => _e.MoveNext(); }", DisplayName = "BCL mutable struct")]
    [DataRow("struct S { int _v; public void Bump() => _v++; } class C { private S _s; void M() { this._s.Bump(); } }", DisplayName = "mutable struct declared in the file")]
    [DataRow("class C { private Unknown _u; void M() { (_u).Reset(); } }", DisplayName = "type declared elsewhere")]
    [DataRow("class C<T> { private T _t; void M() { _t.ToString(); } }", DisplayName = "unconstrained type parameter")]
    [DataRow("struct Timer { int _ticks; public void Tick() => _ticks++; } class C { private Timer _t; void M() => _t.Tick(); }", DisplayName = "mutable struct in the file named like a well-known class")]
    [DataRow("record struct IPoint(int X) { public void Move() => X++; } class C { private IPoint _p; void M() => _p.Move(); }", DisplayName = "mutable record struct in the file named like an interface")]
    [DataRow("namespace Geo { record Point(double X, double Y); class Canvas { private System.Drawing.Point _origin; public void Shift() => _origin.Offset(1, 1); } }", DisplayName = "qualified external type named like a record in the file")]
    [DataRow("using System.Collections.Generic; class C { class Enumerator { } private List<int>.Enumerator _cursor; public bool Next() => _cursor.MoveNext(); }", DisplayName = "nested external type named like a class in the file")]
    [DataRow("extern alias Draw; record Point(double X, double Y); class C { private Draw::Point _p; void M() => _p.Offset(1, 1); }", DisplayName = "alias-qualified external type named like a record in the file")]
    [DataRow("class Node { } class C { private Node<int> _n; void M() => _n.Advance(); }", DisplayName = "generic type named like a non-generic class in the file")]
    [DataRow("class C { private Acme.Timer _t; void M() => _t.Tick(); }", DisplayName = "well-known class name in another namespace")]
    [DataRow("class C { private ID _id; void M() => _id.Next(); }", DisplayName = "all-caps name starting with I")]
    [DataRow("class C { private IPv4Address _a; void M() => _a.Advance(); }", DisplayName = "name with a digit starting with I")]
    [DataRow("class C { private Timer _t; void M() => _t.Tick(); }", DisplayName = "well-known class name without its namespace imported")]
    public void MethodCallOnFieldOfPossiblyMutableStructType_KeepsFieldMutable(string input)
    {
        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C { private string _s = \"a\"; string M() => _s.Trim(); }", "class C { private readonly string _s = \"a\"; string M() => _s.Trim(); }", DisplayName = "predefined type")]
    [DataRow("class C { private int[] _a = new int[1]; object M() => _a.Clone(); }", "class C { private readonly int[] _a = new int[1]; object M() => _a.Clone(); }", DisplayName = "array")]
    [DataRow("class H { public void Go() { } } class C { private H _h = new H(); void M() => _h.Go(); }", "class H { public void Go() { } } class C { private readonly H _h = new H(); void M() => _h.Go(); }", DisplayName = "class declared in the file")]
    [DataRow("readonly struct R { public int Get() => 1; } class C { private R _r; int M() => _r.Get(); }", "readonly struct R { public int Get() => 1; } class C { private readonly R _r; int M() => _r.Get(); }", DisplayName = "readonly struct declared in the file")]
    [DataRow("interface IService { void Run(); } class C { private IService _s; C(IService s) { _s = s; } void M() => _s.Run(); }", "interface IService { void Run(); } class C { private readonly IService _s; C(IService s) { _s = s; } void M() => _s.Run(); }", DisplayName = "interface declared in the file")]
    [DataRow("using System.Collections.Generic; class C { private List<int> _l = new List<int>(); void M() => _l.Add(1); }", "using System.Collections.Generic; class C { private readonly List<int> _l = new List<int>(); void M() => _l.Add(1); }", DisplayName = "well-known BCL class")]
    [DataRow("class C { private System.Text.StringBuilder _b = new System.Text.StringBuilder(); void M() => _b.Append(1); }", "class C { private readonly System.Text.StringBuilder _b = new System.Text.StringBuilder(); void M() => _b.Append(1); }", DisplayName = "qualified well-known BCL class")]
    [DataRow("class C { private global::System.Threading.Tasks.Task _t; void M() => _t.Wait(); }", "class C { private readonly global::System.Threading.Tasks.Task _t; void M() => _t.Wait(); }", DisplayName = "globally qualified well-known BCL class")]
    [DataRow("class C { private Unknown _u; void M() { _u?.Reset(); } }", "class C { private readonly Unknown _u; void M() { _u?.Reset(); } }", DisplayName = "null-conditional call")]
    [DataRow("class C { private IClock _c; C(IClock c) { _c = c; } void M() => _c.Now(); }", "class C { private readonly IClock _c; C(IClock c) { _c = c; } void M() => _c.Now(); }", DisplayName = "interface-named type declared elsewhere")]
    [DataRow("using System.Threading; class C { private Timer _t; void M() => _t.Dispose(); }", "using System.Threading; class C { private readonly Timer _t; void M() => _t.Dispose(); }", DisplayName = "well-known class with its namespace imported")]
    public void MethodCallOnFieldOfReferenceOrImmutableType_BecomesReadonly(string input, string expected)
    {
        Assert.AreEqual(expected, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FixedBuffer_IsUnchanged()
    {
        string input = "unsafe struct S { private fixed int _b[4]; int F() => _b[0]; }";

        Assert.AreEqual(input, _converter.AddReadonlyWhenSafe(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async System.Threading.Tasks.Task NewlyHandledFields_CompileWithoutNewErrors()
    {
        string input =
            "using System.Collections.Generic;\n" +
            "class C\n" +
            "{\n" +
            "    int _a;\n" +
            "    [System.NonSerialized]\n" +
            "    int _b;\n" +
            "    private int _c;\n" +
            "    private List<int> _list = new List<int>();\n" +
            "    C(C other)\n" +
            "    {\n" +
            "        (_a, this._b) = (1, 2);\n" +
            "        (_c, other._c) = (3, 4);\n" +
            "        _list.Add(_a + _b + _c);\n" +
            "    }\n" +
            "    void M() => _list.Add(0);\n" +
            "}\n";
        Microsoft.CodeAnalysis.Document document = CompilingTestProject.CreateDocument(input);
        string expected = input
            .Replace("    int _a;", "    readonly int _a;")
            .Replace("    int _b;", "    readonly int _b;")
            .Replace("private List<int> _list", "private readonly List<int> _list");

        string result = _converter.AddReadonlyWhenSafe(input);

        Assert.AreEqual(expected, result);
        Assert.IsEmpty(await CompilingTestProject.GetCompileErrorsAsync(document, result));
    }
}
