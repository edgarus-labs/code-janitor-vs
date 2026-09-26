using CodeJanitor.Properties;
using Microsoft.VisualStudio.Shell;
using System.Collections.Generic;

namespace CodeJanitor.UI.Dialogs.Options;

/// <summary>
/// The abstract base class for option pages.
/// </summary>

public abstract class OptionsPageViewModel : Bindable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OptionsPageViewModel" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <param name="activeSettings">The active settings.</param>

    protected OptionsPageViewModel(CodeJanitorPackage package, Settings activeSettings)
    {
        Package = package;
        ActiveSettings = activeSettings;
    }

    /// <summary>
    /// Gets the header.
    /// </summary>
    public abstract string Header { get; }

    /// <summary>
    /// Gets the hosting package.
    /// </summary>
    public CodeJanitorPackage Package { get; private set; }

    /// <summary>
    /// Gets the active settings.
    /// </summary>
    public Settings ActiveSettings { get; private set; }

    private IEnumerable<OptionsPageViewModel> _children;

    /// <summary>
    /// Gets or sets the children.
    /// </summary>

    public IEnumerable<OptionsPageViewModel> Children
    {
        get
        {
            return _children ?? (_children = new OptionsPageViewModel[0]);
        }
        set
        {
            if (_children != value)
            {
                _children = value;
                RaisePropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the list of settings to options mappings.
    /// </summary>
    protected SettingsToOptionsList Mappings { get; set; }

    private EditorConfigOverrideNotes _editorConfigOverrides;

    /// <summary>
    /// Gets the notes for the settings the open solution's .editorconfig overrides, resolved when first bound after
    /// the settings are loaded; no setting is annotated when no solution is open.
    /// </summary>
    public EditorConfigOverrideNotes EditorConfigOverrides =>
        _editorConfigOverrides ??= EditorConfigOverrideNotes.ForSolution(GetOpenSolutionFullName());

    /// <summary>
    /// Loads the settings and discards the .editorconfig notes, so they are resolved again for the solution open now.
    /// </summary>

    public virtual void LoadSettings()
    {
        Mappings?.CopySettingsToOptions();

        _editorConfigOverrides = null;
        RaisePropertyChanged(nameof(EditorConfigOverrides));
    }

    /// <summary>
    /// Saves the settings.
    /// </summary>

    public virtual void SaveSettings()
    {
        Mappings?.CopyOptionsToSettings();
    }

    /// <summary>
    /// Gets the full path of the open solution file.
    /// </summary>
    /// <returns>The solution file path, or null when no solution is open.</returns>

    private string GetOpenSolutionFullName()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var solution = Package?.IDE?.Solution;
        return solution is not null && solution.IsOpen ? solution.FullName : null;
    }
}
