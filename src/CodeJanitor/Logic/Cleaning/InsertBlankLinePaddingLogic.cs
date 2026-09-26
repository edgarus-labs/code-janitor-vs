using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Model.CodeItems;
using CodeJanitor.Properties;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A class for encapsulating insertion of blank line padding logic.
/// </summary>

internal sealed class InsertBlankLinePaddingLogic
{
    private readonly CodeJanitorPackage _package;

    /// <summary>
    /// The singleton instance of the <see cref="InsertBlankLinePaddingLogic" /> class.
    /// </summary>
    private static InsertBlankLinePaddingLogic _instance;

    /// <summary>
    /// Gets an instance of the <see cref="InsertBlankLinePaddingLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>An instance of the <see cref="InsertBlankLinePaddingLogic" /> class.</returns>

    internal static InsertBlankLinePaddingLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new InsertBlankLinePaddingLogic(package));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InsertBlankLinePaddingLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    private InsertBlankLinePaddingLogic(CodeJanitorPackage package)
    {
        _package = package;
    }

    /// <summary>
    /// Determines if the specified code item instance should be preceded by a blank line, per the user's Visual
    /// Studio settings (the reorganizer does not resolve per-file cleanup settings).
    /// Defaults to false for unknown kinds or null objects.
    /// </summary>
    /// <param name="codeItem">The code item.</param>
    /// <returns>True if code item should be preceded by a blank line, otherwise false.</returns>

    internal bool ShouldBePrecededByBlankLine(BaseCodeItem codeItem) =>
        ShouldBePrecededByBlankLine(codeItem, EffectiveCleanupSettings.For(null));

    /// <summary>
    /// Determines if the specified code item instance should be preceded by a blank line.
    /// Defaults to false for unknown kinds or null objects.
    /// </summary>
    /// <param name="codeItem">The code item.</param>
    /// <param name="settings">The effective cleanup settings of the document containing the code item.</param>
    /// <returns>True if code item should be preceded by a blank line, otherwise false.</returns>

    internal bool ShouldBePrecededByBlankLine(BaseCodeItem codeItem, EffectiveCleanupSettings settings)
    {
        if (codeItem is null)
        {
            return false;
        }

        switch (codeItem.Kind)
        {
            case KindCodeItem.Class:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeClasses));

            case KindCodeItem.Delegate:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeDelegates));

            case KindCodeItem.Enum:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeEnumerations));

            case KindCodeItem.Event:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeEvents));

            case KindCodeItem.Field:
                return codeItem.IsMultiLine
                    ? settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeFieldsMultiLine))
                    : settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeFieldsSingleLine));

            case KindCodeItem.Interface:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeInterfaces));

            case KindCodeItem.Namespace:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeNamespaces));

            case KindCodeItem.Constructor:
            case KindCodeItem.Destructor:
            case KindCodeItem.Method:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeMethods));

            case KindCodeItem.Indexer:
            case KindCodeItem.Property:
                return codeItem.IsMultiLine
                    ? settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforePropertiesMultiLine))
                    : settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine));

            case KindCodeItem.Region:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeRegionTags));

            case KindCodeItem.Struct:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeStructs));

            case KindCodeItem.Using:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeUsingStatementBlocks));

            default:
                return false;
        }
    }

    /// <summary>
    /// Determines if the specified code item instance should be followed by a blank line, per the user's Visual
    /// Studio settings (the reorganizer does not resolve per-file cleanup settings).
    /// Defaults to false for unknown kinds or null objects.
    /// </summary>
    /// <param name="codeItem">The code item.</param>
    /// <returns>True if code item should be followed by a blank line, otherwise false.</returns>

    internal bool ShouldBeFollowedByBlankLine(BaseCodeItem codeItem) =>
        ShouldBeFollowedByBlankLine(codeItem, EffectiveCleanupSettings.For(null));

    /// <summary>
    /// Determines if the specified code item instance should be followed by a blank line.
    /// Defaults to false for unknown kinds or null objects.
    /// </summary>
    /// <param name="codeItem">The code item.</param>
    /// <param name="settings">The effective cleanup settings of the document containing the code item.</param>
    /// <returns>True if code item should be followed by a blank line, otherwise false.</returns>

    internal bool ShouldBeFollowedByBlankLine(BaseCodeItem codeItem, EffectiveCleanupSettings settings)
    {
        if (codeItem is null)
        {
            return false;
        }

        switch (codeItem.Kind)
        {
            case KindCodeItem.Class:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterClasses));

            case KindCodeItem.Delegate:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterDelegates));

            case KindCodeItem.Enum:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterEnumerations));

            case KindCodeItem.Event:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterEvents));

            case KindCodeItem.Field:
                return codeItem.IsMultiLine
                    ? settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterFieldsMultiLine))
                    : settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterFieldsSingleLine));

            case KindCodeItem.Interface:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterInterfaces));

            case KindCodeItem.Namespace:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterNamespaces));

            case KindCodeItem.Constructor:
            case KindCodeItem.Destructor:
            case KindCodeItem.Method:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterMethods));

            case KindCodeItem.Indexer:
            case KindCodeItem.Property:
                return codeItem.IsMultiLine
                    ? settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterPropertiesMultiLine))
                    : settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine));

            case KindCodeItem.Region:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterEndRegionTags));

            case KindCodeItem.Struct:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterStructs));

            case KindCodeItem.Using:
                return settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterUsingStatementBlocks));

            default:
                return false;
        }
    }

    /// <summary>
    /// Inserts a blank line before #region tags except where adjacent to a brace, per the user's Visual Studio
    /// settings (region generation does not resolve per-file cleanup settings).
    /// </summary>
    /// <param name="regions">The regions to pad.</param>

    internal void InsertPaddingBeforeRegionTags(IEnumerable<CodeItemRegion> regions) =>
        InsertPaddingBeforeRegionTags(regions, EffectiveCleanupSettings.For(null));

    /// <summary>
    /// Inserts a blank line before #region tags except where adjacent to a brace.
    /// </summary>
    /// <param name="regions">The regions to pad.</param>
    /// <param name="settings">The effective cleanup settings of the document containing the regions.</param>

    internal void InsertPaddingBeforeRegionTags(IEnumerable<CodeItemRegion> regions, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeRegionTags))) return;

        foreach (var region in regions.Where(x => !x.IsInvalidated))
        {
            var startPoint = region.StartPoint.CreateEditPoint();

            TextDocumentHelper.InsertBlankLineBeforePoint(startPoint);
        }
    }

    /// <summary>
    /// Inserts a blank line after #region tags except where adjacent to a brace, per the user's Visual Studio
    /// settings (region generation does not resolve per-file cleanup settings).
    /// </summary>
    /// <param name="regions">The regions to pad.</param>

    internal void InsertPaddingAfterRegionTags(IEnumerable<CodeItemRegion> regions) =>
        InsertPaddingAfterRegionTags(regions, EffectiveCleanupSettings.For(null));

    /// <summary>
    /// Inserts a blank line after #region tags except where adjacent to a brace.
    /// </summary>
    /// <param name="regions">The regions to pad.</param>
    /// <param name="settings">The effective cleanup settings of the document containing the regions.</param>

    internal void InsertPaddingAfterRegionTags(IEnumerable<CodeItemRegion> regions, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterRegionTags))) return;

        foreach (var region in regions.Where(x => !x.IsInvalidated))
        {
            var startPoint = region.StartPoint.CreateEditPoint();

            TextDocumentHelper.InsertBlankLineAfterPoint(startPoint);
        }
    }

    /// <summary>
    /// Inserts a blank line before #endregion tags except where adjacent to a brace, per the user's Visual Studio
    /// settings (region generation does not resolve per-file cleanup settings).
    /// </summary>
    /// <param name="regions">The regions to pad.</param>

    internal void InsertPaddingBeforeEndRegionTags(IEnumerable<CodeItemRegion> regions) =>
        InsertPaddingBeforeEndRegionTags(regions, EffectiveCleanupSettings.For(null));

    /// <summary>
    /// Inserts a blank line before #endregion tags except where adjacent to a brace.
    /// </summary>
    /// <param name="regions">The regions to pad.</param>
    /// <param name="settings">The effective cleanup settings of the document containing the regions.</param>

    internal void InsertPaddingBeforeEndRegionTags(IEnumerable<CodeItemRegion> regions, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeEndRegionTags))) return;

        foreach (var region in regions.Where(x => !x.IsInvalidated))
        {
            var endPoint = region.EndPoint.CreateEditPoint();

            TextDocumentHelper.InsertBlankLineBeforePoint(endPoint);
        }
    }

    /// <summary>
    /// Inserts a blank line after #endregion tags except where adjacent to a brace, per the user's Visual Studio
    /// settings (region generation does not resolve per-file cleanup settings).
    /// </summary>
    /// <param name="regions">The regions to pad.</param>

    internal void InsertPaddingAfterEndRegionTags(IEnumerable<CodeItemRegion> regions) =>
        InsertPaddingAfterEndRegionTags(regions, EffectiveCleanupSettings.For(null));

    /// <summary>
    /// Inserts a blank line after #endregion tags except where adjacent to a brace.
    /// </summary>
    /// <param name="regions">The regions to pad.</param>
    /// <param name="settings">The effective cleanup settings of the document containing the regions.</param>

    internal void InsertPaddingAfterEndRegionTags(IEnumerable<CodeItemRegion> regions, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterEndRegionTags))) return;

        foreach (var region in regions.Where(x => !x.IsInvalidated))
        {
            var endPoint = region.EndPoint.CreateEditPoint();

            TextDocumentHelper.InsertBlankLineAfterPoint(endPoint);
        }
    }

    /// <summary>
    /// Inserts a blank line before the specified code elements except where adjacent to a brace.
    /// </summary>
    /// <typeparam name="T">The type of the code element.</typeparam>
    /// <param name="codeElements">The code elements to pad.</param>
    /// <param name="settings">The effective cleanup settings of the document containing the code elements.</param>

    internal void InsertPaddingBeforeCodeElements<T>(IEnumerable<T> codeElements, EffectiveCleanupSettings settings)
        where T : BaseCodeItemElement
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        foreach (T codeElement in codeElements.Where(element => ShouldBePrecededByBlankLine(element, settings)))
        {
            TextDocumentHelper.InsertBlankLineBeforePoint(GetPointAboveDocumentationComment(codeElement.StartPoint));
        }
    }

    /// <summary>
    /// DTE reports a member's start below its documentation comment, so padding has to be placed
    /// above the comment instead of between the comment and the member it documents.
    /// </summary>

    private static EnvDTE.EditPoint GetPointAboveDocumentationComment(EnvDTE.TextPoint startPoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var point = startPoint.CreateEditPoint();

        while (point.Line > 1)
        {
            var probe = point.CreateEditPoint();
            probe.LineUp();
            probe.StartOfLine();

            var lineText = probe.GetText(probe.LineLength);
            if (lineText is null || !lineText.TrimStart().StartsWith("///", System.StringComparison.Ordinal))
            {
                break;
            }

            point = probe;
        }

        return point;
    }

    /// <summary>
    /// Inserts a blank line after the specified code elements except where adjacent to a brace.
    /// </summary>
    /// <typeparam name="T">The type of the code element.</typeparam>
    /// <param name="codeElements">The code elements to pad.</param>
    /// <param name="settings">The effective cleanup settings of the document containing the code elements.</param>

    internal void InsertPaddingAfterCodeElements<T>(IEnumerable<T> codeElements, EffectiveCleanupSettings settings)
        where T : BaseCodeItemElement
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        foreach (T codeElement in codeElements.Where(element => ShouldBeFollowedByBlankLine(element, settings)))
        {
            TextDocumentHelper.InsertBlankLineAfterPoint(codeElement.EndPoint);
        }
    }

    /// <summary>
    /// Inserts a blank line before case statements except for single-line case statements.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>

    internal void InsertPaddingBeforeCaseStatements(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeCaseStatements))) return;

        const string pattern = @"(^[ \t]*)(break;|return([ \t][^;]*)?;)\r?\n([ \t]*)(case|default)";
        string replacement = @"$1$2" + Environment.NewLine + Environment.NewLine + @"$4$5";

        TextDocumentHelper.SubstituteAllStringMatches(textDocument, pattern, replacement);
    }

    /// <summary>
    /// Inserts a blank line before single line comments except where adjacent to a brace,
    /// another single line comment line or a quadruple slash comment.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>

    internal void InsertPaddingBeforeSingleLineComments(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeSingleLineComments))) return;

        const string pattern = @"(^[ \t]*(?!//)[^ \t\r\n\{].*\r?\n)([ \t]*//)(?!//)";
        string replacement = @"$1" + Environment.NewLine + @"$2";

        TextDocumentHelper.SubstituteAllStringMatches(textDocument, pattern, replacement);
    }

    /// <summary>
    /// Inserts a blank line between multi-line property accessors.
    /// </summary>
    /// <param name="properties">The properties.</param>
    /// <param name="settings">The effective cleanup settings of the document containing the properties.</param>

    internal void InsertPaddingBetweenMultiLinePropertyAccessors(IEnumerable<CodeItemProperty> properties, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBetweenPropertiesMultiLineAccessors))) return;

        foreach (var property in properties)
        {
            var getter = property.CodeProperty.Getter;
            var setter = property.CodeProperty.Setter;

            if (getter is not null && setter is not null && (getter.StartPoint.Line < getter.EndPoint.Line ||
                                                     setter.StartPoint.Line < setter.EndPoint.Line))
            {
                TextDocumentHelper.InsertBlankLineAfterPoint(setter.EndPoint.Line > getter.EndPoint.Line
                                                                 ? getter.EndPoint.CreateEditPoint()
                                                                 : setter.EndPoint.CreateEditPoint());
            }
        }
    }
}
