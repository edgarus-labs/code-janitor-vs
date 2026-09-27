using System;
using System.Collections.Generic;
using System.Linq;
using CodeJanitor.Logic.Transformations;

namespace CodeJanitor.UI.Dialogs.CleanupOptions;

internal sealed class CleanupPreviewFile : Bindable
{
    private readonly SourceTransformationPipeline _pipeline;
    private readonly HashSet<int> _excludedRules = new HashSet<int>();
    private SourceTransformationPipeline.PreviewResult _preview;

    internal CleanupPreviewFile(string path, string source, SourceTransformationPipeline pipeline, string error = null)
    {
        Path = path;
        OriginalSource = source;
        _pipeline = pipeline;
        Status = error;
        Rules = pipeline?.Transformations.Select((rule, index) =>
            new CleanupPreviewRule(index, rule.Name, Recalculate)).ToList()
            ?? new List<CleanupPreviewRule>();
        if (pipeline is not null)
        {
            Recalculate();
            Include = CanApply;
        }
    }

    public string Path { get; }

    public string OriginalSource { get; }

    public string UpdatedSource => _preview?.UpdatedSource ?? OriginalSource;

    public IReadOnlyList<CleanupPreviewRule> Rules { get; }

    public bool CanApply => _preview?.HasChanges == true;

    public bool Include
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    public string Status
    {
        get => GetPropertyValue<string>();
        private set => SetPropertyValue(value);
    }

    internal bool TryApply(string currentSource, Action<string> replaceSource)
        => Include && _preview is not null && _preview.TryApply(currentSource, replaceSource);

    private void Recalculate()
    {
        _excludedRules.Clear();
        foreach (var rule in Rules.Where(rule => !rule.Include))
        {
            _excludedRules.Add(rule.Index);
        }

        try
        {
            _preview = _pipeline.Preview(OriginalSource, _excludedRules);
            Status = _preview.HasChanges ? "Changes ready" : "No changes";
            foreach (var step in _preview.Steps)
            {
                Rules[step.Index].Outcome = !step.Included ? "Excluded" : step.Changed ? "Changed" : "No change";
            }
        }
        catch (Exception exception)
        {
            _preview = null;
            Status = "Skipped: " + exception.Message;
        }

        RaisePropertyChanged(nameof(UpdatedSource));
        RaisePropertyChanged(nameof(CanApply));
    }
}
