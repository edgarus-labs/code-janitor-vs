using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// A source transformation that converts traditional null equality checks (== null, != null)
/// to modern pattern matching (is null, is not null).
/// </summary>
public sealed class NullCheckPatternMatchingConverter : ISourceTransformation
{
    /// <inheritdoc />
    public string Name => "Convert to Pattern Matching Null Checks";

    /// <inheritdoc />
    public string Apply(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return source;
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var rewriter = new NullCheckRewriter();
        var newRoot = rewriter.Visit(root);

        return newRoot.ToFullString();
    }

    private sealed class NullCheckRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            var visitedNode = (BinaryExpressionSyntax)base.VisitBinaryExpression(node);

            var isNotEquals = visitedNode.IsKind(SyntaxKind.NotEqualsExpression);
            var isEquals = visitedNode.IsKind(SyntaxKind.EqualsExpression);

            if (!isNotEquals && !isEquals)
            {
                return visitedNode;
            }

            ExpressionSyntax targetExpr = null;

            if (visitedNode.Right.IsKind(SyntaxKind.NullLiteralExpression))
            {
                targetExpr = visitedNode.Left.WithoutTrivia();
            }
            else if (visitedNode.Left.IsKind(SyntaxKind.NullLiteralExpression))
            {
                targetExpr = visitedNode.Right.WithoutTrivia();
            }

            if (targetExpr == null)
            {
                return visitedNode;
            }

            PatternSyntax pattern;
            var isToken = SyntaxFactory.Token(
                SyntaxFactory.TriviaList(SyntaxFactory.Space),
                SyntaxKind.IsKeyword,
                SyntaxFactory.TriviaList(SyntaxFactory.Space));

            if (isNotEquals)
            {
                var notToken = SyntaxFactory.Token(
                    SyntaxFactory.TriviaList(),
                    SyntaxKind.NotKeyword,
                    SyntaxFactory.TriviaList(SyntaxFactory.Space));

                pattern = SyntaxFactory.UnaryPattern(
                    notToken,
                    SyntaxFactory.ConstantPattern(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));
            }
            else
            {
                pattern = SyntaxFactory.ConstantPattern(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression));
            }

            var isPatternExpr = SyntaxFactory.IsPatternExpression(targetExpr, isToken, pattern)
                .WithLeadingTrivia(visitedNode.GetLeadingTrivia())
                .WithTrailingTrivia(visitedNode.GetTrailingTrivia());

            return isPatternExpr;
        }
    }
}
