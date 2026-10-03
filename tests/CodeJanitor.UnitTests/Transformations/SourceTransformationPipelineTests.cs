using System.Collections.Generic;
using System.Linq;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.UI.Dialogs.CleanupOptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
        SourceTransformationPipeline pipeline = new SourceTransformationPipeline();
        string input = "class C\n{\n}\n";

        Assert.AreEqual(input, pipeline.Run(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void SingleBlock_IsApplied()
    {
        SourceTransformationPipeline pipeline = new SourceTransformationPipeline(new TabToSpaceConverter());
        string input = "class C\n{\n\tint x;\n}\n";
        string expected = "class C\n{\n    int x;\n}\n";

        Assert.AreEqual(expected, pipeline.Run(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void MultipleBlocks_RunInOrderAndFeedEachOther()
    {
        // Tab-indented, unsorted usings inside a namespace. First convert tabs to spaces, then
        // sort the using directives - each block consumes the previous block's output.
        SourceTransformationPipeline pipeline = new SourceTransformationPipeline(
            new TabToSpaceConverter(),
            new UsingDirectiveOrganizer());

        string input = "namespace N\n{\n\tusing B;\n\tusing A;\n}\n";
        string expected = "namespace N\n{\n    using A;\n    using B;\n}\n";

        Assert.AreEqual(expected, pipeline.Run(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NullBlocks_AreIgnored()
    {
        SourceTransformationPipeline pipeline = new SourceTransformationPipeline(null, new TabToSpaceConverter(), null);
        string input = "class C\n{\n\tint x;\n}\n";
        string expected = "class C\n{\n    int x;\n}\n";

        Assert.AreEqual(expected, pipeline.Run(input));
        Assert.HasCount(1, pipeline.Transformations);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Transformations_ExposedInOrder()
    {
        SourceTransformationPipeline pipeline = new SourceTransformationPipeline(
            new TabToSpaceConverter(),
            new UsingDirectiveOrganizer());

        List<string> names = pipeline.Transformations.Select(t => t.Name).ToList();

        Assert.HasCount(2, names);
        Assert.AreEqual("Convert tabs to spaces", names[0]);
        Assert.AreEqual("Sort using directives", names[1]);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void EmptySource_ReturnsUnchanged()
    {
        SourceTransformationPipeline pipeline = new SourceTransformationPipeline(new TabToSpaceConverter());

        Assert.AreEqual(string.Empty, pipeline.Run(string.Empty));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void FullFlow_TabsThenTrailingThenSortThenFinalNewline()
    {
        // Tab-indented, unsorted usings with trailing spaces and no final newline. The blocks run
        // in order: expand tabs, strip trailing whitespace, sort usings, ensure final newline.
        SourceTransformationPipeline pipeline = new SourceTransformationPipeline(
            new TabToSpaceConverter(),
            new RemoveTrailingWhitespaceConverter(),
            new UsingDirectiveOrganizer(),
            new EnsureFinalNewlineConverter());

        string input = "namespace N\n{\n\tusing B;  \n\tusing A;\n}";
        string expected = "namespace N\n{\n    using A;\n    using B;\n}\n";

        Assert.AreEqual(expected, pipeline.Run(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void AdaptedConverters_AreComposableInPipeline()
    {
        // Verify that VarWhenApparentConverter, ReadonlyFieldConverter and FileScopedNamespaceConverter
        // (which were adapted to implement ISourceTransformation)
        // can be instantiated and composed in a pipeline with other blocks.
        SourceTransformationPipeline pipeline = new SourceTransformationPipeline(
            new UsingDirectiveOrganizer(),
            new VarWhenApparentConverter(),
            new ReadonlyFieldConverter(),
            new FileScopedNamespaceConverter());

        // A simple example: namespace that gets converted to file-scoped. The var/readonly
        // converters won't apply but should not disrupt the pipeline.
        // FileScopedNamespaceConverter appends: header + "namespace N;" + newline + newline + dedented body + newline
        string input = "namespace N\n{\n\tusing B;\n\tusing A;\n}\n";
        string expected = "namespace N;\n\nusing A;\nusing B;\n";

        string result = pipeline.Run(input);
        Assert.AreEqual(expected, result, $"Expected length: {expected.Length}, Actual length: {result.Length}. Expected repr: {repr(expected)}, Actual repr: {repr(result)}");

        // Verify all transformations are exposed with their names.
        List<string> names = pipeline.Transformations.Select(t => t.Name).ToList();
        Assert.HasCount(4, names);
        Assert.Contains("Sort using directives", names);
        Assert.Contains("Var When Apparent", names);
        Assert.Contains("Readonly Field", names);
        Assert.Contains("File-Scoped Namespace", names);
    }

    [TestMethod]
    public void PreviewFile_RuleChangesRecomputeFromOriginalAndFileExclusionPreventsApply()
    {
        string source = "\tclass C {}";
        CleanupPreviewFile file = new CleanupPreviewFile("Example.cs", source,
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
        CleanupPreviewFile file = new CleanupPreviewFile("Example.cs", "\tclass C {}",
            new SourceTransformationPipeline(new TabToSpaceConverter()));
        CleanupPreviewViewModel viewModel = new CleanupPreviewViewModel(new[] { file });

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
        SourceTransformationPipeline.PreviewResult preview = new SourceTransformationPipeline(new TabToSpaceConverter()).Preview("\tclass C {}");
        bool called = false;

        Assert.IsFalse(preview.TryApply("class UserEdit {}", _ => called = true));
        Assert.IsFalse(called);
    }

    [TestMethod]
    public void Preview_ApplyWritesExactlyTheApprovedResult()
    {
        string source = "\tclass C {}";
        SourceTransformationPipeline.PreviewResult preview = new SourceTransformationPipeline(new TabToSpaceConverter()).Preview(source);
        string applied = null;

        Assert.IsTrue(preview.TryApply(source, updated => applied = updated));
        Assert.AreEqual(preview.UpdatedSource, applied);
    }

    [TestMethod]
    public void Preview_ApplyDoesNotWriteWhenNothingChanged()
    {
        string source = "class C {}";
        SourceTransformationPipeline.PreviewResult preview = new SourceTransformationPipeline().Preview(source);

        Assert.IsTrue(preview.TryApply(source, _ => Assert.Fail("Unchanged text must not be written.")));
    }

    [TestMethod]
    public void Preview_MatchesRunAndReportsChangesInOrder()
    {
        SourceTransformationPipeline pipeline = new SourceTransformationPipeline(new TabToSpaceConverter(), new EnsureFinalNewlineConverter());
        string source = "class C {\n\tint value;\n}";

        SourceTransformationPipeline.PreviewResult preview = pipeline.Preview(source);

        Assert.AreEqual(source, preview.OriginalSource);
        Assert.AreEqual(pipeline.Run(source), preview.UpdatedSource);
        Assert.IsTrue(preview.HasChanges);
        Assert.HasCount(2, preview.Steps);
        Assert.AreEqual(0, preview.Steps[0].Index);
        Assert.AreEqual(pipeline.Transformations[1].Name, preview.Steps[1].Name);
        Assert.IsTrue(preview.Steps.All(step => step.Included && step.Changed));
    }

    [TestMethod]
    public void Preview_ExcludedStepIsNotAppliedAndDoesNotAffectLaterSteps()
    {
        SourceTransformationPipeline pipeline = new SourceTransformationPipeline(new TabToSpaceConverter(), new EnsureFinalNewlineConverter());
        string source = "class C {\n\tint value;\n}";

        SourceTransformationPipeline.PreviewResult preview = pipeline.Preview(source, new System.Collections.Generic.HashSet<int> { 0 });

        Assert.AreEqual(new EnsureFinalNewlineConverter().Apply(source), preview.UpdatedSource);
        Assert.IsFalse(preview.Steps[0].Included);
        Assert.IsFalse(preview.Steps[0].Changed);
        Assert.IsTrue(preview.Steps[1].Changed);
    }

    [TestMethod]
    public void Preview_UnchangedSourceReportsNoChanges()
    {
        SourceTransformationPipeline pipeline = new SourceTransformationPipeline(new TabToSpaceConverter());
        SourceTransformationPipeline.PreviewResult preview = pipeline.Preview("class C {}\n");

        Assert.IsFalse(preview.HasChanges);
        Assert.IsFalse(preview.Steps.Single().Changed);
        Assert.IsTrue(preview.Steps.Single().Included);
    }

    [TestMethod]
    public void Preview_EmptyAndNullSourceMatchRun()
    {
        SourceTransformationPipeline pipeline = new SourceTransformationPipeline(new TabToSpaceConverter());

        Assert.AreEqual(pipeline.Run(null), pipeline.Preview(null).UpdatedSource);
        Assert.AreEqual(pipeline.Run(string.Empty), pipeline.Preview(string.Empty).UpdatedSource);
        Assert.IsEmpty(pipeline.Preview(null).Steps);
    }

    [TestMethod]
    public void Preview_AllStepsExcludedPreservesSource()
    {
        SourceTransformationPipeline pipeline = new SourceTransformationPipeline(new TabToSpaceConverter(), new EnsureFinalNewlineConverter());
        string source = "\tclass C {}";
        SourceTransformationPipeline.PreviewResult preview = pipeline.Preview(source, new System.Collections.Generic.HashSet<int> { 0, 1 });

        Assert.AreEqual(source, preview.UpdatedSource);
        Assert.IsFalse(preview.HasChanges);
        Assert.IsTrue(preview.Steps.All(step => !step.Included));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NullTransformationSequence_Throws()
    {
        Assert.ThrowsExactly<System.ArgumentNullException>(() => new SourceTransformationPipeline((IEnumerable<ISourceTransformation>)null));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void BlockReturningNull_LeavesTheTextOfThePreviousBlockForTheNextOne()
    {
        SourceTransformationPipeline pipeline = new SourceTransformationPipeline(
            new TabToSpaceConverter(), new NullResultTransformation(), new EnsureFinalNewlineConverter());

        SourceTransformationPipeline.PreviewResult preview = pipeline.Preview("\tclass C {}");

        Assert.AreEqual("    class C {}\n", preview.UpdatedSource);
        Assert.IsFalse(preview.Steps[1].Changed);
        Assert.IsTrue(preview.Steps[1].Included);
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void Preview_ApplyWithChangesButNoWriter_Throws()
    {
        string source = "\tclass C {}";
        SourceTransformationPipeline.PreviewResult preview = new SourceTransformationPipeline(new TabToSpaceConverter()).Preview(source);

        Assert.ThrowsExactly<System.ArgumentNullException>(() => preview.TryApply(source, null));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void WhitespaceFlowOverATopLevelStatementsFile_ProducesCleanOutputWithoutAddedComments()
    {
        string input =
            "\uFEFFusing System;  \r\n\r\n\r\n\r\n//Entry point\r\nConsole.WriteLine(\"hi\");\t\r\nRun();\r\n\r\n" +
            "static int Run()\r\n{\r\n\tvar x = 1;\r\n\treturn x;\r\n}";
        string expected =
            "using System;\r\n\r\n// Entry point\r\nConsole.WriteLine(\"hi\");\r\nRun();\r\n\r\n" +
            "static int Run()\r\n{\r\n    var x = 1;\r\n\r\n    return x;\r\n}\r\n";
        SourceTransformationPipeline pipeline = new SourceTransformationPipeline(
            new ByteOrderMarkConverter(),
            new RemoveTrailingWhitespaceConverter(),
            new ReturnThrowBlankLinePaddingConverter(),
            new CommentFormatConverter(),
            new TabToSpaceConverter(),
            new NormalizeBlankLinesConverter(),
            new EnsureFinalNewlineConverter());
        bool commentFormatting = CodeJanitor.Properties.Settings.Default.Formatting_CommentRunDuringCleanup;
        CodeJanitor.Properties.Settings.Default.Formatting_CommentRunDuringCleanup = true;

        try
        {
            Assert.AreEqual(expected, pipeline.Run(input));
        }
        finally
        {
            CodeJanitor.Properties.Settings.Default.Formatting_CommentRunDuringCleanup = commentFormatting;
        }
    }

    private sealed class NullResultTransformation : ISourceTransformation
    {
        public string Name => "Returns null";

        public string Apply(string source) => null;
    }

    private static string repr(string s)
    {
        return "\"" + s.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    }
}
