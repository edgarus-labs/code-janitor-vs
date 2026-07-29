using Microsoft.VisualStudio.Shell;
using SteveCadwallader.CodeJanitor.UI.Dialogs.Options;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace SteveCadwallader.CodeJanitor.Integration.Options
{
    /// <summary>
    /// Visual Studio Tools > Options page host for CodeJanitor settings.
    /// </summary>
    public class CodeJanitorOptionsDialogPage : UIElementDialogPage
    {
        private OptionsPageControl _control;

        /// <summary>
        /// Gets the hosted UI element shown in the VS Options dialog.
        /// </summary>
        protected override UIElement Child
        {
            get
            {
                if (_control == null)
                {
                    var package = CodeJanitorPackage.Instance;
                    if (package == null)
                    {
                        return new TextBlock
                        {
                            Text = "Code Janitor options are initializing. Please close and reopen this page.",
                            Margin = new Thickness(8)
                        };
                    }

                    _control = new OptionsPageControl();
                    _control.Initialize(package, OptionsPageNavigation.ConsumePendingPage());
                }

                return _control;
            }
        }

        /// <inheritdoc />
        protected override void OnActivate(CancelEventArgs e)
        {
            base.OnActivate(e);

            var pageType = OptionsPageNavigation.ConsumePendingPage();
            if (_control == null)
            {
                _ = Child;
            }

            _control?.Reload(pageType);
        }

        /// <inheritdoc />
        public override void SaveSettingsToStorage()
        {
            _control?.ApplyChanges();
            base.SaveSettingsToStorage();
        }
    }
}
