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
/// Unit tests for <see cref="MoveUsingsOutsideNamespaceConverter" />. Every moved result is compiled together with
/// <see cref="Library" /> and must not contain compile errors.
/// </summary>
[TestClass]
public sealed class MoveUsingsOutsideNamespaceConverterTests
{
    private const string Library =
        "namespace Company.App.Services { public class Svc { } }\r\n" +
        "namespace Company.App.Models { public class Foo { } public static class Helpers { public static int Twice(int x) => x * 2; } }\r\n" +
        "namespace Company.Shared { public class Util { } }\r\n" +
        "namespace Alpha { public class T { } }\r\n" +
        "namespace Beta { public class T { } }\r\n";

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task IssueRepro_QualifiesNamespaceRelativeUsingsAndKeepsQualifiedOnes()
    {
        var input = "namespace Company.App\r\n{\r\n    using Services;\r\n    using Shared;\r\n    using System.Text;\r\n    class C { Svc s; Util u; StringBuilder b; }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual(
            "using Company.App.Services;\r\nusing Company.Shared;\r\nusing System.Text;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; Util u; StringBuilder b; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ChildNamespaceRelativeUsing_IsFullyQualified()
    {
        var input = "namespace Company.App\r\n{\r\n    using Services;\r\n    class C { Svc s; }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        StringAssert.StartsWith(result, "using Company.App.Services;\r\n\r\nnamespace Company.App\r\n");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ParentNamespaceRelativeUsing_IsFullyQualified()
    {
        var input = "namespace Company.App\r\n{\r\n    using Shared;\r\n    class C { Util u; }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        StringAssert.StartsWith(result, "using Company.Shared;\r\n\r\nnamespace Company.App\r\n");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task RelativeAliasTargets_AreFullyQualified_IncludingTypeArguments()
    {
        var input = "namespace Company.App\r\n{\r\n    using X = Models.Foo;\r\n    using L = System.Collections.Generic.List<Models.Foo>;\r\n    class C { X x; L l; }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        StringAssert.StartsWith(
            result,
            "using X = Company.App.Models.Foo;\r\nusing L = System.Collections.Generic.List<Company.App.Models.Foo>;\r\n\r\nnamespace Company.App\r\n");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task RelativeUsingStatic_IsFullyQualified()
    {
        var input = "namespace Company.App\r\n{\r\n    using static Models.Helpers;\r\n    class C { int y = Twice(1); }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        StringAssert.StartsWith(result, "using static Company.App.Models.Helpers;\r\n\r\nnamespace Company.App\r\n");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task QualifiedUsingStaysUnchanged_AndIsDeduplicatedWithTopLevelUsings()
    {
        var input = "using System;\r\nusing System.Text;\r\n\r\nnamespace Company.App\r\n{\r\n    using System.Text;\r\n    using Services;\r\n    class C { Svc s; StringBuilder b; Action a; }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual(
            "using System;\r\nusing System.Text;\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; StringBuilder b; Action a; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task RelativeUsing_IsDeduplicatedWithEquivalentQualifiedTopLevelUsing()
    {
        var input = "using Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    using Services;\r\n    class C { Svc s; }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual("using Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; }\r\n}\r\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task PreservesFileHeaderCrLfAndBlankLineAfterUsings()
    {
        var input = "// Copyright (c) 2026\r\n\r\nnamespace Company.App\r\n{\r\n    using Services;\r\n    class C { Svc s; }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual(
            "// Copyright (c) 2026\r\n\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { Svc s; }\r\n}\r\n",
            result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task PreservesLfLineEndings()
    {
        var input = "// header\n\nnamespace Company.App\n{\n    using Services;\n    class C { Svc s; }\n}\n";

        var result = await AssertMovedAndCompilesAsync(input);

        Assert.AreEqual("// header\n\nusing Company.App.Services;\n\nnamespace Company.App\n{\n    class C { Svc s; }\n}\n", result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task FileScopedNamespaceUsings_AreQualifiedAndMoved()
    {
        var input = "namespace Company.App;\r\n\r\nusing Services;\r\n\r\nclass C { Svc s; }\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        StringAssert.StartsWith(result, "using Company.App.Services;\r\n\r\nnamespace Company.App;");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task UsingsAlreadyOutside_ReportsNoUsingsInsideNamespace()
    {
        var input = "using System;\r\n\r\nnamespace Company.App\r\n{\r\n    class C { }\r\n}\r\n";

        var result = await MoveAsync(CompilingTestProject.CreateDocument(input, Library));

        Assert.AreEqual(MoveUsingsOutsideNamespaceStatus.NoUsingsInsideNamespace, result.Status);
        Assert.IsNull(result.Text);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task UnresolvableUsing_IsSkippedWithReason()
    {
        var input = "namespace Company.App\r\n{\r\n    using Services;\r\n    using Missing;\r\n    class C { Svc s; }\r\n}\r\n";

        var result = await MoveAsync(CompilingTestProject.CreateDocument(input, Library));

        Assert.AreEqual(MoveUsingsOutsideNamespaceStatus.Skipped, result.Status);
        Assert.IsNull(result.Text);
        StringAssert.Contains(result.Reason, "using Missing;");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task MoveThatIntroducesCompileErrors_IsSkippedWithReason()
    {
        // Each namespace imports a different T; merged at file level, T becomes ambiguous.
        var input = "namespace N1\r\n{\r\n    using Alpha;\r\n    class C1 { T t; }\r\n}\r\n\r\nnamespace N2\r\n{\r\n    using Beta;\r\n    class C2 { T t; }\r\n}\r\n";
        var document = CompilingTestProject.CreateDocument(input, Library);
        Assert.AreEqual(0, (await CompilingTestProject.GetCompileErrorsAsync(document, input)).Count, "The input must compile.");

        var result = await MoveAsync(document);

        Assert.AreEqual(MoveUsingsOutsideNamespaceStatus.Skipped, result.Status);
        Assert.IsNull(result.Text);
        StringAssert.Contains(result.Reason, "CS0104");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task QualifiedAliasTargets_StayVerbatim_InsteadOfKeywordOrShorthandSyntax()
    {
        var input = "namespace Company.App\r\n{\r\n    using Str = System.String;\r\n    using N = System.Nullable<System.Int32>;\r\n    using P = System.ValueTuple<System.Int32, System.Int32>;\r\n    class C { Str s; N n; P p; }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        StringAssert.StartsWith(
            result,
            "using Str = System.String;\r\nusing N = System.Nullable<System.Int32>;\r\nusing P = System.ValueTuple<System.Int32, System.Int32>;\r\n\r\nnamespace Company.App\r\n");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task QualifiedAliasToSpecialType_IsMoved_OnCSharp73Projects()
    {
        var input = "namespace Company.App\r\n{\r\n    using Str = System.String;\r\n    using Services;\r\n    class C { Str s; Svc v; }\r\n}\r\n";
        var document = CompilingTestProject.CreateDocument(input, LanguageVersion.CSharp7_3, new MetadataReference[0], Library);

        var result = await AssertMovedAndCompilesAsync(document);

        StringAssert.StartsWith(result, "using Str = System.String;\r\nusing Company.App.Services;\r\n\r\nnamespace Company.App\r\n");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ExternAliasQualifiedUsing_KeepsItsAlias()
    {
        var aliased = CompilingTestProject.CreateAliasedReference("Ext", "namespace Ext { public class Thing { } }", "V1");
        var input = "extern alias V1;\r\n\r\nnamespace Company.App\r\n{\r\n    using V1::Ext;\r\n    class C { Thing t; }\r\n}\r\n";
        var document = CompilingTestProject.CreateDocument(input, LanguageVersion.Latest, new[] { aliased }, Library);

        var result = await AssertMovedAndCompilesAsync(document);

        StringAssert.Contains(result, "using V1::Ext;");
        Assert.IsTrue(result.IndexOf("using V1::Ext;", StringComparison.Ordinal) < result.IndexOf("namespace Company.App", StringComparison.Ordinal), result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task MoveThatSilentlyRebindsAName_IsSkippedWithReason()
    {
        // Inside Company.App, 'using Models;' makes Foo mean Company.App.Models.Foo. At file level the import is
        // searched after the enclosing namespaces, so Foo would silently become Company.Foo - without any error.
        var input = "namespace Company.App\r\n{\r\n    using Models;\r\n    class C { Foo f; }\r\n}\r\n";
        var document = CompilingTestProject.CreateDocument(input, Library, "namespace Company { public class Foo { } }");
        Assert.AreEqual(0, (await CompilingTestProject.GetCompileErrorsAsync(document, input)).Count, "The input must compile.");

        var result = await MoveAsync(document);

        Assert.AreEqual(MoveUsingsOutsideNamespaceStatus.Skipped, result.Status);
        Assert.IsNull(result.Text);
        StringAssert.Contains(result.Reason, "Company.App.Models.Foo");
        StringAssert.Contains(result.Reason, "Company.Foo");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task NestedNamespaceRelativeUsing_IsQualifiedAgainstTheInnermostNamespace()
    {
        var input = "namespace Company\r\n{\r\n    namespace App\r\n    {\r\n        using Services;\r\n        class C { Svc s; }\r\n    }\r\n}\r\n";

        var result = await AssertMovedAndCompilesAsync(input);

        StringAssert.StartsWith(result, "using Company.App.Services;\r\n\r\nnamespace Company\r\n");
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task UsingsInterleavedWithPreprocessorDirectives_AreSkippedWithReason()
    {
        var input = "namespace Company.App\r\n{\r\n#if true\r\n    using Services;\r\n#endif\r\n    class C { Svc s; }\r\n}\r\n";

        var result = await MoveAsync(CompilingTestProject.CreateDocument(input, Library));

        Assert.AreEqual(MoveUsingsOutsideNamespaceStatus.Skipped, result.Status);
        Assert.IsNull(result.Text);
        StringAssert.Contains(result.Reason, "interleaved with preprocessor directives");
    }

    private static Task<MoveUsingsOutsideNamespaceResult> MoveAsync(Microsoft.CodeAnalysis.Document document) =>
        new MoveUsingsOutsideNamespaceConverter().MoveUsingsOutsideAsync(document, CancellationToken.None);

    private static Task<string> AssertMovedAndCompilesAsync(string input) =>
        AssertMovedAndCompilesAsync(CompilingTestProject.CreateDocument(input, Library));

    private static async Task<string> AssertMovedAndCompilesAsync(Microsoft.CodeAnalysis.Document document)
    {
        var input = (await document.GetTextAsync()).ToString();
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, input), "The input must compile.");

        var result = await MoveAsync(document);

        Assert.AreEqual(MoveUsingsOutsideNamespaceStatus.Moved, result.Status, result.Reason);
        AssertNoErrors(await CompilingTestProject.GetCompileErrorsAsync(document, result.Text), "The moved output must compile:" + Environment.NewLine + result.Text);

        return result.Text;
    }

    private static void AssertNoErrors(IReadOnlyList<string> errors, string message) =>
        Assert.AreEqual(0, errors.Count, message + Environment.NewLine + string.Join(Environment.NewLine, errors));
}
