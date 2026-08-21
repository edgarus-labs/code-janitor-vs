using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A class for simplifying single-statement lambda block bodies to expression bodies during cleanup.
/// </summary>

internal sealed class SingleStatementLambdaLogic
{
    private readonly ISourceTransformation _converter;

    /// <summary>
    /// The singleton instance of the <see cref="SingleStatementLambdaLogic" /> class.
    /// </summary>
    private static SingleStatementLambdaLogic _instance;

    /// <summary>
    /// Gets an instance of the <see cref="SingleStatementLambdaLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>An instance of the <see cref="SingleStatementLambdaLogic" /> class.</returns>

    internal static SingleStatementLambdaLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new SingleStatementLambdaLogic(package));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleStatementLambdaLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    private SingleStatementLambdaLogic(CodeJanitorPackage package)
    {
        _converter = new SingleStatementLambdaConverter();
    }

    /// <summary>
    /// Simplifies single-statement lambda block bodies to expression bodies, when enabled in settings.
    /// </summary>
    /// <param name="textDocument">The text document to update.</param>

    internal void SimplifySingleStatementLambdas(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!Settings.Default.Cleaning_SimplifySingleStatementLambdas)
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
