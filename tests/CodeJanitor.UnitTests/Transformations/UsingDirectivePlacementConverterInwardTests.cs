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
/// Unit tests for <see cref="UsingDirectivePlacementConverter.MoveUsingsInsideAsync" /> (the
/// <c>csharp_using_directive_placement = inside_namespace</c> direction). Every moved result is compiled together with
/// <see cref="Library" /> and must not contain compile errors.
/// </summary>
[TestClass]
public sealed class UsingDirectivePlacementConverterInwardTests
{
    private const string Library =
        "namespace Company.App.Services { public class Svc { } }\r\n" +
        "namespace Company.App.Models { public class Foo { } }\r\n" +
        "namespace Company.App.Shared { public class Other { } }\r\n" +
        "namespace Shared { public class Util { } }\r\n" +
        "namespace Models { public class Bar { } public static class Helpers { public static int Thrice(int x) => x * 3; } }\r\n";

    /// <summary>
    /// A type of the enclosing namespace <c>Company</c> that the import <c>Company.App.Models</c> shadows once it is
    /// searched inside <c>namespace Company.App</c>, before the members of <c>Company</c>.
    /// </summary>
    private const string EnclosingFoo = "namespace Company { public class Foo { } }\r\n";

    private const string AppNamespace = "namespace Company.App\r\n{\r\n    class C { Action a; }\r\n}\r\n";

    /// <summary>
    /// Receiver types for the extension members that <c>Ext.E</c> and <c>Company.E2</c> both declare: each lacks the
    /// member the tested construct calls implicitly, so only the extension member can provide it.
    /// </summary>
    private const string ExtensionReceivers =
        "namespace Company\r\n{\r\n" +
        "    public class Thing { }\r\n" +
        "    public class Bag : System.Collections.IEnumerable { public System.Collections.IEnumerator GetEnumerator() => null; }\r\n" +
        "}\r\n";

    private const string DeconstructExtension = "public static void Deconstruct(this Thing t, out int a, out int b) { a = 0; b = 0; }";

    private const string EqualityExtension = "extension(Thing) { public static bool operator ==(Thing a, Thing b) => true; public static bool operator !=(Thing a, Thing b) => false; }";

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task FileLevelUsings_AreMovedIntoTheBlockScopedNamespace()
    {
        var input = "using System.Text;\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; StringBuilder b; }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual(
            "namespace Company.App\r\n{\r\n    using System.Text;\r\n    using Company.App.Services;\r\n\r\n    class C { Svc s; StringBuilder b; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task NameThatWouldResolveRelativeToTheNamespace_IsGlobalQualified()
    {
        // Inside Company.App, 'Shared' means Company.App.Shared; the file-level directive imports the global Shared.
        var input = "using Shared;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Util u; }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual("namespace Company.App\r\n{\r\n    using global::Shared;\r\n\r\n    class C { Util u; }\r\n}\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task AliasAndUsingStaticTargets_AreGlobalQualifiedOnlyWhenTheyWouldRebind()
    {
        // Inside Company.App, 'Models' means Company.App.Models; System.String means the same everywhere.
        var input =
            "using X = Models.Bar;\r\nusing static Models.Helpers;\r\nusing Str = System.String;\r\n\r\n" +
            "namespace Company.App\r\n{\r\n    class C { X x; Str s; int y = Thrice(1); }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual(
            "namespace Company.App\r\n{\r\n    using X = global::Models.Bar;\r\n    using static global::Models.Helpers;\r\n    using Str = System.String;\r\n\r\n" +
            "    class C { X x; Str s; int y = Thrice(1); }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task UsingThatImportsWhatTheNamespaceAlreadyImports_IsDropped()
    {
        // 'using System.Text;' is written the same inside; 'using Company.App.Services;' imports what 'using Services;'
        // already imports there.
        var input =
            "using System;\r\nusing System.Text;\r\nusing Company.App.Services;\r\n\r\n" +
            "namespace Company.App\r\n{\r\n    using System.Text;\r\n    using Services;\r\n\r\n    class C { Svc s; StringBuilder b; Action a; }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual(
            "namespace Company.App\r\n{\r\n    using System;\r\n    using System.Text;\r\n    using Services;\r\n\r\n    class C { Svc s; StringBuilder b; Action a; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task CommentOfADroppedDuplicate_GoesToTheDirectiveInTheNamespace()
    {
        var input = "using System; // for Action\r\n\r\nnamespace Company.App\r\n{\r\n    using System;\r\n    class C { Action a; }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual("namespace Company.App\r\n{\r\n    using System; // for Action\r\n    class C { Action a; }\r\n}\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task FileHeaderAndCrLf_StayInPlace()
    {
        var input = "// Copyright (c) 2026\r\n\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual(
            "// Copyright (c) 2026\r\n\r\nnamespace Company.App\r\n{\r\n    using Company.App.Services;\r\n\r\n    class C { Svc s; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task FileHeaderAndLf_StayInPlace()
    {
        var input = "// header\n\nusing Company.App.Services;\n\nnamespace Company.App\n{\n    class C { Svc s; }\n}\n";

        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual("// header\n\nnamespace Company.App\n{\n    using Company.App.Services;\n\n    class C { Svc s; }\n}\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task DocumentationCommentFileHeader_StaysAtTheTopOfTheFile()
    {
        var input = "/// <copyright file=\"C.cs\">x</copyright>\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual(
            "/// <copyright file=\"C.cs\">x</copyright>\r\nnamespace Company.App\r\n{\r\n    using Company.App.Services;\r\n\r\n    class C { Svc s; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task DocumentationCommentInFrontOfTheNamespace_StaysInFrontOfIt()
    {
        var input = "using System;\r\n\r\n/// <summary>App types</summary>\r\nnamespace Company.App\r\n{\r\n    class C { Action a; }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual("/// <summary>App types</summary>\r\nnamespace Company.App\r\n{\r\n    using System;\r\n\r\n    class C { Action a; }\r\n}\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task CommentsOfMovedUsings_AreKeptAndIndented()
    {
        var input =
            "using System; // for Action\r\n// The services\r\nusing Company.App.Services;\r\n\r\n" +
            "namespace Company.App\r\n{\r\n    class C { Svc s; Action a; }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual(
            "namespace Company.App\r\n{\r\n    using System; // for Action\r\n    // The services\r\n    using Company.App.Services;\r\n\r\n    class C { Svc s; Action a; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task CommentsOfAMovedUsingThatDuplicatesAnEarlierMovedUsing_GoToTheDirectiveThatStays()
    {
        // 'using global::Company.App.Services;' imports what 'using Company.App.Services;' already imports.
        var input =
            "using System;\r\n// The services\r\nusing Company.App.Services;\r\n// The services again\r\nusing global::Company.App.Services; /* duplicate */\r\n\r\n" +
            "namespace Company.App\r\n{\r\n    class C { Svc s; Action a; }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual(
            "namespace Company.App\r\n{\r\n    using System;\r\n    // The services\r\n    // The services again\r\n    using Company.App.Services; /* duplicate */\r\n\r\n" +
            "    class C { Svc s; Action a; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow(
        "using System;\r\n// The services\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n\tclass C { Svc s; Action a; }\r\n}\r\n",
        "namespace Company.App\r\n{\r\n\tusing System;\r\n\t// The services\r\n\tusing Company.App.Services;\r\n\r\n\tclass C { Svc s; Action a; }\r\n}\r\n",
        DisplayName = "tab-indented members")]
    [DataRow(
        "using System;\r\n\r\nnamespace Company.App\r\n{\r\n  using Services;\r\n\r\n  class C { Svc s; Action a; }\r\n}\r\n",
        "namespace Company.App\r\n{\r\n  using System;\r\n  using Services;\r\n\r\n  class C { Svc s; Action a; }\r\n}\r\n",
        DisplayName = "two-space-indented using already in the namespace")]
    public async Task MovedUsings_TakeTheIndentationOfTheNamespaceBody(string input, string expected)
    {
        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task FileLevelUsings_AreMovedBelowTheFileScopedNamespace_WithoutIndentation()
    {
        var input = "using Shared;\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App;\r\n\r\nclass C { Svc s; Util u; }\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual("namespace Company.App;\r\n\r\nusing global::Shared;\r\nusing Company.App.Services;\r\n\r\nclass C { Svc s; Util u; }\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task FileLevelUsings_JoinTheUsingsOfTheFileScopedNamespace()
    {
        var input = "using System;\r\n\r\nnamespace Company.App;\r\n\r\nusing Services;\r\n\r\nclass C { Svc s; Action a; }\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual("namespace Company.App;\r\n\r\nusing System;\r\nusing Services;\r\n\r\nclass C { Svc s; Action a; }\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task GlobalUsingsAndExternAliases_StayAtFileLevel()
    {
        var aliased = CompilingTestProject.CreateAliasedReference("Ext", "namespace Ext { public class Thing { } }", "V1");
        var input = "extern alias V1;\r\nglobal using System;\r\nusing V1::Ext;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Thing t; Action a; }\r\n}\r\n";
        var document = CompilingTestProject.CreateDocument(input, LanguageVersion.Latest, new[] { aliased }, Library);

        var result = await AssertMovedAndCompilesAsync(document);

        Assert.AreEqual(
            "extern alias V1;\r\nglobal using System;\r\n\r\nnamespace Company.App\r\n{\r\n    using V1::Ext;\r\n\r\n    class C { Thing t; Action a; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ExternAliasQualifiedUsing_IsNotDroppedAsADuplicateOfTheSameNamedNamespaceOfAnotherAssembly()
    {
        // V1::Ext (of the aliased assembly) and Ext (of the project) read the same but are different namespaces, so the
        // moved directive does not import what 'using Ext;' already imports.
        var aliased = CompilingTestProject.CreateAliasedReference("Ext", "namespace Ext { public class Thing { } }", "V1");
        var input = "extern alias V1;\r\nusing V1::Ext;\r\n\r\nnamespace Company.App\r\n{\r\n    using Ext;\r\n\r\n    class C { Thing t; Other o; }\r\n}\r\n";
        var document = CompilingTestProject.CreateDocument(input, LanguageVersion.Latest, new[] { aliased }, Library, "namespace Ext { public class Other { } }\r\n");

        var result = await AssertMovedAndCompilesAsync(document);

        Assert.AreEqual(
            "extern alias V1;\r\n\r\nnamespace Company.App\r\n{\r\n    using V1::Ext;\r\n    using Ext;\r\n\r\n    class C { Thing t; Other o; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ExternAliasInsideTheNamespace_StaysInFrontOfTheMovedUsings()
    {
        var aliased = CompilingTestProject.CreateAliasedReference("Ext", "namespace Ext { public class Thing { } }", "V1");
        var input = "using System;\r\n\r\nnamespace Company.App\r\n{\r\n    extern alias V1;\r\n    using V1::Ext;\r\n\r\n    class C { Thing t; Action a; }\r\n}\r\n";
        var document = CompilingTestProject.CreateDocument(input, LanguageVersion.Latest, new[] { aliased }, Library);

        var result = await AssertMovedAndCompilesAsync(document);

        Assert.AreEqual(
            "namespace Company.App\r\n{\r\n    extern alias V1;\r\n    using System;\r\n    using V1::Ext;\r\n\r\n    class C { Thing t; Action a; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task RelativeTupleAliasTarget_IsGlobalQualifiedAsAValueTuple_OnCSharp73Projects()
    {
        // Inside Company.App, 'Shared' means Company.App.Shared, so the target must be qualified; before C# 12 an alias
        // target cannot be written in tuple syntax.
        var input = "using P = System.ValueTuple<Shared.Util, int>;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { P p; }\r\n}\r\n";
        var document = CompilingTestProject.CreateDocument(input, LanguageVersion.CSharp7_3, new MetadataReference[0], Library);

        var result = await AssertMovedAndCompilesAsync(document);

        Assert.AreEqual(
            "namespace Company.App\r\n{\r\n    using P = global::System.ValueTuple<global::Shared.Util, global::System.Int32>;\r\n\r\n    class C { P p; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task OnlyGlobalUsingsAtFileLevel_ReportsNothingToMove()
    {
        var input = "global using System;\r\n\r\n" + AppNamespace;

        var result = await MoveAsync(CompilingTestProject.CreateDocument(input, Library));

        Assert.AreEqual(UsingDirectivePlacementStatus.NothingToMove, result.Status);
        Assert.IsNull(result.Text);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("using System;\r\n\r\nnamespace N1\r\n{\r\n    class C1 { Action a; }\r\n}\r\n\r\nnamespace N2\r\n{\r\n    class C2 { }\r\n}\r\n", "2 namespaces", DisplayName = "two namespaces")]
    [DataRow("using System;\r\n\r\nclass C { Action a; }\r\n", "no namespace", DisplayName = "no namespace")]
    public async Task FileWithoutASingleNamespace_IsSkippedWithReason(string input, string expectedReason)
    {
        var document = CompilingTestProject.CreateDocument(input, Library);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        var result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        StringAssert.Contains(result.Reason, expectedReason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task UnresolvableUsing_IsSkippedWithReason()
    {
        var input = "using Missing;\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; }\r\n}\r\n";

        var result = await MoveAsync(CompilingTestProject.CreateDocument(input, Library));

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        StringAssert.Contains(result.Reason, "'using Missing;' cannot be resolved");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task MoveThatSilentlyRebindsAName_IsSkippedWithReason()
    {
        // At file level, 'using Company.App.Models;' is searched after the enclosing namespace Company, so Foo means
        // Company.Foo. Inside Company.App it is searched first, so Foo would silently become Company.App.Models.Foo.
        var input = "using Company.App.Models;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Foo f; }\r\n}\r\n";
        var document = CompilingTestProject.CreateDocument(input, Library, EnclosingFoo);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        var result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        StringAssert.Contains(result.Reason, "Company.Foo -> Company.App.Models.Foo");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task MoveThatSilentlyRebindsANameToTheSameNamedTypeOfAnotherAssembly_IsSkippedWithReason()
    {
        // At file level, 'using V1::Ext;' is searched after the enclosing namespace Ext of the project, so Thing means the
        // project's Ext.Thing. Inside Ext.Inner it is searched first, so Thing would silently become the Ext.Thing of the
        // aliased assembly, which reads the same.
        var aliased = CompilingTestProject.CreateAliasedReference("Ext", "namespace Ext { public class Thing { } }", "V1");
        var input = "extern alias V1;\r\nusing V1::Ext;\r\n\r\nnamespace Ext.Inner\r\n{\r\n    class C { Thing t; }\r\n}\r\n";
        var document = CompilingTestProject.CreateDocument(input, LanguageVersion.Latest, new[] { aliased }, Library, "namespace Ext { public class Thing { } }\r\n");
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        var result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        StringAssert.Contains(result.Reason, "Ext.Thing from TestProject -> Ext.Thing from Ext");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("int y = 1.Twice();", "public static int Twice(this int x) => x;", "Twice", DisplayName = "extension method call")]
    [DataRow("void M(Thing t) { foreach (var x in t) { } }", "public static System.Collections.Generic.IEnumerator<int> GetEnumerator(this Thing t) => null;", "GetEnumerator", DisplayName = "foreach GetEnumerator")]
    [DataRow("void M(Thing t) { var (a, b) = t; }", DeconstructExtension, "Deconstruct", DisplayName = "deconstruction")]
    public async Task MoveThatRebindsAnExtensionMember_IsSkippedWithReason(string usage, string extensionMember, string memberName)
    {
        // At file level, 'using Ext;' is searched after the enclosing namespace Company; inside Company.App it is
        // searched before it, so the same-named extension member of Ext.E would silently win over that of Company.E2.
        var input = "using Ext;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { " + usage + " }\r\n}\r\n";
        var document = CreateExtensionMemberDocument(input, extensionMember);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        var result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        StringAssert.Contains(result.Reason, "Company.E2." + memberName + "(");
        StringAssert.Contains(result.Reason, " -> Ext.E." + memberName + "(");
    }

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
        var input = "using Ext;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { " + usage + " }\r\n}\r\n";
        var document = CreateExtensionMemberDocument(input, extensionMember);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        var result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        StringAssert.Contains(result.Reason, "'using Ext;' imports an extension method '" + memberName + "'");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task MoveThatIntroducesCompileErrors_IsSkippedWithReason()
    {
        // The type outside the namespace would lose the import.
        var input = "using System;\r\n\r\nclass Top { Action a; }\r\n\r\n" + AppNamespace;
        var document = CompilingTestProject.CreateDocument(input, Library);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        var result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        StringAssert.Contains(result.Reason, "CS0246");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task MoveThatRebindsANameInAnotherConditionalCompilationVariant_IsSkippedWithReason()
    {
        // The active configuration (DEBUG undefined) is safe, but in a DEBUG build the move would silently rebind Foo in
        // class D from Company.Foo to Company.App.Models.Foo.
        var input = "using Company.App.Models;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { }\r\n#if DEBUG\r\n    class D { Foo f; }\r\n#endif\r\n}\r\n";
        var document = CompilingTestProject.CreateDocument(input, Library, EnclosingFoo);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        var result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        StringAssert.StartsWith(result.Reason, "with DEBUG defined: ");
        StringAssert.Contains(result.Reason, "Company.Foo -> Company.App.Models.Foo");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task UsingThatOnlyAnotherVariantCompiles_IsSkippedWithReason()
    {
        // With DEBUG undefined the #if block is part of the file header, which stays at the top of the file; with DEBUG
        // defined its using directive would be left behind outside the namespace.
        var input = "#if DEBUG\r\nusing System.Diagnostics;\r\n#endif\r\nusing System;\r\n\r\n" + AppNamespace;
        var document = CompilingTestProject.CreateDocument(input, Library);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        var result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        Assert.AreEqual("with DEBUG defined: 'using System.Diagnostics;' would stay outside the namespace", result.Reason);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("using System;\r\n#if DEBUG\r\nusing System.Diagnostics;\r\n#endif\r\n\r\n" + AppNamespace, "DEBUG", DisplayName = "using in an #if block below another using")]
    [DataRow("#region Usings\r\nusing System;\r\n#endregion\r\n\r\n" + AppNamespace, null, DisplayName = "usings in a #region")]
    [DataRow("using System;\r\n\r\n#if FEATURE\r\n" + AppNamespace + "#endif\r\n", "FEATURE", DisplayName = "namespace in an #if block")]
    [DataRow("global using System.Text;\r\n#nullable enable\r\nusing System;\r\n\r\n" + AppNamespace, null, DisplayName = "#nullable in front of a using that does not start the file")]
    public async Task MoveAcrossPreprocessorDirectives_IsSkippedWithReason(string input, string preprocessorSymbol)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: preprocessorSymbol == null ? null : new[] { preprocessorSymbol });
        var document = CompilingTestProject.CreateDocument(input, parseOptions, new MetadataReference[0], Library);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        var result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Skipped, result.Status, result.Text);
        Assert.IsNull(result.Text);
        StringAssert.Contains(result.Reason, "interleaved with preprocessor directives");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("", false, DisplayName = "empty source")]
    [DataRow("using System;\r\n\r\nnamespace N\r\n{\r\n    class C { }\r\n}\r\n", true, DisplayName = "using outside the namespace")]
    [DataRow("namespace N\r\n{\r\n    using System;\r\n    class C { }\r\n}\r\n", false, DisplayName = "using inside the namespace")]
    [DataRow("using System;\r\n\r\nclass C { }\r\n", false, DisplayName = "no namespace to move into")]
    [DataRow("global using System;\r\n\r\nnamespace N;\r\n\r\nclass C { }\r\n", false, DisplayName = "only a global using")]
    [DataRow("#if DEBUG\r\nusing System;\r\n#endif\r\n\r\nnamespace N;\r\n\r\nclass C { }\r\n", true, DisplayName = "using in an #if block in front of the namespace")]
    [DataRow("namespace N\r\n{\r\n    class C\r\n    {\r\n#if DEBUG\r\n        void M() { }\r\n#endif\r\n    }\r\n}\r\n", false, DisplayName = "#if block inside a type")]
    [DataRow("using System;\r\n\r\nnamespace N1\r\n{\r\n}\r\n\r\nnamespace N2\r\n{\r\n}\r\n", false, DisplayName = "two namespaces")]
    [DataRow("using System;\r\n\r\nclass Top { }\r\n\r\nnamespace N\r\n{\r\n}\r\n", false, DisplayName = "type beside the namespace")]
    [DataRow("using System;\r\n\r\nnamespace N\r\n{\r\n}\r\n\r\ndelegate void D();\r\n", false, DisplayName = "delegate beside the namespace")]
    [DataRow("using System;\r\n\r\nConsole.WriteLine();\r\n\r\nnamespace N\r\n{\r\n}\r\n", false, DisplayName = "top-level statement beside the namespace")]
    [DataRow("using System.Reflection;\r\n\r\n[assembly: AssemblyVersion(\"1.0\")]\r\n\r\nnamespace N\r\n{\r\n}\r\n", false, DisplayName = "assembly attribute beside the namespace")]
    [DataRow("#if DEBUG\r\nusing System;\r\n#endif\r\n\r\nenum E { }\r\n\r\nnamespace N\r\n{\r\n}\r\n", false, DisplayName = "#if block in front of an enum beside the namespace")]
    public void HasUsingsOutsideNamespace_CountsOnlyLayoutsTheInwardMoveSupports(string source, bool expected)
    {
        Assert.AreEqual(expected, UsingDirectivePlacementConverter.HasUsingsOutsideNamespace(source));
    }

    private static Task<UsingDirectivePlacementResult> MoveAsync(Microsoft.CodeAnalysis.Document document) =>
        new UsingDirectivePlacementConverter().MoveUsingsInsideAsync(document, CancellationToken.None);

    private static Task<string> AssertMovedAndCompilesAsync(string input) =>
        AssertMovedAndCompilesAsync(CompilingTestProject.CreateDocument(input, Library));

    private static async Task<string> AssertMovedAndCompilesAsync(Microsoft.CodeAnalysis.Document document)
    {
        var input = (await document.GetTextAsync()).ToString();
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        var result = await MoveAsync(document);

        Assert.AreEqual(UsingDirectivePlacementStatus.Moved, result.Status, result.Reason);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, result.Text), "The moved output must compile:" + Environment.NewLine + result.Text);

        return result.Text;
    }

    /// <summary>
    /// Creates <paramref name="input" /> in a project where <c>Ext.E</c> and <c>Company.E2</c> both declare
    /// <paramref name="extensionMember" />.
    /// </summary>
    private static Microsoft.CodeAnalysis.Document CreateExtensionMemberDocument(string input, string extensionMember) =>
        CompilingTestProject.CreateDocument(
            input,
            Library,
            ExtensionReceivers,
            "namespace Ext { using Company; public static class E { " + extensionMember + " } }\r\n",
            "namespace Company { public static class E2 { " + extensionMember + " } }\r\n");

    private static void AssertNoErrors(IReadOnlyList<string> errors, string message) =>
        Assert.AreEqual(0, errors.Count, message + Environment.NewLine + string.Join(Environment.NewLine, errors));
}
