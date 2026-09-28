using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeJanitor.Logic.Transformations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="ClassSealingConverter" />. A top-level class or record class is sealed only when the
/// semantic model proves it is not designed or used for inheritance anywhere in the solution: it has no virtual or
/// protected members (constructors included), no class of the solution derives from it and no generic constraint of
/// the solution names it. Every sealed result must compile without errors and without CS0628 (new protected member
/// declared in sealed type).
/// </summary>
[TestClass]
public sealed class ClassSealingConverterTests
{
    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task InternalClassWithoutDerivedTypes_BecomesSealed()
    {
        Assert.AreEqual("internal sealed class Foo { }", await SealAsync("internal class Foo { }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassWithNoAccessModifier_BecomesSealed()
    {
        Assert.AreEqual("sealed class Foo { }", await SealAsync("class Foo { }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task PublicClass_BecomesSealed()
    {
        Assert.AreEqual("public sealed class Foo { }", await SealAsync("public class Foo { }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task PublicRecordClass_BecomesSealed()
    {
        Assert.AreEqual("public sealed record Person(string Name);", await SealAsync("public record Person(string Name);"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task FileScopedNamespace_SealsTopLevelClass()
    {
        Assert.AreEqual("namespace N;\r\nsealed class Foo { }", await SealAsync("namespace N;\r\nclass Foo { }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassImplementingInterface_BecomesSealed()
    {
        Assert.AreEqual(
            "internal sealed class Foo : System.IDisposable { public void Dispose() { } }",
            await SealAsync("internal class Foo : System.IDisposable { public void Dispose() { } }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task NestedClass_IsNotSealed_OnlyTopLevelConsidered()
    {
        Assert.AreEqual(
            "internal sealed class Outer { internal class Inner { } }",
            await SealAsync("internal class Outer { internal class Inner { } }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ProtectedOverride_DoesNotPreventSealing()
    {
        Assert.AreEqual(
            "public class B { protected virtual void M() { } } public sealed class A : B { protected override void M() { } }",
            await SealAsync("public class B { protected virtual void M() { } } public class A : B { protected override void M() { } }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task PublicOverride_DoesNotPreventSealing()
    {
        Assert.AreEqual(
            "public sealed class A { public override string ToString() => \"A\"; }",
            await SealAsync("public class A { public override string ToString() => \"A\"; }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("internal sealed class Foo { }", DisplayName = "already sealed")]
    [DataRow("internal abstract class Foo { }", DisplayName = "abstract")]
    [DataRow("internal static class Foo { }", DisplayName = "static")]
    [DataRow("internal partial class Foo { }", DisplayName = "partial")]
    [DataRow("public record struct Point(int X, int Y);", DisplayName = "record struct")]
    [DataRow("public struct Point { }", DisplayName = "struct")]
    [DataRow("public interface IFoo { }", DisplayName = "interface")]
    public async Task TypeThatCannotOrNeedNotBeSealed_IsUnchanged(string input)
    {
        Assert.AreEqual(input, await SealAsync(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("public class Example\r\n{\r\n    protected Example()\r\n    {\r\n    }\r\n}\r\n", DisplayName = "protected constructor (issue #43)")]
    [DataRow("public class Example { private protected Example() { } }", DisplayName = "private protected constructor")]
    [DataRow("public class Example { protected internal Example() { } }", DisplayName = "protected internal constructor")]
    [DataRow("public class Example { public Example() { } protected Example(int value) { } }", DisplayName = "protected constructor overload")]
    [DataRow("public class Example { protected void M() { } }", DisplayName = "protected method")]
    [DataRow("public class Example { protected int _value; }", DisplayName = "protected field")]
    [DataRow("public class Example { protected int Value { get; set; } }", DisplayName = "protected property")]
    [DataRow("public class Example { public int Value { get; protected set; } }", DisplayName = "protected property setter")]
    [DataRow("public class Example { protected event System.EventHandler Changed; }", DisplayName = "protected event")]
    [DataRow("public class Example { protected class Nested { } }", DisplayName = "protected nested type")]
    [DataRow("public class Example { protected const int Value = 1; }", DisplayName = "protected constant")]
    [DataRow("public record Example(string Name) { protected Example(Example original) { Name = original.Name; } }", DisplayName = "protected record copy constructor")]
    public async Task ClassWithProtectedMember_StaysUnsealed(string input)
    {
        Assert.AreEqual(input, await SealAsync(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("public class Foo { public virtual void DoWork() { } }", DisplayName = "virtual method")]
    [DataRow("public class Foo { public virtual int Value { get; set; } }", DisplayName = "virtual property")]
    [DataRow("public class Foo { public virtual int this[int i] => i; }", DisplayName = "virtual indexer")]
    [DataRow("public class Foo { public virtual event System.EventHandler Changed; }", DisplayName = "virtual event")]
    [DataRow("public class Foo : SomeUnresolvedBase { public virtual int Value { get; } }", DisplayName = "virtual member with unresolved base")]
    public async Task ClassWithVirtualMember_StaysUnsealed(string input)
    {
        Assert.AreEqual(input, await SealAsync(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassWithDerivedClassInSameFile_BaseStaysUnsealed_DerivedBecomesSealed()
    {
        Assert.AreEqual(
            "public class Animal { } public sealed class Dog : Animal { }",
            await SealAsync("public class Animal { } public class Dog : Animal { }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassWithDerivedClassInAnotherFile_StaysUnsealed()
    {
        string input = "namespace Demo { public class Result { } }";

        Assert.AreEqual(input, await SealAsync(input, "namespace Demo { public class Result<T> : Result { } }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassWithDerivedClassInAnotherProject_StaysUnsealed()
    {
        string input = "namespace Demo { public class Result { } }";
        Document derivingDocument = CompilingTestProject.CreateDocumentReferencingProject(
            "namespace Consumer { public class Special : Demo.Result { } }",
            new[] { input });
        Document document = derivingDocument.Project.Solution.Projects
            .Single(project => project.Name == "ReferencedProject")
            .Documents.Single();

        Assert.AreEqual(input, await SealAsync(document));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task GenericBase_DerivedThroughClosedConstruction_StaysUnsealed()
    {
        Assert.AreEqual(
            "public class Box<T> { } public sealed class IntBox : Box<int> { }",
            await SealAsync("public class Box<T> { } public class IntBox : Box<int> { }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task QualifiedBaseTypes_IdentifyTheBaseClass()
    {
        Assert.AreEqual(
            "namespace N { class Animal { } sealed class Dog : global::N.Animal { } sealed class Cat : N.Animal { } }",
            await SealAsync("namespace N { class Animal { } class Dog : global::N.Animal { } class Cat : N.Animal { } }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task SameNameInAnotherNamespace_DoesNotPreventSealing()
    {
        Assert.AreEqual(
            "namespace X { public class A { } } namespace Y { public sealed class A { } public sealed class D : X.A { } }",
            await SealAsync("namespace X { public class A { } } namespace Y { public class A { } public class D : X.A { } }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassUsedInGenericConstraintInSameFile_StaysUnsealed()
    {
        Assert.AreEqual(
            "public class Result { } public sealed class Handler<T> where T : Result { }",
            await SealAsync("public class Result { } public class Handler<T> where T : Result { }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassUsedInNullableGenericConstraintInSameFile_StaysUnsealed()
    {
        Assert.AreEqual(
            "#nullable enable\r\npublic class Result { } public sealed class Handler<T> where T : Result? { }",
            await SealAsync("#nullable enable\r\npublic class Result { } public class Handler<T> where T : Result? { }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassUsedInGenericMethodConstraintInAnotherFile_StaysUnsealed()
    {
        string input = "namespace Demo { public class Result { } }";

        Assert.AreEqual(input, await SealAsync(input, "namespace Demo { public static class Handler { public static void Handle<T>(T value) where T : Result { } } }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task FileCompiledByTwoProjects_IsSealedOnlyWhenSafeInBoth()
    {
        string input = "namespace Demo { public class Result { } }";
        Solution solution = new AdhocWorkspace().CurrentSolution;
        Document first = AddProject(ref solution, "First", input, "namespace Demo { public class Other { } }");
        Document second = AddProject(ref solution, "Second", input, "namespace Demo { public class Derived : Result { } }");

        string result = await new ClassSealingConverter().SealWhenSafeAsync(
            new[] { solution.GetDocument(first.Id), solution.GetDocument(second.Id) },
            CancellationToken.None);

        Assert.AreEqual(input, result);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task FileCompiledByTwoProjects_IsSealedWhenSafeInBoth()
    {
        string input = "namespace Demo { public class Result { } }";
        Solution solution = new AdhocWorkspace().CurrentSolution;
        Document first = AddProject(ref solution, "First", input);
        Document second = AddProject(ref solution, "Second", input);

        string result = await new ClassSealingConverter().SealWhenSafeAsync(
            new[] { solution.GetDocument(first.Id), solution.GetDocument(second.Id) },
            CancellationToken.None);

        Assert.AreEqual("namespace Demo { public sealed class Result { } }", result);
    }

    private static Document AddProject(ref Solution solution, string name, string targetSource, params string[] librarySources)
    {
        Project project = solution
            .AddProject(name, name, LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        for (int index = 0; index < librarySources.Length; index++)
        {
            project = project.AddDocument($"Library{index}.cs", SourceText.From(librarySources[index])).Project;
        }

        Document target = project.AddDocument("Target.cs", SourceText.From(targetSource));
        solution = target.Project.Solution;

        return target;
    }

    private static Task<string> SealAsync(string input, params string[] librarySources) =>
        SealAsync(CompilingTestProject.CreateDocument(input, librarySources));

    /// <summary>
    /// Seals <paramref name="document" /> and, when it changed, asserts that the result still compiles without errors
    /// and without CS0628.
    /// </summary>
    private static async Task<string> SealAsync(Document document)
    {
        string input = (await document.GetTextAsync()).ToString();
        string result = await new ClassSealingConverter().SealWhenSafeAsync(new[] { document }, CancellationToken.None);

        if (result != input)
        {
            Compilation compilation = await document.WithText(SourceText.From(result)).Project.GetCompilationAsync();
            List<string> problems = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error || diagnostic.Id == "CS0628")
                .Where(diagnostic => diagnostic.Id != "CS0246")
                .Select(diagnostic => diagnostic.ToString())
                .ToList();
            Assert.IsEmpty(problems, string.Join("\r\n", problems));
        }

        return result;
    }
}
