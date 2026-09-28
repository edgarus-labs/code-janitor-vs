using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// A source transformation that converts string.Format calls with string literals to modern string interpolation ($"...").
/// </summary>
public sealed class StringInterpolationConverter : ISourceTransformation
{
    // An escaped brace pair ("{{" or "}}") is matched first so that a placeholder-like text inside it stays literal.
    private static readonly Regex PlaceholderRegex = new Regex(@"\{\{|\}\}|\{(\d+)(?:,(-?\d+))?(?::([^}]+))?\}", RegexOptions.Compiled);

    private static readonly char[] LineBreakCharacters = { '\r', '\n' };

    /// <inheritdoc />
    public string Name => "Convert string.Format to String Interpolation";

    /// <inheritdoc />
    public string Apply(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return source;
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var rewriter = new StringFormatRewriter();
        var newRoot = rewriter.Visit(root);

        return newRoot.ToFullString();
    }

    /// <summary>
    /// syntax rewriter that transforms string.Format invocations into interpolated string expressions.
    /// </summary>
    private sealed class StringFormatRewriter : CSharpSyntaxRewriter
    {
        /// <summary>
        /// This CSharpSyntaxRewriter override transforms valid `string.Format` invocations with string-literal format strings and in-range placeholder indices into equivalent interpolated strings, returning the original node unchanged when the call is not a string-format invocation, has fewer than two arguments, uses a non-literal format string, or contains out-of-range placeholders.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>A SyntaxNode value produced by this method.</returns>
        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node);

            if (visited.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return visited;
            }

            if (memberAccess.Name.Identifier.Text != "Format")
            {
                return visited;
            }

            var caller = memberAccess.Expression.ToString();
            if (caller != "string" && caller != "String" && caller != "System.String")
            {
                return visited;
            }

            var arguments = visited.ArgumentList.Arguments;
            if (arguments.Count < 2)
            {
                return visited;
            }

            // Check if first argument is string literal
            if (arguments[0].Expression is not LiteralExpressionSyntax formatLiteral ||
                !formatLiteral.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return visited;
            }

            var formatString = formatLiteral.Token.ValueText;
            var formatArgs = new ExpressionSyntax[arguments.Count - 1];
            for (var i = 1; i < arguments.Count; i++)
            {
                formatArgs[i - 1] = arguments[i].Expression;
            }

            // Check that all placeholder indices are within range of formatArgs, and that each referenced argument
            // fits on one line: a line break inside an interpolation hole of a regular interpolated string needs C# 11.
            var matches = PlaceholderRegex.Matches(formatString);
            var placeholderCount = 0;

            foreach (Match match in matches)
            {
                if (!match.Groups[1].Success)
                {
                    continue; // escaped brace, copied as literal text
                }

                placeholderCount++;
                if (int.TryParse(match.Groups[1].Value, out var idx))
                {
                    if (idx < 0 || idx >= formatArgs.Length)
                    {
                        return visited; // Invalid index or mismatch, keep original
                    }

                    if (formatArgs[idx].ToString().IndexOfAny(LineBreakCharacters) >= 0)
                    {
                        return visited;
                    }
                }
                else
                {
                    return visited;
                }
            }

            if (placeholderCount == 0)
            {
                return visited;
            }

            // Build interpolated string components
            var builder = new System.Text.StringBuilder();
            builder.Append("$\"");

            var lastIndex = 0;
            foreach (Match match in matches)
            {
                if (!match.Groups[1].Success)
                {
                    continue; // escaped brace: stays part of the surrounding literal text, where it is escaped the same way
                }

                // Text before match
                if (match.Index > lastIndex)
                {
                    var textSegment = formatString.Substring(lastIndex, match.Index - lastIndex);
                    builder.Append(EscapeForInterpolatedString(textSegment));
                }

                var argIndex = int.Parse(match.Groups[1].Value);
                var argExpr = formatArgs[argIndex].ToString();
                var alignment = match.Groups[2].Success ? "," + match.Groups[2].Value : string.Empty;
                var formatSpecifier = match.Groups[3].Success ? ":" + EscapeForInterpolatedString(match.Groups[3].Value) : string.Empty;

                // A top-level ':' in a hole starts the format clause, so a conditional expression or an alias-qualified
                // name (global::X) must be parenthesized.
                if (formatArgs[argIndex] is ConditionalExpressionSyntax
                    || formatArgs[argIndex].DescendantNodesAndSelf().Any(n => n is AliasQualifiedNameSyntax))
                {
                    argExpr = "(" + argExpr + ")";
                }

                builder.Append('{');
                builder.Append(argExpr);
                builder.Append(alignment);
                builder.Append(formatSpecifier);
                builder.Append('}');

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < formatString.Length)
            {
                var textSegment = formatString.Substring(lastIndex);
                builder.Append(EscapeForInterpolatedString(textSegment));
            }

            builder.Append('\"');

            var interpolatedText = builder.ToString();
            var parsedExpr = SyntaxFactory.ParseExpression(interpolatedText);

            // ParseExpression stops at the first token it cannot use; keep the call when any text was dropped or misparsed.
            if (parsedExpr.ContainsDiagnostics || parsedExpr.FullSpan.Length != interpolatedText.Length)
            {
                return visited;
            }

            return parsedExpr
                .WithLeadingTrivia(visited.GetLeadingTrivia())
                .WithTrailingTrivia(visited.GetTrailingTrivia());
        }

        /// <summary>
        /// Escapes a string for use in an interpolated string by replacing backslashes, double quotes, carriage returns, newlines, and tabs with their escaped backslash representations.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>A string value produced by this method.</returns>
        private static string EscapeForInterpolatedString(string text)
        {
            return text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }
    }
}
