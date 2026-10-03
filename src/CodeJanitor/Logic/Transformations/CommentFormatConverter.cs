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
    /// changed: string literals, <c>///</c> documentation comments, comments starting with more than two slashes
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
        var changes = new List<TextChange>();
        AddCommentChanges(text, CSharpSyntaxTree.ParseText(text).GetRoot(), 0, changes);

        if (changes.Count == 0)
        {
            return source;
        }

        changes.Sort((left, right) => left.Span.Start.CompareTo(right.Span.Start));

        return text.WithChanges(changes).ToString();
    }

    /// <summary>
    /// Adds the changes for the comments of <paramref name="root" />, whose text starts at
    /// <paramref name="offset" /> in <paramref name="text" />.
    /// </summary>
    private static void AddCommentChanges(SourceText text, SyntaxNode root, int offset, List<TextChange> changes)
    {
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            var span = new TextSpan(offset + trivia.SpanStart, trivia.Span.Length);
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
            {
                AddSingleLineCommentChange(text, span, changes);
            }
            else if (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
                AddMultiLineCommentChanges(text, span, changes);
            }
            else if (trivia.IsKind(SyntaxKind.DisabledTextTrivia))
            {
                // An inactive #if branch is kept as plain text; parsing it on its own finds its comments
                // while its string literals stay strings.
                AddCommentChanges(text, CSharpSyntaxTree.ParseText(trivia.ToString()).GetRoot(), span.Start, changes);
            }
        }
    }

    private static void AddSingleLineCommentChange(SourceText text, TextSpan span, List<TextChange> changes)
    {
        var line = text.Lines.GetLineFromPosition(span.Start);
        if (!IsWhitespace(text, line.Start, span.Start))
        {
            // Trailing comments after code are left alone.
            return;
        }

        var comment = text.ToString(span);
        if (comment.StartsWith("///", StringComparison.Ordinal))
        {
            // Commented-out documentation comments and //// separators are not reformatted.
            return;
        }

        var body = comment.Substring(2);
        var formatted = string.IsNullOrWhiteSpace(body) ? "//" : "// " + body.TrimStart();
        if (formatted != comment)
        {
            changes.Add(new TextChange(span, formatted));
        }
    }

    private static void AddMultiLineCommentChanges(SourceText text, TextSpan span, List<TextChange> changes)
    {
        var startLine = text.Lines.GetLineFromPosition(span.Start);
        if (!IsWhitespace(text, startLine.Start, span.Start))
        {
            // A block comment opened after code keeps its content as written.
            return;
        }

        var baseIndentation = text.ToString(TextSpan.FromBounds(startLine.Start, span.Start));
        var closingLineNumber = text.Lines.GetLineFromPosition(span.End).LineNumber;

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
