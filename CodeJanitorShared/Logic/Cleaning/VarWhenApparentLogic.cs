using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A class for encapsulating the logic of converting local variable declarations to
/// <c>var</c> when the type is apparent from the right-hand side, during cleanup.
/// </summary>
/// <remarks>
/// This is a thin integration layer over the pure, unit-tested
/// <see cref="ITypeStyleConverter" /> (see ADR-0005 / ADR-0006 / ADR-0007).
/// </remarks>

internal sealed class VarWhenApparentLogic
{
    private readonly CodeJanitorPackage _package;
    private readonly ITypeStyleConverter _converter;

    /// <summary>
    /// The singleton instance of the <see cref="VarWhenApparentLogic" /> class.
    /// </summary>
    private static VarWhenApparentLogic _instance;

    /// <summary>
    /// Gets an instance of the <see cref="VarWhenApparentLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>An instance of the <see cref="VarWhenApparentLogic" /> class.</returns>

    internal static VarWhenApparentLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new VarWhenApparentLogic(package));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VarWhenApparentLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    private VarWhenApparentLogic(CodeJanitorPackage package)
    {
        _package = package;
        _converter = new VarWhenApparentConverter();
    }

    /// <summary>
    /// Converts local variable declarations in the specified document to <c>var</c> when
    /// the type is apparent from the right-hand side, when enabled in settings.
    /// </summary>
    /// <param name="textDocument">The text document to update.</param>

    internal void ConvertToVarWhenApparent(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!Settings.Default.Cleaning_ConvertToVarWhenApparent)
        {
            return;
        }

        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var originalText = startPoint.GetText(textDocument.EndPoint);

        var convertedText = _converter.UseVarWhenApparent(originalText);
        if (convertedText == originalText)
        {
            return;
        }

        var endPoint = textDocument.EndPoint.CreateEditPoint();
        startPoint.ReplaceText(endPoint, convertedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
    }
}
