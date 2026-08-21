using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Cleaning;
using System;
using System.IO;
using System.Linq;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public class TopLevelTypeToFileSplitPlannerTests
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
        var source =
            "namespace Demo;\r\n\r\nclass Foo { }\r\ninterface IBar { }\r\nenum Baz { A }\r\n";

        var plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.IsTrue(plan.HasChanges);
        StringAssert.Contains(plan.UpdatedSource, "class Foo");
        Assert.IsFalse(plan.UpdatedSource.Contains("interface IBar"));
        Assert.IsFalse(plan.UpdatedSource.Contains("enum Baz"));
        CollectionAssert.AreEquivalent(new[] { "IBar.cs", "Baz.cs" }, plan.NewFiles.Select(x => Path.GetFileName(x.FilePath)).ToArray());
    }

    [TestMethod]
    public void CreatePlan_EncodesGenericParametersInNewFileNames_AndResolvesCollisions()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, "Thing{TIn}.cs"), string.Empty);

        var source =
            "namespace Demo;\r\n\r\nclass Thing { }\r\nclass Thing<TIn> { }\r\nclass Thing<TIn, TOut> { }\r\n";

        var plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Thing.cs"));

        CollectionAssert.AreEquivalent(
            new[] { "Thing{TIn}~1.cs", "Thing{TIn,TOut}.cs" },
            plan.NewFiles.Select(x => Path.GetFileName(x.FilePath)).ToArray());
    }

    [TestMethod]
    public void CreatePlan_SkipsFilesWithConditionalCompilationDirectives()
    {
        var source =
            "namespace Demo;\r\n\r\n#if DEBUG\r\nclass Foo { }\r\n#endif\r\nclass Bar { }\r\n";

        var plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.IsFalse(plan.HasChanges);
        Assert.AreEqual(source, plan.UpdatedSource);
    }

    [TestMethod]
    public void CreatePlan_LeavesPartialAndNestedTypesInTheOriginalFile()
    {
        var source =
            "namespace Demo;\r\n\r\nclass Foo { class Nested { } }\r\npartial class Shared { }\r\ninterface IBar { }\r\n";

        var plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.IsTrue(plan.HasChanges);
        StringAssert.Contains(plan.UpdatedSource, "partial class Shared");
        StringAssert.Contains(plan.UpdatedSource, "class Nested");
        CollectionAssert.AreEquivalent(new[] { "IBar.cs" }, plan.NewFiles.Select(x => Path.GetFileName(x.FilePath)).ToArray());
    }

    [TestMethod]
    public void CreatePlan_PreservesFileScopedNamespace_InGeneratedFiles()
    {
        var source =
            "namespace Demo;\r\n\r\nclass Foo { }\r\ndelegate void Work<TIn, TOut>(TIn input);\r\n";

        var plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        var generated = plan.NewFiles.Single();
        StringAssert.Contains(generated.Content, "namespace Demo;");
        StringAssert.Contains(generated.Content, "delegate void Work<TIn, TOut>");
    }

    [TestMethod]
    public void CreatePlan_SkipsFilesWithAssemblyAttributes()
    {
        var source =
            "[assembly: CLSCompliant(true)]\r\nnamespace Demo;\r\n\r\nclass Foo { }\r\nclass Bar { }\r\n";

        var plan = _planner.CreatePlan(source, Path.Combine(_tempDirectory, "Foo.cs"));

        Assert.IsFalse(plan.HasChanges);
        Assert.AreEqual(source, plan.UpdatedSource);
    }
}
