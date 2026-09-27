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
    /// unchanged, because a directive unused in the active configuration may be needed by another one. A directive
    /// that shares its lines with other code or carries comments is kept, so no comment or code is lost.
    /// </summary>
    /// <param name="document">The C# document.</param>
    /// <param name="usingsToKeep">Using directives that are never removed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document without unnecessary using directives.</returns>
    internal static async Task<Document> RemoveUnnecessaryUsingsAsync(Document document, IReadOnlyCollection<string> usingsToKeep, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || root.DescendantTrivia().Any(trivia => trivia.IsKind(SyntaxKind.IfDirectiveTrivia)))
        {
            return document;
        }

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
        {
            return document;
        }

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var removals = new List<TextChange>();
        foreach (var diagnostic in semanticModel.GetDiagnostics(cancellationToken: cancellationToken))
        {
            if (diagnostic.Id != UnnecessaryUsingDiagnosticId || !diagnostic.Location.IsInSource)
            {
                continue;
            }

            var usingDirective = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<UsingDirectiveSyntax>();
            if (usingDirective is null || usingsToKeep.Contains(usingDirective.ToString().Trim()))
            {
                continue;
            }

            if (TryGetWholeLinesSpan(text, usingDirective, out var span))
            {
                removals.Add(new TextChange(span, string.Empty));
            }
        }

        if (removals.Count == 0)
        {
            return document;
        }

        return document.WithText(text.WithChanges(removals.Distinct().OrderBy(change => change.Span.Start)));
    }

    /// <summary>
    /// Gets the span of the lines a using directive occupies, including the line break of its last line, when those
    /// lines hold nothing but the directive and whitespace.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <param name="usingDirective">The using directive.</param>
    /// <param name="span">The span to delete.</param>
    /// <returns>True when the directive can be removed with its lines.</returns>
    private static bool TryGetWholeLinesSpan(SourceText text, UsingDirectiveSyntax usingDirective, out TextSpan span)
    {
        span = default(TextSpan);

        if (usingDirective.GetLeadingTrivia().Concat(usingDirective.GetTrailingTrivia())
            .Any(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia)))
        {
            return false;
        }

        var firstLine = text.Lines.GetLineFromPosition(usingDirective.Span.Start);
        var lastLine = text.Lines.GetLineFromPosition(usingDirective.Span.End);

        var before = text.ToString(TextSpan.FromBounds(firstLine.Start, usingDirective.Span.Start));
        var after = text.ToString(TextSpan.FromBounds(usingDirective.Span.End, lastLine.End));
        if (!string.IsNullOrWhiteSpace(before) || !string.IsNullOrWhiteSpace(after))
        {
            return false;
        }

        span = TextSpan.FromBounds(firstLine.Start, lastLine.EndIncludingLineBreak);

        return true;
    }
}
