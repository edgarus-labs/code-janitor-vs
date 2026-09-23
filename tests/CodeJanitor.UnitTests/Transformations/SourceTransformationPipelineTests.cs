using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.UI.Dialogs.CleanupOptions;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="SourceTransformationPipeline" />. Verifies that composable blocks
/// run in order and feed each other, which is the core "flow" of the headless-Roslyn cleanup
/// path (BL-018).
/// </summary>

[TestClass]
public sealed class SourceTransformationPipelineTests
{
    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void EmptyPipeline_ReturnsSourceUnchanged()
    {
        var pipeline = new SourceTransformationPipeline();
        var input = "class C\n{\n}\n";

        Assert.AreEqual(input, pipeline.Run(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SingleBlock_IsApplied()
    {
        var pipeline = new SourceTransformationPipeline(new TabToSpaceConverter());
        var input = "class C\n{\n\tint x;\n}\n";
        var expected = "class C\n{\n    int x;\n}\n";

        Assert.AreEqual(expected, pipeline.Run(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void MultipleBlocks_RunInOrderAndFeedEachOther()
    {
        // Tab-indented, unsorted usings inside a namespace. First convert tabs to spaces, then
        // sort the using directives - each block consumes the previous block's output.
        var pipeline = new SourceTransformationPipeline(
            new TabToSpaceConverter(),
            new UsingDirectiveOrganizer());

        var input = "namespace N\n{\n\tusing B;\n\tusing A;\n}\n";
        var expected = "namespace N\n{\n    using A;\n    using B;\n}\n";

        Assert.AreEqual(expected, pipeline.Run(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NullBlocks_AreIgnored()
    {
        var pipeline = new SourceTransformationPipeline(null, new TabToSpaceConverter(), null);
        var input = "class C\n{\n\tint x;\n}\n";
        var expected = "class C\n{\n    int x;\n}\n";

        Assert.AreEqual(expected, pipeline.Run(input));
        Assert.AreEqual(1, pipeline.Transformations.Count);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Transformations_ExposedInOrder()
    {
        var pipeline = new SourceTransformationPipeline(
            new TabToSpaceConverter(),
            new UsingDirectiveOrganizer());

        var names = pipeline.Transformations.Select(t => t.Name).ToList();

        Assert.AreEqual(2, names.Count);
        Assert.AreEqual("Convert tabs to spaces", names[0]);
        Assert.AreEqual("Sort using directives", names[1]);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void EmptySource_ReturnsUnchanged()
    {
        var pipeline = new SourceTransformationPipeline(new TabToSpaceConverter());

        Assert.AreEqual(string.Empty, pipeline.Run(string.Empty));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FullFlow_TabsThenTrailingThenSortThenFinalNewline()
    {
        // Tab-indented, unsorted usings with trailing spaces and no final newline. The blocks run
        // in order: expand tabs, strip trailing whitespace, sort usings, ensure final newline.
        var pipeline = new SourceTransformationPipeline(
            new TabToSpaceConverter(),
            new RemoveTrailingWhitespaceConverter(),
            new UsingDirectiveOrganizer(),
            new EnsureFinalNewlineConverter());

        var input = "namespace N\n{\n\tusing B;  \n\tusing A;\n}";
        var expected = "namespace N\n{\n    using A;\n    using B;\n}\n";

        Assert.AreEqual(expected, pipeline.Run(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void AdaptedConverters_AreComposableInPipeline()
    {
        // Verify that VarWhenApparentConverter, ReadonlyFieldConverter, SealedClassConverter,
        // and FileScopedNamespaceConverter (which were adapted to implement ISourceTransformation)
        // can be instantiated and composed in a pipeline with other blocks.
        var pipeline = new SourceTransformationPipeline(
            new UsingDirectiveOrganizer(),
            new VarWhenApparentConverter(),
            new ReadonlyFieldConverter(),
            new SealedClassConverter(),
            new FileScopedNamespaceConverter());

        // A simple example: namespace that gets converted to file-scoped. The var/readonly/sealed
        // converters won't apply but should not disrupt the pipeline.
        // FileScopedNamespaceConverter appends: header + "namespace N;" + newline + newline + dedented body + newline
        var input = "namespace N\n{\n\tusing B;\n\tusing A;\n}\n";
        var expected = "using A;\nusing B;\n\nnamespace N;\n";

        var result = pipeline.Run(input);
        Assert.AreEqual(expected, result, $"Expected length: {expected.Length}, Actual length: {result.Length}. Expected repr: {repr(expected)}, Actual repr: {repr(result)}");

        // Verify all transformations are exposed with their names.
        var names = pipeline.Transformations.Select(t => t.Name).ToList();
        Assert.AreEqual(5, names.Count);
        Assert.IsTrue(names.Contains("Sort using directives"));
        Assert.IsTrue(names.Contains("Var When Apparent"));
        Assert.IsTrue(names.Contains("Readonly Field"));
        Assert.IsTrue(names.Contains("Sealed Class"));
        Assert.IsTrue(names.Contains("File-Scoped Namespace"));
    }

    [TestMethod]
    public void PreviewFile_RuleChangesRecomputeFromOriginalAndFileExclusionPreventsApply()
    {
        var source = "\tclass C {}";
        var file = new CleanupPreviewFile("Example.cs", source,
            new SourceTransformationPipeline(new TabToSpaceConverter(), new EnsureFinalNewlineConverter()));

        file.Rules[0].Include = false;
        Assert.AreEqual(new EnsureFinalNewlineConverter().Apply(source), file.UpdatedSource);
        file.Rules[0].Include = true;
        Assert.AreEqual(new EnsureFinalNewlineConverter().Apply(new TabToSpaceConverter().Apply(source)), file.UpdatedSource);
        file.Include = false;
        Assert.IsFalse(file.TryApply(source, _ => Assert.Fail("Excluded files must not be applied.")));
    }

    [TestMethod]
    public void PreviewViewModel_DisablesApplyWithoutSelectedChanges()
    {
        var file = new CleanupPreviewFile("Example.cs", "\tclass C {}",
            new SourceTransformationPipeline(new TabToSpaceConverter()));
        var viewModel = new CleanupPreviewViewModel(new[] { file });

        Assert.IsTrue(viewModel.ApplyCommand.CanExecute(null));
        file.Include = false;
        Assert.IsFalse(viewModel.ApplyCommand.CanExecute(null));
        file.Include = true;
        file.Rules[0].Include = false;
        Assert.IsFalse(viewModel.ApplyCommand.CanExecute(null));
    }

    [TestMethod]
    public void Preview_ApplyRejectsStaleSourceWithoutCallingWriter()
    {
        var preview = new SourceTransformationPipeline(new TabToSpaceConverter()).Preview("\tclass C {}");
        var called = false;

        Assert.IsFalse(preview.TryApply("class UserEdit {}", _ => called = true));
        Assert.IsFalse(called);
    }

    [TestMethod]
    public void Preview_ApplyWritesExactlyTheApprovedResult()
    {
        var source = "\tclass C {}";
        var preview = new SourceTransformationPipeline(new TabToSpaceConverter()).Preview(source);
        string applied = null;

        Assert.IsTrue(preview.TryApply(source, updated => applied = updated));
        Assert.AreEqual(preview.UpdatedSource, applied);
    }

    [TestMethod]
    public void Preview_ApplyDoesNotWriteWhenNothingChanged()
    {
        var source = "class C {}";
        var preview = new SourceTransformationPipeline().Preview(source);

        Assert.IsTrue(preview.TryApply(source, _ => Assert.Fail("Unchanged text must not be written.")));
    }

    [TestMethod]
    public void Preview_MatchesRunAndReportsChangesInOrder()
    {
        var pipeline = new SourceTransformationPipeline(new TabToSpaceConverter(), new EnsureFinalNewlineConverter());
        var source = "class C {\n\tint value;\n}";

        var preview = pipeline.Preview(source);

        Assert.AreEqual(source, preview.OriginalSource);
        Assert.AreEqual(pipeline.Run(source), preview.UpdatedSource);
        Assert.IsTrue(preview.HasChanges);
        Assert.AreEqual(2, preview.Steps.Count);
        Assert.AreEqual(0, preview.Steps[0].Index);
        Assert.AreEqual(pipeline.Transformations[1].Name, preview.Steps[1].Name);
        Assert.IsTrue(preview.Steps.All(step => step.Included && step.Changed));
    }

    [TestMethod]
    public void Preview_ExcludedStepIsNotAppliedAndDoesNotAffectLaterSteps()
    {
        var pipeline = new SourceTransformationPipeline(new TabToSpaceConverter(), new EnsureFinalNewlineConverter());
        var source = "class C {\n\tint value;\n}";

        var preview = pipeline.Preview(source, new System.Collections.Generic.HashSet<int> { 0 });

        Assert.AreEqual(new EnsureFinalNewlineConverter().Apply(source), preview.UpdatedSource);
        Assert.IsFalse(preview.Steps[0].Included);
        Assert.IsFalse(preview.Steps[0].Changed);
        Assert.IsTrue(preview.Steps[1].Changed);
    }

    [TestMethod]
    public void Preview_UnchangedSourceReportsNoChanges()
    {
        var pipeline = new SourceTransformationPipeline(new TabToSpaceConverter());
        var preview = pipeline.Preview("class C {}\n");

        Assert.IsFalse(preview.HasChanges);
        Assert.IsFalse(preview.Steps.Single().Changed);
        Assert.IsTrue(preview.Steps.Single().Included);
    }

    [TestMethod]
    public void Preview_EmptyAndNullSourceMatchRun()
    {
        var pipeline = new SourceTransformationPipeline(new TabToSpaceConverter());

        Assert.AreEqual(pipeline.Run(null), pipeline.Preview(null).UpdatedSource);
        Assert.AreEqual(pipeline.Run(string.Empty), pipeline.Preview(string.Empty).UpdatedSource);
        Assert.AreEqual(0, pipeline.Preview(null).Steps.Count);
    }

    [TestMethod]
    public void Preview_AllStepsExcludedPreservesSource()
    {
        var pipeline = new SourceTransformationPipeline(new TabToSpaceConverter(), new EnsureFinalNewlineConverter());
        var source = "\tclass C {}";
        var preview = pipeline.Preview(source, new System.Collections.Generic.HashSet<int> { 0, 1 });

        Assert.AreEqual(source, preview.UpdatedSource);
        Assert.IsFalse(preview.HasChanges);
        Assert.IsTrue(preview.Steps.All(step => !step.Included));
    }

    private static string repr(string s)
    {
        return "\"" + s.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    }
}
