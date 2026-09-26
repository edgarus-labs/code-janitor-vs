using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Converts a single top-level block-scoped namespace to a file-scoped namespace, and back.
/// Uses Roslyn to safely detect applicability (ignoring comments and strings) and to
/// locate the namespace braces precisely, then dedents the body by one indentation level
/// (or, converting back, indents it by one level). Lines starting inside a multi-line string
/// literal, text disabled by <c>#if</c> or a multi-line comment keep their exact text.
/// </summary>
/// <remarks>
/// This is a pure text transformation with no dependency on Visual Studio / EnvDTE,
/// which keeps it unit-testable in isolation (see ADR-0005 / ADR-0006).
/// </remarks>

public sealed class FileScopedNamespaceConverter : INamespaceScopeConverter, ISourceTransformation
{
    private const int DefaultIndentSize = 4;

    private readonly bool? _indentWithTabs;
    private readonly string _spaceIndentation;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileScopedNamespaceConverter" /> class that indents like the
    /// namespace body: with a tab when its first indented line starts with one, otherwise with four spaces.
    /// </summary>

    public FileScopedNamespaceConverter()
        : this(null, DefaultIndentSize)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileScopedNamespaceConverter" /> class with the specified
    /// indentation level.
    /// </summary>
    /// <param name="indentWithTabs">
    /// True to indent with a tab, false to indent with <paramref name="indentSize" /> spaces, null to indent like the
    /// namespace body.
    /// </param>
    /// <param name="indentSize">The number of spaces of one indentation level.</param>

    public FileScopedNamespaceConverter(bool? indentWithTabs, int indentSize)
    {
        if (indentSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(indentSize), indentSize, "The indentation size must be positive.");
        }

        _indentWithTabs = indentWithTabs;
        _spaceIndentation = new string(' ', indentSize);
    }

    /// <inheritdoc />
    public string Name => "File-Scoped Namespace";

    /// <inheritdoc />

    public string Apply(string source) => ConvertToFileScoped(source);

    /// <inheritdoc />

    public string ConvertToFileScoped(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return source;
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        if (!(tree.GetRoot() is CompilationUnitSyntax root))
        {
            return source;
        }

        var blockNamespaces = root.DescendantNodes().OfType<NamespaceDeclarationSyntax>().ToList();
        var fileScopedNamespaces = root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().ToList();

        // Only applicable when there is exactly one block namespace, it is top-level (not
        // nested), and there is no existing file-scoped namespace in the file.
        if (fileScopedNamespaces.Count > 0 || blockNamespaces.Count != 1)
        {
            return source;
        }

        // Using directives inside the block stay inside the file-scoped namespace, where their names keep
        // resolving relative to the namespace. Moving them to file level needs the semantic model, which is
        // the job of the separate "move using directives outside namespace" step. A file-scoped namespace
        // must precede every type of the file and contains all of them, so the namespace must be the only
        // member of the file, also in the build configurations that enable text disabled before it.
        var ns = blockNamespaces[0];
        if (root.Members.Count != 1 || root.Members[0] != ns || HasDisabledMembersBefore(root, ns.NamespaceKeyword.SpanStart))
        {
            return source;
        }

        var openBrace = ns.OpenBraceToken;
        var closeBrace = ns.CloseBraceToken;
        if (openBrace.IsMissing || closeBrace.IsMissing)
        {
            return source;
        }

        var newline = source.IndexOf("\r\n", openBrace.Span.End, closeBrace.Span.Start - openBrace.Span.End, StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";

        // Comments between the name and the body stay after the declaration; a directive there cannot, so the file is
        // left unchanged.
        var declarationTrivia = ns.Name.GetLastToken().TrailingTrivia.Concat(openBrace.LeadingTrivia).Concat(openBrace.TrailingTrivia);
        if (!TryRenderComments(declarationTrivia, newline, false, out var declarationComments))
        {
            return source;
        }

        // The closing brace (and an optional semicolon after it) is removed: comments on its line follow the body on a
        // line of their own, and everything after that line is kept as it is.
        var closingTrivia = closeBrace.TrailingTrivia.AsEnumerable();
        var tailStart = closeBrace.FullSpan.End;
        if (!ns.SemicolonToken.IsKind(SyntaxKind.None))
        {
            closingTrivia = closingTrivia.Concat(ns.SemicolonToken.LeadingTrivia).Concat(ns.SemicolonToken.TrailingTrivia);
            tailStart = ns.SemicolonToken.FullSpan.End;
        }

        if (!TryRenderComments(closingTrivia, newline, true, out var closingComments))
        {
            return source;
        }

        // The text strictly between the namespace braces, from the line after the opening brace.
        var bodyStart = openBrace.FullSpan.End;
        var bodyEnd = closeBrace.Span.Start;

        // Directives must stay balanced without the closing brace: a conditional block opened in the body must end in
        // it, and text disabled after the namespace would move into it in another build configuration.
        if (!HasBalancedConditionalDirectives(root, bodyStart, bodyEnd)
            || root.EndOfFileToken.LeadingTrivia.Any(trivia => trivia.IsKind(SyntaxKind.DisabledTextTrivia)))
        {
            return source;
        }

        var tail = source.Substring(tailStart);
        var dedentedBody = Dedent(source, root, bodyStart, bodyEnd, newline);

        var builder = new StringBuilder(source.Length);

        // Everything before the 'namespace' keyword (file header, outer usings, leading trivia).
        builder.Append(source, 0, ns.NamespaceKeyword.SpanStart);
        builder.Append("namespace ").Append(ns.Name.ToString()).Append(';').Append(declarationComments);
        if (!string.IsNullOrWhiteSpace(dedentedBody))
        {
            builder.Append(newline).Append(newline);
            builder.Append(dedentedBody);
        }

        builder.Append(closingComments);
        builder.Append(newline);
        if (!string.IsNullOrWhiteSpace(tail))
        {
            builder.Append(tail);
        }

        return builder.ToString();
    }

    /// <inheritdoc />

    public bool HasMultipleNamespaces(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return false;
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        if (!(tree.GetRoot() is CompilationUnitSyntax root))
        {
            return false;
        }

        var namespaceCount = root.DescendantNodes().OfType<NamespaceDeclarationSyntax>().Count()
            + root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().Count();

        return namespaceCount > 1;
    }

    /// <inheritdoc />

    public string ConvertToBlockScoped(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return source;
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        // The body is re-indented line by line, which is only safe when strings, comments and disabled text are
        // recognized exactly; a file with syntax errors is left alone.
        if (tree.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return source;
        }

        var namespaces = root
            .DescendantNodes(node => node is CompilationUnitSyntax || node is BaseNamespaceDeclarationSyntax)
            .OfType<BaseNamespaceDeclarationSyntax>()
            .ToList();
        if (namespaces.Count != 1
            || !(namespaces[0] is FileScopedNamespaceDeclarationSyntax ns)
            || ns.AttributeLists.Count > 0
            || ns.Modifiers.Count > 0
            || ns.SemicolonToken.IsMissing
            || IsInsideConditionalDirective(root, ns.NamespaceKeyword.SpanStart))
        {
            return source;
        }

        var text = tree.GetText();
        var semicolon = ns.SemicolonToken;
        var keywordStart = ns.NamespaceKeyword.SpanStart;
        var bodyLines = GetBodyLines(text, semicolon.FullSpan.End);
        var newline = GetFirstLineBreak(source) ?? "\r\n";
        var indentation = GetIndentationUnit(source, root, bodyLines);

        var builder = new StringBuilder(source.Length + (bodyLines.Count * indentation.Length) + 8);
        builder.Append(source, 0, keywordStart);
        builder.Append(source.Substring(keywordStart, semicolon.SpanStart - keywordStart).TrimEnd());

        // A comment after the semicolon stays on the declaration line; its line break is replaced below.
        builder.Append(source.Substring(semicolon.Span.End, semicolon.FullSpan.End - semicolon.Span.End).TrimEnd());
        builder.Append(newline).Append('{').Append(newline);

        foreach (var line in bodyLines)
        {
            if (ShouldIndent(source, root, line))
            {
                builder.Append(indentation);
            }

            builder.Append(source, line.Start, line.EndIncludingLineBreak - line.Start);
        }

        if (bodyLines.Count > 0 && bodyLines[bodyLines.Count - 1].End == bodyLines[bodyLines.Count - 1].EndIncludingLineBreak)
        {
            builder.Append(newline);
        }

        builder.Append('}');
        if (source.EndsWith("\n", StringComparison.Ordinal) || source.EndsWith("\r", StringComparison.Ordinal))
        {
            builder.Append(newline);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Removes one indentation level (a single tab, or the configured number of spaces) from the start of each line of
    /// the body between <paramref name="bodyStart" /> and <paramref name="bodyEnd" />, after trimming surrounding blank
    /// lines. Lines starting inside a multi-line string literal, text disabled by <c>#if</c> or a multi-line comment
    /// keep their exact text (see <see cref="IndentationGuard" />).
    /// </summary>

    private string Dedent(string source, SyntaxNode root, int bodyStart, int bodyEnd, string newline)
    {
        while (bodyStart < bodyEnd && (source[bodyStart] == '\r' || source[bodyStart] == '\n'))
        {
            bodyStart++;
        }

        // The text before the closing brace ends with whitespace trivia (its indentation), never inside a token.
        while (bodyEnd > bodyStart && (source[bodyEnd - 1] == '\r' || source[bodyEnd - 1] == '\n' || source[bodyEnd - 1] == ' ' || source[bodyEnd - 1] == '\t'))
        {
            bodyEnd--;
        }

        var indentSize = _spaceIndentation.Length;
        var builder = new StringBuilder(bodyEnd - bodyStart);
        for (var lineStart = bodyStart; ;)
        {
            var lineBreak = source.IndexOf(newline, lineStart, bodyEnd - lineStart, StringComparison.Ordinal);
            var lineEnd = lineBreak < 0 ? bodyEnd : lineBreak;
            var removed = 0;
            if (lineEnd - lineStart >= indentSize && string.CompareOrdinal(source, lineStart, _spaceIndentation, 0, indentSize) == 0)
            {
                removed = indentSize;
            }
            else if (lineStart < lineEnd && source[lineStart] == '\t')
            {
                removed = 1;
            }

            if (removed > 0 && !IndentationGuard.CanChangeIndentation(root, lineStart))
            {
                removed = 0;
            }

            builder.Append(source, lineStart + removed, lineEnd - lineStart - removed);
            if (lineBreak < 0)
            {
                return builder.ToString();
            }

            builder.Append(newline);
            lineStart = lineBreak + newline.Length;
        }
    }

    /// <summary>
    /// Renders the comments among the trivia around a removed namespace brace: a comment that continues a line follows
    /// a space, one that starts a line (and the first one, when <paramref name="firstOnNewLine" /> is set) follows a
    /// line break. Any trivia other than whitespace, line breaks and comments (a directive) cannot be kept in place.
    /// </summary>
    /// <param name="trivia">The trivia.</param>
    /// <param name="newline">The line break of the file.</param>
    /// <param name="firstOnNewLine">Whether the first comment starts a line.</param>
    /// <param name="comments">The rendered comments; empty when there are none.</param>
    /// <returns>False when the trivia contains something other than whitespace, line breaks and comments.</returns>

    private static bool TryRenderComments(IEnumerable<SyntaxTrivia> trivia, string newline, bool firstOnNewLine, out string comments)
    {
        var builder = new StringBuilder();
        var startsLine = firstOnNewLine;
        foreach (var item in trivia)
        {
            switch (item.Kind())
            {
                case SyntaxKind.WhitespaceTrivia:
                    break;

                case SyntaxKind.EndOfLineTrivia:
                    startsLine = true;
                    break;

                case SyntaxKind.SingleLineCommentTrivia:
                case SyntaxKind.MultiLineCommentTrivia:
                    builder.Append(startsLine ? newline : " ").Append(item.ToFullString());
                    startsLine = false;
                    break;

                default:
                    comments = null;

                    return false;
            }
        }

        comments = builder.ToString();

        return true;
    }

    /// <summary>
    /// Whether text disabled by <c>#if</c> before <paramref name="position" /> declares members (types or
    /// namespaces). Disabled using and extern alias directives and global attributes declare none.
    /// </summary>

    private static bool HasDisabledMembersBefore(CompilationUnitSyntax root, int position) =>
        root.DescendantTrivia(TextSpan.FromBounds(0, position))
            .Any(trivia => trivia.IsKind(SyntaxKind.DisabledTextTrivia)
                && trivia.SpanStart < position
                && SyntaxFactory.ParseCompilationUnit(trivia.ToString()).Members.Count > 0);

    /// <summary>
    /// Whether the conditional directives between <paramref name="start" /> and <paramref name="end" /> form complete
    /// blocks: every <c>#if</c> there ends there, and no <c>#elif</c>, <c>#else</c> or <c>#endif</c> there continues a
    /// block opened before <paramref name="start" />.
    /// </summary>

    private static bool HasBalancedConditionalDirectives(CompilationUnitSyntax root, int start, int end)
    {
        var depth = 0;
        for (var directive = root.GetFirstDirective(); directive != null && directive.SpanStart < end; directive = directive.GetNextDirective())
        {
            if (directive.SpanStart < start)
            {
                continue;
            }

            switch (directive.Kind())
            {
                case SyntaxKind.IfDirectiveTrivia:
                    depth++;
                    break;

                case SyntaxKind.ElifDirectiveTrivia:
                case SyntaxKind.ElseDirectiveTrivia:
                    if (depth == 0)
                    {
                        return false;
                    }

                    break;

                case SyntaxKind.EndIfDirectiveTrivia:
                    if (depth == 0)
                    {
                        return false;
                    }

                    depth--;
                    break;
            }
        }

        return depth == 0;
    }

    /// <summary>
    /// Whether <paramref name="position" /> lies between an <c>#if</c> and its <c>#endif</c>. A namespace declared
    /// there cannot get braces: the closing brace would have to follow the <c>#endif</c>.
    /// </summary>

    private static bool IsInsideConditionalDirective(CompilationUnitSyntax root, int position)
    {
        var depth = 0;
        for (var directive = root.GetFirstDirective(); directive != null && directive.SpanStart < position; directive = directive.GetNextDirective())
        {
            if (directive.IsKind(SyntaxKind.IfDirectiveTrivia))
            {
                depth++;
            }
            else if (directive.IsKind(SyntaxKind.EndIfDirectiveTrivia))
            {
                depth--;
            }
        }

        return depth > 0;
    }

    /// <summary>
    /// Gets the lines of the namespace body, which runs from <paramref name="bodyStart" /> to the end of the file (so
    /// directives and comments after the last member stay inside the braces), without leading and trailing blank lines.
    /// The first line starts at <paramref name="bodyStart" /> even when that is not the start of a line.
    /// </summary>

    private static List<BodyLine> GetBodyLines(SourceText text, int bodyStart)
    {
        var lines = new List<BodyLine>();
        for (var start = bodyStart; start < text.Length;)
        {
            var line = text.Lines.GetLineFromPosition(start);
            lines.Add(new BodyLine(start, line.End, line.EndIncludingLineBreak));
            start = line.EndIncludingLineBreak;
        }

        var first = 0;
        while (first < lines.Count && IsBlank(text, lines[first]))
        {
            first++;
        }

        var last = lines.Count - 1;
        while (last >= first && IsBlank(text, lines[last]))
        {
            last--;
        }

        return lines.GetRange(first, last - first + 1);
    }

    private static bool IsBlank(SourceText text, BodyLine line)
    {
        for (var position = line.Start; position < line.End; position++)
        {
            if (!char.IsWhiteSpace(text[position]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether one indentation level is inserted before <paramref name="line" />: not for blank lines, lines starting
    /// inside a string literal, disabled text or a multi-line comment, and preprocessor directives in column zero.
    /// </summary>

    private static bool ShouldIndent(string source, SyntaxNode root, BodyLine line)
    {
        var contentStart = line.Start;
        while (contentStart < line.End && (source[contentStart] == ' ' || source[contentStart] == '\t'))
        {
            contentStart++;
        }

        if (contentStart == line.End || !IndentationGuard.CanChangeIndentation(root, line.Start))
        {
            return false;
        }

        return contentStart > line.Start || source[contentStart] != '#';
    }

    /// <summary>
    /// Gets one indentation level: the configured one, otherwise in the style of the body (a tab when its first
    /// indented line starts with a tab, otherwise the configured number of spaces).
    /// </summary>

    private string GetIndentationUnit(string source, SyntaxNode root, List<BodyLine> lines)
    {
        if (_indentWithTabs.HasValue)
        {
            return _indentWithTabs.Value ? "\t" : _spaceIndentation;
        }

        foreach (var line in lines)
        {
            var first = source[line.Start];
            if ((first == ' ' || first == '\t') && ShouldIndent(source, root, line))
            {
                return first == '\t' ? "\t" : _spaceIndentation;
            }
        }

        return _spaceIndentation;
    }

    /// <summary>
    /// Gets the first line break of <paramref name="source" /> (<c>\r\n</c>, <c>\n</c> or <c>\r</c>), or null when
    /// it has none.
    /// </summary>

    private static string GetFirstLineBreak(string source)
    {
        var index = source.IndexOfAny(new[] { '\r', '\n' });
        if (index < 0)
        {
            return null;
        }

        if (source[index] == '\n')
        {
            return "\n";
        }

        return index + 1 < source.Length && source[index + 1] == '\n' ? "\r\n" : "\r";
    }

    /// <summary>
    /// A line of the namespace body: its start, the end of its content and the end including its line break.
    /// </summary>

    private readonly struct BodyLine
    {
        public BodyLine(int start, int end, int endIncludingLineBreak)
        {
            Start = start;
            End = end;
            EndIncludingLineBreak = endIncludingLineBreak;
        }

        public int Start { get; }

        public int End { get; }

        public int EndIncludingLineBreak { get; }
    }
}
