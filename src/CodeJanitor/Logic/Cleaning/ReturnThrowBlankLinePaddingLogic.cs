using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A class for encapsulating the logic of inserting a blank line before <c>return</c> and
/// <c>throw</c> statements during cleanup.
/// </summary>
/// <remarks>
/// This is a thin integration layer over the pure, unit-tested
/// <see cref="ReturnThrowBlankLinePaddingConverter" /> (see ADR-0005 / ADR-0006).
/// </remarks>

internal sealed class ReturnThrowBlankLinePaddingLogic
{
    private readonly CodeJanitorPackage _package;
    private readonly ISourceTransformation _converter;

    /// <summary>
    /// The singleton instance of the <see cref="ReturnThrowBlankLinePaddingLogic" /> class.
    /// </summary>
    private static ReturnThrowBlankLinePaddingLogic _instance;

    /// <summary>
    /// Gets an instance of the <see cref="ReturnThrowBlankLinePaddingLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>An instance of the <see cref="ReturnThrowBlankLinePaddingLogic" /> class.</returns>

    internal static ReturnThrowBlankLinePaddingLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new ReturnThrowBlankLinePaddingLogic(package));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ReturnThrowBlankLinePaddingLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    private ReturnThrowBlankLinePaddingLogic(CodeJanitorPackage package)
    {
        _package = package;
        _converter = new ReturnThrowBlankLinePaddingConverter();
    }

    /// <summary>
    /// Inserts a blank line before <c>return</c> and <c>throw</c> statements in the specified
    /// document, when enabled in the effective settings.
    /// </summary>
    /// <param name="textDocument">The text document to update.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>

    internal void InsertPaddingBeforeReturnAndThrowStatements(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLineBeforeReturnAndThrowStatements)))
        {
            OutputWindowHelper.InfoWriteLine(
                $"ReturnThrowBlankLinePaddingLogic.InsertPaddingBeforeReturnAndThrowStatements skipped for '{textDocument.Parent.FullName}' because Cleaning_InsertBlankLineBeforeReturnAndThrowStatements is false.");

            return;
        }

        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var originalText = startPoint.GetText(textDocument.EndPoint);

        var convertedText = _converter.Apply(originalText);
        if (convertedText == originalText)
        {
            return;
        }

        var endPoint = textDocument.EndPoint.CreateEditPoint();
        startPoint.ReplaceText(endPoint, convertedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
    }
}
