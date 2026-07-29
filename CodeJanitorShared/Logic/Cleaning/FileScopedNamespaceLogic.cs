using EnvDTE;
using Microsoft.VisualStudio.Shell;
using SteveCadwallader.CodeJanitor.Logic.Transformations;
using SteveCadwallader.CodeJanitor.Properties;

namespace SteveCadwallader.CodeJanitor.Logic.Cleaning
{
    /// <summary>
    /// A class for encapsulating the logic of converting a block-scoped namespace to a
    /// file-scoped namespace during cleanup.
    /// </summary>
    /// <remarks>
    /// This is a thin integration layer over the pure, unit-tested
    /// <see cref="INamespaceScopeConverter" /> (see ADR-0005 / ADR-0006).
    /// </remarks>
    internal class FileScopedNamespaceLogic
    {
        #region Fields

        private readonly CodeJanitorPackage _package;
        private readonly INamespaceScopeConverter _converter;

        #endregion Fields

        #region Constructors

        /// <summary>
        /// The singleton instance of the <see cref="FileScopedNamespaceLogic" /> class.
        /// </summary>
        private static FileScopedNamespaceLogic _instance;

        /// <summary>
        /// Gets an instance of the <see cref="FileScopedNamespaceLogic" /> class.
        /// </summary>
        /// <param name="package">The hosting package.</param>
        /// <returns>An instance of the <see cref="FileScopedNamespaceLogic" /> class.</returns>
        internal static FileScopedNamespaceLogic GetInstance(CodeJanitorPackage package)
        {
            return _instance ?? (_instance = new FileScopedNamespaceLogic(package));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileScopedNamespaceLogic" /> class.
        /// </summary>
        /// <param name="package">The hosting package.</param>
        private FileScopedNamespaceLogic(CodeJanitorPackage package)
        {
            _package = package;
            _converter = new FileScopedNamespaceConverter();
        }

        #endregion Constructors

        #region Methods

        /// <summary>
        /// Converts a single top-level block-scoped namespace in the specified document to a
        /// file-scoped namespace, when enabled in settings.
        /// </summary>
        /// <param name="textDocument">The text document to update.</param>
        internal void ConvertToFileScopedNamespace(TextDocument textDocument)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!Settings.Default.Cleaning_ConvertToFileScopedNamespace)
            {
                return;
            }

            var startPoint = textDocument.StartPoint.CreateEditPoint();
            var originalText = startPoint.GetText(textDocument.EndPoint);

            var convertedText = _converter.ConvertToFileScoped(originalText);
            if (convertedText == originalText)
            {
                return;
            }

            var endPoint = textDocument.EndPoint.CreateEditPoint();
            startPoint.ReplaceText(endPoint, convertedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
        }

        #endregion Methods
    }
}
