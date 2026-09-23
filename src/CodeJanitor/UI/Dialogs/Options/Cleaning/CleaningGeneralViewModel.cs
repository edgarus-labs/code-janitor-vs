using CodeJanitor.Properties;
using CodeJanitor.UI.Enumerations;

namespace CodeJanitor.UI.Dialogs.Options.Cleaning;

/// <summary>
/// The view model for cleaning general options.
/// </summary>

public sealed class CleaningGeneralViewModel : OptionsPageViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CleaningGeneralViewModel" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <param name="activeSettings">The active settings.</param>

    public CleaningGeneralViewModel(CodeJanitorPackage package, Settings activeSettings)
        : base(package, activeSettings)
    {
        Mappings = new SettingsToOptionsList(ActiveSettings, this)
        {
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_AutoCleanupOnFileSave, x => AutoCleanupOnFileSave),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_AutoSaveAndCloseIfOpenedByCleanup, x => AutoSaveAndCloseIfOpenedByCleanup),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_EnableParallelCleanup, x => EnableParallelCleanup),
            new SettingToOptionMapping<int, int>(x => ActiveSettings.Cleaning_MaxDegreeOfParallelism, x => MaxDegreeOfParallelism),
            new SettingToOptionMapping<int, AskYesNo>(x => ActiveSettings.Cleaning_PerformPartialCleanupOnExternal, x => PerformPartialCleanupOnExternal)
        };
    }

    /// <summary>
    /// Gets the header.
    /// </summary>
    public override string Header => Resources.General;

    /// <summary>
    /// Gets or sets the flag indicating if cleanup should run automatically on file save.
    /// </summary>

    public bool AutoCleanupOnFileSave
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if files should be automatically saved and closed if
    /// opened by cleanup.
    /// </summary>

    public bool AutoSaveAndCloseIfOpenedByCleanup
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets a value indicating whether background/headless cleanup runs in parallel across multiple threads.
    /// </summary>
    public bool EnableParallelCleanup
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the maximum degree of parallelism (0 = automatic based on processor count).
    /// </summary>
    public int MaxDegreeOfParallelism
    {
        get { return GetPropertyValue<int>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the options for performing partial cleanup on external files.
    /// </summary>

    public AskYesNo PerformPartialCleanupOnExternal
    {
        get { return GetPropertyValue<AskYesNo>(); }
        set { SetPropertyValue(value); }
    }
}
