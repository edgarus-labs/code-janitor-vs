using System;
using System.IO;
using System.Linq;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.UnitTests.Transformations;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public sealed class TopLevelTypeToFileSplitPlannerTests
{
    private string _tempDirectory;
    private TopLevelTypeToFileSplitPlanner _planner;

    [TestInitialize]
    public void TestInitialize()
    {
        _planner = new TopLevelTypeToFileSplitPlanner();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [TestMethod]
    public void CreatePlan_KeepsTypeMatchingOriginalFileName_AndMovesOtherTypes()
    {
        string source =
            "namespace Demo;\r\n\r\nclass Foo { }\r\ninterface IBar { }\r\nenum Baz { A }\r\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.IsTrue(plan.HasChanges);
        Assert.Contains("class Foo", plan.UpdatedSource);
        Assert.DoesNotContain("interface IBar", plan.UpdatedSource);
        Assert.DoesNotContain("enum Baz", plan.UpdatedSource);
        Assert.AreSequenceEqual(new[] { "IBar.cs", "Baz.cs" }, plan.NewFiles.Select(x => Path.GetFileName(x.FilePath)).ToArray(), Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public void CreatePlan_MovesEveryKindOfTopLevelType()
    {
        string source =
            "namespace Demo;\r\n\r\nclass Foo { }\r\nstruct Point { }\r\nrecord struct Pair(int A, int B);\r\nrecord Person(string Name);\r\ninterface IBar { }\r\nenum Baz { A }\r\ndelegate void Work();\r\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.AreEqual("namespace Demo;\r\n\r\nclass Foo { }\r\n", plan.UpdatedSource);
        Assert.AreSequenceEqual(
            new[] { "Point.cs", "Pair.cs", "Person.cs", "IBar.cs", "Baz.cs", "Work.cs" },
            plan.NewFiles.Select(x => Path.GetFileName(x.FilePath)).ToArray(),
            Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public void CreatePlan_EncodesGenericParametersInNewFileNames_AndResolvesCollisions()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, "Thing{TIn}.cs"), string.Empty);

        string source =
            "namespace Demo;\r\n\r\nclass Thing { }\r\nclass Thing<TIn> { }\r\nclass Thing<TIn, TOut> { }\r\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Thing.cs"));

        Assert.AreSequenceEqual(
            new[] { "Thing{TIn}~1.cs", "Thing{TIn,TOut}.cs" }, plan.NewFiles.Select(x => Path.GetFileName(x.FilePath)).ToArray(), Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public void CreatePlan_SkipsFilesWithConditionalCompilationDirectives()
    {
        string source =
            "namespace Demo;\r\n\r\n#if DEBUG\r\nclass Foo { }\r\n#endif\r\nclass Bar { }\r\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.IsFalse(plan.HasChanges);
        Assert.AreEqual(source, plan.UpdatedSource);
    }

    [TestMethod]
    public void CreatePlan_LeavesPartialAndNestedTypesInTheOriginalFile()
    {
        string source =
            "namespace Demo;\r\n\r\nclass Foo { class Nested { } }\r\npartial class Shared { }\r\ninterface IBar { }\r\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.IsTrue(plan.HasChanges);
        Assert.Contains("partial class Shared", plan.UpdatedSource);
        Assert.Contains("class Nested", plan.UpdatedSource);
        Assert.AreSequenceEqual(new[] { "IBar.cs" }, plan.NewFiles.Select(x => Path.GetFileName(x.FilePath)).ToArray(), Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public void CreatePlan_PreservesFileScopedNamespace_InGeneratedFiles()
    {
        string source =
            "namespace Demo;\r\n\r\nclass Foo { }\r\ndelegate void Work<TIn, TOut>(TIn input);\r\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        TopLevelTypeToFileSplitPlanner.PlannedFile generated = plan.NewFiles.Single();
        Assert.Contains("namespace Demo;", generated.Content);
        Assert.Contains("delegate void Work<TIn, TOut>", generated.Content);
    }

    [TestMethod]
    public void CreatePlan_SkipsFilesWithAssemblyAttributes()
    {
        string source =
            "[assembly: CLSCompliant(true)]\r\nnamespace Demo;\r\n\r\nclass Foo { }\r\nclass Bar { }\r\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.IsFalse(plan.HasChanges);
        Assert.AreEqual(source, plan.UpdatedSource);
    }

    [TestMethod]
    public void CreatePlan_TopLevelStatementsFileWithTypesAfterTheStatements_IsLeftUnchanged()
    {
        string source =
            "using System;\r\n\r\nConsole.WriteLine(Greeter.Greet());\r\nstatic void Local() { }\r\n\r\nclass Greeter\r\n{\r\n    public static string Greet() => \"class Hidden { }\";\r\n}\r\n\r\nrecord Person(string Name);\r\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Program.cs"));

        Assert.IsFalse(plan.HasChanges);
        Assert.AreEqual(TopLevelTypeSplitSkipReason.UnsupportedStructure, plan.SkipReason);
        Assert.AreEqual(source, plan.UpdatedSource);
    }

    [TestMethod]
    [DataRow("namespace A { class Foo { } }\r\nnamespace B { class Bar { } }\r\n", DisplayName = "Two namespaces")]
    [DataRow("namespace A;\r\n\r\nclass Foo { }\r\nnamespace B { class Bar { } }\r\n", DisplayName = "Namespace nested in a file-scoped namespace")]
    [DataRow("class Outside { }\r\nnamespace A { class Foo { } class Bar { } }\r\n", DisplayName = "Type next to a namespace")]
    [DataRow("namespace A { namespace B { class Foo { } } class Bar { } }\r\n", DisplayName = "Nested block namespace")]
    [DataRow("namespace Demo;\r\n\r\n#pragma warning disable CS0169\r\nclass Foo { int unused; }\r\nclass Bar { }\r\n", DisplayName = "Pragma directive")]
    [DataRow("#nullable enable\r\nnamespace Demo;\r\n\r\nclass Foo { }\r\nclass Bar { }\r\n", DisplayName = "Nullable directive")]
    [DataRow("namespace Demo;\r\n\r\nclass Foo\r\n{\r\n#if DEBUG\r\n    int debugOnly;\r\n#endif\r\n}\r\nclass Bar { }\r\n", DisplayName = "Conditional directive inside a type")]
    public void CreatePlan_UnsupportedFileStructure_IsLeftUnchanged(string source)
    {
        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.IsFalse(plan.HasChanges);
        Assert.AreEqual(TopLevelTypeSplitSkipReason.UnsupportedStructure, plan.SkipReason);
        Assert.AreEqual(source, plan.UpdatedSource);
        Assert.IsEmpty(plan.NewFiles);
    }

    [TestMethod]
    [DataRow("", "Foo.cs")]
    [DataRow(" \r\n\t\r\n", "Foo.cs")]
    [DataRow("class Foo { }\r\nclass Bar { }\r\n", "")]
    [DataRow("class Foo { }\r\nclass Bar { }\r\n", null)]
    public void CreatePlan_EmptySourceOrPath_IsSkippedAsEmpty(string source, string fileName)
    {
        string filePath = fileName is null ? null : fileName.Length == 0 ? " " : Path.Combine(_tempDirectory, fileName);

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, filePath);

        Assert.IsFalse(plan.HasChanges);
        Assert.AreEqual(TopLevelTypeSplitSkipReason.EmptySource, plan.SkipReason);
        Assert.AreEqual(source, plan.UpdatedSource);
    }

    [TestMethod]
    public void CreatePlan_SingleEligibleTypeNextToPartialAndNestedTypes_IsNotSplit()
    {
        string source =
            "namespace Demo;\r\n\r\nclass Foo\r\n{\r\n    class Nested { }\r\n    enum Kind { A }\r\n}\r\n\r\npartial class Shared { }\r\npartial record Other;\r\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.IsFalse(plan.HasChanges);
        Assert.AreEqual(TopLevelTypeSplitSkipReason.NotMultipleEligibleTypes, plan.SkipReason);
        Assert.AreEqual(source, plan.UpdatedSource);
    }

    [TestMethod]
    public void CreatePlan_BlockNamespace_MovesTypesTogetherWithTheirDocCommentsAndAttributes()
    {
        string source =
            "using System;\r\n\r\nnamespace Demo\r\n{\r\n    /// <summary>Foo doc.</summary>\r\n    [Serializable]\r\n    public class Foo { }\r\n\r\n    /// <summary>Bar doc.</summary>\r\n    [Obsolete(\"class Baz { }\")]\r\n    public sealed class Bar\r\n    {\r\n        public string Text => $\"{nameof(Bar)} {{ }}\";\r\n    }\r\n}\r\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.AreEqual(
            "using System;\r\n\r\nnamespace Demo\r\n{\r\n    /// <summary>Foo doc.</summary>\r\n    [Serializable]\r\n    public class Foo { }\r\n}\r\n",
            plan.UpdatedSource);
        TopLevelTypeToFileSplitPlanner.PlannedFile generated = plan.NewFiles.Single();
        Assert.AreEqual(Path.Combine(_tempDirectory, "Bar.cs"), generated.FilePath);
        Assert.StartsWith("using System;\r\n\r\nnamespace Demo\r\n{\r\n", generated.Content);
        Assert.EndsWith(
            "    /// <summary>Bar doc.</summary>\r\n    [Obsolete(\"class Baz { }\")]\r\n    public sealed class Bar\r\n    {\r\n        public string Text => $\"{nameof(Bar)} {{ }}\";\r\n    }\r\n}\r\n",
            generated.Content);
    }

    [TestMethod]
    public void CreatePlan_TabIndentedCrLfBlockNamespace_KeepsLineEndingsAndIndentation()
    {
        string source = "namespace Demo\r\n{\r\n\tclass Foo { }\r\n\r\n\tinterface IBar { }\r\n}\r\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.AreEqual("namespace Demo\r\n{\r\n\tclass Foo { }\r\n}\r\n", plan.UpdatedSource);
        string generated = plan.NewFiles.Single().Content;
        Assert.StartsWith("namespace Demo\r\n{\r\n", generated);
        Assert.EndsWith("\tinterface IBar { }\r\n}\r\n", generated);
        Assert.DoesNotContain("\n", generated.Replace("\r\n", string.Empty));
    }

    [TestMethod]
    public void CreatePlan_TypesInTheGlobalNamespace_CopiesHeaderAndUsingsIntoEveryFile()
    {
        string source = "// Header\nusing System;\n\nclass Foo { }\n\nclass Bar { }\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.AreEqual("// Header\nusing System;\n\nclass Foo { }\n", plan.UpdatedSource);
        TopLevelTypeToFileSplitPlanner.PlannedFile generated = plan.NewFiles.Single();
        Assert.AreEqual(Path.Combine(_tempDirectory, "Bar.cs"), generated.FilePath);
        Assert.AreEqual("// Header\nusing System;\n\nclass Bar { }\n", generated.Content);
    }

    [TestMethod]
    public void CreatePlan_GlobalUsings_StayOnlyInTheOriginalFile()
    {
        // A global using repeated in a split-out file is reported as a duplicate global using (CS8933).
        string source = "// Header\nglobal using System;\nusing System.IO;\n\nclass Foo { }\n\nclass Bar { }\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.AreEqual("// Header\nglobal using System;\nusing System.IO;\n\nclass Foo { }\n", plan.UpdatedSource);
        Assert.AreEqual("// Header\nusing System.IO;\n\nclass Bar { }\n", plan.NewFiles.Single().Content);
    }

    [TestMethod]
    public void CreatePlan_OnlyGlobalUsings_LeavesTheGeneratedFileWithoutUsings()
    {
        string source = "// Header\nglobal using System;\n\nclass Foo { }\n\nclass Bar { }\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.AreEqual("// Header\nglobal using System;\n\nclass Foo { }\n", plan.UpdatedSource);
        Assert.AreEqual("// Header\n\nclass Bar { }\n", plan.NewFiles.Single().Content);
    }

    [TestMethod]
    public void CreatePlan_NoTypeMatchesTheFileName_KeepsTheFirstTypeAndMovesTheOthers()
    {
        string source = "namespace Demo;\n\nclass Alpha { }\n\nclass Beta { }\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Helpers.cs"));

        Assert.AreEqual("namespace Demo;\n\nclass Alpha { }\n", plan.UpdatedSource);
        Assert.AreEqual(Path.Combine(_tempDirectory, "Beta.cs"), plan.NewFiles.Single().FilePath);
    }

    [TestMethod]
    public void CreatePlan_KeepsTheTypeMatchingTheFileNameCaseInsensitively_EvenWhenItIsNotFirst()
    {
        string source = "namespace Demo;\n\nclass Alpha { }\n\nclass Beta { }\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "BETA.cs"));

        Assert.AreEqual("namespace Demo;\n\nclass Beta { }\n", plan.UpdatedSource);
        Assert.AreEqual(Path.Combine(_tempDirectory, "Alpha.cs"), plan.NewFiles.Single().FilePath);
    }

    [TestMethod]
    public void CreatePlan_GenericTypeKeptByItsEncodedFileName_MovesTheOtherGenericTypeWithItsConstraints()
    {
        string source = "namespace Demo;\n\npublic class Foo<T> where T : class { }\n\npublic class Bar<TKey, TValue> where TKey : notnull { }\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo{T}.cs"));

        Assert.AreEqual("namespace Demo;\n\npublic class Foo<T> where T : class { }\n", plan.UpdatedSource);
        TopLevelTypeToFileSplitPlanner.PlannedFile generated = plan.NewFiles.Single();
        Assert.AreEqual(Path.Combine(_tempDirectory, "Bar{TKey,TValue}.cs"), generated.FilePath);
        Assert.AreEqual("namespace Demo;\n\npublic class Bar<TKey, TValue> where TKey : notnull { }\n", generated.Content);
    }

    [TestMethod]
    public void CreatePlan_FileNamesTakenCaseInsensitively_PicksTheNextFreeSuffix()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, "bar.cs"), string.Empty);
        File.WriteAllText(Path.Combine(_tempDirectory, "Bar~1.cs"), string.Empty);
        File.WriteAllText(Path.Combine(_tempDirectory, "Baz.txt"), string.Empty);

        string source = "namespace Demo;\n\nclass Foo { }\nclass Bar { }\nclass Baz { }\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.AreSequenceEqual(
            new[] { "Bar~2.cs", "Baz.cs" },
            plan.NewFiles.Select(x => Path.GetFileName(x.FilePath)).ToArray());
    }

    [TestMethod]
    public void CreatePlan_RegionInsideAType_StaysWithThatType()
    {
        string source = "namespace Demo;\n\nclass Foo\n{\n    #region Fields\n    private int _count;\n    #endregion\n}\n\nclass Bar { }\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.AreEqual("namespace Demo;\n\nclass Foo\n{\n    #region Fields\n    private int _count;\n    #endregion\n}\n", plan.UpdatedSource);
        Assert.AreEqual("namespace Demo;\n\nclass Bar { }\n", plan.NewFiles.Single().Content);
    }

    [TestMethod]
    [DataRow("namespace Demo;\n\n#region Types\nclass Foo { }\nclass Bar { }\n#endregion\n", false, DisplayName = "region around several types")]
    [DataRow("namespace Demo;\n\nclass Foo { }\n\n#region Helpers\nclass Bar { }\n#endregion\n", true, DisplayName = "region around the moved type")]
    [DataRow("namespace Demo;\n\n#region Helpers\nclass Bar { }\n#endregion\n\nclass Foo { }\n", true, DisplayName = "region around a moved type followed by another type")]
    [DataRow("namespace Demo\n{\n    class Foo { }\n\n    #region Helpers\n    class Bar { }\n    #endregion\n}\n", true, DisplayName = "region around the last type of a block namespace")]
    [DataRow("namespace Demo;\n\n#region Main\nclass Foo { }\n#endregion\n\nclass Bar { }\n", true, DisplayName = "region around the kept type")]
    [DataRow("#region Types\nclass Foo { }\n\nclass Bar { }\n#endregion\n", false, DisplayName = "region around types without a namespace")]
    public void CreatePlan_RegionAroundTopLevelTypes_KeepsEveryFileCompilable(string source, bool expectSplit)
    {
        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.AreEqual(expectSplit, plan.HasChanges);
        Document document = CompilingTestProject.CreateDocument(plan.UpdatedSource, plan.NewFiles.Select(x => x.Content).ToArray());
        Assert.IsEmpty(document.Project.GetCompilationAsync(TestContext.CancellationToken).Result.GetDiagnostics(TestContext.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [TestMethod]
    public void CreatePlan_RegionAroundOneMovedType_MovesTheWholeRegionWithThatType()
    {
        string source = "namespace Demo;\n\n#region Helpers\nclass Bar { }\n#endregion\n\nclass Foo { }\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.AreEqual("namespace Demo;\n\nclass Foo { }\n", plan.UpdatedSource);
        Assert.AreEqual("namespace Demo;\n\n#region Helpers\nclass Bar { }\n#endregion\n", plan.NewFiles.Single().Content);
    }

    [TestMethod]
    [DataRow("namespace Demo;\n\nclass Foo { private Hidden _hidden = new Hidden(); }\n\nfile class Hidden { }\n", DisplayName = "used by the kept type")]
    [DataRow("namespace Demo;\n\nclass Foo { }\n\nclass Bar { private Hidden _hidden = new Hidden(); }\n\nfile class Hidden { }\n", DisplayName = "used by a type that would move")]
    public void CreatePlan_FileWithAFileLocalType_IsNotSplit(string source)
    {
        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.IsFalse(plan.HasChanges);
        Assert.AreEqual(source, plan.UpdatedSource);
    }

    [TestMethod]
    public void CreatePlan_FileHeaderBeforeTheFirstMovedType_StaysInTheOriginalFile()
    {
        string source = "// Copyright (c) Demo.\n\n/// <summary>Bar.</summary>\nclass Bar { }\n\nclass Foo { }\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.AreEqual("// Copyright (c) Demo.\n\nclass Foo { }\n", plan.UpdatedSource);
        Assert.AreEqual("// Copyright (c) Demo.\n\n/// <summary>Bar.</summary>\nclass Bar { }\n", plan.NewFiles.Single().Content);
    }

    [TestMethod]
    public void CreatePlan_RecordsAndRecordStructs_AreMovedToTheirOwnFiles()
    {
        string source = "namespace Demo;\n\npublic record Foo(int A);\npublic readonly record struct Bar(int B);\npublic record class Baz<T>(T Value);\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.AreEqual("namespace Demo;\n\npublic record Foo(int A);\n", plan.UpdatedSource);
        Assert.AreSequenceEqual(
            new[] { "namespace Demo;\npublic readonly record struct Bar(int B);\n", "namespace Demo;\npublic record class Baz<T>(T Value);\n" },
            plan.NewFiles.Select(x => x.Content).ToArray());
        Assert.AreSequenceEqual(new[] { "Bar.cs", "Baz{T}.cs" }, plan.NewFiles.Select(x => Path.GetFileName(x.FilePath)).ToArray());
    }

    [TestMethod]
    public void CreatePlan_PathWithoutADirectory_PlansNewFilesWithoutADirectory()
    {
        string source = "class Foo { }\nclass Bar { }\n";

        TopLevelTypeToFileSplitPlanner.SplitPlan relative = _planner.CreatePlan(source, "Foo.cs");
        TopLevelTypeToFileSplitPlanner.SplitPlan root = _planner.CreatePlan(source, Path.GetPathRoot(_tempDirectory));

        Assert.AreEqual("Bar.cs", relative.NewFiles.Single().FilePath);
        Assert.AreEqual("class Foo { }\n", relative.UpdatedSource);
        Assert.AreEqual("Bar.cs", root.NewFiles.Single().FilePath);
    }

    public TestContext TestContext { get; set; }
}
