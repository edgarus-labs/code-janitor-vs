using CodeJanitor.Properties;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Formats comments in C# source code by applying consistent spacing and alignment rules.
/// Handles both single-line (//) and multi-line (/* */) comments.
/// </summary>
public sealed class CommentFormatConverter : ISourceTransformation
{
    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name => "Format comments";

    /// <summary>
    /// Puts exactly one space after the <c>//</c> of every comment that starts its line and aligns the
    /// <c>*</c> continuation lines of block comments that start their line. Only real comment trivia is
    /// changed: string literals, documentation comments, comments starting with more than two slashes
    /// and the line endings of the file are left as they are.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>The source with its comments formatted.</returns>
    public string Apply(string source)
    {
        if (string.IsNullOrEmpty(source) || !Settings.Default.Formatting_CommentRunDuringCleanup)
        {
            return source;
        }

        var text = SourceText.From(source);
        var root = CSharpSyntaxTree.ParseText(text).GetRoot();
        var changes = new List<TextChange>();

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
            {
                AddSingleLineCommentChange(text, trivia, changes);
            }
            else if (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
                AddMultiLineCommentChanges(text, trivia, changes);
            }
        }

        if (changes.Count == 0)
        {
            return source;
        }

        changes.Sort((left, right) => left.Span.Start.CompareTo(right.Span.Start));

        return text.WithChanges(changes).ToString();
    }

    private static void AddSingleLineCommentChange(SourceText text, SyntaxTrivia trivia, List<TextChange> changes)
    {
        var line = text.Lines.GetLineFromPosition(trivia.SpanStart);
        if (!IsWhitespace(text, line.Start, trivia.SpanStart))
        {
            // Trailing comments after code are left alone.
            return;
        }

        var comment = trivia.ToString();
        if (comment.StartsWith("///", StringComparison.Ordinal))
        {
            // Commented-out documentation comments and //// separators are not reformatted.
            return;
        }

        var body = comment.Substring(2);
        var formatted = string.IsNullOrWhiteSpace(body) ? "//" : "// " + body.TrimStart();
        if (formatted != comment)
        {
            changes.Add(new TextChange(trivia.Span, formatted));
        }
    }

    private static void AddMultiLineCommentChanges(SourceText text, SyntaxTrivia trivia, List<TextChange> changes)
    {
        var startLine = text.Lines.GetLineFromPosition(trivia.SpanStart);
        if (!IsWhitespace(text, startLine.Start, trivia.SpanStart))
        {
            // A block comment opened after code keeps its content as written.
            return;
        }

        var baseIndentation = text.ToString(TextSpan.FromBounds(startLine.Start, trivia.SpanStart));
        var closingLineNumber = text.Lines.GetLineFromPosition(trivia.Span.End).LineNumber;

        // The opening and closing lines are kept as they are; only the lines in between are aligned.
        for (var lineNumber = startLine.LineNumber + 1; lineNumber < closingLineNumber; lineNumber++)
        {
            var lineSpan = text.Lines[lineNumber].Span;
            var line = text.ToString(lineSpan);
            var formatted = NormalizeMultiLineCommentLine(line, baseIndentation);
            if (formatted != line)
            {
                changes.Add(new TextChange(lineSpan, formatted));
            }
        }
    }

    private static bool IsWhitespace(SourceText text, int start, int end)
    {
        for (var position = start; position < end; position++)
        {
            if (!char.IsWhiteSpace(text[position]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Trims leading whitespace from a comment line and, if it begins with an asterisk, realigns it under the base indentation; otherwise returns the line unchanged, with no side effects or thrown exceptions.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="baseIndentation">The base indentation.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string NormalizeMultiLineCommentLine(string line, string baseIndentation)
    {
        var trimmed = line.TrimStart();

        // If line starts with *, align it with base indentation
        if (trimmed.StartsWith("*", StringComparison.Ordinal))
        {
            return baseIndentation + " " + trimmed;
        }

        // Otherwise preserve as-is (content lines)

        return line;
    }
}
