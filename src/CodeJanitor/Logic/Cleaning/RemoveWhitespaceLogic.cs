using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Transformations;
using CodeJanitor.Properties;
using System;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A class for encapsulating the logic of removing whitespace.
/// </summary>

internal sealed class RemoveWhitespaceLogic
{
    private readonly CodeJanitorPackage _package;
    private readonly RemoveFinalNewlineConverter _removeFinalNewlineConverter;

    /// <summary>
    /// The singleton instance of the <see cref="RemoveWhitespaceLogic" /> class.
    /// </summary>
    private static RemoveWhitespaceLogic _instance;

    /// <summary>
    /// Gets an instance of the <see cref="RemoveWhitespaceLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>An instance of the <see cref="RemoveWhitespaceLogic" /> class.</returns>

    internal static RemoveWhitespaceLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new RemoveWhitespaceLogic(package));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoveWhitespaceLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    private RemoveWhitespaceLogic(CodeJanitorPackage package)
    {
        _package = package;
        _removeFinalNewlineConverter = new RemoveFinalNewlineConverter();
    }

    /// <summary>
    /// Removes blank lines from the bottom of the specified text document.
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>

    internal void RemoveBlankLinesAtBottom(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_RemoveBlankLinesAtBottom))) return;

        EditPoint cursor = textDocument.EndPoint.CreateEditPoint();
        cursor.DeleteWhitespace(vsWhitespaceOptions.vsWhitespaceOptionsVertical);
    }

    /// <summary>
    /// Removes blank lines from the top of the specified text document.
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>

    internal void RemoveBlankLinesAtTop(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_RemoveBlankLinesAtTop))) return;

        EditPoint cursor = textDocument.StartPoint.CreateEditPoint();
        cursor.DeleteWhitespace(vsWhitespaceOptions.vsWhitespaceOptionsVertical);
    }

    /// <summary>
    /// Removes blank lines after attributes.
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>

    internal void RemoveBlankLinesAfterAttributes(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_RemoveBlankLinesAfterAttributes))) return;

        const string pattern = @"(^[ \t]*\[[^\]]+\][ \t]*(//[^\r\n]*)*)(\r?\n){2}(?![ \t]*//)";
        string replacement = @"$1" + Environment.NewLine;

        TextDocumentHelper.SubstituteAllStringMatches(textDocument, pattern, replacement);
    }

    /// <summary>
    /// Removes blank lines after an opening brace.
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>

    internal void RemoveBlankLinesAfterOpeningBrace(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_RemoveBlankLinesAfterOpeningBrace))) return;

        const string pattern = @"\{([ \t]*(//[^\r\n]*)*)(\r?\n){2,}";
        string replacement = @"{$1" + Environment.NewLine;

        TextDocumentHelper.SubstituteAllStringMatches(textDocument, pattern, replacement);
    }

    /// <summary>
    /// Removes blank lines before a closing brace.
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>

    internal void RemoveBlankLinesBeforeClosingBrace(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_RemoveBlankLinesBeforeClosingBrace))) return;

        const string pattern = @"(\r?\n){2,}([ \t]*)\}";
        string replacement = Environment.NewLine + @"$2}";

        TextDocumentHelper.SubstituteAllStringMatches(textDocument, pattern, replacement);
    }

    /// <summary>
    /// Removes blank lines before a closing tag.
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>

    internal void RemoveBlankLinesBeforeClosingTag(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_RemoveBlankLinesBeforeClosingTags))) return;

        const string pattern = @"(\r?\n){2,}([ \t]*)</";
        string replacement = Environment.NewLine + @"$2</";

        TextDocumentHelper.SubstituteAllStringMatches(textDocument, pattern, replacement);
    }

    /// <summary>
    /// Removes blank lines between chained statements.
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>

    internal void RemoveBlankLinesBetweenChainedStatements(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_RemoveBlankLinesBetweenChainedStatements))) return;

        const string pattern = @"(\r?\n){2,}([ \t]*)(else|catch|finally)( |\t|\r?\n)";
        string replacement = Environment.NewLine + @"$2$3$4";

        TextDocumentHelper.SubstituteAllStringMatches(textDocument, pattern, replacement);
    }

    /// <summary>
    /// Removes blank spaces before a closing angle bracket.
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>

    internal void RemoveBlankSpacesBeforeClosingAngleBracket(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_RemoveBlankSpacesBeforeClosingAngleBrackets))) return;

        // Remove blank spaces before regular closing angle brackets.
        const string pattern = @"(\r?\n)*[ \t]+>\r?\n";
        string replacement = @">" + Environment.NewLine;

        TextDocumentHelper.SubstituteAllStringMatches(textDocument, pattern, replacement);

        // Handle blank spaces before self closing angle brackets based on insert blank space setting.
        if (settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankSpaceBeforeSelfClosingAngleBrackets)))
        {
            const string oneSpacePattern = @"(\r?\n)*[ \t]{2,}/>\r?\n";
            string oneSpaceReplacement = @" />" + Environment.NewLine;

            TextDocumentHelper.SubstituteAllStringMatches(textDocument, oneSpacePattern, oneSpaceReplacement);
        }
        else
        {
            const string noSpacePattern = @"(\r?\n)*[ \t]+/>\r?\n";
            string noSpaceReplacement = @"/>" + Environment.NewLine;

            TextDocumentHelper.SubstituteAllStringMatches(textDocument, noSpacePattern, noSpaceReplacement);
        }
    }

    /// <summary>
    /// Removes all end of line whitespace from the specified text document.
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>

    internal void RemoveEOLWhitespace(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_RemoveEndOfLineWhitespace))) return;

        const string pattern = @"[ \t]+\r?\n";
        string replacement = Environment.NewLine;

        TextDocumentHelper.SubstituteAllStringMatches(textDocument, pattern, replacement);
    }

    /// <summary>
    /// Removes multiple consecutive blank lines from the specified text document.
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>

    internal void RemoveMultipleConsecutiveBlankLines(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_RemoveMultipleConsecutiveBlankLines))) return;

        const string pattern = @"(\r?\n){3,}";
        string replacement = Environment.NewLine + Environment.NewLine;

        TextDocumentHelper.SubstituteAllStringMatches(textDocument, pattern, replacement);
    }

    /// <summary>
    /// Removes the line breaks, and the blank lines between them, at the end of the specified text document when the
    /// effective settings require the file to end without a final newline (.editorconfig
    /// <c>insert_final_newline = false</c>); the counterpart of <see cref="InsertWhitespaceLogic.InsertEOFTrailingNewLine" />.
    /// </summary>
    /// <param name="textDocument">The text document to cleanup.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>

    internal void RemoveEOFTrailingNewLine(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (settings.InsertFinalNewline) return;

        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var originalText = startPoint.GetText(textDocument.EndPoint);

        var convertedText = _removeFinalNewlineConverter.Apply(originalText);
        if (convertedText == originalText)
        {
            return;
        }

        var endPoint = textDocument.EndPoint.CreateEditPoint();
        startPoint.ReplaceText(endPoint, convertedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
    }
}
