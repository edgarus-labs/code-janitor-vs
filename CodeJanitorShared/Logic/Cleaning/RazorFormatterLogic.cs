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

namespace CodeJanitor.Logic.Cleaning
{
    /// <summary>
    /// Applies safe formatting to Razor component files. Scope is limited to .razor files.
    /// </summary>
    internal class RazorFormatterLogic
    {
        private const int DefaultAttributeInlineThreshold = 2;
        private const string IndentUnit = "    ";

        private readonly CodeJanitorPackage _package;

        private static RazorFormatterLogic _instance;

        internal static RazorFormatterLogic GetInstance(CodeJanitorPackage package)
        {
            return _instance ?? (_instance = new RazorFormatterLogic(package));
        }

        private RazorFormatterLogic(CodeJanitorPackage package)
        {
            _package = package;
        }

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

        internal static string FormatRazorText(string input, int attributeInlineThreshold)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var lineEnding = DetectLineEnding(input);
            var withFormattedCode = FormatCodeBlocks(input, lineEnding);
            var withFormattedControlBlocks = FormatControlBlocks(withFormattedCode, attributeInlineThreshold, lineEnding);
            var protectedRanges = FindProtectedRanges(withFormattedControlBlocks);

            return FormatTagAttributes(withFormattedControlBlocks, protectedRanges, attributeInlineThreshold, lineEnding);
        }

        private static string DetectLineEnding(string text)
        {
            return text.Contains("\r\n") ? "\r\n" : "\n";
        }

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

                buffer = buffer.Substring(0, range.Start) + formatted + buffer.Substring(range.End + 1);
            }

            return buffer;
        }

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

        private static string TryBuildControlBlockHeader(string rawBlock, int braceIndex, string lineEnding)
        {
            var headerText = rawBlock.Substring(0, braceIndex).TrimEnd();
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

        private static int FindNameEnd(string inner)
        {
            for (var i = 0; i < inner.Length; i++)
            {
                if (char.IsWhiteSpace(inner[i])) return i;
            }

            return inner.Length;
        }

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

        private static string GetLineIndent(string text, int index)
        {
            var lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;

            var i = lineStart;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
            return text.Substring(lineStart, i - lineStart);
        }

        private static List<RazorCodeBlockRange> FindProtectedRanges(string text)
        {
            var ranges = FindDirectiveCodeBlockRanges(text);
            ranges.AddRange(FindControlBlockRanges(text));
            return ranges.OrderBy(x => x.Start).ToList();
        }

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

        private static List<RazorCodeBlockRange> FindControlBlockRanges(string text)
        {
            var ranges = new List<RazorCodeBlockRange>();
            var directives = new[] { "@if", "@for", "@foreach", "@while", "@switch", "@else" };
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
                if (!string.Equals(directive, "@else", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryFindConditionRange(text, i, out var openParenIndex, out var closeParenIndex))
                    {
                        i++;
                        continue;
                    }

                    cursor = closeParenIndex + 1;
                }

                while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;

                if (string.Equals(directive, "@else", StringComparison.OrdinalIgnoreCase) &&
                    cursor + 1 < text.Length &&
                    string.Compare(text, cursor, "if", 0, 2, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (!TryFindConditionRange(text, cursor + 2, out _, out var elseIfCloseParenIndex))
                    {
                        i++;
                        continue;
                    }

                    cursor = elseIfCloseParenIndex + 1;
                    while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
                }

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

        private static bool IsDirectiveAt(string text, int index, string directive)
        {
            if (index < 0 || index + directive.Length > text.Length) return false;
            if (!text.AsSpan(index, directive.Length).Equals(directive.AsSpan(), StringComparison.OrdinalIgnoreCase)) return false;

            var beforeOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var afterIndex = index + directive.Length;
            var afterOk = afterIndex >= text.Length || !char.IsLetterOrDigit(text[afterIndex]);
            return beforeOk && afterOk;
        }

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
}
