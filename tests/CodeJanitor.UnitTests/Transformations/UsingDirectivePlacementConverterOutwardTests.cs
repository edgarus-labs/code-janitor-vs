using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeJanitor.Logic.Transformations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="UsingDirectivePlacementConverter.MoveUsingsOutsideAsync" /> (the
/// <c>csharp_using_directive_placement = outside_namespace</c> direction). Every moved result is compiled together with
/// <see cref="Library" /> and must not contain compile errors.
/// </summary>
[TestClass]
public sealed class UsingDirectivePlacementConverterOutwardTests
{
    private const string Library =
        "namespace Company.App.Services { public class Svc { } }\r\n" +
        "namespace Company.App.Models { public class Foo { } public static class Helpers { public static int Twice(int x) => x * 2; } }\r\n" +
        "namespace Company.Shared { public class Util { } }\r\n" +
        "namespace Alpha { public class T { } }\r\n" +
        "namespace Beta { public class T { } }\r\n";

    /// <summary>
    /// Receiver types for the extension members that <c>Company.App.Ext.E</c> and <c>Company.E2</c> both declare: each
    /// lacks exactly the member the tested construct calls implicitly, so only the extension member can provide it.
    /// </summary>
    private const string ExtensionReceivers =
        "namespace Company\r\n{\r\n" +
        "    public class Thing { }\r\n" +
        "    public class Bag : System.Collections.IEnumerable { public System.Collections.IEnumerator GetEnumerator() => null; }\r\n" +
        "    public class Indexed { public int this[int i] => i; public Indexed Slice(int start, int length) => this; }\r\n" +
        "    public class Counted { public int Count => 0; public int this[int i] => i; }\r\n" +
        "    public class Countable { public int Count => 0; }\r\n" +
        "    public class AsyncEnumerator { public System.Threading.Tasks.Task<bool> MoveNextAsync() => null; public int Current => 0; }\r\n" +
        "}\r\n";

    private const string CountExtension = "extension(Indexed t) { public int Count => 0; }";

    private const string IndexerExtension = "extension(Countable t) { public int this[int i] => i; }";

    private const string SliceExtension = "public static Counted Slice(this Counted t, int start, int length) => t;";

    private const string TruthExtension = "extension(Thing) { public static bool operator true(Thing t) => true; public static bool operator false(Thing t) => false; }";

    private const string LogicalExtension =
        "extension(Thing) { public static Thing operator &(Thing a, Thing b) => a; public static Thing operator |(Thing a, Thing b) => a; " +
        "public static bool operator true(Thing t) => true; public static bool operator false(Thing t) => false; }";

    private const string EqualityExtension = "extension(Thing) { public static bool operator ==(Thing a, Thing b) => true; public static bool operator !=(Thing a, Thing b) => false; }";

    private const string DeconstructExtension = "public static void Deconstruct(this Thing t, out int a, out int b) { a = 0; b = 0; }";

    private const string ServicesNamespace = "namespace Company.App\r\n{\r\n    using Services;\r\n    class C { Svc s; }\r\n}\r\n";

    private const string AppToolsNamespace = "namespace Company.App.Tools { public class Tool { } }\r\n";

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task IssueRepro_QualifiesNamespaceRelativeUsingsAndKeepsQualifiedOnes()
    {
        string input = "namespace Company.App\r\n{\r\n    using Services;\r\n    using Shared;\r\n    using System.Text;\r\n    class C { Svc s; Util u; StringBuilder b; }\r\n}\r\n";

        string result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual(
            "using Company.App.Services;\r\nusing Company.Shared;\r\nusing System.Text;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; Util u; StringBuilder b; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ChildNamespaceRelativeUsing_IsFullyQualified()
    {
        string input = "namespace Company.App\r\n{\r\n    using Services;\r\n    class C { Svc s; }\r\n}\r\n";

        string result = await AssertMovedAndCompilesAsync(input);

        Assert.StartsWith("using Company.App.Services;\r\n\r\nnamespace Company.App\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ParentNamespaceRelativeUsing_IsFullyQualified()
    {
        string input = "namespace Company.App\r\n{\r\n    using Shared;\r\n    class C { Util u; }\r\n}\r\n";

        string result = await AssertMovedAndCompilesAsync(input);

        Assert.StartsWith("using Company.Shared;\r\n\r\nnamespace Company.App\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task RelativeAliasTargets_AreFullyQualified_IncludingTypeArguments()
    {
        string input = "namespace Company.App\r\n{\r\n    using X = Models.Foo;\r\n    using L = System.Collections.Generic.List<Models.Foo>;\r\n    class C { X x; L l; }\r\n}\r\n";

        string result = await AssertMovedAndCompilesAsync(input);

        Assert.StartsWith(
            "using X = Company.App.Models.Foo;\r\nusing L = System.Collections.Generic.List<Company.App.Models.Foo>;\r\n\r\nnamespace Company.App\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task RelativeUsingStatic_IsFullyQualified()
    {
        string input = "namespace Company.App\r\n{\r\n    using static Models.Helpers;\r\n    class C { int y = Twice(1); }\r\n}\r\n";

        string result = await AssertMovedAndCompilesAsync(input);

        Assert.StartsWith("using static Company.App.Models.Helpers;\r\n\r\nnamespace Company.App\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task QualifiedUsingStaysUnchanged_AndIsDeduplicatedWithTopLevelUsings()
    {
        string input = "using System;\r\nusing System.Text;\r\n\r\nnamespace Company.App\r\n{\r\n    using System.Text;\r\n    using Services;\r\n    class C { Svc s; StringBuilder b; Action a; }\r\n}\r\n";

        string result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual(
            "using System;\r\nusing System.Text;\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; StringBuilder b; Action a; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task RelativeUsing_IsDeduplicatedWithEquivalentQualifiedTopLevelUsing()
    {
        string input = "using Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    using Services;\r\n    class C { Svc s; }\r\n}\r\n";

        string result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual("using Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; }\r\n}\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task PreservesFileHeaderCrLfAndBlankLineAfterUsings()
    {
        string input = "// Copyright (c) 2026\r\n\r\nnamespace Company.App\r\n{\r\n    using Services;\r\n    class C { Svc s; }\r\n}\r\n";

        string result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual(
            "// Copyright (c) 2026\r\n\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task PreservesLfLineEndings()
    {
        string input = "// header\n\nnamespace Company.App\n{\n    using Services;\n    class C { Svc s; }\n}\n";

        string result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual("// header\n\nusing Company.App.Services;\n\nnamespace Company.App\n{\n    class C { Svc s; }\n}\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task FileScopedNamespaceUsings_AreQualifiedAndMoved()
    {
        string input = "namespace Company.App;\r\n\r\nusing Services;\r\n\r\nclass C { Svc s; }\r\n";

        string result = await AssertMovedAndCompilesAsync(input);

        Assert.StartsWith("using Company.App.Services;\r\n\r\nnamespace Company.App;", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task UsingsAlreadyOutside_ReportsNothingToMove()
    {
        string input = "using System;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { }\r\n}\r\n";

        UsingDirectivePlacementResult result = await MoveAsync(CompilingTestProject.CreateDocument(input, Library));

        Assert.AreEqual(UsingDirectivePlacementStatus.NothingToMove, result.Status);
        Assert.IsNull(result.Text);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task UnresolvableUsing_IsSkippedWithReason()
    {
        string input = "namespace Company.App\r\n{\r\n    using Services;\r\n    using Missing;\r\n    class C { Svc s; }\r\n}\r\n";

        UsingDirectivePlacementResult result = await MoveAsync(CompilingTestProject.CreateDocument(input, Library));

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status);
        Assert.IsNull(result.Text);
        Assert.Contains("using Missing;", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task MoveThatIntroducesCompileErrors_IsSkippedWithReason()
    {
        // Each namespace imports a different T; merged at file level, T becomes ambiguous.
        string input = "namespace N1\r\n{\r\n    using Alpha;\r\n    class C1 { T t; }\r\n}\r\n\r\nnamespace N2\r\n{\r\n    using Beta;\r\n    class C2 { T t; }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, Library);
        Assert.IsEmpty(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status);
        Assert.IsNull(result.Text);
        Assert.Contains("CS0104", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task QualifiedAliasTargets_StayVerbatim_InsteadOfKeywordOrShorthandSyntax()
    {
        string input = "namespace Company.App\r\n{\r\n    using Str = System.String;\r\n    using N = System.Nullable<System.Int32>;\r\n    using P = System.ValueTuple<System.Int32, System.Int32>;\r\n    class C { Str s; N n; P p; }\r\n}\r\n";

        string result = await AssertMovedAndCompilesAsync(input);

        Assert.StartsWith(
            "using Str = System.String;\r\nusing N = System.Nullable<System.Int32>;\r\nusing P = System.ValueTuple<System.Int32, System.Int32>;\r\n\r\nnamespace Company.App\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task QualifiedAliasToSpecialType_IsMoved_OnCSharp73Projects()
    {
        string input = "namespace Company.App\r\n{\r\n    using Str = System.String;\r\n    using Services;\r\n    class C { Str s; Svc v; }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, LanguageVersion.CSharp7_3, new MetadataReference[0], Library);

        string result = await AssertMovedAndCompilesAsync(document);

        Assert.StartsWith("using Str = System.String;\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task RelativeAliasTargets_AreQualifiedWithoutKeywords_OnCSharp73Projects()
    {
        // At file level an alias target does not see the file's using directives, so String and List<int> must be
        // qualified; before C# 12 an alias target cannot be a keyword such as 'string'.
        string input = "using System;\r\nusing System.Collections.Generic;\r\n\r\nnamespace Company.App\r\n{\r\n    using Str = String;\r\n    using L = List<int>;\r\n    class C { Str s; L l; }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, LanguageVersion.CSharp7_3, new MetadataReference[0], Library);

        string result = await AssertMovedAndCompilesAsync(document);

        Assert.AreEqual(
            "using System;\r\nusing System.Collections.Generic;\r\nusing Str = System.String;\r\nusing L = System.Collections.Generic.List<System.Int32>;\r\n\r\n" +
            "namespace Company.App\r\n{\r\n    class C { Str s; L l; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow(
        LanguageVersion.CSharp7_3,
        "using P = System.ValueTuple<Services.Svc, int>;",
        "using P = System.ValueTuple<Company.App.Services.Svc, System.Int32>;",
        "P p;",
        DisplayName = "tuple on C# 7.3")]
    [DataRow(
        LanguageVersion.CSharp7_3,
        "using L = System.Collections.Generic.List<(Services.Svc A, int B)>;",
        "using L = System.Collections.Generic.List<(Company.App.Services.Svc A, System.Int32 B)>;",
        "L l; int M() => l[0].B;",
        DisplayName = "named tuple type argument on C# 7.3")]
    [DataRow(
        LanguageVersion.Latest,
        "using P = (Services.Svc A, int B);",
        "using P = (Company.App.Services.Svc A, System.Int32 B);",
        "P p; int M() => p.B;",
        DisplayName = "named tuple on latest C#")]
    public async Task RelativeTupleAliasTargets_AreQualifiedInSyntaxTheLanguageVersionAccepts(LanguageVersion languageVersion, string alias, string expectedAlias, string members)
    {
        // Before C# 12 an alias target cannot be written in tuple syntax, but a type argument can; element names can only
        // be written in tuple syntax.
        string input = "namespace Company.App\r\n{\r\n    " + alias + "\r\n    class C { " + members + " }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, languageVersion, new MetadataReference[0], Library);

        string result = await AssertMovedAndCompilesAsync(document);

        Assert.StartsWith(expectedAlias + "\r\n\r\nnamespace Company.App\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ExternAliasQualifiedUsing_KeepsItsAlias()
    {
        MetadataReference aliased = CompilingTestProject.CreateAliasedReference("Ext", "namespace Ext { public class Thing { } }", "V1");
        string input = "extern alias V1;\r\n\r\nnamespace Company.App\r\n{\r\n    using V1::Ext;\r\n    class C { Thing t; }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, LanguageVersion.Latest, new[] { aliased }, Library);

        string result = await AssertMovedAndCompilesAsync(document);

        Assert.Contains("using V1::Ext;", result);
        Assert.IsLessThan(result.IndexOf("namespace Company.App", StringComparison.Ordinal), result.IndexOf("using V1::Ext;", StringComparison.Ordinal), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task UsingOfAnExternAliasThatStaysInsideTheNamespace_IsSkippedWithReason()
    {
        // The extern alias stays inside the namespace, so V1::Ext cannot be named at file level. Written as 'using Ext;',
        // the directive would import the project's own Ext instead, and Thing would silently bind to its Thing.
        MetadataReference aliased = CompilingTestProject.CreateAliasedReference("Ext", "namespace Ext { public class Thing { } }", "V1");
        string input = "namespace Company.App\r\n{\r\n    extern alias V1;\r\n    using V1::Ext;\r\n    class C { Thing t; }\r\n}\r\n";
        string rebound = "using Ext;\r\n\r\nnamespace Company.App\r\n{\r\n    extern alias V1;\r\n    class C { Thing t; }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, LanguageVersion.Latest, new[] { aliased }, Library, "namespace Ext { public class Thing { } }\r\n");
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, rebound), "The rebound text must compile, so that only the binding check can reject it.");

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        Assert.Contains("'using V1::Ext;'", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task MoveThatSilentlyRebindsAName_IsSkippedWithReason()
    {
        // Inside Company.App, 'using Models;' makes Foo mean Company.App.Models.Foo. At file level the import is
        // searched after the enclosing namespaces, so Foo would silently become Company.Foo - without any error.
        string input = "namespace Company.App\r\n{\r\n    using Models;\r\n    class C { Foo f; }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, Library, "namespace Company { public class Foo { } }");
        Assert.IsEmpty(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status);
        Assert.IsNull(result.Text);
        Assert.Contains("Company.App.Models.Foo", result.Reason);
        Assert.Contains("Company.Foo", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task NestedNamespaceRelativeUsing_IsQualifiedAgainstTheInnermostNamespace()
    {
        string input = "namespace Company\r\n{\r\n    namespace App\r\n    {\r\n        using Services;\r\n        class C { Svc s; }\r\n    }\r\n}\r\n";

        string result = await AssertMovedAndCompilesAsync(input);

        Assert.StartsWith("using Company.App.Services;\r\n\r\nnamespace Company\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task UsingsInterleavedWithPreprocessorDirectives_AreSkippedWithReason()
    {
        string input = "namespace Company.App\r\n{\r\n#if true\r\n    using Services;\r\n#endif\r\n    class C { Svc s; }\r\n}\r\n";

        UsingDirectivePlacementResult result = await MoveAsync(CompilingTestProject.CreateDocument(input, Library));

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status);
        Assert.IsNull(result.Text);
        Assert.Contains("interleaved with preprocessor directives", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("int y = 1.Twice();", "public static int Twice(this int x) => x;", "Twice", DisplayName = "extension method call")]
    [DataRow("void M(Thing t) { foreach (var x in t) { } }", "public static System.Collections.Generic.IEnumerator<int> GetEnumerator(this Thing t) => null;", "GetEnumerator", DisplayName = "foreach GetEnumerator")]
    [DataRow("Bag b = new Bag { 1 };", "public static void Add(this Bag b, int x) { }", "Add", DisplayName = "collection initializer Add")]
    [DataRow("async System.Threading.Tasks.Task M(Thing t) { await t; }", "public static System.Runtime.CompilerServices.TaskAwaiter GetAwaiter(this Thing t) => default;", "GetAwaiter", DisplayName = "await GetAwaiter")]
    [DataRow("void M(Thing t) { var (a, b) = t; }", DeconstructExtension, "Deconstruct", DisplayName = "deconstruction")]
    [DataRow("void M((Thing, int) p) { var ((a, b), c) = p; }", DeconstructExtension, "Deconstruct", DisplayName = "nested deconstruction")]
    [DataRow("void M(Thing[] ts) { foreach (var (a, b) in ts) { } }", DeconstructExtension, "Deconstruct", DisplayName = "foreach deconstruction")]
    [DataRow("bool M(Thing t) => t is (0, 0);", DeconstructExtension, "Deconstruct", DisplayName = "positional pattern")]
    [DataRow("bool M(Thing t) => t is var (a, b);", DeconstructExtension, "Deconstruct", DisplayName = "var pattern")]
    [DataRow("object M(Thing t) => from x in t select x + 1;", "public static Thing Select(this Thing t, System.Func<int, int> f) => t;", "Select", DisplayName = "query select clause")]
    [DataRow("object M(Thing t) => from x in t where x > 0 select x;", "public static Thing Where(this Thing t, System.Func<int, bool> f) => t;", "Where", DisplayName = "query where clause")]
    [DataRow("Thing M(Thing t) => t + t;", "extension(Thing) { public static Thing operator +(Thing a, Thing b) => a; }", "operator +", DisplayName = "extension operator")]
    [DataRow("bool M(Indexed t) => t is [1];", CountExtension, "Count", DisplayName = "list pattern extension Count")]
    [DataRow("bool M(Counted t) => t is [_, .. var r];", SliceExtension, "Slice", DisplayName = "slice pattern extension Slice")]
    [DataRow("int M(Indexed t) => t[^1];", CountExtension, "Count", DisplayName = "index from end extension Count")]
    [DataRow("int? M(Indexed t) => t?[^1];", CountExtension, "Count", DisplayName = "conditional index from end extension Count")]
    [DataRow("object M(Indexed t) => t[1..];", CountExtension, "Count", DisplayName = "range element access extension Count")]
    [DataRow("object M(Counted t) => t[1..];", SliceExtension, "Slice", DisplayName = "range element access extension Slice")]
    [DataRow("void M(Thing t) { if (t) { } }", TruthExtension, "operator true", DisplayName = "if condition extension operator true")]
    [DataRow("int M(Thing t) => (t) ? 1 : 0;", TruthExtension, "operator true", DisplayName = "conditional expression extension operator true")]
    [DataRow("void M(Thing t) { while (t) { } }", TruthExtension, "operator true", DisplayName = "while condition extension operator true")]
    [DataRow("void M(Thing t) { do { } while (t); }", TruthExtension, "operator true", DisplayName = "do condition extension operator true")]
    [DataRow("void M(Thing t) { for (; t;) { } }", TruthExtension, "operator true", DisplayName = "for condition extension operator true")]
    [DataRow("void M(Thing t) { switch (1) { case 1 when t: break; } }", TruthExtension, "operator true", DisplayName = "case guard extension operator true")]
    [DataRow("int M(Thing t) => 1 switch { 1 when t => 1, _ => 0 };", TruthExtension, "operator true", DisplayName = "switch arm guard extension operator true")]
    [DataRow("void M(Thing t) { try { } catch when (t) { } }", TruthExtension, "operator true", DisplayName = "catch filter extension operator true")]
    [DataRow("Thing M(Thing a, Thing b) => a && b;", LogicalExtension, "operator &", DisplayName = "&& extension operators")]
    [DataRow("Thing M(Thing a, Thing b) => a || b;", LogicalExtension, "operator |", DisplayName = "|| extension operators")]
    [DataRow("async System.Threading.Tasks.Task M(Thing t) { await foreach (var x in t) { } }", "public static AsyncEnumerator GetAsyncEnumerator(this Thing t) => null;", "GetAsyncEnumerator", DisplayName = "await foreach GetAsyncEnumerator")]
    public Task MoveThatRebindsAnExtensionMember_IsSkippedWithReason(string usage, string extensionMember, string memberName)
        => AssertExtensionMemberRebindingIsSkippedAsync(usage, extensionMember, memberName, LanguageVersion.Latest);

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("bool M(Countable t) => t is [1];", DisplayName = "list pattern extension indexer")]
    [DataRow("int M(Countable t) => t[^1];", DisplayName = "index from end extension indexer")]
    public Task MoveThatRebindsAPreviewExtensionIndexer_IsSkippedWithReason(string usage)
        => AssertExtensionMemberRebindingIsSkippedAsync(usage, IndexerExtension, "this[", LanguageVersion.Preview);

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("Bag b = [1];", "public static void Add(this Bag b, int x) { }", "Add", DisplayName = "collection expression Add")]
    [DataRow("System.Collections.Generic.List<int> l = [.. new Thing()];", "public static System.Collections.Generic.IEnumerator<int> GetEnumerator(this Thing t) => null;", "GetEnumerator", DisplayName = "spread element GetEnumerator")]
    [DataRow("unsafe void M(Thing t) { fixed (int* p = t) { } }", "public static ref int GetPinnableReference(this Thing t) => ref (new int[1])[0];", "GetPinnableReference", DisplayName = "fixed statement GetPinnableReference")]
    [DataRow("bool M((Thing, int) a, (Thing, int) b) => a == b;", EqualityExtension, "operator ==", DisplayName = "tuple equality element operator ==")]
    [DataRow("bool M((Thing, int) a, (Thing, int) b) => a != b;", EqualityExtension, "operator !=", DisplayName = "tuple inequality element operator !=")]
    public async Task MoveOfAnExtensionMemberTheCompilerCallsUnverifiably_IsSkippedWithReason(string usage, string extensionMember, string memberName)
    {
        // The semantic model does not expose which extension member these implicit calls bind to, so a move that
        // changes where the member is looked up cannot be verified.
        string input = "namespace Company.App\r\n{\r\n    using Ext;\r\n    class C { " + usage + " }\r\n}\r\n";
        Document document = CreateExtensionMemberDocument(input, extensionMember, LanguageVersion.Latest);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        Assert.Contains("using Ext;", result.Reason);
        Assert.Contains("'" + memberName + "'", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("using System;\r\n#if DEBUG\r\nusing System.Diagnostics;\r\n#endif\r\n\r\n" + ServicesNamespace, "DEBUG", DisplayName = "top-level using in an #if block")]
    [DataRow("#if NET48\r\nusing System.Text;\r\n#else\r\nusing System.IO;\r\n#endif\r\n\r\n" + ServicesNamespace, null, DisplayName = "top-level usings in #if/#else branches")]
    [DataRow("using System;\r\n\r\n#if FEATURE\r\n" + ServicesNamespace + "#endif\r\n", "FEATURE", DisplayName = "namespace in an #if block")]
    [DataRow("#region Usings\r\nusing System;\r\n#endregion\r\n\r\n" + ServicesNamespace, null, DisplayName = "top-level usings in a #region")]
    public async Task MoveAcrossPreprocessorDirectives_IsSkippedWithReason(string input, string preprocessorSymbol)
    {
        // Appended after the last top-level using, the moved directive would land inside the #if/#else/#region block
        // (or leave the #if block of its namespace), so it would be missing or duplicated in other configurations.
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: preprocessorSymbol is null ? null : new[] { preprocessorSymbol });
        Document document = CompilingTestProject.CreateDocument(input, parseOptions, new MetadataReference[0], Library);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        Assert.Contains("preprocessor directives", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task MoveInFileWithConditionalCompilation_IsSkippedWithReason()
    {
        // The active configuration (DEBUG undefined) is safe, but in a DEBUG build the move would silently rebind Foo in
        // class D from Company.App.Models.Foo to Company.Foo.
        string input = "namespace Company.App\r\n{\r\n    using Models;\r\n    class C { }\r\n#if DEBUG\r\n    class D { Foo f; }\r\n#endif\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, Library, "namespace Company { public class Foo { } }");
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        Assert.StartsWith("with DEBUG defined: ", result.Reason);
        Assert.Contains("Company.App.Models.Foo -> Company.Foo", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task MoveInFileWithConditionalCompilation_IsMoved_WhenEveryVariantIsSafe()
    {
        string input =
            "namespace Company.App\r\n{\r\n    using Services;\r\n    using Shared;\r\n    class C\r\n    {\r\n        object M()\r\n        {\r\n" +
            "#if DEBUG\r\n            return new Svc();\r\n#else\r\n            return new Util();\r\n#endif\r\n        }\r\n    }\r\n}\r\n";
        Document debugDocument = CompilingTestProject.CreateDocument(input, DefiningSymbols("DEBUG"), new MetadataReference[0], Library);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(debugDocument, input), "The input must compile with DEBUG defined.");

        string result = await AssertMovedAndCompilesAsync(input);

        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(debugDocument, result), "The moved output must compile with DEBUG defined:" + Environment.NewLine + result);
        Assert.AreEqual(
            "using Company.App.Services;\r\nusing Company.Shared;\r\n\r\nnamespace Company.App\r\n{\r\n    class C\r\n    {\r\n        object M()\r\n        {\r\n" +
            "#if DEBUG\r\n            return new Svc();\r\n#else\r\n            return new Util();\r\n#endif\r\n        }\r\n    }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task MoveThatRebindsANameInAVariantOfAnotherDocument_IsSkippedWithReason()
    {
        // The file itself has no conditional compilation, but in a DEBUG build another document declares Company.Foo,
        // which the moved import would be searched after.
        string input = "namespace Company.App\r\n{\r\n    using Models;\r\n    class C { Foo f; }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, Library, "#if DEBUG\r\nnamespace Company { public class Foo { } }\r\n#endif\r\n");
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        Assert.StartsWith("with DEBUG defined: ", result.Reason);
        Assert.Contains("Company.App.Models.Foo -> Company.Foo", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task MoveThatRebindsANameInAVariantOfAReferencedProject_IsSkippedWithReason()
    {
        // In a DEBUG build the referenced project declares Company.Foo, which the moved import would be searched after.
        string input = "namespace Company.App\r\n{\r\n    using Models;\r\n    class C { Foo f; }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocumentReferencingProject(
            input,
            new[] { "#if DEBUG\r\nnamespace Company { public class Foo { } }\r\n#endif\r\n" },
            Library);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        Assert.StartsWith("with DEBUG defined: ", result.Reason);
        Assert.Contains("Company.App.Models.Foo -> Company.Foo", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("#if A && B\r\n" + AppToolsNamespace + "#endif\r\n", DisplayName = "both symbols in one condition")]
    [DataRow("#if A\r\n#if B\r\n" + AppToolsNamespace + "#endif\r\n#endif\r\n", DisplayName = "nested conditions")]
    public async Task MoveThatRebindsANameOnlyWhenTwoSymbolsOfAnotherDocumentAreDefined_IsSkippedWithReason(string otherDocument)
    {
        // Neither A nor B alone changes the declarations of the other document; together they declare
        // Company.App.Tools, which 'using Tools;' means inside Company.App, while at file level it means the global Tools.
        const string GlobalTools = "namespace Tools { public class Tool { } }\r\n";
        string input = "namespace Company.App\r\n{\r\n    using Tools;\r\n    class C { Tool t; }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, Library, GlobalTools, otherDocument);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");
        Document abDocument = CompilingTestProject.CreateDocument(input, DefiningSymbols("A", "B"), new MetadataReference[0], Library, GlobalTools, otherDocument);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(abDocument, input), "The input must compile with A and B defined.");

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        Assert.StartsWith("with A defined, B defined: ", result.Reason);
        Assert.Contains("Company.App.Tools.Tool -> Tools.Tool", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("global using LibB;", "CS0104", DisplayName = "global using")]
    [DataRow("global using Foo = LibB.Foo;", "LibA.Foo -> LibB.Foo", DisplayName = "global using alias")]
    public async Task MoveThatConflictsWithAConditionalGlobalUsingOfAnotherDocument_IsSkippedWithReason(string globalUsing, string expectedReason)
    {
        // Inside N, 'using LibA;' is searched before the global usings; at file level it is searched together with them,
        // so the move is safe without X, but in an X build Foo would become ambiguous or bind to LibB.Foo.
        const string Libraries = "namespace LibA { public class Foo { } }\r\nnamespace LibB { public class Foo { } }\r\n";
        string globalUsings = "#if X\r\n" + globalUsing + "\r\n#endif\r\n";
        string input = "namespace N\r\n{\r\n    using LibA;\r\n    class C { Foo f; }\r\n}\r\n";
        string moved = "using LibA;\r\n\r\nnamespace N\r\n{\r\n    class C { Foo f; }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, Library, Libraries, globalUsings);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, moved), "The moved text must compile without X.");
        Document xDocument = CompilingTestProject.CreateDocument(input, DefiningSymbols("X"), new MetadataReference[0], Library, Libraries, globalUsings);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(xDocument, input), "The input must compile with X defined.");

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        Assert.StartsWith("with X defined: ", result.Reason);
        Assert.Contains(expectedReason, result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("#if A || B\r\n#elif C && (D || !E)\r\n#endif\r\n", "", DisplayName = "five symbols in the file")]
    [DataRow("#if A || B\r\n#elif C && D\r\n#endif\r\n", "namespace Company\r\n{\r\n#if E\r\n    class Extra { }\r\n#endif\r\n}\r\n", DisplayName = "fifth symbol changes the declarations of another document")]
    [DataRow(
        "#if A || B\r\n#elif C && D\r\n#endif\r\n",
        "namespace Company\r\n{\r\n    public static class Twice\r\n    {\r\n#if E\r\n        public static int Of(this long x) => 2;\r\n#else\r\n        public static int Of(this int x) => 2;\r\n#endif\r\n    }\r\n}\r\n",
        DisplayName = "fifth symbol changes only a signature in another document")]
    [DataRow(
        "",
        "#if A && B && C && D && E && F && G\r\nnamespace Company { class Extra { } }\r\n#endif\r\n",
        DisplayName = "seven symbols change the declarations of another document only together")]
    public async Task MoveDependingOnMoreThanFourConditionalCompilationSymbols_IsSkippedWithReason(string conditions, string otherDocument)
    {
        string input = "namespace Company.App\r\n{\r\n    using Services;\r\n    class C\r\n    {\r\n        Svc s;\r\n        void M()\r\n        {\r\n" + conditions + "        }\r\n    }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, Library, otherDocument);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        Assert.Contains("A, B, C, D, E", result.Reason);
        Assert.Contains("more than 4 conditional-compilation symbols are too many variants to verify", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task SymbolThatDoesNotChangeTheDeclarationsOfAnotherDocument_IsNotAVariant()
    {
        // E only changes a method body of another document, so only the 16 variants of A, B, C and D are verified.
        string input =
            "namespace Company.App\r\n{\r\n    using Services;\r\n    class C\r\n    {\r\n        Svc s;\r\n        void M()\r\n        {\r\n" +
            "#if A || B\r\n#elif C && D\r\n#endif\r\n        }\r\n    }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(
            input,
            Library,
            "namespace Company\r\n{\r\n    class Other\r\n    {\r\n        int M()\r\n        {\r\n#if E\r\n            return 1;\r\n#else\r\n            return 2;\r\n#endif\r\n        }\r\n    }\r\n}\r\n");

        string result = await AssertMovedAndCompilesAsync(document);

        Assert.StartsWith("using Company.App.Services;\r\n\r\nnamespace Company.App\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("", false, DisplayName = "empty source")]
    [DataRow("namespace N\r\n{\r\n    using System;\r\n    class C { }\r\n}\r\n", true, DisplayName = "using inside a namespace")]
    [DataRow("using System;\r\n\r\nnamespace N\r\n{\r\n    class C { }\r\n}\r\n", false, DisplayName = "using outside the namespace")]
    [DataRow("#if DEBUG\r\nusing System;\r\n#endif\r\n\r\nnamespace N\r\n{\r\n    class C { }\r\n}\r\n", false, DisplayName = "#if block outside the namespace")]
    [DataRow("namespace N\r\n{\r\n    class C\r\n    {\r\n#if DEBUG\r\n        void M() { }\r\n#endif\r\n    }\r\n}\r\n", false, DisplayName = "#if block inside a type")]
    [DataRow("namespace N\r\n{\r\n#if DEBUG\r\n    using System;\r\n#endif\r\n    class C { }\r\n}\r\n", true, DisplayName = "using in an #if block of a block-scoped namespace")]
    [DataRow("namespace N;\r\n\r\n#if DEBUG\r\nusing System;\r\n#endif\r\n\r\nclass C { }\r\n", true, DisplayName = "using in an #if block of a file-scoped namespace")]
    [DataRow("namespace N\r\n{\r\n#if !DEBUG\r\n#else\r\n    using System;\r\n#endif\r\n}\r\n", true, DisplayName = "using in an #else block of a namespace without members")]
    [DataRow("namespace N\r\n{\r\n#if DEBUG\r\n    class D { }\r\n#endif\r\n    class C { }\r\n}\r\n", false, DisplayName = "#if block of types in front of the first member")]
    [DataRow("namespace N;\r\n\r\n#if DEBUG\r\nclass D { }\r\n#else\r\nclass C { }\r\n#endif\r\n", false, DisplayName = "#if block of types in a file-scoped namespace")]
    [DataRow("namespace N\r\n{\r\n#if !DEBUG\r\n    class C { }\r\n#endif\r\n}\r\n", false, DisplayName = "active #if block around the first member")]
    public void HasUsingsInsideNamespace_CountsUsingsInTheUsingSectionIncludingDisabledOnes(string source, bool expected)
    {
        // The precheck parses without the project's symbols: a using in an #if block is disabled text there, but may be
        // active in the project, where the move has to decide (and skip with a reason).
        Assert.AreEqual(expected, UsingDirectivePlacementConverter.HasUsingsInsideNamespace(source));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task DocumentationCommentFileHeader_IsKept()
    {
        string input = "/// <copyright file=\"C.cs\">x</copyright>\r\nnamespace Company.App\r\n{\r\n    using Services;\r\n    class C { Svc s; }\r\n}\r\n";

        string result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual(
            "/// <copyright file=\"C.cs\">x</copyright>\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task DocumentationCommentOfTheFirstType_IsNotHandedToAMovedUsing()
    {
        // The comment in front of the first token documents class Top; handing it to the moved directive as the file
        // header would strip Top's documentation.
        string input = "/// <summary>Top</summary>\r\nclass Top { }\r\n\r\nnamespace Company.App\r\n{\r\n    using Services;\r\n    class C { Svc s; }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, Library);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow(
        "using System; // for Action\r\n\r\nnamespace Company.App\r\n{\r\n    using Services;\r\n    class C { Svc s; Action a; }\r\n}\r\n",
        "using System; // for Action\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; Action a; }\r\n}\r\n",
        DisplayName = "end-of-line comment of a top-level using")]
    [DataRow(
        "namespace Company.App\r\n{\r\n    using Services; // the services\r\n    using Shared;   /* shared */\r\n    class C { Svc s; Util u; }\r\n}\r\n",
        "using Company.App.Services; // the services\r\nusing Company.Shared;   /* shared */\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; Util u; }\r\n}\r\n",
        DisplayName = "end-of-line comments of moved usings")]
    [DataRow(
        "using System;\r\n\r\nnamespace Company.App\r\n{\r\n    using Shared;\r\n    // Services of the app\r\n    using Services;\r\n    class C { Svc s; Util u; Action a; }\r\n}\r\n",
        "using System;\r\nusing Company.Shared;\r\n// Services of the app\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; Util u; Action a; }\r\n}\r\n",
        DisplayName = "comment line above a moved using")]
    [DataRow(
        "// header\r\n\r\nnamespace Company.App\r\n{\r\n    /* Services\r\n       of the app */\r\n    using Services;\r\n    class C { Svc s; }\r\n}\r\n",
        "// header\r\n\r\n/* Services\r\n       of the app */\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; }\r\n}\r\n",
        DisplayName = "multi-line comment above the first moved using, below the file header")]
    [DataRow(
        "namespace Company.App\r\n{\r\n    /* services */ using Services;\r\n    class C { Svc s; }\r\n}\r\n",
        "/* services */ using Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; }\r\n}\r\n",
        DisplayName = "comment in front of a moved using on its line")]
    [DataRow(
        "namespace Company.App\r\n{\r\n    /// <summary>Services</summary>\r\n    using Services;\r\n    class C { Svc s; }\r\n}\r\n",
        "/// <summary>Services</summary>\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; }\r\n}\r\n",
        DisplayName = "documentation comment above a moved using")]
    [DataRow(
        "using System;\r\n\r\nnamespace Company.App\r\n{\r\n    using System; // needed for Action\r\n    using Services;\r\n    class C { Svc s; Action a; }\r\n}\r\n",
        "using System; // needed for Action\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; Action a; }\r\n}\r\n",
        DisplayName = "end-of-line comment of a moved using that duplicates a top-level using")]
    [DataRow(
        "namespace Company.App\r\n{\r\n    using System;\r\n    class C1 { Action a; }\r\n}\r\n\r\nnamespace Company.Other\r\n{\r\n    // for Func\r\n    using System;\r\n    class C2 { Func<int> f; }\r\n}\r\n",
        "// for Func\r\nusing System;\r\n\r\nnamespace Company.App\r\n{\r\n    class C1 { Action a; }\r\n}\r\n\r\nnamespace Company.Other\r\n{\r\n    class C2 { Func<int> f; }\r\n}\r\n",
        DisplayName = "comment line above a moved using that duplicates an earlier moved using")]
    public async Task CommentsOfUsings_AreKept(string input, string expected)
    {
        string result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task FileHeader_StaysAboveExternAliases()
    {
        MetadataReference aliased = CompilingTestProject.CreateAliasedReference("Ext", "namespace Ext { public class Thing { } }", "V1");
        string input = "// Copyright (c) 2026\r\nextern alias V1;\r\n\r\nnamespace Company.App\r\n{\r\n    using V1::Ext;\r\n    class C { Thing t; }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, LanguageVersion.Latest, new[] { aliased }, Library);

        string result = await AssertMovedAndCompilesAsync(document);

        Assert.AreEqual(
            "// Copyright (c) 2026\r\nextern alias V1;\r\nusing V1::Ext;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Thing t; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task UsingBelowFileHeader_IsStillDeduplicated()
    {
        string input = "// header\r\nusing System;\r\n\r\nnamespace Company.App\r\n{\r\n    using System;\r\n    using Services;\r\n    class C { Svc s; Action a; }\r\n}\r\n";

        string result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual(
            "// header\r\nusing System;\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; Action a; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task QualifiedAliasTarget_StaysVerbatim_WhenItReceivesTheFileHeader()
    {
        string input = "// header\r\n\r\nnamespace Company.App\r\n{\r\n    using Str = System.String;\r\n    class C { Str s; }\r\n}\r\n";

        string result = await AssertMovedAndCompilesAsync(input);

        Assert.StartsWith("// header\r\n\r\nusing Str = System.String;\r\n\r\nnamespace Company.App\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task NullDocument_Throws()
    {
        ArgumentNullException exception = await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => MoveAsync(null));

        Assert.AreEqual("document", exception.ParamName);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task TopLevelStatementsFileWithoutNamespace_ReportsNothingToMove()
    {
        string input = "using System;\r\nusing System.Text;\r\n\r\nConsole.WriteLine(new StringBuilder(\"namespace N { using X; }\"));\r\n\r\nstatic int Twice(int x) => x * 2;\r\n\r\nrecord R(int X);\r\n";

        UsingDirectivePlacementResult result = await MoveAsync(CompilingTestProject.CreateDocument(input, Library));

        Assert.AreEqual(UsingDirectivePlacementStatus.NothingToMove, result.Status);
        Assert.IsNull(result.Text);
        Assert.IsNull(result.Reason);
        Assert.IsFalse(UsingDirectivePlacementConverter.HasUsingsInsideNamespace(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task TopLevelStatementsFileWithANamespace_MovesTheNamespaceUsingsBelowTheFileLevelOnes()
    {
        // A program's top-level statements (an error in this library project either way) keep compiling as before.
        string input =
            "using System;\r\n\r\nConsole.WriteLine(new Company.App.C());\r\n\r\nstatic void Local() { }\r\n\r\n" +
            "namespace Company.App\r\n{\r\n    using Services;\r\n    public class C { Svc s; }\r\n}\r\n";

        string result = await AssertMovedWithoutNewErrorsAsync(CompilingTestProject.CreateDocument(input, Library));

        Assert.AreEqual(
            "using System;\r\nusing Company.App.Services;\r\n\r\nConsole.WriteLine(new Company.App.C());\r\n\r\nstatic void Local() { }\r\n\r\n" +
            "namespace Company.App\r\n{\r\n    public class C { Svc s; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task PreexistingCompileErrors_DoNotPreventTheMove()
    {
        string input = "namespace Company.App\r\n{\r\n    using Services;\r\n    class C { Svc s; int x = \"text\"; int y = \"text\"; }\r\n}\r\n";

        string result = await AssertMovedWithoutNewErrorsAsync(CompilingTestProject.CreateDocument(input, Library));

        Assert.AreEqual("using Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; int x = \"text\"; int y = \"text\"; }\r\n}\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task UsingsOfSeveralNamespaces_AreMergedBelowTheGlobalUsings_WithoutDuplicates()
    {
        string input =
            "global using System;\r\n\r\nnamespace Company.App\r\n{\r\n    using System.Text;\r\n    using static System.Math;\r\n    class C1 { StringBuilder b; double d = Abs(-1); Action a; }\r\n}\r\n\r\n" +
            "namespace Company.Other\r\n{\r\n    using System.Text;\r\n    using Services = Company.App.Services;\r\n    class C2 { StringBuilder b; Services.Svc s; }\r\n}\r\n";

        string result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual(
            "global using System;\r\nusing System.Text;\r\nusing static System.Math;\r\nusing Services = Company.App.Services;\r\n\r\n" +
            "namespace Company.App\r\n{\r\n    class C1 { StringBuilder b; double d = Abs(-1); Action a; }\r\n}\r\n\r\n" +
            "namespace Company.Other\r\n{\r\n    class C2 { StringBuilder b; Services.Svc s; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task WhitespaceOnlyLineBetweenTheUsingsAndTheNamespace_BecomesOneBlankLine()
    {
        string input = "using System;\r\n   \r\nnamespace Company.App\r\n{\r\n    using Services;\r\n    class C { Svc s; Action a; }\r\n}\r\n";

        string result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual("using System;\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; Action a; }\r\n}\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task AliasesToArrayPointerFunctionPointerAndNestedTypes_AndCodeTheCheckBinds_AreMoved()
    {
        string input =
            "namespace Company.App\r\n{\r\n" +
            "    using Arr = System.Int32[];\r\n" +
            "    using unsafe Ptr = System.Int32*;\r\n" +
            "    using unsafe Fn = delegate*<void>;\r\n" +
            "    using Keys = System.Collections.Generic.Dictionary<int, string>.KeyCollection;\r\n" +
            "    using Models;\r\n" +
            "\r\n" +
            "    unsafe class C\r\n    {\r\n" +
            "        Arr a; Ptr p; Fn f; Keys k;\r\n" +
            "        int M(bool flag, Point point)\r\n        {\r\n" +
            "            var (x, y) = point;\r\n" +
            "            dynamic boxed = x;\r\n" +
            "            if (!flag) { return Helpers.Twice(x); }\r\n" +
            "            for (;;) { return y; }\r\n" +
            "        }\r\n" +
            "    }\r\n\r\n" +
            "    struct Point { public void Deconstruct(out int x, out int y) { x = 1; y = 2; } }\r\n" +
            "}\r\n";

        string result = await AssertMovedAndCompilesAsync(input);

        Assert.StartsWith(
            "using Arr = System.Int32[];\r\nusing unsafe Ptr = System.Int32*;\r\nusing unsafe Fn = delegate*<void>;\r\n" +
            "using Keys = System.Collections.Generic.Dictionary<int, string>.KeyCollection;\r\nusing Company.App.Models;\r\n\r\nnamespace Company.App\r\n{\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("using X = Missing;", DisplayName = "alias to a missing type")]
    [DataRow("using L = System.Collections.Generic.List<Missing>;", DisplayName = "alias to a type argument that is missing")]
    [DataRow("using A = Missing[];", DisplayName = "alias to an array of a missing type")]
    [DataRow("using unsafe P = Missing*;", DisplayName = "alias to a pointer to a missing type")]
    [DataRow("using N = Company.App.Outer<Missing>.Inner;", DisplayName = "alias to a nested type of a missing type argument")]
    public async Task AliasToAnUnresolvableType_IsSkippedWithReason(string alias)
    {
        string input = "namespace Company.App\r\n{\r\n    using Services;\r\n    " + alias + "\r\n    class C { Svc s; }\r\n    class Outer<T> { public class Inner { } }\r\n}\r\n";

        UsingDirectivePlacementResult result = await MoveAsync(CompilingTestProject.CreateDocument(input, Library));

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        Assert.AreEqual("'" + alias + "' cannot be resolved semantically", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task UsingThatOnlyAnotherVariantCompilesInsideALaterNamespace_IsSkippedWithReason()
    {
        // The #if block lies after the directives that move, so the layout is supported; with DEBUG defined, the using
        // of namespace B would be left behind inside it.
        string input =
            "namespace A\r\n{\r\n    using System;\r\n    class C { Action a; }\r\n}\r\n\r\n" +
            "namespace B\r\n{\r\n#if DEBUG\r\n    using System.Text;\r\n#endif\r\n    class D { }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, Library);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        Assert.AreEqual("with DEBUG defined: 'using System.Text;' would stay inside the namespace", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task UsingThatAnotherVariantCannotResolve_IsSkippedWithReason()
    {
        string input = "namespace Company.App\r\n{\r\n    using Diag;\r\n    class C { }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, Library, "#if !RELEASE\r\nnamespace Company.App.Diag { public class D { } }\r\n#endif\r\n");
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        Assert.AreEqual("with RELEASE defined: 'using Diag;' cannot be resolved semantically", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task RebindingOfALongStatement_IsDescribedByItsShortenedText()
    {
        string input =
            "namespace Company.App\r\n{\r\n    using Ext;\r\n    class C { void M(Thing thing) { foreach (var element in thing) { Log(element); } } static void Log(int value) { } }\r\n}\r\n";
        Document document = CreateExtensionMemberDocument(input, "public static System.Collections.Generic.IEnumerator<int> GetEnumerator(this Thing t) => null;", LanguageVersion.Latest);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.StartsWith("moving them would change what 'foreach (var element in thing) { Log(ele...' refers to (", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("namespace N;\r\n#if DEBUG\r\nusing System;\r\n#endif\r\n", true, DisplayName = "using in an #if block of a file-scoped namespace without members")]
    [DataRow("#region File\r\nnamespace N\r\n{\r\n    class C { }\r\n}\r\n#endregion\r\n", false, DisplayName = "#region around a namespace without usings")]
    [DataRow("using System;\r\n\r\nConsole.WriteLine(\"namespace N { using X; }\");\r\n", false, DisplayName = "top-level statements")]
    [DataRow("   \r\n", false, DisplayName = "whitespace-only file")]
    [DataRow("namespace Outer\r\n{\r\n    namespace Inner\r\n    {\r\n        using System;\r\n    }\r\n}\r\n", true, DisplayName = "using in a nested namespace")]
    public void HasUsingsInsideNamespace_EdgeCases(string source, bool expected)
    {
        Assert.AreEqual(expected, UsingDirectivePlacementConverter.HasUsingsInsideNamespace(source));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("using System;\r\n\r\nConsole.WriteLine(\"namespace N { }\");\r\n", DisplayName = "top-level program")]
    [DataRow("namespace N;\r\n\r\nnamespace M\r\n{\r\n    using System;\r\n    class C { Action a; }\r\n}\r\n", DisplayName = "file-scoped and block-scoped namespaces")]
    [DataRow("namespace Company.App\r\n{\r\n    using Services\r\n    class C { Svc s; }\r\n", DisplayName = "missing semicolon and closing brace")]
    [DataRow("namespace Company.App\r\n{\r\n    using Services;\r\n    class C { Svc s; }\r\n    unsafe struct S { fixed int b[new int[0] is [1, .. var r] ? 1 : 2]; }\r\n}\r\n", DisplayName = "list and slice patterns in a non-constant fixed buffer size")]
    public async Task FileWithErrorsOrNoNamespace_GetsNoNewCompileErrors(string input)
    {
        Document document = CompilingTestProject.CreateDocument(input, Library);
        IReadOnlyList<string> before = await CompilingTestProject.GetCompileErrorsAsync(document, input);

        UsingDirectivePlacementResult result = await MoveAsync(document);

        if (result.Status == UsingDirectivePlacementStatus.Moved)
        {
            CollectionAssert.IsSubsetOf((System.Collections.ICollection)await CompilingTestProject.GetCompileErrorsAsync(document, result.Text), (System.Collections.ICollection)before, result.Text);
        }
        else
        {
            Assert.IsNull(result.Text);
        }
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("System.Console.WriteLine(1);\r\n#if DEBUG\r\nSystem.Console.WriteLine(2);\r\n#endif\r\nint\r\n", DisplayName = "top-level statements and an incomplete member")]
    [DataRow("namespace Company\r\n{\r\n    enum Level\r\n    {\r\n        Low,\r\n#if DEBUG\r\n        Trace,\r\n#endif\r\n    }\r\n}\r\n", DisplayName = "enum member")]
    [DataRow("namespace Company\r\n{\r\n    class Base { public Base(int x) { } }\r\n    class Derived : Base\r\n    {\r\n#if DEBUG\r\n        public Derived() : base(1) { }\r\n#else\r\n        public Derived() : base(2) { }\r\n#endif\r\n    }\r\n}\r\n", DisplayName = "constructor initializer")]
    [DataRow("namespace Company.Legacy\r\n{\r\n    extern alias Old;\r\n    using System;\r\n#if DEBUG\r\n    class D { }\r\n#endif\r\n}\r\n", DisplayName = "namespace with an extern alias and a using")]
    [DataRow("namespace Company\r\n{\r\n    class Options\r\n    {\r\n#if DEBUG\r\n        public void M(int level = 1) { }\r\n#endif\r\n    }\r\n}\r\n", DisplayName = "method with a default parameter value")]
    public async Task OtherDocumentWhoseConditionalCodeDeclaresNothingNew_OrOnlyAMember_IsVerified(string otherDocument)
    {
        string input = "namespace Company.App\r\n{\r\n    using Services;\r\n    class C { Svc s; }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, Library, otherDocument);

        string result = await AssertMovedWithoutNewErrorsAsync(document);

        Assert.AreEqual("using Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; }\r\n}\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task MoveInADebugBuild_IsVerifiedWithDebugUndefinedAsWell()
    {
        // DEBUG is defined in the active configuration; without it, the other document declares Company.Foo, which the
        // moved import would be searched after.
        string input = "namespace Company.App\r\n{\r\n    using Models;\r\n    class C { Foo f; }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(input, DefiningSymbols("DEBUG"), new MetadataReference[0], Library, "#if !DEBUG\r\nnamespace Company { public class Foo { } }\r\n#endif\r\n");
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.StartsWith("with DEBUG undefined: ", result.Reason);
        Assert.Contains("Company.App.Models.Foo -> Company.Foo", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task FixedSizeBufferWithAConditionalSize_AndStaticClassesWithoutExtensionMethods_DoNotBlockTheMove()
    {
        string input =
            "namespace Company.App\r\n{\r\n    using Tools;\r\n\r\n    unsafe struct S { fixed int buffer[true ? 1 : 2]; }\r\n\r\n" +
            "    class C { int[] a = [1]; void M() { Adder.Add(1); } }\r\n}\r\n";
        Document document = CompilingTestProject.CreateDocument(
            input,
            Library,
            "namespace Company.App.Tools\r\n{\r\n    public static class Adder\r\n    {\r\n        public static void Add(int x) { }\r\n        public static int GetEnumerator;\r\n        public class Nested { }\r\n    }\r\n}\r\n");

        string result = await AssertMovedAndCompilesAsync(document);

        Assert.StartsWith("using Company.App.Tools;\r\n\r\nnamespace Company.App\r\n{\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task MoveThatWouldMakeAnUnresolvedNameResolve_IsSkippedWithReason()
    {
        // Top is outside the namespace, so 'Svc' does not resolve there; at file level the moved import would make it
        // resolve, changing what the file means even though no error is added.
        string input = "namespace Company.App\r\n{\r\n    using Services;\r\n    class C { Svc s; }\r\n}\r\n\r\nclass Top { Svc s; }\r\n";

        UsingDirectivePlacementResult result = await MoveAsync(CompilingTestProject.CreateDocument(input, Library));

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        Assert.AreEqual("moving them would change what 'Svc' refers to (nothing -> Company.App.Services.Svc)", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task MoveThatWouldMakeAFailingNestedDeconstructionBind_IsSkippedWithReason()
    {
        // Top is outside the namespace, so its nested deconstruction finds no Deconstruct for Thing (an error the move
        // would remove): at file level the moved import would supply one, changing what the file means.
        string input =
            "namespace Company.App\r\n{\r\n    using Ext;\r\n    class C { }\r\n}\r\n\r\n" +
            "class Top { void M((Company.Thing, int) p) { var ((a, b), c) = p; } }\r\n";
        Document document = CreateExtensionMemberDocument(input, DeconstructExtension, LanguageVersion.Latest);

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        Assert.AreEqual("moving them would change what 'var ((a, b), c) = p' refers to (nothing -> Company.App.Ext.E.Deconstruct(Company.Thing, out int, out int))", result.Reason);
    }

    private static async Task<string> AssertMovedWithoutNewErrorsAsync(Document document)
    {
        string input = (await document.GetTextAsync()).ToString();
        IReadOnlyList<string> before = await CompilingTestProject.GetCompileErrorsAsync(document, input);

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Moved, result.Status, result.Reason);
        CollectionAssert.AreEquivalent((System.Collections.ICollection)before, (System.Collections.ICollection)await CompilingTestProject.GetCompileErrorsAsync(document, result.Text), result.Text);

        return result.Text;
    }

    private static Task<UsingDirectivePlacementResult> MoveAsync(Microsoft.CodeAnalysis.Document document)
        => new UsingDirectivePlacementConverter().MoveUsingsOutsideAsync(document, CancellationToken.None);

    private static CSharpParseOptions DefiningSymbols(params string[] symbols)
        => new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: symbols);

    private static Task<string> AssertMovedAndCompilesAsync(string input)
        => AssertMovedAndCompilesAsync(CompilingTestProject.CreateDocument(input, Library));

    private static async Task<string> AssertMovedAndCompilesAsync(Microsoft.CodeAnalysis.Document document)
    {
        string input = (await document.GetTextAsync()).ToString();
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Moved, result.Status, result.Reason);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, result.Text), "The moved output must compile:" + Environment.NewLine + result.Text);

        return result.Text;
    }

    private static async Task AssertExtensionMemberRebindingIsSkippedAsync(string usage, string extensionMember, string memberName, LanguageVersion languageVersion)
    {
        // Inside Company.App, 'using Ext;' is searched before the enclosing namespace Company; at file level it is
        // searched after it, so the same-named extension member of Company.E2 would silently win - without any error.
        string input = "namespace Company.App\r\n{\r\n    using Ext;\r\n    class C { " + usage + " }\r\n}\r\n";
        Document document = CreateExtensionMemberDocument(input, extensionMember, languageVersion);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        UsingDirectivePlacementResult result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        Assert.Contains(memberName, result.Reason);
        Assert.Contains("Company.App.Ext.E.", result.Reason);
        Assert.Contains("Company.E2.", result.Reason);
    }

    /// <summary>
    /// Creates <paramref name="input" /> in a project where <c>Company.App.Ext.E</c> and <c>Company.E2</c> both declare
    /// <paramref name="extensionMember" />.
    /// </summary>
    private static Microsoft.CodeAnalysis.Document CreateExtensionMemberDocument(string input, string extensionMember, LanguageVersion languageVersion)
        => CompilingTestProject.CreateDocument(
            input,
            languageVersion,
            new MetadataReference[0],
            Library,
            CompilingTestProject.IndexAndRangeSource,
            ExtensionReceivers,
            "namespace Company.App.Ext { public static class E { " + extensionMember + " } }\r\n",
            "namespace Company { public static class E2 { " + extensionMember + " } }\r\n");

    private static void AssertNoErrors(IReadOnlyList<string> errors, string message)
        => Assert.IsEmpty(errors, message + Environment.NewLine + string.Join(Environment.NewLine, errors));
}
