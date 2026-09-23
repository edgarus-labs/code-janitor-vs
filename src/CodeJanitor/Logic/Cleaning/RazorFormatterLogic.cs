using EnvDTE;
using TextDocument = EnvDTE.TextDocument;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// Applies safe formatting to Razor component files. Scope is limited to .razor files and to
/// C# inside code and control blocks - markup tags and attributes are left exactly as authored.
/// </summary>

internal sealed class RazorFormatterLogic
{
    /// <summary>
    /// The indent unit.
    /// </summary>
    private const string IndentUnit = "    ";

    private readonly CodeJanitorPackage _package;

    private static RazorFormatterLogic _instance;

    /// <summary>
    /// Returns the cached `RazorFormatterLogic` singleton, lazily creating and assigning a new instance using the provided package on first call.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>A RazorFormatterLogic value produced by this method.</returns>
    internal static RazorFormatterLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new RazorFormatterLogic(package));
    }

    private RazorFormatterLogic(CodeJanitorPackage package)
    {
        _package = package;
    }

    /// <summary>
    /// Formats the text of a Razor (.razor) TextDocument on the UI thread when Razor component formatting is enabled, replacing the original content with the formatted output only if it differs and preserving any existing markers.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    internal void FormatRazorDocument(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (textDocument?.Parent?.FullName is null) return;
        if (!string.Equals(Path.GetExtension(textDocument.Parent.FullName), ".razor", StringComparison.OrdinalIgnoreCase)) return;
        if (!Settings.Default.Cleaning_FormatRazorComponents) return;

        var start = textDocument.StartPoint.CreateEditPoint();
        var end = textDocument.EndPoint.CreateEditPoint();
        var original = start.GetText(end);
        if (string.IsNullOrWhiteSpace(original)) return;

        var formatted = FormatRazorText(original);
        if (string.Equals(original, formatted, StringComparison.Ordinal)) return;

        start.ReplaceText(end, formatted, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
    }

    /// <summary>
    /// Formats Razor text by detecting and preserving the input&apos;s line ending while normalizing code blocks and control blocks, returning the input unchanged when null or empty.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <returns>A string value produced by this method.</returns>
    internal static string FormatRazorText(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var lineEnding = DetectLineEnding(input);
        var withFormattedCode = FormatCodeBlocks(input, lineEnding);

        return FormatControlBlocks(withFormattedCode, lineEnding);
    }

    /// <summary>
    /// ects the line ending style of the given text by returning &quot;\r\n&quot; if the text contains carriage-return and newline sequences, otherwise returning &quot;\n&quot;, with no side effects or exceptions.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string DetectLineEnding(string text)
    {
        return text.Contains("\r\n") ? "\r\n" : "\n";
    }

    /// <summary>
    /// Locates directive code block ranges in the text, attempts to format each block&apos;s inner C# content, re-indents it to match the original line&apos;s indentation, and returns the text with the successfully formatted blocks replaced in place (processing in descending order to preserve offsets).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="lineEnding">The line ending.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string FormatCodeBlocks(string text, string lineEnding)
    {
        var ranges = FindDirectiveCodeBlockRanges(text);
        if (ranges.Count == 0) return text;

        var buffer = text;
        foreach (var range in ranges.OrderByDescending(x => x.BraceStart))
        {
            var contentStart = range.BraceStart + 1;
            var contentLength = range.BraceEnd - contentStart;
            if (contentLength < 0) continue;

            var content = buffer.Substring(contentStart, contentLength);
            var formatted = TryFormatCSharpBlock(content, lineEnding);
            if (formatted is null) continue;

            var baseIndent = GetLineIndent(buffer, range.Start);
            var indented = IndentBlock(formatted, baseIndent, lineEnding);
            buffer = buffer.Substring(0, contentStart) + indented + buffer.Substring(contentStart + contentLength);
        }

        return buffer;
    }

    /// <summary>
    /// control blocks within the text by locating their ranges, reformatting each in reverse order with proper indentation, and inserting line endings after blocks that aren&apos;t followed by whitespace, returning the modified string (with no exceptions thrown).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="lineEnding">The line ending.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string FormatControlBlocks(string text, string lineEnding)
    {
        var ranges = FindControlBlockRanges(text);
        if (ranges.Count == 0) return text;

        var buffer = text;
        foreach (var range in ranges.OrderByDescending(x => x.Start))
        {
            var rawBlock = buffer.Substring(range.Start, range.End - range.Start + 1);
            var formatted = TryFormatControlBlock(rawBlock, GetLineIndent(buffer, range.Start), lineEnding);
            if (string.IsNullOrEmpty(formatted) || string.Equals(rawBlock, formatted, StringComparison.Ordinal))
            {
                continue;
            }

            var separator = string.Empty;
            if (range.End + 1 < buffer.Length)
            {
                var next = buffer[range.End + 1];
                if (next != '\r' && next != '\n' && !char.IsWhiteSpace(next))
                {
                    separator = lineEnding;
                }
            }

            buffer = buffer.Substring(0, range.Start) + formatted + separator + buffer.Substring(range.End + 1);
        }

        return buffer;
    }

    /// <summary>
    /// Parses the given C# content as the body of a dummy class and, if it contains no syntax errors, returns the whitespace-normalized inner block using the specified indent unit and line ending; otherwise returns null (or the original content if blank).
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="lineEnding">The line ending.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string TryFormatCSharpBlock(string content, string lineEnding)
    {
        if (string.IsNullOrWhiteSpace(content)) return content;

        var wrapped = "class __CodeJanitorRazorDummy__\n{\n" + content + "\n}";
        var tree = CSharpSyntaxTree.ParseText(wrapped);
        var diagnostics = tree.GetDiagnostics();
        if (diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            return null;
        }

        var root = tree.GetCompilationUnitRoot();
        var classDeclaration = root.Members.OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().FirstOrDefault();
        if (classDeclaration is null)
        {
            return null;
        }

        var normalized = classDeclaration.NormalizeWhitespace(IndentUnit, lineEnding, elasticTrivia: false).ToFullString();
        var open = normalized.IndexOf('{');
        var close = normalized.LastIndexOf('}');
        if (open < 0 || close <= open)
        {
            return null;
        }

        var inner = normalized.Substring(open + 1, close - open - 1);

        return inner.Trim('\r', '\n');
    }

    /// <summary>
    /// Returns the given content as a multi-line string with every line prefixed by baseIndent and the entire block wrapped between lineEnding separators, normalizing CRLF to LF and returning the input unchanged when null or whitespace.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="baseIndent">The base indent.</param>
    /// <param name="lineEnding">The line ending.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string IndentBlock(string content, string baseIndent, string lineEnding)
    {
        if (string.IsNullOrWhiteSpace(content)) return content;

        var lines = content.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder();
        builder.Append(lineEnding);

        for (var i = 0; i < lines.Length; i++)
        {
            builder.Append(baseIndent);
            builder.Append(lines[i]);

            if (i < lines.Length - 1)
            {
                builder.Append(lineEnding);
            }
        }

        builder.Append(lineEnding);
        builder.Append(baseIndent);

        return builder.ToString();
    }

    /// <summary>
    /// a control block string beginning with &apos;@&apos;, returning null when the input is blank, missing a brace, or has an unformattable header; otherwise it indents and recursively reformats the inner content between the braces, wrapping it with the header and braces using the provided base indent and line ending.
    /// </summary>
    /// <param name="rawBlock">The raw block.</param>
    /// <param name="baseIndent">The base indent.</param>
    /// <param name="lineEnding">The line ending.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string TryFormatControlBlock(string rawBlock, string baseIndent, string lineEnding)
    {
        if (string.IsNullOrWhiteSpace(rawBlock) || rawBlock[0] != '@') return null;

        var braceIndex = rawBlock.IndexOf('{');
        if (braceIndex < 0)
        {
            return null;
        }

        var header = TryBuildControlBlockHeader(rawBlock, braceIndex, lineEnding);
        if (header is null)
        {
            return null;
        }

        var inner = rawBlock.Substring(braceIndex + 1, rawBlock.Length - braceIndex - 2);
        var childIndent = baseIndent + IndentUnit;
        var formattedInner = FormatMixedBlockInner(inner, childIndent, lineEnding);

        var builder = new StringBuilder();
        builder.Append(baseIndent).Append(header).Append(lineEnding);
        builder.Append(baseIndent).Append('{');

        if (!string.IsNullOrWhiteSpace(formattedInner))
        {
            builder.Append(lineEnding).Append(formattedInner).Append(lineEnding);
        }
        else
        {
            builder.Append(lineEnding);
        }

        builder.Append(baseIndent).Append('}');

        return builder.ToString();
    }

    /// <summary>
    /// s and normalizes a Razor control block header (handling `@try`, `@finally`, `@catch`, `@else if`, and conditional blocks like `@if`) by trimming the leading keyword, extracting and normalizing the parenthesized condition, and returning `null` for malformed input.
    /// </summary>
    /// <param name="rawBlock">The raw block.</param>
    /// <param name="braceIndex">The brace index.</param>
    /// <param name="lineEnding">The line ending.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string TryBuildControlBlockHeader(string rawBlock, int braceIndex, string lineEnding)
    {
        var headerText = rawBlock.Substring(0, braceIndex).TrimEnd();
        if (headerText.StartsWith("@try", StringComparison.OrdinalIgnoreCase) ||
            headerText.StartsWith("@finally", StringComparison.OrdinalIgnoreCase))
        {
            return headerText;
        }

        if (headerText.StartsWith("@catch", StringComparison.OrdinalIgnoreCase))
        {
            var catchSuffix = headerText.Substring("@catch".Length).Trim();
            if (string.IsNullOrEmpty(catchSuffix))
            {
                return "@catch";
            }

            var catchConditionStart = catchSuffix.IndexOf('(');
            if (catchConditionStart < 0 || !TryFindConditionRange(catchSuffix, catchConditionStart, out var catchOpenParenIndex, out var catchCloseParenIndex))
            {
                return null;
            }

            var catchCondition = catchSuffix.Substring(catchOpenParenIndex, catchCloseParenIndex - catchOpenParenIndex + 1);
            var normalizedCatch = TryNormalizeControlHeader("catch", catchCondition, lineEnding) ?? ("@catch " + catchCondition.Trim());

            return normalizedCatch;
        }

        if (headerText.StartsWith("@else", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = headerText.Substring("@else".Length).Trim();
            if (string.IsNullOrEmpty(suffix))
            {
                return "@else";
            }

            if (!suffix.StartsWith("if", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var conditionStart = suffix.IndexOf('(');
            if (conditionStart < 0 || !TryFindConditionRange(suffix, conditionStart, out var openParenIndex, out var closeParenIndex))
            {
                return null;
            }

            var condition = suffix.Substring(openParenIndex, closeParenIndex - openParenIndex + 1);
            var normalized = TryNormalizeControlHeader("if", condition, lineEnding) ?? ("@if " + condition.Trim());

            return "@else " + normalized.Substring(1);
        }

        if (!TryFindConditionRange(rawBlock, 0, out var rawOpenParenIndex, out var rawCloseParenIndex))
        {
            return null;
        }

        var keyword = rawBlock.Substring(1, rawOpenParenIndex - 1).Trim();
        var rawCondition = rawBlock.Substring(rawOpenParenIndex, rawCloseParenIndex - rawOpenParenIndex + 1);

        return TryNormalizeControlHeader(keyword, rawCondition, lineEnding) ?? ("@" + keyword + " " + rawCondition.Trim());
    }

    /// <summary>
    /// Normalizes a control-flow header (keyword plus condition) into a verbatim-string-compatible prefix by parsing it as a statement and returning the text up to the opening brace prefixed with &quot;@&quot;, or null if no brace is present.
    /// </summary>
    /// <param name="keyword">The keyword.</param>
    /// <param name="condition">The condition.</param>
    /// <param name="lineEnding">The line ending.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string TryNormalizeControlHeader(string keyword, string condition, string lineEnding)
    {
        var statement = SyntaxFactory.ParseStatement(keyword + condition + "{}")
            .NormalizeWhitespace(IndentUnit, lineEnding, elasticTrivia: false)
            .ToFullString();

        var braceIndex = statement.IndexOf('{');
        if (braceIndex < 0)
        {
            return null;
        }

        return "@" + statement.Substring(0, braceIndex).TrimEnd();
    }

    /// <summary>
    /// MixedBlockInner splits mixed Razor content into markup and code segments, trims and indents each segment using the provided indent and line ending (attempting C# statement formatting for code segments), then returns the non-empty results joined by the line ending.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="indent">The indent.</param>
    /// <param name="lineEnding">The line ending.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string FormatMixedBlockInner(string content, string indent, string lineEnding)
    {
        var segments = SplitMarkupAndCodeSegments(content);
        if (segments.Count == 0) return string.Empty;

        var formattedSegments = new List<string>();
        foreach (var segment in segments)
        {
            if (segment.Kind == RazorSegmentKind.Markup)
            {
                formattedSegments.Add(IndentLines(segment.Content.Trim(), indent, lineEnding));
                continue;
            }

            var trimmedCode = segment.Content.Trim();
            if (string.IsNullOrWhiteSpace(trimmedCode))
            {
                continue;
            }

            var formattedCode = TryFormatCSharpStatements(trimmedCode, indent, lineEnding) ?? IndentLines(trimmedCode, indent, lineEnding);
            formattedSegments.Add(formattedCode);
        }

        return string.Join(lineEnding, formattedSegments.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    /// <summary>
    /// Wraps the provided C# statements in a dummy class/method, parses and validates them, then returns the inner body normalized to the requested indentation and line ending (assigning the trimmed content to an `Inner` member and returning null on parse errors or missing body).
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="indent">The indent.</param>
    /// <param name="lineEnding">The line ending.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string TryFormatCSharpStatements(string content, string indent, string lineEnding)
    {
        var wrapped = "class __CodeJanitorRazorDummy__\n{\n    void __M()\n    {\n" + content + "\n    }\n}";
        var tree = CSharpSyntaxTree.ParseText(wrapped);
        if (tree.GetDiagnostics().Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            return null;
        }

        var root = tree.GetCompilationUnitRoot();
        var method = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().FirstOrDefault();
        var body = method?.Body;
        if (body is null)
        {
            return null;
        }

        var normalizedBody = body.NormalizeWhitespace(IndentUnit, lineEnding, elasticTrivia: false).ToFullString();
        var open = normalizedBody.IndexOf('{');
        var close = normalizedBody.LastIndexOf('}');
        if (open < 0 || close <= open)
        {
            return null;
        }

        var inner = normalizedBody.Substring(open + 1, close - open - 1).Trim('\r', '\n');
        inner = TrimCommonLeadingIndent(inner, IndentUnit);

        return IndentLines(inner, indent, lineEnding);
    }

    /// <summary>
    /// Normalizes line endings, then for each non-blank line that begins with the specified indent unit, strips that prefix before rejoining the lines with newline separators.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="indentUnit">The indent unit.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string TrimCommonLeadingIndent(string content, string indentUnit)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            if (lines[i].StartsWith(indentUnit, StringComparison.Ordinal))
            {
                lines[i] = lines[i].Substring(indentUnit.Length);
            }
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Indents every line of the supplied content with the given indent string, normalizing line breaks to the provided line ending via a StringBuilder, with no side effects.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="indent">The indent.</param>
    /// <param name="lineEnding">The line ending.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string IndentLines(string content, string indent, string lineEnding)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder();

        for (var i = 0; i < lines.Length; i++)
        {
            builder.Append(indent).Append(lines[i]);
            if (i < lines.Length - 1)
            {
                builder.Append(lineEnding);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Scans the input string to partition it into a list of `RazorSegment` entries, marking portions enclosed by non-comment/non-directive `&lt;...&gt;` tags as `Markup` and the remaining text as `Code`, with no side effects.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <returns>A List&lt;RazorSegment&gt; value produced by this method.</returns>
    private static List<RazorSegment> SplitMarkupAndCodeSegments(string content)
    {
        var segments = new List<RazorSegment>();
        var index = 0;
        var codeStart = 0;
        var scannerState = RazorScannerState.Default;

        while (index < content.Length)
        {
            var current = content[index];
            var next = index + 1 < content.Length ? content[index + 1] : '\0';

            UpdateScannerState(current, next, ref scannerState, ref index);
            if (scannerState != RazorScannerState.Default)
            {
                index++;
                continue;
            }

            if (current == '<' && next != '/' && next != '!' && next != '?')
            {
                var tagEnd = FindTagEnd(content, index);
                if (tagEnd > index)
                {
                    if (index > codeStart)
                    {
                        segments.Add(new RazorSegment(RazorSegmentKind.Code, content.Substring(codeStart, index - codeStart)));
                    }

                    segments.Add(new RazorSegment(RazorSegmentKind.Markup, content.Substring(index, tagEnd - index + 1)));
                    index = tagEnd + 1;
                    codeStart = index;
                    continue;
                }
            }

            index++;
        }

        if (codeStart < content.Length)
        {
            segments.Add(new RazorSegment(RazorSegmentKind.Code, content.Substring(codeStart)));
        }

        return segments;
    }

    /// <summary>
    /// Updates the Razor scanner state based on the current and next characters, transitioning into or out of line/block comments and character or string literals, and increments the index to skip over paired delimiters or escape sequences.
    /// </summary>
    /// <param name="current">The current.</param>
    /// <param name="next">The next.</param>
    /// <param name="state">The state.</param>
    /// <param name="index">The index.</param>
    private static void UpdateScannerState(char current, char next, ref RazorScannerState state, ref int index)
    {
        switch (state)
        {
            case RazorScannerState.LineComment:
                if (current == '\n') state = RazorScannerState.Default;
                return;

            case RazorScannerState.BlockComment:
                if (current == '*' && next == '/')
                {
                    state = RazorScannerState.Default;
                    index++;
                }
                return;

            case RazorScannerState.CharLiteral:
                if (current == '\\')
                {
                    index++;

                    return;
                }
                if (current == '\'') state = RazorScannerState.Default;
                return;

            case RazorScannerState.StringLiteral:
                if (current == '\\')
                {
                    index++;

                    return;
                }
                if (current == '"') state = RazorScannerState.Default;
                return;

            case RazorScannerState.VerbatimStringLiteral:
                if (current == '"' && next == '"')
                {
                    index++;

                    return;
                }
                if (current == '"') state = RazorScannerState.Default;
                return;

            default:
                if (current == '/' && next == '/')
                {
                    state = RazorScannerState.LineComment;
                    index++;

                    return;
                }
                if (current == '/' && next == '*')
                {
                    state = RazorScannerState.BlockComment;
                    index++;

                    return;
                }
                if (current == '\'')
                {
                    state = RazorScannerState.CharLiteral;

                    return;
                }
                if (current == '@' && next == '"')
                {
                    state = RazorScannerState.VerbatimStringLiteral;
                    index++;

                    return;
                }
                if (current == '"')
                {
                    state = RazorScannerState.StringLiteral;
                }
                return;
        }
    }

    /// <summary>
    /// Scans the text from `start + 1` for the closing `&gt;` of a tag, respecting single- and double-quoted regions and skipping over Razor `@(...)` expressions, returning the index of the `&gt;` or -1 if no closing tag is found.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The start.</param>
    /// <returns>A int value produced by this method.</returns>
    private static int FindTagEnd(string text, int start)
    {
        var quote = '\0';

        for (var i = start + 1; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '@' && TrySkipRazorExpression(text, i, out var afterExpression))
            {
                i = afterExpression - 1;
                continue;
            }

            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c == '\'' || c == '"')
            {
                quote = c;
                continue;
            }

            if (c == '>')
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Skips a Razor transition so quotes belonging to C# code - such as the inner quotes in
    /// <c>href="@Assets["app.css"]"</c> - are not mistaken for HTML attribute delimiters.
    /// </summary>

    private static bool TrySkipRazorExpression(string text, int index, out int nextIndex)
    {
        nextIndex = index;

        if (index >= text.Length || text[index] != '@') return false;

        var i = index + 1;
        if (i >= text.Length)
        {
            nextIndex = i;

            return true;
        }

        if (text[i] == '@')
        {
            nextIndex = i + 1;

            return true;
        }

        if (text[i] == '(')
        {
            if (!TrySkipBalanced(text, i, '(', ')', out i)) return false;
        }
        else if (IsIdentifierStart(text[i]))
        {
            while (i < text.Length && IsIdentifierPart(text[i])) i++;
        }
        else
        {
            nextIndex = i;

            return true;
        }

        while (i < text.Length)
        {
            if (text[i] == '[')
            {
                if (!TrySkipBalanced(text, i, '[', ']', out i)) return false;

                continue;
            }

            if (text[i] == '(')
            {
                if (!TrySkipBalanced(text, i, '(', ')', out i)) return false;

                continue;
            }

            if (text[i] == '.' && i + 1 < text.Length && IsIdentifierStart(text[i + 1]))
            {
                i += 2;
                while (i < text.Length && IsIdentifierPart(text[i])) i++;

                continue;
            }

            break;
        }

        nextIndex = i;

        return true;
    }

    /// <summary>
    /// ips a balanced pair of `open`/`close` delimiters starting at `index`, ignoring delimiters inside C# string and char literals via `TrySkipCSharpLiteral`, and sets `nextIndex` to the position after the matching closing delimiter on success or returns false (with `nextIndex` unchanged from input) if unbalanced or starting on a non-opening character.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="index">The index.</param>
    /// <param name="open">The open.</param>
    /// <param name="close">The close.</param>
    /// <param name="nextIndex">The next index.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool TrySkipBalanced(string text, int index, char open, char close, out int nextIndex)
    {
        nextIndex = index;

        if (index >= text.Length || text[index] != open) return false;

        var depth = 0;
        var i = index;

        while (i < text.Length)
        {
            var c = text[i];

            if (c == '"' || c == '\'')
            {
                if (!TrySkipCSharpLiteral(text, i, out i)) return false;

                continue;
            }

            if (c == open)
            {
                depth++;
            }
            else if (c == close)
            {
                depth--;
                if (depth == 0)
                {
                    nextIndex = i + 1;

                    return true;
                }
            }

            i++;
        }

        return false;
    }

    /// <summary>
    /// ips past a C# string or character literal at the given index, supporting both verbatim strings (with doubled-quote escaping) and regular literals (with backslash escapes), returning true and setting nextIndex to the position after the closing quote, or false if no valid literal is found or it is unterminated.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="index">The index.</param>
    /// <param name="nextIndex">The next index.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool TrySkipCSharpLiteral(string text, int index, out int nextIndex)
    {
        nextIndex = index;

        if (index >= text.Length) return false;

        var quote = text[index];
        if (quote != '"' && quote != '\'') return false;

        var verbatim = quote == '"' && index > 0 && text[index - 1] == '@';
        var i = index + 1;

        while (i < text.Length)
        {
            var c = text[i];

            if (verbatim)
            {
                if (c == quote)
                {
                    if (i + 1 < text.Length && text[i + 1] == quote)
                    {
                        i += 2;

                        continue;
                    }

                    nextIndex = i + 1;

                    return true;
                }

                i++;

                continue;
            }

            if (c == '\\')
            {
                i += 2;

                continue;
            }

            if (c == quote)
            {
                nextIndex = i + 1;

                return true;
            }

            i++;
        }

        return false;
    }

    /// <summary>
    /// IsIdentifierStart returns true when the given character is a letter or underscore, with no side effects or thrown exceptions.
    /// </summary>
    /// <param name="c">The c.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool IsIdentifierStart(char c)
    {
        return char.IsLetter(c) || c == '_';
    }

    /// <summary>
    /// ines whether the specified character is a valid identifier part by returning true if it is a letter, digit, or underscore.
    /// </summary>
    /// <param name="c">The c.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool IsIdentifierPart(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    /// <summary>
    /// leading whitespace (spaces and tabs) of the line that contains the specified index by locating the previous newline and scanning forward to the first non-whitespace character.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="index">The index.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string GetLineIndent(string text, int index)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;

        var i = lineStart;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;

        return text.Substring(lineStart, i - lineStart);
    }

    /// <summary>
    /// the input text sequentially for `@code` or `@functions` directives, locates their enclosing `{...}` blocks via brace matching, and returns a list of `RazorCodeBlockRange` entries marking each directive&apos;s start, opening brace, and matching closing brace, advancing past each found block to prevent re-processing.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A List&lt;RazorCodeBlockRange&gt; value produced by this method.</returns>
    private static List<RazorCodeBlockRange> FindDirectiveCodeBlockRanges(string text)
    {
        var ranges = new List<RazorCodeBlockRange>();
        var i = 0;

        while (i < text.Length)
        {
            var directiveLength = 0;
            if (IsDirectiveAt(text, i, "@code"))
            {
                directiveLength = "@code".Length;
            }
            else if (IsDirectiveAt(text, i, "@functions"))
            {
                directiveLength = "@functions".Length;
            }

            if (directiveLength > 0)
            {
                var cursor = i + directiveLength;
                while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
                if (cursor >= text.Length || text[cursor] != '{')
                {
                    i++;
                    continue;
                }

                var closeBrace = FindMatchingBrace(text, cursor);
                if (closeBrace < 0)
                {
                    i++;
                    continue;
                }

                ranges.Add(new RazorCodeBlockRange(i, closeBrace, cursor, closeBrace));
                i = closeBrace + 1;
                continue;
            }

            i++;
        }

        return ranges;
    }

    /// <summary>
    /// Scans text for Razor control directives (@if, @for, @foreach, @while, @switch, @else, @try, @catch, @finally), locates each directive&apos;s opening brace and matching close brace (handling @else if specially and skipping condition parsing for @try/@finally), and returns a list of RazorCodeBlockRange entries marking their extents.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A List&lt;RazorCodeBlockRange&gt; value produced by this method.</returns>
    private static List<RazorCodeBlockRange> FindControlBlockRanges(string text)
    {
        var ranges = new List<RazorCodeBlockRange>();
        var directives = new[] { "@if", "@for", "@foreach", "@while", "@switch", "@else", "@try", "@catch", "@finally" };
        var i = 0;

        while (i < text.Length)
        {
            var directive = directives.FirstOrDefault(x => IsDirectiveAt(text, i, x));
            if (directive is null)
            {
                i++;
                continue;
            }

            var cursor = i + directive.Length;
            if (string.Equals(directive, "@else", StringComparison.OrdinalIgnoreCase))
            {
                while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;

                if (cursor + 1 < text.Length && string.Compare(text, cursor, "if", 0, 2, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (!TryFindConditionRange(text, cursor + 2, out _, out var elseIfCloseParenIndex))
                    {
                        i++;
                        continue;
                    }

                    cursor = elseIfCloseParenIndex + 1;
                    while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
                }
            }
            else if (!string.Equals(directive, "@try", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(directive, "@finally", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryFindConditionRange(text, i, out _, out var closeParenIndex))
                {
                    i++;
                    continue;
                }

                cursor = closeParenIndex + 1;
            }

            while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;

            if (cursor >= text.Length || text[cursor] != '{')
            {
                i++;
                continue;
            }

            var closeBrace = FindMatchingBrace(text, cursor);
            if (closeBrace < 0)
            {
                i++;
                continue;
            }

            ranges.Add(new RazorCodeBlockRange(i, closeBrace, cursor, closeBrace));
            i = closeBrace + 1;
        }

        return ranges;
    }

    /// <summary>
    /// es for the matching parenthesis pair enclosing a condition starting at `directiveStart`, updating `openParenIndex` and `closeParenIndex` to the outer paren positions and returning true on success, while respecting Razor scanner state to skip characters inside code blocks and returning false (with outputs set to -1) if no matching pair is found.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="directiveStart">The directive start.</param>
    /// <param name="openParenIndex">The open paren index.</param>
    /// <param name="closeParenIndex">The close paren index.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool TryFindConditionRange(string text, int directiveStart, out int openParenIndex, out int closeParenIndex)
    {
        openParenIndex = -1;
        closeParenIndex = -1;

        for (var i = directiveStart; i < text.Length; i++)
        {
            if (text[i] != '(') continue;

            openParenIndex = i;
            var depth = 1;
            var scannerState = RazorScannerState.Default;
            for (var j = i + 1; j < text.Length; j++)
            {
                var current = text[j];
                var next = j + 1 < text.Length ? text[j + 1] : '\0';
                UpdateScannerState(current, next, ref scannerState, ref j);
                if (scannerState != RazorScannerState.Default)
                {
                    continue;
                }

                if (current == '(')
                {
                    depth++;
                }
                else if (current == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        closeParenIndex = j;

                        return true;
                    }
                }
            }

            return false;
        }

        return false;
    }

    /// <summary>
    /// Checks whether `directive` appears at `index` in `text` as a whole word (case-insensitive, not adjacent to other letter/digit characters).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="index">The index.</param>
    /// <param name="directive">The directive.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool IsDirectiveAt(string text, int index, string directive)
    {
        if (index < 0 || index + directive.Length > text.Length) return false;
        if (!text.AsSpan(index, directive.Length).Equals(directive.AsSpan(), StringComparison.OrdinalIgnoreCase)) return false;

        var beforeOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
        var afterIndex = index + directive.Length;
        var afterOk = afterIndex >= text.Length || !char.IsLetterOrDigit(text[afterIndex]);

        return beforeOk && afterOk;
    }

    /// <summary>
    /// ans forward from the given opening brace index to locate the matching closing brace, tracking nesting depth while skipping over line comments, block comments, character literals, regular strings, and verbatim strings.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="openBraceIndex">The open brace index.</param>
    /// <returns>A int value produced by this method.</returns>
    private static int FindMatchingBrace(string text, int openBraceIndex)
    {
        var depth = 0;
        var inLineComment = false;
        var inBlockComment = false;
        var inChar = false;
        var inString = false;
        char stringQuote = '\0';
        var isVerbatimString = false;

        for (var i = openBraceIndex; i < text.Length; i++)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n') inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (inChar)
            {
                if (c == '\\')
                {
                    i++;
                    continue;
                }

                if (c == '\'') inChar = false;
                continue;
            }

            if (inString)
            {
                if (isVerbatimString)
                {
                    if (c == '"' && next == '"')
                    {
                        i++;
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = false;
                        isVerbatimString = false;
                    }

                    continue;
                }

                if (c == '\\')
                {
                    i++;
                    continue;
                }

                if (c == stringQuote)
                {
                    inString = false;
                }

                continue;
            }

            if (c == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (c == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (c == '\'')
            {
                inChar = true;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                stringQuote = '"';
                isVerbatimString = i > 0 && text[i - 1] == '@';
                continue;
            }

            if (c == '{')
            {
                depth++;
                continue;
            }

            if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Represents a range in a Razor code block defined by its start position, end position, and the locations of its opening and closing braces.
    /// </summary>
    private readonly struct RazorCodeBlockRange
    {
        internal RazorCodeBlockRange(int start, int end, int braceStart, int braceEnd)
        {
            Start = start;
            End = end;
            BraceStart = braceStart;
            BraceEnd = braceEnd;
        }

        /// <summary>
        /// Gets the start.
        /// </summary>
        internal int Start { get; }

        /// <summary>
        /// Gets the end.
        /// </summary>
        internal int End { get; }

        /// <summary>
        /// Gets the brace start.
        /// </summary>
        internal int BraceStart { get; }

        /// <summary>
        /// Gets the brace end.
        /// </summary>
        internal int BraceEnd { get; }
    }

    /// <summary>
    /// presents a parsed segment of Razor template syntax, combining a kind identifier with its associated content.
    /// </summary>
    private readonly struct RazorSegment
    {
        internal RazorSegment(RazorSegmentKind kind, string content)
        {
            Kind = kind;
            Content = content;
        }

        /// <summary>
        /// Gets the kind.
        /// </summary>
        internal RazorSegmentKind Kind { get; }

        /// <summary>
        /// Gets the content.
        /// </summary>
        internal string Content { get; }
    }

    /// <summary>
    /// RazorSegmentKind identifies the type of a Razor template segment, distinguishing between executable code and static markup content.
    /// </summary>
    private enum RazorSegmentKind
    {
        Code,
        Markup
    }

    /// <summary>
    /// RazorScannerState is an enumeration that defines the lexical analysis states a Razor parser can be in while tokenizing input, distinguishing between default scanning, comment contexts, and various string and character literal modes.
    /// </summary>
    private enum RazorScannerState
    {
        Default,
        LineComment,
        BlockComment,
        CharLiteral,
        StringLiteral,
        VerbatimStringLiteral
    }
}
