using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.RegularExpressions;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Collapses runs of two or more consecutive blank lines down to one (headless-Roslyn-path block
/// for BL-018). Pure and unit-testable. Safe alongside <c>InsertBlankLinePaddingLogic</c>, which
/// adds at most one blank line at a time.
/// </summary>
/// <remarks>
/// Blank lines that are part of a token (a verbatim/raw/interpolated string literal spanning lines)
/// are content, not layout, and are never collapsed.
/// </remarks>
public sealed class NormalizeBlankLinesConverter : ISourceTransformation
{
    // Matches 3+ consecutive newlines (= 2+ blank lines), where intermediate lines may contain
    // only horizontal whitespace. Handles \n and \r\n. [^\S\r\n] = whitespace excluding newlines.

    private static readonly Regex _excessiveBlankLines =
        new Regex(@"\r?\n([^\S\r\n]*\r?\n){2,}", RegexOptions.Compiled);

    // Matches 2+ blank lines at the very start of the file (no preceding line break).

    private static readonly Regex _excessiveLeadingBlankLines =
        new Regex(@"\A([^\S\r\n]*\r?\n){2,}", RegexOptions.Compiled);

    /// <inheritdoc />
    public string Name => "Normalize blank lines";

    /// <inheritdoc />
    public string Apply(string source)
    {
        return Normalize(source);
    }

    /// <summary>
    /// Collapses any run of two or more consecutive blank lines to a single blank line.
    /// </summary>
    public string Normalize(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return source;
        }

        var result = _excessiveLeadingBlankLines.Replace(source, m => NewLineOf(m));
        if (!_excessiveBlankLines.IsMatch(result))
        {
            return result;
        }

        // A run can only lie inside a token when that token is a multi-line string literal; since
        // a run consists of whitespace and line breaks only, checking its first position suffices.

        var root = CSharpSyntaxTree.ParseText(result).GetRoot();

        // MatchEvaluator ensures the replacement uses the file's own line-ending style.

        return _excessiveBlankLines.Replace(result, m =>
        {
            if (root.FindToken(m.Index).Span.Contains(m.Index) || IsInsideDisabledLiteral(root, m.Index))
            {
                return m.Value;
            }

            var nl = NewLineOf(m);

            return nl + nl;
        });
    }

    /// <summary>
    /// Returns true when <paramref name="position"/> lies inside a string literal of an inactive <c>#if</c> branch,
    /// found by parsing the disabled text on its own.
    /// </summary>
    private static bool IsInsideDisabledLiteral(SyntaxNode root, int position)
    {
        var trivia = root.FindTrivia(position);
        if (!trivia.IsKind(SyntaxKind.DisabledTextTrivia))
        {
            return false;
        }

        int offset = position - trivia.SpanStart;
        return CSharpSyntaxTree.ParseText(trivia.ToString()).GetRoot().FindToken(offset).Span.Contains(offset);
    }

    /// <summary>
    /// Returns the line break used in a matched run: CRLF when the run contains one, LF otherwise.
    /// </summary>
    private static string NewLineOf(Match match)
    {
        return match.Value.Contains("\r\n") ? "\r\n" : "\n";
    }
}
