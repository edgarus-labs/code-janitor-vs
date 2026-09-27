using System;

namespace CodeJanitor.UI.Dialogs.CleanupOptions;

internal sealed class CleanupPreviewRule : Bindable
{
    private readonly Action _changed;

    internal CleanupPreviewRule(int index, string name, Action changed)
    {
        Index = index;
        Name = name;
        Include = true;
        _changed = changed;
    }

    public int Index { get; }

    public string Name { get; }

    public bool Include
    {
        get => GetPropertyValue<bool>();
        set
        {
            if (SetPropertyValue(value))
            {
                _changed?.Invoke();
            }
        }
    }

    public string Outcome
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }
}
