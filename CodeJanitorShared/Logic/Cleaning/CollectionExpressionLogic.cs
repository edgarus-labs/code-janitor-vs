using EnvDTE;
using Microsoft.VisualStudio.Shell;
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
    /// collection expression syntax, when enabled in settings.
    /// </summary>
    /// <param name="textDocument">The text document to update.</param>

    internal void ConvertToCollectionExpressions(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!Settings.Default.Cleaning_ConvertToCollectionExpressions)
        {
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
