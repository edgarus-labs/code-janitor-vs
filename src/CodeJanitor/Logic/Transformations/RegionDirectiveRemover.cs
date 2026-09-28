using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Removes C# #region and #endregion directive lines while preserving other preprocessor directives.
/// </summary>
public sealed class RegionDirectiveRemover : ISourceTransformation
{
    private static readonly Regex RegionDirectiveLineRegex = new Regex(
        @"^[ \t]*#(?:end)?region\b",
        RegexOptions.Compiled);

    private static readonly char[] LineBreakCharacters = { '\r', '\n' };

    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name => "Remove region directives";

    /// <summary>
    /// Returns the input unchanged if it is null or empty; otherwise removes every #region and #endregion directive
    /// line together with its line break. Lines that start inside a multi-line string literal or comment are kept.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>A string value produced by this method.</returns>
    public string Apply(string source)
    {
        if (string.IsNullOrEmpty(source) || source.IndexOf("region", StringComparison.Ordinal) < 0)
        {
            return source;
        }

        List<TextSpan> protectedSpans = FindMultiLineLiteralAndCommentSpans(source);
        var result = new StringBuilder(source.Length);
        int lineStart = 0;
        while (lineStart < source.Length)
        {
            int contentEnd = FindLineEnd(source, lineStart, out int nextLineStart);
            bool isRegionDirective = RegionDirectiveLineRegex.IsMatch(source.Substring(lineStart, contentEnd - lineStart))
                && !StartsInside(protectedSpans, lineStart);
            if (!isRegionDirective)
            {
                result.Append(source, lineStart, nextLineStart - lineStart);
            }

            lineStart = nextLineStart;
        }

        return result.ToString();
    }

    /// <summary>
    /// Returns the spans of the string literals and comments of <paramref name="source"/> that contain a line break,
    /// i.e. the places where a line can start without being code (and so without being a preprocessor directive).
    /// </summary>
    /// <param name="source">The C# source.</param>
    /// <returns>The spans, in no particular order.</returns>
    internal static List<TextSpan> FindMultiLineLiteralAndCommentSpans(string source)
    {
        var spans = new List<TextSpan>();
        AddMultiLineLiteralAndCommentSpans(source, CSharpSyntaxTree.ParseText(source).GetRoot(), 0, spans);
        return spans;
    }

    /// <summary>
    /// Adds the multi-line literal and comment spans of <paramref name="root"/>, shifted by <paramref name="offset"/>;
    /// inactive <c>#if</c>/<c>#elif</c>/<c>#else</c> branches are parsed on their own so their literals and comments are found too.
    /// </summary>
    private static void AddMultiLineLiteralAndCommentSpans(string source, SyntaxNode root, int offset, List<TextSpan> spans)
    {
        foreach (SyntaxNodeOrToken nodeOrToken in root.DescendantNodesAndTokens())
        {
            bool isLiteral = nodeOrToken.IsToken || nodeOrToken.AsNode() is InterpolatedStringExpressionSyntax;
            TextSpan span = Shift(nodeOrToken.Span, offset);
            if (isLiteral && ContainsLineBreak(source, span))
            {
                spans.Add(span);
            }
        }

        foreach (SyntaxTrivia trivia in root.DescendantTrivia())
        {
            TextSpan span = Shift(trivia.Span, offset);
            if ((trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                && ContainsLineBreak(source, span))
            {
                spans.Add(span);
            }
        }

        // The directives inside an inactive branch split its text into several disabled-text trivia, so
        // the whole branch (up to its #elif, #else or #endif) is parsed on its own instead.
        string rootText = null;
        foreach (SyntaxTrivia directiveTrivia in root.DescendantTrivia().Where(trivia => trivia.IsDirective))
        {
            var directive = (DirectiveTriviaSyntax)directiveTrivia.GetStructure();
            if (directive is BranchingDirectiveTriviaSyntax branch && !branch.BranchTaken)
            {
                int start = directive.FullSpan.End;
                DirectiveTriviaSyntax next = directive.GetRelatedDirectives().SkipWhile(d => d != directive).Skip(1).FirstOrDefault();
                int end = next?.FullSpan.Start ?? root.FullSpan.End;
                if (end > start)
                {
                    rootText = rootText ?? root.ToFullString();
                    string block = rootText.Substring(start, end - start);
                    AddMultiLineLiteralAndCommentSpans(source, CSharpSyntaxTree.ParseText(block).GetRoot(), offset + start, spans);
                }
            }
        }
    }

    private static TextSpan Shift(TextSpan span, int offset) => new TextSpan(span.Start + offset, span.Length);

    /// <summary>
    /// Returns true when <paramref name="position"/> lies strictly inside one of <paramref name="spans"/>.
    /// </summary>
    /// <param name="spans">The spans.</param>
    /// <param name="position">The position of a line start.</param>
    /// <returns>True when the position is inside a span.</returns>
    internal static bool StartsInside(List<TextSpan> spans, int position)
    {
        foreach (TextSpan span in spans)
        {
            if (span.Start < position && position < span.End)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the end of the content of the line starting at <paramref name="lineStart"/> (the position of its
    /// line break, or the end of the text) and the start of the next line. CRLF, LF and CR are line breaks.
    /// </summary>
    /// <param name="source">The text.</param>
    /// <param name="lineStart">The start of the line.</param>
    /// <param name="nextLineStart">The start of the next line (the end of the text for the last line).</param>
    /// <returns>The end of the line content.</returns>
    internal static int FindLineEnd(string source, int lineStart, out int nextLineStart)
    {
        int contentEnd = source.IndexOfAny(LineBreakCharacters, lineStart);
        if (contentEnd < 0)
        {
            nextLineStart = source.Length;

            return source.Length;
        }

        nextLineStart = source[contentEnd] == '\r' && contentEnd + 1 < source.Length && source[contentEnd + 1] == '\n'
            ? contentEnd + 2
            : contentEnd + 1;

        return contentEnd;
    }

    private static bool ContainsLineBreak(string source, TextSpan span)
    {
        return span.Length > 0 && source.IndexOfAny(LineBreakCharacters, span.Start, span.Length) >= 0;
    }
}
