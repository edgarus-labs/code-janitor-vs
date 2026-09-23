using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A class for encapsulating the logic of adding the <c>sealed</c> modifier to classes that
/// are provably safe to seal, during cleanup.
/// </summary>
/// <remarks>
/// This is a thin integration layer over the pure, unit-tested
/// <see cref="IClassSealingConverter" /> (see ADR-0005 / ADR-0006 / ADR-0007). When this
/// cleanup runs as part of an orchestrated multi-file batch (see
/// <see cref="CleanupProgressViewModel" />), the converter is also given the current batch's
/// solution-wide disqualified type names (base types, generic constraints), so cross-file
/// safety is enforced for those batches. Outside a batch (e.g. cleanup-on-save of a single
/// document), only in-file safety checks apply, to avoid a full-solution rescan on every save.
/// </remarks>

internal sealed class SealedClassLogic
{
    private readonly CodeJanitorPackage _package;
    private readonly IClassSealingConverter _converter;

    /// <summary>
    /// The singleton instance of the <see cref="SealedClassLogic" /> class.
    /// </summary>
    private static SealedClassLogic _instance;

    /// <summary>
    /// Gets an instance of the <see cref="SealedClassLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>An instance of the <see cref="SealedClassLogic" /> class.</returns>

    internal static SealedClassLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new SealedClassLogic(package));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SealedClassLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    private SealedClassLogic(CodeJanitorPackage package)
    {
        _package = package;
        _converter = new SealedClassConverter();
    }

    /// <summary>
    /// Adds the <c>sealed</c> modifier to classes in the specified document that are provably
    /// safe to convert, when enabled in settings.
    /// </summary>
    /// <param name="textDocument">The text document to update.</param>

    internal void SealWhenSafe(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!Settings.Default.Cleaning_SealClassesWhenSafe)
        {
            return;
        }

        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var originalText = startPoint.GetText(textDocument.EndPoint);

        // Use the current batch's solution-wide disqualified types when this cleanup is
        // running as part of an orchestrated multi-file batch (see CleanupProgressViewModel).
        // Outside a batch (e.g. cleanup-on-save of a single document), no solution-wide scan
        // is performed here to avoid a full-solution rescan on every save; only the in-file
        // safety checks (virtual members, same-file constraints) apply in that case.
        var externalDisqualifiedTypeNames = CodeCleanupManager.GetInstance(_package).GetCurrentBatchDisqualifiedTypes();
        var convertedText = _converter.SealWhenSafe(originalText, externalDisqualifiedTypeNames);
        if (convertedText == originalText)
        {
            return;
        }

        var endPoint = textDocument.EndPoint.CreateEditPoint();
        startPoint.ReplaceText(endPoint, convertedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
    }
}
