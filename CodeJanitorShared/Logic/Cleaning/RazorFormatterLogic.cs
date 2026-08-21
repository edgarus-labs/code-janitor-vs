using EnvDTE;
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
/// Applies safe formatting to Razor component files. Scope is limited to .razor files.
/// </summary>

internal sealed class RazorFormatterLogic
{
    private const int DefaultAttributeInlineThreshold = 2;
    private const string IndentUnit = "    ";

    private readonly CodeJanitorPackage _package;

    private static RazorFormatterLogic _instance;

    /// <summary>
    /// Returns a cached singleton RazorFormatterLogic instance, lazily creating and storing it with the given package on first call without thread synchronization.
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
    /// Formats a Razor document in place by replacing its entire text with formatted content (preserving markers) only when it is a .razor file, formatting is enabled, and the non-whitespace text differs from the original.
    /// </summary>
    /// <param name="textDocument">The text document.</param>

    internal void FormatRazorDocument(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (textDocument?.Parent?.FullName == null) return;
        if (!string.Equals(Path.GetExtension(textDocument.Parent.FullName), ".razor", StringComparison.OrdinalIgnoreCase)) return;
        if (!Settings.Default.Cleaning_FormatRazorComponents) return;

        var start = textDocument.StartPoint.CreateEditPoint();
        var end = textDocument.EndPoint.CreateEditPoint();
        var original = start.GetText(end);
        if (string.IsNullOrWhiteSpace(original)) return;

        var attributeInlineThreshold = Math.Max(0, Settings.Default.Cleaning_RazorAttributeWrapThreshold);
        if (attributeInlineThreshold == 0)
        {
            attributeInlineThreshold = DefaultAttributeInlineThreshold;
        }

        var formatted = FormatRazorText(original, attributeInlineThreshold);
        if (string.Equals(original, formatted, StringComparison.Ordinal)) return;

        start.ReplaceText(end, formatted, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
    }

    /// <summary>
    /// Formats Razor text by sequentially applying code block, control block, and tag attribute formatting with.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <param name="attributeInlineThreshold">The attribute inline threshold.</param>
    /// <returns>A string value produced by this method.</returns>

    internal static string FormatRazorText(string input, int attributeInlineThreshold)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var lineEnding = DetectLineEnding(input);
        var withFormattedCode = FormatCodeBlocks(input, lineEnding);
        var withFormattedControlBlocks = FormatControlBlocks(withFormattedCode, attributeInlineThreshold, lineEnding);
        var protectedRanges = FindProtectedRanges(withFormattedControlBlocks);

        return FormatTagAttributes(withFormattedControlBlocks, protectedRanges, attributeInlineThreshold, lineEnding);
    }

    /// <summary>
    /// Returns the string &quot;\r\n&quot; if the input contains a CRLF sequence, otherwise returns &quot;\n&quot;, with no side effects or thrown exceptions.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string DetectLineEnding(string text)
    {
        return text.Contains("\r\n") ? "\r\n" : "\n";
    }

    /// <summary>
    /// Finds directive-delimited C# code block ranges, formats each block&apos;s content with TryFormatCSharpBlock, applies base indentation, and replaces the original content in the text, returning the original text unchanged if no ranges are found or formatting fails.
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
            if (formatted == null) continue;

            var baseIndent = GetLineIndent(buffer, range.Start);
            var indented = IndentBlock(formatted, baseIndent, lineEnding);
            buffer = buffer.Substring(0, contentStart) + indented + buffer.Substring(contentStart + contentLength);
        }

        return buffer;
    }

    /// <summary>
    /// Finds control block ranges in the input text and, for each range in reverse order, replaces the block with its formatted version (skipping if unchanged or failed) while inserting a lineEnding separator when the following character is non-whitespace, returning the modified string without mutating the original.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="threshold">The threshold.</param>
    /// <param name="lineEnding">The line ending.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string FormatControlBlocks(string text, int threshold, string lineEnding)
    {
        var ranges = FindControlBlockRanges(text);
        if (ranges.Count == 0) return text;

        var buffer = text;
        foreach (var range in ranges.OrderByDescending(x => x.Start))
        {
            var rawBlock = buffer.Substring(range.Start, range.End - range.Start + 1);
            var formatted = TryFormatControlBlock(rawBlock, GetLineIndent(buffer, range.Start), threshold, lineEnding);
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
    /// Wraps the input in a dummy C# class, parses it, returns null if there are syntax errors or no class found, otherwise returns the formatted inner code with normalized whitespace using the specified line ending and trimmed of leading/trailing newlines.
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
        if (classDeclaration == null)
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
    /// Returns the input unchanged if it is null or whitespace; otherwise normalizes line endings to &apos;\n&apos;, splits the content into lines, and produces a new string starting with a line ending, each line prefixed with baseIndent and joined by lineEnding, then ending with another lineEnding and baseIndent.
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
    /// Returns a formatted control block with indented inner content inserted between braces, or null if the input is null/whitespace or lacks a valid &apos;@&apos; and &apos;{&apos;, with no side effects.
    /// </summary>
    /// <param name="rawBlock">The raw block.</param>
    /// <param name="baseIndent">The base indent.</param>
    /// <param name="threshold">The threshold.</param>
    /// <param name="lineEnding">The line ending.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string TryFormatControlBlock(string rawBlock, string baseIndent, int threshold, string lineEnding)
    {
        if (string.IsNullOrWhiteSpace(rawBlock) || rawBlock[0] != '@') return null;

        var braceIndex = rawBlock.IndexOf('{');
        if (braceIndex < 0)
        {
            return null;
        }

        var header = TryBuildControlBlockHeader(rawBlock, braceIndex, lineEnding);
        if (header == null)
        {
            return null;
        }

        var inner = rawBlock.Substring(braceIndex + 1, rawBlock.Length - braceIndex - 2);
        var childIndent = baseIndent + IndentUnit;
        var formattedInner = FormatMixedBlockInner(inner, childIndent, threshold, lineEnding);

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
    /// We need to analyze the given C# method and produce exactly one concise summary sentence. The method is private static string TryBuildControlBlockHeader(string rawBlock, int braceIndex, string lineEnding). The body is provided, but it&apos;s truncated at the end (&quot;re...&quot;). However, the key behavior can be inferred: it extracts header text before brace, checks for @try/@finally returns as is, handles @catch with condition normalization, @else with optional &quot;if&quot; condition, and otherwise tries to find a condition range and build header from keyword and raw condition. Side effects: none apparent, just parsing. We need one concise sentence mentioning key behavior and side effects. Since exceptions none, side effects none, likely returns null on invalid. We&apos;ll craft a summary. Need to mention: It trims header before brace, returns unchanged for @try/@finally, normalizes @catch and @else-if conditions, falls back to generic keyword+condition parsing, returns null for malformed headers, no side effects. But exactly one sentence. Keep concise. Let&apos;s produce: &quot;This method parses a control block header preceding a brace, returning the original header for @try/@finally, normalizing @catch and @else-if conditionals via TryNormalizeControl.
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
    /// We need to analyze the method. It takes keyword, condition, lineEnding. It parses a statement by concatenating keyword + condition + &quot;{}&quot;, normalizes whitespace with IndentUnit and lineEnding, gets full string. Then finds index of &apos;{&apos;. If not found, returns null. Otherwise returns &quot;@&quot; + substring before brace, trimmed end. So it normalizes control header like &quot;if (x)&quot; to &quot;@if (x)&quot;? Actually returns string starting with @. Side effects: none obvious, but note that parsing may throw? But detected none. Also it uses SyntaxFactory.ParseStatement which could throw on invalid syntax but method doesn&apos;t catch. But the prompt says &quot;Detected thrown exceptions: none detected&quot; so we ignore. We need one concise summary sentence mentioning key behavior and side effects. Behavior: constructs a statement by appending &quot;{}&quot; to keyword and condition, normalizes whitespace, extracts text before opening brace, trims, prefixes with &quot;@&quot; or returns null if no brace. Side effects: none (no state change). Also note that it assumes keyword and condition form a valid statement. Let&apos;s write a concise summary. Need plain text only, no quotes. Something like: &quot;Parses keyword plus condition plus an empty.
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
    /// Splits mixed Razor content into markup and code segments, trims and formats each segment using tag or C# statement formatters with indentation, skips blank code segments, and joins the non-empty formatted results with the specified line ending without throwing exceptions.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="indent">The indent.</param>
    /// <param name="threshold">The threshold.</param>
    /// <param name="lineEnding">The line ending.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string FormatMixedBlockInner(string content, string indent, int threshold, string lineEnding)
    {
        var segments = SplitMarkupAndCodeSegments(content);
        if (segments.Count == 0) return string.Empty;

        var formattedSegments = new List<string>();
        foreach (var segment in segments)
        {
            if (segment.Kind == RazorSegmentKind.Markup)
            {
                var formattedTag = TryFormatTag(segment.Content.Trim(), threshold, lineEnding) ?? segment.Content.Trim();
                formattedSegments.Add(IndentLines(formattedTag, indent, lineEnding));
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
    /// Wraps the input content in a dummy C# method, parses it, returns null on syntax errors or missing body, otherwise normalizes whitespace, extracts and re-indents the inner statements with the given indent and line ending, with no side effects.
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
        if (body == null)
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
    /// Normalizes line endings to LF, trims the specified indent unit from the start of each non-blank line exactly once, and rejoins the lines with LF, with no external side effects.
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
    /// Normalizes line endings to \n, splits into lines, prepends the indent to every line, and joins them with the specified lineEnding without adding a trailing line ending; it is a pure method with no side effects.
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
    /// Splits the input string into a list of RazorSegment objects by identifying HTML-like markup tags, while using a scanner state to skip over code regions and with no side effects beyond building and returning the segment list.
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
    /// Updates the Razor scanner state based on the current and next characters, incrementing the index to skip escaped characters or closing comment/literal delimiters, and transitioning back to Default when a literal or comment ends.
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
    /// Scans text for HTML tags outside protected ranges, formats eligible tags via TryFormatTag, and returns a new string with replacements applied from the end, or the original text if no changes.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="protectedRanges">The protected ranges.</param>
    /// <param name="threshold">The threshold.</param>
    /// <param name="lineEnding">The line ending.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string FormatTagAttributes(string text, IReadOnlyList<RazorCodeBlockRange> protectedRanges, int threshold, string lineEnding)
    {
        var replacements = new List<TagReplacement>();
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] != '<' || IsInsideProtectedRange(index, protectedRanges))
            {
                index++;
                continue;
            }

            if (index + 1 >= text.Length)
            {
                break;
            }

            var next = text[index + 1];
            if (next == '/' || next == '!' || next == '?')
            {
                index++;
                continue;
            }

            var tagEnd = FindTagEnd(text, index);
            if (tagEnd < 0)
            {
                index++;
                continue;
            }

            if (AnyProtectedRangeInside(index, tagEnd, protectedRanges))
            {
                index = tagEnd + 1;
                continue;
            }

            var rawTag = text.Substring(index, tagEnd - index + 1);
            var replacement = TryFormatTag(rawTag, threshold, lineEnding);
            if (replacement != null && !string.Equals(rawTag, replacement, StringComparison.Ordinal))
            {
                replacements.Add(new TagReplacement(index, tagEnd, replacement));
            }

            index = tagEnd + 1;
        }

        if (replacements.Count == 0) return text;

        var output = text;
        foreach (var replacement in replacements.OrderByDescending(x => x.Start))
        {
            output = output.Substring(0, replacement.Start)
                + replacement.Replacement
                + output.Substring(replacement.End + 1);
        }

        return output;
    }

    /// <summary>
    /// Returns true if the index lies within any provided range inclusive of start and end, otherwise false, with no side effects.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <param name="ranges">The ranges.</param>
    /// <returns>A bool value produced by this method.</returns>

    private static bool IsInsideProtectedRange(int index, IReadOnlyList<RazorCodeBlockRange> ranges)
    {
        for (var i = 0; i < ranges.Count; i++)
        {
            if (index >= ranges[i].Start && index <= ranges[i].End)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if any range in the list overlaps the given start-end interval and false otherwise, with no side effects or exceptions thrown.
    /// </summary>
    /// <param name="start">The start.</param>
    /// <param name="end">The end.</param>
    /// <param name="ranges">The ranges.</param>
    /// <returns>A bool value produced by this method.</returns>

    private static bool AnyProtectedRangeInside(int start, int end, IReadOnlyList<RazorCodeBlockRange> ranges)
    {
        for (var i = 0; i < ranges.Count; i++)
        {
            if (ranges[i].Start <= end && ranges[i].End >= start)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Scans from start+1 for the first unquoted &apos;&gt;&apos; (tracking single/double quote state) and returns its index, or -1 if none exists, with no side effects or exceptions.
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
    /// Normalizes a valid HTML/XML tag by trimming its content, rejecting empty, closing, or invalid tags, and returns a compact single-line form when the attribute count is within the threshold, otherwise a multi-line aligned form using the provided line ending, with no side effects.
    /// </summary>
    /// <param name="rawTag">The raw tag.</param>
    /// <param name="threshold">The threshold.</param>
    /// <param name="lineEnding">The line ending.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string TryFormatTag(string rawTag, int threshold, string lineEnding)
    {
        if (string.IsNullOrWhiteSpace(rawTag)) return null;
        if (!rawTag.StartsWith("<", StringComparison.Ordinal) || !rawTag.EndsWith(">", StringComparison.Ordinal)) return null;

        var inner = rawTag.Substring(1, rawTag.Length - 2).Trim();
        if (string.IsNullOrWhiteSpace(inner) || inner.StartsWith("/", StringComparison.Ordinal)) return null;

        var selfClosing = inner.EndsWith("/", StringComparison.Ordinal);
        if (selfClosing)
        {
            inner = inner.Substring(0, inner.Length - 1).TrimEnd();
        }

        var nameEnd = FindNameEnd(inner);
        if (nameEnd <= 0) return null;

        var tagName = inner.Substring(0, nameEnd);
        if (tagName.Contains("@")) return null;

        var rest = inner.Substring(nameEnd);
        var attributes = ParseAttributes(rest);
        if (attributes.Count == 0)
        {
            return selfClosing ? "<" + tagName + " />" : "<" + tagName + ">";
        }

        if (attributes.Count <= threshold)
        {
            var compact = "<" + tagName + " " + string.Join(" ", attributes);
            compact += selfClosing ? " />" : ">";

            return compact;
        }

        var alignPrefix = new string(' ', tagName.Length + 2);
        var builder = new StringBuilder();
        builder.Append('<').Append(tagName).Append(' ').Append(attributes[0]);

        for (var i = 1; i < attributes.Count; i++)
        {
            builder.Append(lineEnding).Append(alignPrefix).Append(attributes[i]);
        }

        builder.Append(selfClosing ? " />" : ">");

        return builder.ToString();
    }

    /// <summary>
    /// Returns the index of the first whitespace character in the input string, or the string&apos;s length if none exists, with no side effects.
    /// </summary>
    /// <param name="inner">The inner.</param>
    /// <returns>A int value produced by this method.</returns>

    private static int FindNameEnd(string inner)
    {
        for (var i = 0; i < inner.Length; i++)
        {
            if (char.IsWhiteSpace(inner[i])) return i;
        }

        return inner.Length;
    }

    /// <summary>
    /// Parses an attribute string by extracting name and optional =value tokens (skipping whitespace and consuming quoted or unquoted values) into a new List&lt;string&gt; with no side effects or thrown exceptions.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A List&lt;string&gt; value produced by this method.</returns>

    private static List<string> ParseAttributes(string text)
    {
        var result = new List<string>();
        var i = 0;

        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length) break;

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '=') i++;
            if (i <= start) break;

            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;

            if (i < text.Length && text[i] == '=')
            {
                i++;
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;

                if (i < text.Length && (text[i] == '"' || text[i] == '\''))
                {
                    var quote = text[i];
                    i++;

                    while (i < text.Length)
                    {
                        if (text[i] == quote)
                        {
                            i++;
                            break;
                        }

                        i++;
                    }
                }
                else
                {
                    while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
                }
            }

            var attribute = text.Substring(start, i - start).Trim();
            if (!string.IsNullOrWhiteSpace(attribute))
            {
                result.Add(attribute);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the leading spaces and tabs of the line containing the specified index by first locating the line start from the previous newline and then scanning forward, with no side effects.
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
    /// Combines directive and control block ranges from the text into a single list ordered by start position, with no side effects.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A List&lt;RazorCodeBlockRange&gt; value produced by this method.</returns>

    private static List<RazorCodeBlockRange> FindProtectedRanges(string text)
    {
        var ranges = FindDirectiveCodeBlockRanges(text);
        ranges.AddRange(FindControlBlockRanges(text));

        return ranges.OrderBy(x => x.Start).ToList();
    }

    /// <summary>
    /// Scans the input text for `@code` and `@functions` directives followed by a `{`, finds each matching closing brace, and returns a list of `RazorCodeBlockRange` objects spanning those blocks, skipping malformed occurrences without throwing exceptions.
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
    /// Scans the input text for Razor control-flow directives (@if, @for, @foreach, @while, @switch, @else, @try, @catch, @finally), skips optional conditions and whitespace, finds the matching opening and closing braces, and returns a list of RazorCodeBlockRange entries for each valid block without throwing exceptions.
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
            if (directive == null)
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
    /// This method scans from directiveStart for the first &apos;(&apos; and then tracks nested parentheses while using scanner state to skip over non-code regions, returning true and setting the matching close-paren index when depth returns to zero, otherwise setting both out parameters to -1 and returning false; its side effects include updating openParenIndex, closeParenIndex, and advancing the inner loop index via UpdateScannerState.
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
    /// Checks whether the given directive appears at the specified index as a whole word using a case-insensitive comparison, returning false for out-of-range or partial matches, with no side effects.
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
    /// The user wants a concise summary sentence of the C# method `FindMatchingBrace`. The method is presumably scanning from an open brace index and tracking nesting depth while skipping comments, strings, chars, and verbatim strings, returning the index of the matching closing brace. I need to mention key behavior and side effects. Side effects: none apparent (it&apos;s a pure scan, no mutation except local variables). Exceptions: none detected. The body is truncated (ends with `...`), but I can infer the rest: it likely increments depth on `{` and decrements on `}`, returning `i` when depth becomes 0. I&apos;ll phrase it as one plain sentence. Key behavior: - Scans C# text from a given `{` index. - Tracks brace depth. - Ignores braces inside comments (`//`, `/* */`), character literals, regular strings, and verbatim strings (`@&quot;...&quot;` with `&quot;&quot;` escapes). - Handles escape sequences (`\\`). - Returns index of matching `}` (presumably), or maybe -1 if not found? Since the body is truncated, I&apos;ll say it returns the matching brace index when depth reaches zero. Side effects: none; it only reads.
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

    private readonly struct RazorCodeBlockRange
    {
        internal RazorCodeBlockRange(int start, int end, int braceStart, int braceEnd)
        {
            Start = start;
            End = end;
            BraceStart = braceStart;
            BraceEnd = braceEnd;
        }

        internal int Start { get; }

        internal int End { get; }

        internal int BraceStart { get; }

        internal int BraceEnd { get; }
    }

    private readonly struct RazorSegment
    {
        internal RazorSegment(RazorSegmentKind kind, string content)
        {
            Kind = kind;
            Content = content;
        }

        internal RazorSegmentKind Kind { get; }

        internal string Content { get; }
    }

    private enum RazorSegmentKind
    {
        Code,
        Markup
    }

    private enum RazorScannerState
    {
        Default,
        LineComment,
        BlockComment,
        CharLiteral,
        StringLiteral,
        VerbatimStringLiteral
    }

    private readonly struct TagReplacement
    {
        internal TagReplacement(int start, int end, string replacement)
        {
            Start = start;
            End = end;
            Replacement = replacement;
        }

        internal int Start { get; }

        internal int End { get; }

        internal string Replacement { get; }
    }
}
