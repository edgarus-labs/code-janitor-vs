using CodeJanitor.Logic.Transformations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("   \r\n\t\r\n", DisplayName = "whitespace-only file")]
    [DataRow("namespace N;\r\n\r\nusing A;\r\nusing B;\r\n\r\nclass C { }\r\n", DisplayName = "sorted usings in a file-scoped namespace")]
    [DataRow("namespace N\r\n{\r\n    using A;\r\n    using B;\r\n}\r\n", DisplayName = "sorted usings in a block namespace")]
    [DataRow("global using B;\r\nglobal using A;\r\nusing D;\r\nusing C;\r\n", DisplayName = "global usings mixed with regular usings")]
    [DataRow("#if DEBUG\r\nusing B;\r\n#endif\r\nusing A;\r\n", DisplayName = "using inside #if")]
    [DataRow("using B; // needed for Foo\r\nusing A;\r\n", DisplayName = "trailing comment on a using")]
    [DataRow("/// <summary>doc</summary>\r\nusing B;\r\nusing A;\r\n", DisplayName = "doc comment before the usings")]
    [DataRow("#region Usings\r\nusing B;\r\nusing A;\r\n#endregion\r\n", DisplayName = "usings inside #region")]
    [DataRow("using A;\r\nusing B;\r\n\r\nConsole.WriteLine(\"using C; using A;\");\r\n", DisplayName = "top-level statements after sorted usings")]
    public void Organize_NothingToSortOrUnsafe_ReturnsInputUnchanged(string input)
    {
        Assert.AreEqual(input, _organizer.Organize(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow(
        "using System.Threading.Tasks;\r\nusing Contoso;\r\nusing System;\r\n\r\nawait Task.Delay(1);\r\nConsole.WriteLine(\"using Zeta;\");\r\n\r\nstatic void Local() { }\r\n\r\nrecord R(int X);\r\n",
        "using System;\r\nusing System.Threading.Tasks;\r\nusing Contoso;\r\n\r\nawait Task.Delay(1);\r\nConsole.WriteLine(\"using Zeta;\");\r\n\r\nstatic void Local() { }\r\n\r\nrecord R(int X);\r\n",
        DisplayName = "top-level statements file")]
    [DataRow(
        "using SystemX;\nusing System.Linq;\nusing Systemic;\n",
        "using System.Linq;\nusing SystemX;\nusing Systemic;\n",
        DisplayName = "only System and System.* rank first")]
    [DataRow(
        "using b;\nusing B;\nusing a;\n",
        "using B;\nusing a;\nusing b;\n",
        DisplayName = "ordinal, case-sensitive order")]
    [DataRow(
        "using static Contoso.Helpers;\nusing static System.Math;\n",
        "using static System.Math;\nusing static Contoso.Helpers;\n",
        DisplayName = "System first among static usings")]
    [DataRow(
        "using Pair = (int, string);\nusing unsafe Ptr = int*;\nusing Alpha = Contoso.Alpha;\nusing Contoso;\n",
        "using Contoso;\nusing Alpha = Contoso.Alpha;\nusing Pair = (int, string);\nusing unsafe Ptr = int*;\n",
        DisplayName = "tuple and pointer aliases sort by alias name")]
    [DataRow(
        "extern alias Legacy;\r\nusing B;\r\nusing A;\r\n",
        "extern alias Legacy;\r\nusing A;\r\nusing B;\r\n",
        DisplayName = "extern alias stays first")]
    [DataRow(
        "using B;\r\nusing A;\r\n\r\nnamespace N\r\n{\r\n    using D;\r\n    using C;\r\n\r\n    namespace Inner\r\n    {\r\n        using F;\r\n        using E;\r\n    }\r\n}\r\n",
        "using A;\r\nusing B;\r\n\r\nnamespace N\r\n{\r\n    using C;\r\n    using D;\r\n\r\n    namespace Inner\r\n    {\r\n        using E;\r\n        using F;\r\n    }\r\n}\r\n",
        DisplayName = "every level of nested namespaces")]
    [DataRow(
        "using B;\n\nusing A;\n",
        "using A;\n\nusing B;\n",
        DisplayName = "blank line stays in its slot")]
    [DataRow(
        "\tusing B;\r\n\tusing A;\r\n",
        "\tusing A;\r\n\tusing B;\r\n",
        DisplayName = "tab indentation")]
    [DataRow(
        "using B;\r\nusing A;\r\n[assembly: System.CLSCompliant(false)]\r\nnamespace N { class C { string s = @\"using Z;\r\nusing Y;\"; } }\r\n",
        "using A;\r\nusing B;\r\n[assembly: System.CLSCompliant(false)]\r\nnamespace N { class C { string s = @\"using Z;\r\nusing Y;\"; } }\r\n",
        DisplayName = "attributes and verbatim strings with code-like text after the usings")]
    public void Organize_SortsEveryBlock_AndKeepsTheRestOfTheFile(string input, string expected)
    {
        Assert.AreEqual(expected, _organizer.Organize(input));
    }
}
