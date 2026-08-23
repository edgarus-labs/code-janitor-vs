using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A class for encapsulating conservative CA1869-related cleanup for
/// <c>JsonSerializerOptions</c> allocations.
/// </summary>

internal sealed class JsonSerializerOptionsReuseLogic
{
    private readonly ISourceTransformation _converter;

    /// <summary>
    /// The singleton instance of the <see cref="JsonSerializerOptionsReuseLogic" /> class.
    /// </summary>
    private static JsonSerializerOptionsReuseLogic _instance;

    /// <summary>
    /// Gets an instance of the <see cref="JsonSerializerOptionsReuseLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>An instance of the <see cref="JsonSerializerOptionsReuseLogic" /> class.</returns>

    internal static JsonSerializerOptionsReuseLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new JsonSerializerOptionsReuseLogic(package));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonSerializerOptionsReuseLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    private JsonSerializerOptionsReuseLogic(CodeJanitorPackage package)
    {
        _converter = new JsonSerializerOptionsReuseConverter();
    }

    /// <summary>
    /// Replaces direct <c>new JsonSerializerOptions()</c> arguments in
    /// <c>JsonSerializer.*</c> calls with <c>null</c>, when enabled in settings.
    /// </summary>
    /// <param name="textDocument">The text document to update.</param>

    internal void ReuseJsonSerializerOptionsForCA1869(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!Settings.Default.Cleaning_ReuseJsonSerializerOptionsForCA1869)
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
