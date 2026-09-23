using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Runs an ordered sequence of <see cref="ISourceTransformation" /> blocks over a piece of C#
/// source text - the composable "flow" of the headless-Roslyn cleanup path (BL-018). Each block
/// receives the output of the previous one; a block that does not apply returns its input
/// unchanged, so the pipeline is safe to run with any subset or ordering of blocks.
/// </summary>

public sealed class SourceTransformationPipeline
{
    private readonly IList<ISourceTransformation> _transformations;

    /// <summary>
    /// Initializes a new pipeline from the given transformations, in the order they should run.
    /// </summary>

    public SourceTransformationPipeline(params ISourceTransformation[] transformations)
        : this((IEnumerable<ISourceTransformation>)transformations)
    {
    }

    /// <summary>
    /// Initializes a new pipeline from the given transformations, in the order they should run.
    /// </summary>

    public SourceTransformationPipeline(IEnumerable<ISourceTransformation> transformations)
    {
        if (transformations is null)
        {
            throw new ArgumentNullException(nameof(transformations));
        }

        _transformations = transformations.Where(t => t is not null).ToList();
    }

    /// <summary>
    /// Gets the ordered transformations that make up this pipeline.
    /// </summary>
    public IReadOnlyList<ISourceTransformation> Transformations => (IReadOnlyList<ISourceTransformation>)_transformations;

    /// <summary>
    /// Runs every transformation in order and returns the final source text. Empty or null input
    /// is returned unchanged.
    /// </summary>

    public string Run(string source)
    {
        return Execute(source, null, null);
    }

    public PreviewResult Preview(string source, ISet<int> excludedTransformations = null)
    {
        var steps = new List<PreviewStep>();
        var updatedSource = Execute(source, excludedTransformations, steps);

        return new PreviewResult(source, updatedSource, steps.AsReadOnly());
    }

    private string Execute(string source, ISet<int> excludedTransformations, IList<PreviewStep> steps)
    {
        if (string.IsNullOrEmpty(source))
        {
            return source;
        }

        var current = source;
        for (var index = 0; index < _transformations.Count; index++)
        {
            var transformation = _transformations[index];
            var included = excludedTransformations?.Contains(index) != true;
            var updated = included ? transformation.Apply(current) ?? current : current;
            steps?.Add(new PreviewStep(index, transformation.Name, included,
                !string.Equals(current, updated, StringComparison.Ordinal)));
            current = updated;
        }

        return current;
    }

    public sealed class PreviewResult
    {
        internal PreviewResult(string originalSource, string updatedSource, IReadOnlyList<PreviewStep> steps)
        {
            OriginalSource = originalSource;
            UpdatedSource = updatedSource;
            Steps = steps;
        }

        public string OriginalSource { get; }
        public string UpdatedSource { get; }
        public IReadOnlyList<PreviewStep> Steps { get; }
        public bool HasChanges => !string.Equals(OriginalSource, UpdatedSource, StringComparison.Ordinal);

        public bool TryApply(string currentSource, Action<string> replaceSource)
        {
            if (!string.Equals(OriginalSource, currentSource, StringComparison.Ordinal))
            {
                return false;
            }

            if (HasChanges)
            {
                if (replaceSource is null)
                {
                    throw new ArgumentNullException(nameof(replaceSource));
                }

                replaceSource(UpdatedSource);
            }

            return true;
        }
    }

    public sealed class PreviewStep
    {
        internal PreviewStep(int index, string name, bool included, bool changed)
        {
            Index = index;
            Name = name;
            Included = included;
            Changed = changed;
        }

        public int Index { get; }
        public string Name { get; }
        public bool Included { get; }
        public bool Changed { get; }
    }
}
