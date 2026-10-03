using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Cleaning.Diagnostics;

/// <summary>
/// The Roslyn equivalents of the Visual Studio "Remove and Sort Usings" and "Format Document" commands for a C#
/// document, so a closed file is cleaned without opening it in the editor. The document options (.editorconfig) of
/// the workspace decide the sort order and the formatting.
/// </summary>
internal static class RoslynDocumentCleanup
{
    /// <summary>
    /// The compiler diagnostic reported for a using directive that is not needed.
    /// </summary>
    private const string UnnecessaryUsingDiagnosticId = "CS8019";

    /// <summary>
    /// Applies the requested steps: removes unnecessary using directives, sorts them, then formats the document.
    /// </summary>
    /// <param name="document">The C# document.</param>
    /// <param name="removeAndSortUsings">True to remove unnecessary using directives and sort them.</param>
    /// <param name="format">True to format the document.</param>
    /// <param name="usingsToKeep">Using directives, as written in the source (e.g. <c>using System.Linq;</c>), that are never removed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cleaned document; the same instance when no step was requested.</returns>
    internal static async Task<Document> ApplyAsync(
        Document document,
        bool removeAndSortUsings,
        bool format,
        IReadOnlyCollection<string> usingsToKeep,
        CancellationToken cancellationToken)
    {
        if (removeAndSortUsings)
        {
            document = await RemoveUnnecessaryUsingsAsync(document, usingsToKeep ?? Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
            document = await Formatter.OrganizeImportsAsync(document, cancellationToken).ConfigureAwait(false);
        }

        if (format)
        {
            document = await Formatter.FormatAsync(document, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return document;
    }

    /// <summary>
    /// Removes the using directives the compiler reports as unnecessary. A file with conditional compilation is left
    /// unchanged, because a directive unused in the active configuration may be needed by another one. Only the lines
    /// of a directive are removed, so a file header or directive above it stays; a directive that shares its lines
    /// with other code or a comment is kept, so no comment or code is lost.
    /// </summary>
    /// <param name="document">The C# document.</param>
    /// <param name="usingsToKeep">Using directives that are never removed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document without unnecessary using directives.</returns>
    internal static async Task<Document> RemoveUnnecessaryUsingsAsync(Document document, IReadOnlyCollection<string> usingsToKeep, CancellationToken cancellationToken)
    {
        SyntaxNode root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || root.DescendantTrivia().Any(trivia => trivia.IsKind(SyntaxKind.IfDirectiveTrivia)))
        {
            return document;
        }

        SemanticModel semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
        {
            return document;
        }

        SourceText text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var removals = new List<TextChange>();
        foreach (Diagnostic diagnostic in semanticModel.GetDiagnostics(cancellationToken: cancellationToken))
        {
            if (diagnostic.Id != UnnecessaryUsingDiagnosticId || !diagnostic.Location.IsInSource)
            {
                continue;
            }

            UsingDirectiveSyntax usingDirective = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<UsingDirectiveSyntax>();
            if (usingDirective is null || usingsToKeep.Contains(usingDirective.ToString().Trim()))
            {
                continue;
            }

            if (TryGetWholeLinesSpan(text, usingDirective, out TextSpan span))
            {
                removals.Add(new TextChange(span, string.Empty));
            }
        }

        if (removals.Count == 0)
        {
            return document;
        }

        // Removing the using directives of a block leaves the blank line that separated the block from the code; when
        // the removed lines start the file or follow a blank line, drop the blank lines after them as well.
        var changes = new List<TextChange>();
        foreach (TextSpan block in MergeAdjacent(removals.Select(change => change.Span).Distinct().OrderBy(span => span.Start)))
        {
            int end = block.End;
            if (block.Start == 0 || IsBlank(text.Lines.GetLineFromPosition(block.Start - 1)))
            {
                while (end < text.Length)
                {
                    TextLine line = text.Lines.GetLineFromPosition(end);
                    if (!IsBlank(line) || line.EndIncludingLineBreak == line.End)
                    {
                        break;
                    }

                    end = line.EndIncludingLineBreak;
                }
            }

            changes.Add(new TextChange(TextSpan.FromBounds(block.Start, end), string.Empty));
        }

        return document.WithText(text.WithChanges(changes));
    }

    /// <summary>
    /// Merges spans that touch each other into one span.
    /// </summary>
    /// <param name="orderedSpans">The spans, ordered by their start.</param>
    /// <returns>The merged spans, in order.</returns>
    private static IEnumerable<TextSpan> MergeAdjacent(IEnumerable<TextSpan> orderedSpans)
    {
        TextSpan? current = null;
        foreach (TextSpan span in orderedSpans)
        {
            if (current is { } open && span.Start <= open.End)
            {
                current = TextSpan.FromBounds(open.Start, Math.Max(open.End, span.End));
            }
            else
            {
                if (current is { } done)
                {
                    yield return done;
                }

                current = span;
            }
        }

        if (current is { } last)
        {
            yield return last;
        }
    }

    /// <summary>
    /// Determines whether a line holds only whitespace.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>True for an empty or whitespace-only line.</returns>
    private static bool IsBlank(TextLine line) => string.IsNullOrWhiteSpace(line.ToString());

    /// <summary>
    /// Gets the span of the lines a using directive occupies, including the line break of its last line, when those
    /// lines hold nothing but the directive and whitespace. Trivia on the lines above the directive (a file header,
    /// <c>#nullable</c>, <c>#region</c>) is outside the span and stays.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <param name="usingDirective">The using directive.</param>
    /// <param name="span">The span to delete.</param>
    /// <returns>True when the directive can be removed with its lines.</returns>
    private static bool TryGetWholeLinesSpan(SourceText text, UsingDirectiveSyntax usingDirective, out TextSpan span)
    {
        span = default(TextSpan);

        TextLine firstLine = text.Lines.GetLineFromPosition(usingDirective.Span.Start);
        TextLine lastLine = text.Lines.GetLineFromPosition(usingDirective.Span.End);

        string before = text.ToString(TextSpan.FromBounds(firstLine.Start, usingDirective.Span.Start));
        string after = text.ToString(TextSpan.FromBounds(usingDirective.Span.End, lastLine.End));
        if (!string.IsNullOrWhiteSpace(before) || !string.IsNullOrWhiteSpace(after))
        {
            return false;
        }

        span = TextSpan.FromBounds(firstLine.Start, lastLine.EndIncludingLineBreak);

        return true;
    }
}
