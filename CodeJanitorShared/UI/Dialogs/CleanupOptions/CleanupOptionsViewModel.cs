using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.Options;
using CodeJanitor.UI.Dialogs.Options.Cleaning;
using System;
using System.Collections.Generic;
using System.Configuration;

namespace CodeJanitor.UI.Dialogs.CleanupOptions
{
    /// <summary>
    /// View model for selecting cleanup execution mode and optional temporary cleanup settings.
    /// </summary>
    internal class CleanupOptionsViewModel : Bindable
    {
        private readonly Settings _temporarySettings;

        /// <summary>
        /// Initializes a new instance of the <see cref="CleanupOptionsViewModel"/> class.
        /// </summary>
        internal CleanupOptionsViewModel(CodeJanitorPackage package, Settings activeSettings, int itemCount)
        {
            ItemCount = itemCount;

            _temporarySettings = CreateSettingsCopy(activeSettings);
            TemporaryCleaningSettingsViewModel = new CleaningParentViewModel(package, _temporarySettings);
            TemporaryCleaningSettingsViewModel.LoadSettings();

            UseConfiguredCleanupSettings = true;
        }

        /// <summary>
        /// Gets item count in selected cleanup scope.
        /// </summary>
        public int ItemCount
        {
            get { return GetPropertyValue<int>(); }
            private set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets whether to run cleanup using persisted settings.
        /// </summary>
        public bool UseConfiguredCleanupSettings
        {
            get { return GetPropertyValue<bool>(); }
            set
            {
                if (SetPropertyValue(value))
                {
                    RaisePropertyChanged(nameof(UseTemporaryCleanupSettings));
                }
            }
        }

        /// <summary>
        /// Gets a flag indicating temporary settings should be used for this run only.
        /// </summary>
        public bool UseTemporaryCleanupSettings => !UseConfiguredCleanupSettings;

        /// <summary>
        /// Gets the temporary cleaning settings view model hosted in the dialog.
        /// </summary>
        public OptionsPageViewModel TemporaryCleaningSettingsViewModel
        {
            get { return GetPropertyValue<OptionsPageViewModel>(); }
            private set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the dialog result.
        /// </summary>
        public bool? DialogResult
        {
            get { return GetPropertyValue<bool?>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets the temporary settings object containing one-off cleanup overrides.
        /// </summary>
        internal Settings TemporarySettings => _temporarySettings;

        /// <summary>
        /// Persists temporary UI values into the temporary settings object.
        /// </summary>
        internal void SaveTemporarySettings()
        {
            TemporaryCleaningSettingsViewModel.SaveSettings();
        }

        private static Settings CreateSettingsCopy(Settings source)
        {
            var copy = new Settings();
            foreach (SettingsProperty property in source.Properties)
            {
                copy[property.Name] = source[property.Name];
            }

            return copy;
        }

        private DelegateCommand _startCleanupCommand;

        /// <summary>
        /// Gets the command that confirms and starts cleanup.
        /// </summary>
        public DelegateCommand StartCleanupCommand => _startCleanupCommand ?? (_startCleanupCommand = new DelegateCommand(_ => DialogResult = true));

        private DelegateCommand _cancelCommand;

        /// <summary>
        /// Gets the command that cancels cleanup.
        /// </summary>
        public DelegateCommand CancelCommand => _cancelCommand ?? (_cancelCommand = new DelegateCommand(_ => DialogResult = false));
    }
}
