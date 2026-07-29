using System;

namespace SteveCadwallader.CodeJanitor.Integration.Options
{
    /// <summary>
    /// Holds the next options page type to select when opening the Visual Studio Tools > Options
    /// page. Used by commands that deep-link into a specific section (for example Spade options).
    /// </summary>
    internal static class OptionsPageNavigation
    {
        private static Type _pendingPageType;

        /// <summary>
        /// Sets the next page type to select when the options UI is activated.
        /// </summary>
        public static void SetPendingPage(Type optionsPageViewModelType)
        {
            _pendingPageType = optionsPageViewModelType;
        }

        /// <summary>
        /// Gets and clears the pending page type.
        /// </summary>
        public static Type ConsumePendingPage()
        {
            var pageType = _pendingPageType;
            _pendingPageType = null;
            return pageType;
        }
    }
}
