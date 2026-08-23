using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A class for encapsulating the logic of moving using directives outside namespace declarations during cleanup.
/// </summary>
internal sealed class MoveUsingsOutsideNamespaceLogic
{
    private readonly CodeJanitorPackage _package;
    private readonly MoveUsingsOutsideNamespaceConverter _converter;

    private static MoveUsingsOutsideNamespaceLogic _instance;

    /// <summary>
    /// Returns the singleton MoveUsingsOutsideNamespaceLogic instance, lazily creating it with the supplied package on first access.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>A MoveUsingsOutsideNamespaceLogic value produced by this method.</returns>
    internal static MoveUsingsOutsideNamespaceLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new MoveUsingsOutsideNamespaceLogic(package));
    }

    private MoveUsingsOutsideNamespaceLogic(CodeJanitorPackage package)
    {
        _package = package;
        _converter = new MoveUsingsOutsideNamespaceConverter();
    }

    /// <summary>
    /// This method requires the UI thread then, if the relevant setting is enabled, converts the full document text to move usings outside namespaces and replaces the original content (preserving markers) only when a change occurs.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    internal void MoveUsingsOutsideNamespace(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!Settings.Default.Cleaning_MoveUsingsOutsideNamespace)
        {
            OutputWindowHelper.InfoWriteLine(
                $"MoveUsingsOutsideNamespaceLogic.MoveUsingsOutsideNamespace skipped for '{textDocument.Parent?.FullName}' because Cleaning_MoveUsingsOutsideNamespace is false.");

            return;
        }

        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var originalText = startPoint.GetText(textDocument.EndPoint);

        var convertedText = _converter.MoveUsingsOutside(originalText);
        if (convertedText == originalText)
        {
            OutputWindowHelper.InfoWriteLine(
                $"MoveUsingsOutsideNamespaceLogic.MoveUsingsOutsideNamespace made no change for '{textDocument.Parent?.FullName}' (no usings found inside namespace, or already outside).");

            return;
        }

        var endPoint = textDocument.EndPoint.CreateEditPoint();
        startPoint.ReplaceText(endPoint, convertedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);

        OutputWindowHelper.InfoWriteLine(
            $"MoveUsingsOutsideNamespaceLogic.MoveUsingsOutsideNamespace moved using directives outside namespace for '{textDocument.Parent?.FullName}'.");
    }
}
