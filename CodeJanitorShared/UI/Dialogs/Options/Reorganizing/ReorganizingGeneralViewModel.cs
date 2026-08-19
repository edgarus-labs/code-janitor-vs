using CodeJanitor.Properties;
using CodeJanitor.UI.Enumerations;

namespace CodeJanitor.UI.Dialogs.Options.Reorganizing;

/// <summary>
/// The view model for reorganizing general options.
/// </summary>

public class ReorganizingGeneralViewModel : OptionsPageViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReorganizingGeneralViewModel" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <param name="activeSettings">The active settings.</param>

    public ReorganizingGeneralViewModel(CodeJanitorPackage package, Settings activeSettings)
        : base(package, activeSettings)
    {
        Mappings = new SettingsToOptionsList(ActiveSettings, this)
        {
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Reorganizing_AlphabetizeMembersOfTheSameGroup, x => AlphabetizeMembersOfTheSameGroup),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Reorganizing_ExplicitMembersAtEnd, x => ExplicitInterfaceMembersAtTheEnd),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Reorganizing_KeepMembersWithinRegions, x => KeepMembersWithinRegions),
            new SettingToOptionMapping<int, AskYesNo>(x => ActiveSettings.Reorganizing_PerformWhenPreprocessorConditionals, x => PerformWhenPreprocessorConditionals),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Reorganizing_PrimaryOrderByAccessLevel, x => PrimaryOrderByAccessLevel),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Reorganizing_ReverseOrderByAccessLevel, x => ReverseOrderByAccessLevel),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Reorganizing_RunAtStartOfCleanup, x => RunAtStartOfCleanup),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Reorganizing_RegionsIncludeAccessLevel, x => RegionsIncludeAccessLevel),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Reorganizing_RegionsIncludeAccessLevelForMethodsOnly, x => RegionsIncludeAccessLevelForMethodsOnly),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Reorganizing_RegionsInsertKeepEvenIfEmpty, x => RegionsInsertKeepEvenIfEmpty),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Reorganizing_RegionsInsertNewRegions, x => RegionsInsertNewRegions),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Reorganizing_RegionsRemoveExistingRegions, x => RegionsRemoveExistingRegions)
        };
    }

    /// <summary>
    /// Gets the header.
    /// </summary>
    public override string Header => Resources.General;

    /// <summary>
    /// Gets or sets the flag indicating if members of the same group should be alphabetized.
    /// </summary>

    public bool AlphabetizeMembersOfTheSameGroup
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if explicit interface members should be placed at the end of the group.
    /// </summary>

    public bool ExplicitInterfaceMembersAtTheEnd
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if members should be kept within regions.
    /// </summary>

    public bool KeepMembersWithinRegions
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the options for performing reorganization when preprocessor conditionals are present.
    /// </summary>

    public AskYesNo PerformWhenPreprocessorConditionals
    {
        get { return GetPropertyValue<AskYesNo>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if primary ordering should be by access level.
    /// </summary>

    public bool PrimaryOrderByAccessLevel
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if the access level ordering should be reversed (i.e. private first).
    /// </summary>

    public bool ReverseOrderByAccessLevel
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if reorganizing should be run at the start of cleanup.
    /// </summary>

    public bool RunAtStartOfCleanup
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if the access level should be included in regions.
    /// </summary>

    public bool RegionsIncludeAccessLevel
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if the access level should only be included in regions for methods.
    /// </summary>

    public bool RegionsIncludeAccessLevelForMethodsOnly
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if regions should be kept even if they are empty.
    /// </summary>

    public bool RegionsInsertKeepEvenIfEmpty
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if new regions should be inserted.
    /// </summary>

    public bool RegionsInsertNewRegions
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if existing regions should be removed.
    /// </summary>

    public bool RegionsRemoveExistingRegions
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }
}