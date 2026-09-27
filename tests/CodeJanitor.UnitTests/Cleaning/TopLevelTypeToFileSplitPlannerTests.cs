using System;
using System.IO;
using System.Linq;
using CodeJanitor.Logic.Cleaning;
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
}
