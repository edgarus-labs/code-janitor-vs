using CodeJanitor.Properties;
using System.Collections.Generic;

namespace CodeJanitor.UI.Dialogs.Options;

/// <summary>
/// Base class for an option page that hosts a set of child option pages as tabs, instead of
/// each child being its own separate node in the Tools&gt;Options tree.
/// </summary>

public abstract class CompositeOptionsPageViewModel : OptionsPageViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeOptionsPageViewModel" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <param name="activeSettings">The active settings.</param>
    /// <param name="children">The child pages to show as tabs.</param>

    protected CompositeOptionsPageViewModel(CodeJanitorPackage package, Settings activeSettings, IEnumerable<OptionsPageViewModel> children)
        : base(package, activeSettings)
    {
        Children = children;
    }

    /// <inheritdoc />

    public override void LoadSettings()
    {
        base.LoadSettings();

        foreach (var child in Children)
        {
            child.LoadSettings();
        }
    }

    /// <inheritdoc />

    public override void SaveSettings()
    {
        base.SaveSettings();

        foreach (var child in Children)
        {
            child.SaveSettings();
        }
    }
}