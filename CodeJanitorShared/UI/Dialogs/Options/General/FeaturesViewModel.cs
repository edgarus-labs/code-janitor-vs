using CodeJanitor.Properties;
using Mapping = CodeJanitor.UI.Dialogs.Options.SettingToOptionMapping<bool, bool>;

namespace CodeJanitor.UI.Dialogs.Options.General;

/// <summary>
/// model that exposes a collection of editor and solution explorer commands or feature toggles for an IDE extension.
/// </summary>
public sealed class FeaturesViewModel : OptionsPageViewModel
{
    public FeaturesViewModel(CodeJanitorPackage package, Settings activeSettings)
        : base(package, activeSettings)
    {
        Mappings = new SettingsToOptionsList(ActiveSettings, this)
        {
            new Mapping(x => ActiveSettings.Feature_BuildProgressToolWindow, x => BuildProgressToolWindow),
            new Mapping(x => ActiveSettings.Feature_CleanupActiveCode, x => CleanupActiveCode),
            new Mapping(x => ActiveSettings.Feature_CleanupChangedFiles, x => CleanupChangedFiles),
            new Mapping(x => ActiveSettings.Feature_CleanupOpenCode, x => CleanupOpenCode),
            new Mapping(x => ActiveSettings.Feature_CloseAllReadOnly, x => CloseAllReadOnly),
            new Mapping(x => ActiveSettings.Feature_CollapseAllSolutionExplorer, x => CollapseAllSolutionExplorer),
            new Mapping(x => ActiveSettings.Feature_CollapseSelectedSolutionExplorer, x => CollapseSelectedSolutionExplorer),
            new Mapping(x => ActiveSettings.Feature_CommentFormat, x => CommentFormat),
            new Mapping(x => ActiveSettings.Feature_FindInSolutionExplorer, x => FindInSolutionExplorer),
            new Mapping(x => ActiveSettings.Feature_JoinLines, x => JoinLines),
            new Mapping(x => ActiveSettings.Feature_ReadOnlyToggle, x => ReadOnlyToggle),
            new Mapping(x => ActiveSettings.Feature_RemoveRegion, x => RemoveRegion),
            new Mapping(x => ActiveSettings.Feature_ReorganizeActiveCode, x => ReorganizeActiveCode),
            new Mapping(x => ActiveSettings.Feature_SettingCleanupOnSave, x => SettingCleanupOnSave),
            new Mapping(x => ActiveSettings.Feature_SortLines, x => SortLines),
            new Mapping(x => ActiveSettings.Feature_SpadeToolWindow, x => SpadeToolWindow),
            new Mapping(x => ActiveSettings.Feature_SwitchFile, x => SwitchFile),
            new Mapping(x => ActiveSettings.Feature_AddXmlDoc, x => AddXmlDoc),
            new Mapping(x => ActiveSettings.Feature_AiExplainMethod, x => AiExplainMethod),
            new Mapping(x => ActiveSettings.Feature_AiGenerateUnitTests, x => AiGenerateUnitTests),
            new Mapping(x => ActiveSettings.Feature_AiCleanRefactor, x => AiCleanRefactor),
            new Mapping(x => ActiveSettings.Feature_AiCodeReview, x => AiCodeReview),
            new Mapping(x => ActiveSettings.Feature_AiTargetCoverage, x => AiTargetCoverage)
        };
    }

    /// <summary>
    /// Gets the header.
    /// </summary>
    public override string Header => Resources.FeaturesViewModel_Features;

    /// <summary>
    /// Gets or sets the build progress tool window.
    /// </summary>
    public bool BuildProgressToolWindow
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the cleanup active code.
    /// </summary>
    public bool CleanupActiveCode
    {
        get => GetPropertyValue<bool>();
        set
        {
            SetPropertyValue(value);

            if (!value)
            {
                SettingCleanupOnSave = false;
            }
        }
    }

    /// <summary>
    /// Gets or sets the cleanup changed files.
    /// </summary>
    public bool CleanupChangedFiles
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the cleanup open code.
    /// </summary>
    public bool CleanupOpenCode
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the close all read only.
    /// </summary>
    public bool CloseAllReadOnly
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the collapse all solution explorer.
    /// </summary>
    public bool CollapseAllSolutionExplorer
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the collapse selected solution explorer.
    /// </summary>
    public bool CollapseSelectedSolutionExplorer
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the comment format.
    /// </summary>
    public bool CommentFormat
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the find in solution explorer.
    /// </summary>
    public bool FindInSolutionExplorer
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the join lines.
    /// </summary>
    public bool JoinLines
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the read only toggle.
    /// </summary>
    public bool ReadOnlyToggle
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the remove region.
    /// </summary>
    public bool RemoveRegion
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the reorganize active code.
    /// </summary>
    public bool ReorganizeActiveCode
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the setting cleanup on save.
    /// </summary>
    public bool SettingCleanupOnSave
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the sort lines.
    /// </summary>
    public bool SortLines
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the spade tool window.
    /// </summary>
    public bool SpadeToolWindow
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the switch file.
    /// </summary>
    public bool SwitchFile
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the add xml doc.
    /// </summary>
    public bool AddXmlDoc
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the AI Explain Method feature is enabled.
    /// </summary>
    public bool AiExplainMethod
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the AI Generate Unit Tests feature is enabled.
    /// </summary>
    public bool AiGenerateUnitTests
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the AI Clean Refactor feature is enabled.
    /// </summary>
    public bool AiCleanRefactor
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the AI Code Review feature is enabled.
    /// </summary>
    public bool AiCodeReview
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the AI Target Coverage feature is enabled.
    /// </summary>
    public bool AiTargetCoverage
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }
}
