using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A class for encapsulating the logic of adding the <c>readonly</c> modifier to fields that
/// are provably never written outside of their declaring constructor, during cleanup.
/// </summary>
/// <remarks>
/// This is a thin integration layer over the pure, unit-tested
/// <see cref="IFieldMutabilityConverter" /> (see ADR-0005 / ADR-0006 / ADR-0007).
/// </remarks>

internal sealed class ReadonlyFieldLogic
{
    private readonly CodeJanitorPackage _package;
    private readonly IFieldMutabilityConverter _converter;

    /// <summary>
    /// The singleton instance of the <see cref="ReadonlyFieldLogic" /> class.
    /// </summary>
    private static ReadonlyFieldLogic _instance;

    /// <summary>
    /// Gets an instance of the <see cref="ReadonlyFieldLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>An instance of the <see cref="ReadonlyFieldLogic" /> class.</returns>

    internal static ReadonlyFieldLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new ReadonlyFieldLogic(package));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ReadonlyFieldLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    private ReadonlyFieldLogic(CodeJanitorPackage package)
    {
        _package = package;
        _converter = new ReadonlyFieldConverter();
    }

    /// <summary>
    /// Adds the <c>readonly</c> modifier to fields in the specified document that are
    /// provably safe to convert, when enabled in the effective settings.
    /// </summary>
    /// <param name="textDocument">The text document to update.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>

    internal void AddReadonlyWhenSafe(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_MakeFieldsReadonlyWhenSafe)))
        {
            return;
        }

        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var originalText = startPoint.GetText(textDocument.EndPoint);

        var convertedText = _converter.AddReadonlyWhenSafe(originalText);
        if (convertedText == originalText)
        {
            return;
        }

        var endPoint = textDocument.EndPoint.CreateEditPoint();
        startPoint.ReplaceText(endPoint, convertedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
    }
}
