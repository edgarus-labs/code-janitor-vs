using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A class for encapsulating the logic of converting <c>List&lt;T&gt;</c> and array
/// initializations to the C# 12 collection expression syntax, during cleanup.
/// </summary>
/// <remarks>
/// This is a thin integration layer over the pure, unit-tested
/// <see cref="CollectionExpressionConverter" /> (see ADR-0005 / ADR-0006).
/// </remarks>

internal sealed class CollectionExpressionLogic
{
    private readonly CodeJanitorPackage _package;
    private readonly ISourceTransformation _converter;

    /// <summary>
    /// The singleton instance of the <see cref="CollectionExpressionLogic" /> class.
    /// </summary>
    private static CollectionExpressionLogic _instance;

    /// <summary>
    /// Gets an instance of the <see cref="CollectionExpressionLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>An instance of the <see cref="CollectionExpressionLogic" /> class.</returns>

    internal static CollectionExpressionLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new CollectionExpressionLogic(package));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionExpressionLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    private CollectionExpressionLogic(CodeJanitorPackage package)
    {
        _package = package;
        _converter = new CollectionExpressionConverter();
    }

    /// <summary>
    /// Converts <c>List&lt;T&gt;</c> and array initializations in the specified document to
    /// collection expression syntax, when enabled in the effective settings.
    /// </summary>
    /// <param name="textDocument">The text document to update.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>

    internal void ConvertToCollectionExpressions(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_ConvertToCollectionExpressions)))
        {
            return;
        }

        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var originalText = startPoint.GetText(textDocument.EndPoint);

        var convertedText = ConvertToCollectionExpressions(originalText, textDocument.Parent.FullName);
        if (convertedText == originalText)
        {
            return;
        }

        var endPoint = textDocument.EndPoint.CreateEditPoint();
        startPoint.ReplaceText(endPoint, convertedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
    }

    /// <summary>
    /// Converts <c>List&lt;T&gt;</c> and array initializations in C# source text to collection expression syntax when
    /// every project compiling the file uses C# 12 or newer (<see cref="CSharpLanguageVersionSupport" />); otherwise
    /// the text is returned unchanged and a conversion it would have made is reported.
    /// </summary>
    /// <param name="text">The C# source text.</param>
    /// <param name="filePath">The file path, used for the language version check and in log messages.</param>
    /// <returns>The converted text, or the original text when the conversion does not apply.</returns>

    internal string ConvertToCollectionExpressions(string text, string filePath)
    {
        var convertedText = _converter.Apply(text);
        if (convertedText == text)
        {
            return text;
        }

        if (!CSharpLanguageVersionSupport.For(filePath).Supports(CSharpLanguageVersionSupport.CollectionExpressions, out var skipMessage))
        {
            OutputWindowHelper.WarningWriteLine(skipMessage);

            return text;
        }

        return convertedText;
    }
}
