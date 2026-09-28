using System;
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
    [DataRow("public class Example { protected static void M() { } }", DisplayName = "protected static method")]
    [DataRow("public class Example { protected static int _count; }", DisplayName = "protected static field")]
    [DataRow("public class Example { public int this[int i] { get => i; protected set { } } }", DisplayName = "protected indexer setter")]
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
    public async Task ClassUsedInGenericConstraintInAnotherProject_StaysUnsealed()
    {
        string input = "namespace Demo { public class Result { } }";
        Document constrainingDocument = CompilingTestProject.CreateDocumentReferencingProject(
            "namespace Consumer { public class Handler<T> where T : Demo.Result { } }",
            new[] { input });
        Document document = constrainingDocument.Project.Solution.Projects
            .Single(project => project.Name == "ReferencedProject")
            .Documents.Single();

        Assert.AreEqual(input, await SealAsync(document));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task GenericClassUsedInConstraintThroughClosedConstruction_StaysUnsealed()
    {
        Assert.AreEqual(
            "public class Box<T> { } public sealed class Handler<T> where T : Box<int> { }",
            await SealAsync("public class Box<T> { } public class Handler<T> where T : Box<int> { }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task GenericClassWithOwnConstraints_BecomesSealed()
    {
        Assert.AreEqual(
            "public sealed class Repository<T> where T : class, new() { public T Create() => new T(); }",
            await SealAsync("public class Repository<T> where T : class, new() { public T Create() => new T(); }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task FileLocalClass_BecomesSealed()
    {
        Assert.AreEqual("file sealed class Helper { }", await SealAsync("file class Helper { }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task FileLocalClassWithDerivedFileLocalClass_BaseStaysUnsealed()
    {
        Assert.AreEqual(
            "file class Base { } file sealed class Derived : Base { }",
            await SealAsync("file class Base { } file class Derived : Base { }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassInFileWithCompileErrors_IsSealedWithoutAddingErrors()
    {
        Assert.AreEqual(
            "public sealed class Foo { public void M() { int x = ; } }",
            await SealAsync("public class Foo { public void M() { int x = ; } }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassWithProtectedMemberInFileWithCompileErrors_StaysUnsealed()
    {
        string input = "public class Foo { protected Foo() { } public void M() { UnknownType x = null; } }";

        Assert.AreEqual(input, await SealAsync(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassDerivedFromInFileWithCompileErrors_BaseStaysUnsealed_DerivedBecomesSealed()
    {
        string input = "public class Base { } public class Derived : Base { public void M() { int x = ; } }";

        Assert.AreEqual("public class Base { } public sealed class Derived : Base { public void M() { int x = ; } }", await SealAsync(input));
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

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task NullDocumentList_Throws()
    {
        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => new ClassSealingConverter().SealWhenSafeAsync(null, CancellationToken.None));

        Assert.AreEqual("documents", exception.ParamName);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task EmptyDocumentList_Throws()
    {
        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => new ClassSealingConverter().SealWhenSafeAsync(new Document[0], CancellationToken.None));

        Assert.AreEqual("documents", exception.ParamName);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("", DisplayName = "empty file")]
    [DataRow("  \r\n\t\r\n", DisplayName = "whitespace only")]
    [DataRow("// just a comment\r\n", DisplayName = "comment only")]
    [DataRow("namespace Demo { public enum Kind { A } public delegate void Handler(); }", DisplayName = "no class")]
    public async Task FileWithoutCandidates_IsReturnedUnchanged(string input)
    {
        Assert.AreEqual(input, await SealAsync(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassReferencedOutsideConstraints_BecomesSealed()
    {
        Assert.AreEqual(
            "public sealed class Settings { } public sealed class Consumer { Settings _s = new Settings(); System.Collections.Generic.List<Settings> _all; Settings Get() => (Settings)null; }",
            await SealAsync(
                "public class Settings { } public class Consumer { Settings _s = new Settings(); System.Collections.Generic.List<Settings> _all; Settings Get() => (Settings)null; }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("object M(Foo foo) => (IBar)foo;", DisplayName = "cast to an interface")]
    [DataRow("object M(Foo foo) => foo as IBar;", DisplayName = "as an interface")]
    [DataRow("bool M(Foo foo) => foo is IBar;", DisplayName = "is an interface (CS0184 when sealed)")]
    [DataRow("bool M(Foo foo) => foo is IBar bar;", DisplayName = "declaration pattern")]
    [DataRow("int M(Foo foo) { switch (foo) { case IBar bar: return 1; default: return 0; } }", DisplayName = "case declaration pattern")]
    [DataRow("int M(Foo foo) => foo switch { IBar bar => 1, _ => 0 };", DisplayName = "switch expression arm")]
    [DataRow("bool M(Foo[] foos) => foos is IBar[];", DisplayName = "array of the class to array of an interface")]
    [DataRow("object M(IBar bar) => (Foo)bar;", DisplayName = "cast from an interface")]
    [DataRow("bool M(IBar bar) => bar is Foo foo;", DisplayName = "pattern from an interface")]
    [DataRow("void M(Foo[] foos) { foreach (IBar bar in foos) { } }", DisplayName = "foreach with an interface iteration variable")]
    public async Task ClassConvertedToAnInterfaceItDoesNotImplement_StaysUnsealed(string use)
    {
        string input = "public class Foo { }";

        Assert.AreEqual(input, await SealAsync(input, "public interface IBar { } public static class Use { public static " + use + " }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassConvertedToAnInterfaceInAnotherProject_StaysUnsealed()
    {
        string input = "namespace Demo { public class Foo { } public interface IBar { } }";
        Document consumerDocument = CompilingTestProject.CreateDocumentReferencingProject(
            "namespace Consumer { public static class Use { public static object M(Demo.Foo foo) => (Demo.IBar)foo; } }",
            new[] { input });
        Document document = consumerDocument.Project.Solution.Projects
            .Single(project => project.Name == "ReferencedProject")
            .Documents.Single();

        Assert.AreEqual(input, await SealAsync(document));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassConvertedToAnInterfaceItImplements_BecomesSealed()
    {
        Assert.AreEqual(
            "public interface IBar { } public sealed class Foo : IBar { }",
            await SealAsync(
                "public interface IBar { } public class Foo : IBar { }",
                "public static class Use { public static object M(Foo foo) => (IBar)foo; public static bool N(IBar bar) => bar is Foo foo; }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassDerivedFromByItsOwnNestedClass_StaysUnsealed()
    {
        string input = "public class Shape { private sealed class Circle : Shape { } }";

        Assert.AreEqual(input, await SealAsync(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task VirtualMemberOfANestedType_DoesNotPreventSealingTheOuterClass()
    {
        Assert.AreEqual(
            "public sealed class Outer { public class Inner { public virtual void M() { } } }",
            await SealAsync("public class Outer { public class Inner { public virtual void M() { } } }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("public class Foo { } ", "public class Foo { }", DisplayName = "duplicate declaration")]
    [DataRow("public class Foo { } ", "public abstract class Foo { }", DisplayName = "duplicate abstract declaration")]
    [DataRow("public class Foo { } ", "public sealed class Foo { }", DisplayName = "duplicate sealed declaration")]
    [DataRow("public class Foo { } ", "public static class Foo { }", DisplayName = "duplicate static declaration")]
    public async Task ClassDeclaredTwiceInTheProject_StaysUnsealed(string input, string otherDeclaration)
    {
        Assert.AreEqual(input, await SealAsync(input, otherDeclaration));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ExceptionWithProtectedSerializationConstructor_StaysUnsealed()
    {
        string input =
            "using System;\r\n" +
            "using System.Runtime.Serialization;\r\n" +
            "public class ParseException : Exception\r\n" +
            "{\r\n" +
            "    public ParseException(string message) : base(message) { }\r\n" +
            "    protected ParseException(SerializationInfo info, StreamingContext context) : base(info, context) { }\r\n" +
            "}\r\n";

        Assert.AreEqual(input, await SealAsync(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassImplementingAnInterfaceWithADefaultMember_BecomesSealed()
    {
        Assert.AreEqual(
            "public interface IGreeter { string Name { get; } string Greet() => \"Hi \" + Name; } public sealed class Greeter : IGreeter { public string Name => \"x\"; }",
            await SealAsync("public interface IGreeter { string Name { get; } string Greet() => \"Hi \" + Name; } public class Greeter : IGreeter { public string Name => \"x\"; }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassWithOnlyStaticMembersAndSealedOverrides_BecomesSealed()
    {
        Assert.AreEqual(
            "public sealed class Tools { public static int Count; public static void Run() { } public sealed override string ToString() => \"t\"; }",
            await SealAsync("public class Tools { public static int Count; public static void Run() { } public sealed override string ToString() => \"t\"; }"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task TopLevelStatementsFile_SealsTheClassesDeclaredAfterTheStatements()
    {
        string input =
            "using System;\r\n" +
            "\r\n" +
            "var greeter = new Greeter();\r\n" +
            "Console.WriteLine(greeter.Greet(Name()));\r\n" +
            "\r\n" +
            "static string Name() => \"world\";\r\n" +
            "\r\n" +
            "class Greeter\r\n" +
            "{\r\n" +
            "    public string Greet(string name) => \"Hello \" + name;\r\n" +
            "}\r\n" +
            "\r\n" +
            "record Person(string Name);\r\n" +
            "\r\n" +
            "abstract class Animal { }\r\n" +
            "\r\n" +
            "class Dog : Animal { }\r\n";
        string expected = input
            .Replace("class Greeter\r\n", "sealed class Greeter\r\n")
            .Replace("record Person(", "sealed record Person(")
            .Replace("class Dog :", "sealed class Dog :");
        Document document = CompilingTestProject.CreateDocument(
            input,
            new CSharpParseOptions(LanguageVersion.Latest),
            new MetadataReference[0]);
        Document executable = document.Project
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication))
            .GetDocument(document.Id);

        Assert.AreEqual(expected, await SealAsync(executable));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("\r\n", DisplayName = "CRLF")]
    [DataRow("\n", DisplayName = "LF")]
    public async Task DocCommentsAttributesAndIndentation_StayInFrontOfTheSealedModifier(string newLine)
    {
        string input =
            "namespace Demo" + newLine +
            "{" + newLine +
            "\t/// <summary>A value.</summary>" + newLine +
            "\t[System.Serializable]" + newLine +
            "\tclass Value" + newLine +
            "\t{" + newLine +
            "\t}" + newLine +
            newLine +
            "\t// Public one." + newLine +
            "\tpublic /* note */ class Other { }" + newLine +
            newLine +
            "\t#region Records" + newLine +
            "\tinternal record Item(int Id);" + newLine +
            "\t#endregion" + newLine +
            "}" + newLine;
        string expected = input
            .Replace("\tclass Value", "\tsealed class Value")
            .Replace("public /* note */ class Other", "public /* note */ sealed class Other")
            .Replace("internal record Item", "internal sealed record Item");

        Assert.AreEqual(expected, await SealAsync(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassContainingAnInactivePreprocessorBranch_StaysUnsealed()
    {
        string input =
            "public class Widget\r\n" +
            "{\r\n" +
            "#if LEGACY\r\n" +
            "    public void Trace() { }\r\n" +
            "#endif\r\n" +
            "}\r\n";

        Assert.AreEqual(input, await SealAsync(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ClassNamedInInactiveCodeOfAnotherFile_StaysUnsealed()
    {
        string input = "public class Widget { }";

        Assert.AreEqual(input, await SealAsync(input, "#if LEGACY\r\npublic class OldWidget : Widget { }\r\n#endif\r\n"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task InactiveCodeNotNamingTheClass_DoesNotPreventSealing()
    {
        Assert.AreEqual(
            "public sealed class Widget { }",
            await SealAsync("public class Widget { }", "#if LEGACY\r\npublic class WidgetFactory { string _name = \"Widget\"; }\r\n#endif\r\n"));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public async Task ProtectedMemberInsideAnActivePreprocessorBranch_KeepsTheClassUnsealed()
    {
        string input =
            "public class Widget\r\n" +
            "{\r\n" +
            "#if DEBUG\r\n" +
            "    protected virtual void Trace() { }\r\n" +
            "#endif\r\n" +
            "}\r\n";
        Solution solution = new AdhocWorkspace().CurrentSolution;
        Document release = AddProject(ref solution, "Release", input);
        Project debugProject = solution
            .AddProject("Debug", "Debug", LanguageNames.CSharp)
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: new[] { "DEBUG" }))
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        Document debug = debugProject.AddDocument("Target.cs", SourceText.From(input));
        solution = debug.Project.Solution;

        string releaseOnly = await new ClassSealingConverter().SealWhenSafeAsync(new[] { solution.GetDocument(release.Id) }, CancellationToken.None);
        string both = await new ClassSealingConverter().SealWhenSafeAsync(
            new[] { solution.GetDocument(release.Id), solution.GetDocument(debug.Id) },
            CancellationToken.None);

        Assert.AreEqual(input, releaseOnly);
        Assert.AreEqual(input, both);
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
    /// Seals <paramref name="document" /> and, when it changed, asserts that sealing added no compile error and no
    /// CS0628 to those the input already has (the test project references only mscorlib, so for example records lack
    /// <c>IsExternalInit</c> on .NET Framework).
    /// </summary>
    private static async Task<string> SealAsync(Document document)
    {
        string input = (await document.GetTextAsync()).ToString();
        string result = await new ClassSealingConverter().SealWhenSafeAsync(new[] { document }, CancellationToken.None);

        if (result != input)
        {
            List<string> added = await GetProblemsAsync(document, result);
            foreach (string existing in await GetProblemsAsync(document, input))
            {
                added.Remove(existing);
            }

            Assert.IsEmpty(added, string.Join("\r\n", added));
        }

        return result;
    }

    private static async Task<List<string>> GetProblemsAsync(Document document, string text)
    {
        Compilation compilation = await document.WithText(SourceText.From(text)).Project.GetCompilationAsync();

        return compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error || diagnostic.Id == "CS0628")
            .Select(diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}")
            .ToList();
    }
}
