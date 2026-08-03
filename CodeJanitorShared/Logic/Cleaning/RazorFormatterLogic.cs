using EnvDTE;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.Shell;
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
        private const int AttributeInlineThreshold = 2;
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

            var start = textDocument.StartPoint.CreateEditPoint();
            var end = textDocument.EndPoint.CreateEditPoint();
            var original = start.GetText(end);
            if (string.IsNullOrWhiteSpace(original)) return;

            var formatted = FormatRazorText(original, AttributeInlineThreshold);
            if (string.Equals(original, formatted, StringComparison.Ordinal)) return;

            start.ReplaceText(end, formatted, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
        }

        internal static string FormatRazorText(string input, int attributeInlineThreshold)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var lineEnding = DetectLineEnding(input);
            var withFormattedCode = FormatCodeBlocks(input, lineEnding);
            var protectedRanges = FindCodeBlockRanges(withFormattedCode);

            return FormatTagAttributes(withFormattedCode, protectedRanges, attributeInlineThreshold, lineEnding);
        }

        private static string DetectLineEnding(string text)
        {
            return text.Contains("\r\n") ? "\r\n" : "\n";
        }

        private static string FormatCodeBlocks(string text, string lineEnding)
        {
            var ranges = FindCodeBlockRanges(text);
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

        private static List<RazorCodeBlockRange> FindCodeBlockRanges(string text)
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
