using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Model.CodeItems;
using CodeJanitor.Properties;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A class for encapsulating the logic of general updates.
/// </summary>

internal sealed class UpdateLogic
{
    private readonly CodeJanitorPackage _package;

    /// <summary>
    /// The singleton instance of the <see cref="UpdateLogic" /> class.
    /// </summary>
    private static UpdateLogic _instance;

    /// <summary>
    /// Gets an instance of the <see cref="UpdateLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>An instance of the <see cref="UpdateLogic" /> class.</returns>

    internal static UpdateLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new UpdateLogic(package));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    private UpdateLogic(CodeJanitorPackage package)
    {
        _package = package;
    }

    /// <summary>
    /// Updates the #endregion directives to match the names of the matching #region directive
    /// and cleans up any unnecessary white space.
    /// </summary>
    /// <remarks>
    /// This code is very similar to the Common region retrieval function, but since it
    /// manipulates the cursors during processing the logic is different enough to warrant a
    /// separate copy of the code.
    /// </remarks>
    /// <param name="textDocument">The text document to cleanup.</param>

    internal void UpdateEndRegionDirectives(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!Settings.Default.Cleaning_UpdateEndRegionDirectives) return;

        var regionStack = new Stack<string>();
        EditPoint cursor = textDocument.StartPoint.CreateEditPoint();
        const string pattern = @"^[ \t]*#";

        // Keep pushing cursor forwards (note ref cursor parameter) until finished.
        while (cursor != null &&
               TextDocumentHelper.TryFindNextMatch(startPoint: cursor, endPoint: ref cursor, pattern))
        {
            // Create a pointer to capture the text for this line.
            EditPoint eolCursor = cursor.CreateEditPoint();
            eolCursor.EndOfLine();
            string regionText = cursor.GetText(eolCursor);

            if (TryParseRegionDirective(regionText, out string regionNameTrimmed))
            {
                // Normalize whitespace after #region to a single space and trim the region name.
                string expectedRegionSuffix = BuildDirectiveNameSuffix(regionNameTrimmed);
                string actualRegionSuffix = regionText.Substring(6);
                if (actualRegionSuffix != expectedRegionSuffix)
                {
                    cursor.CharRight(6);
                    cursor.Delete(eolCursor);
                    cursor.Insert(expectedRegionSuffix);
                }

                // Push the parsed region name onto the top of the stack.
                regionStack.Push(regionNameTrimmed);
            }
            else if (TryParseEndRegionDirective(regionText, out string endRegionName))
            {
                if (regionStack.Count > 0)
                {
                    string matchingRegion = regionStack.Pop();
                    string expectedEndRegionSuffix = BuildDirectiveNameSuffix(matchingRegion);

                    // Update if the strings do not match.
                    if (expectedEndRegionSuffix != endRegionName)
                    {
                        cursor.CharRight(9);
                        cursor.Delete(eolCursor);
                        cursor.Insert(expectedEndRegionSuffix);
                    }
                }
                else
                {
                    // This document is improperly formatted, abort.
                    return;
                }
            }

            // Note: eolCursor may be outdated now if changes have been made.
            cursor.EndOfLine();
        }
    }

    /// <summary>
    /// Attempts to parse a #region directive suffix (text following the '#').
    /// </summary>
    /// <param name="regionText">The raw directive text after '#'.</param>
    /// <param name="regionName">The parsed, trimmed region name.</param>
    /// <returns>True if the text represents a #region directive; otherwise false.</returns>

    internal static bool TryParseRegionDirective(string regionText, out string regionName)
    {
        regionName = null;

        if (string.IsNullOrEmpty(regionText) ||
            !regionText.StartsWith("region", StringComparison.Ordinal))
        {
            return false;
        }

        if (regionText.Length == 6)
        {
            regionName = string.Empty;

            return true;
        }

        if (!char.IsWhiteSpace(regionText[6]))
        {
            return false;
        }

        regionName = regionText.Substring(7).Trim();

        return true;
    }

    /// <summary>
    /// Attempts to parse a #endregion directive suffix (text following the '#').
    /// </summary>
    /// <param name="regionText">The raw directive text after '#'.</param>
    /// <param name="endRegionName">
    /// The parsed end region name as-is (including leading whitespace) for strict comparison.
    /// </param>
    /// <returns>True if the text represents a #endregion directive; otherwise false.</returns>

    internal static bool TryParseEndRegionDirective(string regionText, out string endRegionName)
    {
        endRegionName = null;

        if (string.IsNullOrEmpty(regionText) ||
            !regionText.StartsWith("endregion", StringComparison.Ordinal))
        {
            return false;
        }

        if (regionText.Length == 9)
        {
            endRegionName = string.Empty;

            return true;
        }

        if (!char.IsWhiteSpace(regionText[9]))
        {
            return false;
        }

        endRegionName = regionText.Substring(9);

        return true;
    }

    /// <summary>
    /// Builds a normalized directive suffix from a region name.
    /// </summary>
    /// <param name="regionName">The region name.</param>
    /// <returns>An empty suffix for empty names, otherwise a single leading space + name.</returns>

    internal static string BuildDirectiveNameSuffix(string regionName)
    {
        return string.IsNullOrWhiteSpace(regionName) ? string.Empty : " " + regionName.Trim();
    }

    /// <summary>
    /// Updates the event accessors to either both be single-line or multi-line.
    /// </summary>
    /// <param name="events">The events to update.</param>

    internal void UpdateEventAccessorsToBothBeSingleLineOrMultiLine(IEnumerable<CodeItemEvent> events)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!Settings.Default.Cleaning_UpdateAccessorsToBothBeSingleLineOrMultiLine) return;

        foreach (var item in events)
        {
            UpdateAccessorsToBothBeSingleLineOrMultiLine(item.CodeEvent.Adder, item.CodeEvent.Remover);
        }
    }

    /// <summary>
    /// Updates the property accessors to either both be single-line or multi-line.
    /// </summary>
    /// <param name="properties">The properties to update.</param>

    internal void UpdatePropertyAccessorsToBothBeSingleLineOrMultiLine(IEnumerable<CodeItemProperty> properties)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!Settings.Default.Cleaning_UpdateAccessorsToBothBeSingleLineOrMultiLine) return;

        foreach (var item in properties)
        {
            UpdateAccessorsToBothBeSingleLineOrMultiLine(item.CodeProperty.Getter, item.CodeProperty.Setter);
        }
    }

    /// <summary>
    /// Updates single line methods by placing braces on separate lines.
    /// </summary>
    /// <param name="methods">The methods to update.</param>

    internal void UpdateSingleLineMethods(IEnumerable<CodeItemMethod> methods)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!Settings.Default.Cleaning_UpdateSingleLineMethods) return;

        var singleLineMethods = methods.Where(x => x.StartPoint.Line == x.EndPoint.Line && x.OverrideKind != vsCMOverrideKind.vsCMOverrideKindAbstract && !(x.CodeFunction.Parent is CodeInterface));
        foreach (var singleLineMethod in singleLineMethods)
        {
            SpreadSingleLineMethodOntoMultipleLines(singleLineMethod.CodeFunction);
        }
    }

    /// <summary>
    /// Joins the specified multi-line method onto a single line.
    /// </summary>
    /// <param name="method">The method to update.</param>

    private void JoinMultiLineMethodOntoSingleLine(CodeFunction method)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var start = method.StartPoint.CreateEditPoint();
        var end = method.EndPoint.CreateEditPoint();

        const string pattern = @"[ \t]*\r?\n[ \t]*";
        const string replacement = @" ";

        // Substitute all new lines (and optional surrounding whitespace) with a single space.
        TextDocumentHelper.SubstituteAllStringMatches(start, end, pattern, replacement);
    }

    /// <summary>
    /// Spreads the specified single line method onto multiple lines.
    /// </summary>
    /// <param name="method">The method to update.</param>

    private void SpreadSingleLineMethodOntoMultipleLines(CodeFunction method)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var start = method.GetStartPoint(vsCMPart.vsCMPartBody).CreateEditPoint();
            var end = method.GetEndPoint(vsCMPart.vsCMPartBody).CreateEditPoint();

            // Insert a new-line before and after the opening brace.
            start.CharLeft();
            start.Insert(Environment.NewLine);
            start.CharRight();
            start.Insert(Environment.NewLine);

            // Insert a new-line before the closing brace, unless the method is empty.
            end.DeleteWhitespace();
            if (end.DisplayColumn > 1)
            {
                end.Insert(Environment.NewLine);
            }

            // Update the formatting of the method.
            method.StartPoint.CreateEditPoint().SmartFormat(method.EndPoint);
        }
        catch (Exception)
        {
            // Methods may not have a body (ex: partial).
        }
    }

    /// <summary>
    /// Updates the specified accessors to both be single-line or multi-line.
    /// </summary>
    /// <param name="first">The first accessor.</param>
    /// <param name="second">The second accessor.</param>

    private void UpdateAccessorsToBothBeSingleLineOrMultiLine(CodeFunction first, CodeFunction second)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (first == null || second == null) return;

        bool isFirstSingleLine = first.StartPoint.Line == first.EndPoint.Line;
        bool isSecondSingleLine = second.StartPoint.Line == second.EndPoint.Line;

        if (isFirstSingleLine == isSecondSingleLine) return;

        var multiLineMethod = isFirstSingleLine ? second : first;
        var singleLineMethod = isFirstSingleLine ? first : second;

        try
        {
            var multiLineBodyStart = multiLineMethod.GetStartPoint(vsCMPart.vsCMPartBody).CreateEditPoint();
            var multiLineBodyEnd = multiLineMethod.GetEndPoint(vsCMPart.vsCMPartBody).CreateEditPoint();

            // Move the body end back one character to account for new-lines.
            multiLineBodyEnd.CharLeft();

            bool multiLineHasSingleLineBody = multiLineBodyStart.Line == multiLineBodyEnd.Line;

            if (multiLineHasSingleLineBody)
            {
                JoinMultiLineMethodOntoSingleLine(multiLineMethod);
            }
            else
            {
                SpreadSingleLineMethodOntoMultipleLines(singleLineMethod);
            }
        }
        catch (Exception)
        {
            // Accessor without a body can be ignored.
        }
    }
}
