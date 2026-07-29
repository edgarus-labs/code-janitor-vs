using System;
using System.Windows.Controls;

namespace SteveCadwallader.CodeJanitor.UI.Dialogs.Options
{
    /// <summary>
    /// Hosts CodeJanitor settings pages inside Visual Studio Tools > Options using native VS dialog
    /// chrome and theming.
    /// </summary>
    public partial class OptionsPageControl : UserControl
    {
        private CodeJanitorPackage _package;

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionsPageControl"/> class.
        /// </summary>
        public OptionsPageControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initializes the view model for the specified package and initial page.
        /// </summary>
        public void Initialize(CodeJanitorPackage package, Type initiallySelectedPageType = null)
        {
            _package = package;
            DataContext = new OptionsViewModel(_package, initiallySelectedPageType);
        }

        /// <summary>
        /// Reloads options from settings and restores selection.
        /// </summary>
        public void Reload(Type initiallySelectedPageType = null)
        {
            if (_package == null)
            {
                return;
            }

            var selectedPageType = initiallySelectedPageType;
            if (selectedPageType == null && DataContext is OptionsViewModel existing && existing.SelectedPage != null)
            {
                selectedPageType = existing.SelectedPage.GetType();
            }

            DataContext = new OptionsViewModel(_package, selectedPageType);
        }

        /// <summary>
        /// Applies settings changes from the current view model to persisted settings.
        /// </summary>
        public void ApplyChanges()
        {
            if (DataContext is OptionsViewModel viewModel && viewModel.SaveCommand.CanExecute(null))
            {
                viewModel.SaveCommand.Execute(null);
            }
        }
    }
}
