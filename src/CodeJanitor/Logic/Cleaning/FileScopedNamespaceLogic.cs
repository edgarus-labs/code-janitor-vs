using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A class for encapsulating the logic of enforcing the effective namespace declaration style during cleanup:
/// converting a block-scoped namespace to a file-scoped namespace, or a file-scoped namespace to a block-scoped one.
/// </summary>
/// <remarks>
/// This is a thin integration layer over the pure, unit-tested
/// <see cref="FileScopedNamespaceConverter" /> (see ADR-0005 / ADR-0006).
/// </remarks>

internal sealed class FileScopedNamespaceLogic
{
    private readonly CodeJanitorPackage _package;

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
    }

    /// <summary>
    /// Creates the namespace converter of a file: one indentation level is a tab or <c>indent_size</c> spaces as
    /// .editorconfig <c>indent_style</c> requires, otherwise it follows the namespace body.
    /// </summary>
    /// <param name="settings">The effective cleanup settings of the file.</param>
    /// <returns>The converter.</returns>

    internal static FileScopedNamespaceConverter CreateConverter(EffectiveCleanupSettings settings)
    {
        bool? indentWithTabs;
        switch (settings.Indentation)
        {
            case IndentationPreference.Tabs:
                indentWithTabs = true;
                break;

            case IndentationPreference.Spaces:
                indentWithTabs = false;
                break;

            default:
                indentWithTabs = null;
                break;
        }

        return new FileScopedNamespaceConverter(indentWithTabs, settings.IndentSize);
    }

    /// <summary>
    /// Converts the namespace declaration of the specified document to the style enforced by the effective settings
    /// (see <see cref="EffectiveCleanupSettings.NamespaceDeclarations" />); does nothing when no style is enforced.
    /// </summary>
    /// <param name="textDocument">The text document to update.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>

    internal void ApplyNamespaceDeclarationStyle(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var filePath = textDocument.Parent.FullName;
        var preference = settings.NamespaceDeclarations;
        if (preference == NamespaceDeclarationPreference.Unchanged)
        {
            OutputWindowHelper.InfoWriteLine(
                $"FileScopedNamespaceLogic.ApplyNamespaceDeclarationStyle skipped for '{filePath}' because no namespace declaration style is enforced.");

            return;
        }

        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var originalText = startPoint.GetText(textDocument.EndPoint);

        var convertedText = ConvertNamespaceDeclarations(originalText, filePath, settings);
        if (convertedText == originalText)
        {
            return;
        }

        var endPoint = textDocument.EndPoint.CreateEditPoint();
        startPoint.ReplaceText(endPoint, convertedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
    }

    /// <summary>
    /// Converts the namespace declaration of C# source text to the style enforced by the effective settings. A
    /// file-scoped namespace is only produced when every project compiling the file uses C# 10 or newer
    /// (<see cref="CSharpLanguageVersionSupport" />); a block-scoped namespace is valid in every language version. When
    /// the conversion does not apply, the text is returned unchanged and the reason is logged.
    /// </summary>
    /// <param name="text">The C# source text.</param>
    /// <param name="filePath">The file path, used for the language version check and in log messages.</param>
    /// <param name="settings">The effective cleanup settings of the file.</param>
    /// <returns>The converted text, or the original text when the conversion does not apply.</returns>

    internal string ConvertNamespaceDeclarations(string text, string filePath, EffectiveCleanupSettings settings)
    {
        var converter = CreateConverter(settings);
        switch (settings.NamespaceDeclarations)
        {
            case NamespaceDeclarationPreference.FileScoped:
                return ConvertToFileScopedNamespace(converter, text, filePath);

            case NamespaceDeclarationPreference.BlockScoped:
                var convertedText = converter.ConvertToBlockScoped(text);
                if (convertedText == text)
                {
                    OutputWindowHelper.InfoWriteLine(
                        $"FileScopedNamespaceLogic.ConvertNamespaceDeclarations made no change for '{filePath}' (not exactly one top-level file-scoped namespace, or already block-scoped).");
                }

                return convertedText;

            default:
                return text;
        }
    }

    /// <summary>
    /// Converts a single top-level block-scoped namespace to a file-scoped namespace when the file's projects support it.
    /// </summary>
    /// <param name="converter">The namespace converter of the file.</param>
    /// <param name="text">The C# source text.</param>
    /// <param name="filePath">The file path.</param>
    /// <returns>The converted text, or the original text when the conversion does not apply.</returns>

    private static string ConvertToFileScopedNamespace(INamespaceScopeConverter converter, string text, string filePath)
    {
        if (converter.HasMultipleNamespaces(text))
        {
            OutputWindowHelper.WarningWriteLine(
                string.Format(Resources.CodeJanitorSkippedFileScopedNamespaceConversion0, filePath));

            return text;
        }

        var convertedText = converter.ConvertToFileScoped(text);
        if (convertedText == text)
        {
            OutputWindowHelper.InfoWriteLine(
                $"FileScopedNamespaceLogic.ConvertNamespaceDeclarations made no change for '{filePath}' (not exactly one top-level block-scoped namespace, already file-scoped, or content around the namespace braces that a file-scoped namespace cannot keep).");

            return text;
        }

        if (!CSharpLanguageVersionSupport.For(filePath).Supports(CSharpLanguageVersionSupport.FileScopedNamespaces, out var skipMessage))
        {
            OutputWindowHelper.WarningWriteLine(skipMessage);

            return text;
        }

        return convertedText;
    }
}
