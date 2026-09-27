using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Transformations;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="UsingDirectiveOrganizer" />.
/// Sorting order: regular usings, then <c>using static</c>, then alias usings; within a group
/// <c>System</c> namespaces first, then ordinal alphabetical. Formatting is preserved and any
/// block containing comments, preprocessor directives or <c>global using</c> directives is left
/// untouched (conservative, headless-Roslyn building block for BL-018, C#-only per scope).
/// </summary>
[TestClass]
public sealed class UsingDirectiveOrganizerTests
{
    private IUsingDirectiveOrganizer _organizer;

    [TestInitialize]
    public void TestInitialize()
    {
        _organizer = new UsingDirectiveOrganizer();
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void UnsortedUsings_GetSortedAlphabetically()
    {
        string input = "using B;\nusing A;\n";
        string expected = "using A;\nusing B;\n";

        Assert.AreEqual(expected, _organizer.Organize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void AlreadySortedUsings_Unchanged()
    {
        string input = "using A;\nusing B;\nusing C;\n";

        Assert.AreEqual(input, _organizer.Organize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SystemNamespaces_SortedFirst()
    {
        string input = "using MyLib;\nusing System;\n";
        string expected = "using System;\nusing MyLib;\n";

        Assert.AreEqual(expected, _organizer.Organize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SystemSubNamespaces_GroupedBeforeOthers()
    {
        string input = "using System.Text;\nusing Abc;\nusing System;\n";
        string expected = "using System;\nusing System.Text;\nusing Abc;\n";

        Assert.AreEqual(expected, _organizer.Organize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void StaticUsings_SortedAfterRegularUsings()
    {
        string input = "using static System.Math;\nusing System;\n";
        string expected = "using System;\nusing static System.Math;\n";

        Assert.AreEqual(expected, _organizer.Organize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void AliasUsings_SortedLast()
    {
        string input = "using Foo = System.Int32;\nusing System;\n";
        string expected = "using System;\nusing Foo = System.Int32;\n";

        Assert.AreEqual(expected, _organizer.Organize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NamespaceScopedUsings_SortedWithIndentationPreserved()
    {
        string input = "namespace N\n{\n    using B;\n    using A;\n}\n";
        string expected = "namespace N\n{\n    using A;\n    using B;\n}\n";

        Assert.AreEqual(expected, _organizer.Organize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FileScopedNamespaceUsings_Sorted()
    {
        string input = "namespace N;\n\nusing B;\nusing A;\n";
        string expected = "namespace N;\n\nusing A;\nusing B;\n";

        Assert.AreEqual(expected, _organizer.Organize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void UsingsWithComment_LeftUntouched()
    {
        string input = "using B; // keep near B\nusing A;\n";

        Assert.AreEqual(input, _organizer.Organize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void UsingsWithPreprocessorDirective_LeftUntouched()
    {
        string input = "#if DEBUG\nusing B;\n#endif\nusing A;\n";

        Assert.AreEqual(input, _organizer.Organize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void GlobalUsings_LeftUntouched()
    {
        string input = "global using B;\nglobal using A;\n";

        Assert.AreEqual(input, _organizer.Organize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SingleUsing_Unchanged()
    {
        string input = "using A;\n";

        Assert.AreEqual(input, _organizer.Organize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NoUsings_Unchanged()
    {
        string input = "namespace N\n{\n}\n";

        Assert.AreEqual(input, _organizer.Organize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void EmptySource_Unchanged()
    {
        Assert.AreEqual(string.Empty, _organizer.Organize(string.Empty));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NullSource_ReturnsNull()
    {
        Assert.IsNull(_organizer.Organize(null));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void BlankLineBeforeNamespace_Preserved()
    {
        string input = "using B;\nusing A;\n\nnamespace N\n{\n}\n";
        string expected = "using A;\nusing B;\n\nnamespace N\n{\n}\n";

        Assert.AreEqual(expected, _organizer.Organize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NameAndApply_WorkCorrectly()
    {
        UsingDirectiveOrganizer transformation = new UsingDirectiveOrganizer();
        Assert.AreEqual("Sort using directives", transformation.Name);

        string input = "using B;\nusing A;\n";
        string expected = "using A;\nusing B;\n";
        Assert.AreEqual(expected, transformation.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void MultipleAliasesAndStaticUsings_SortedProperly()
    {
        string input = "using Z = System.Int32;\nusing A = System.String;\nusing static System.Math;\nusing static System.Console;\n";
        string expected = "using static System.Console;\nusing static System.Math;\nusing A = System.String;\nusing Z = System.Int32;\n";

        Assert.AreEqual(expected, _organizer.Organize(input));
    }
}
