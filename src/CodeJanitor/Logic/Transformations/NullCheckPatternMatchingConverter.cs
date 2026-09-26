using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// A source transformation that converts traditional null equality checks (== null, != null)
/// to modern pattern matching (is null, is not null). Skips expression-bodied non-async lambdas
/// and LINQ query-expression clauses, since those can be converted to expression trees where the
/// compiler rejects the `is`/`is not` pattern-matching operator (CS8122) and this tool has no
/// semantic model to prove otherwise. The `is null` pattern needs C# 7.0, `is not null` needs C# 9.
/// </summary>
public sealed class NullCheckPatternMatchingConverter : ISourceTransformation
{
    private readonly bool _convertInequalityChecks;

    /// <summary>
    /// Initializes a new instance of the <see cref="NullCheckPatternMatchingConverter" /> class that converts both
    /// equality (<c>== null</c>) and inequality (<c>!= null</c>) checks.
    /// </summary>

    public NullCheckPatternMatchingConverter()
        : this(true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NullCheckPatternMatchingConverter" /> class.
    /// </summary>
    /// <param name="convertInequalityChecks">
    /// True to also convert inequality checks (<c>!= null</c>) to <c>is not null</c>, which needs C# 9; false to
    /// convert only equality checks (<c>== null</c>) to <c>is null</c>, which needs C# 7.0.
    /// </param>

    public NullCheckPatternMatchingConverter(bool convertInequalityChecks)
    {
        _convertInequalityChecks = convertInequalityChecks;
    }

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
        var rewriter = new NullCheckRewriter(_convertInequalityChecks);
        var newRoot = rewriter.Visit(root);

        return newRoot.ToFullString();
    }

    /// <summary>
    /// A rewriter that transforms null check binary expressions into a normalized or optimized form.
    /// </summary>

    private sealed class NullCheckRewriter : CSharpSyntaxRewriter
    {
        private readonly bool _convertInequalityChecks;

        /// <summary>
        /// Initializes a new instance of the <see cref="NullCheckRewriter" /> class.
        /// </summary>
        /// <param name="convertInequalityChecks">Whether inequality checks are converted to <c>is not null</c>.</param>

        internal NullCheckRewriter(bool convertInequalityChecks)
        {
            _convertInequalityChecks = convertInequalityChecks;
        }

        /// <summary>

        /// Overriding a syntax visitor, this method rewrites binary `==`/`!=` expressions where one operand is `null` into equivalent `is` or `is not` pattern expressions, preserving the original trivia.
        /// </summary>

        /// <param name="node">The node.</param>
        /// <returns>A SyntaxNode value produced by this method.</returns>
        public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            var visitedNode = (BinaryExpressionSyntax)base.VisitBinaryExpression(node);

            var isNotEquals = visitedNode.IsKind(SyntaxKind.NotEqualsExpression) && _convertInequalityChecks;
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

            if (targetExpr is null)
            {
                return visitedNode;
            }

            if (IsUnsafeForPatternMatching(node))
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

        /// <summary>
        /// Determines whether the given null-check node sits in a syntax position where the C# compiler
        /// could reject an `is`/`is not` pattern-matching operator if the surrounding lambda or query ends up
        /// converted to an Expression tree (CS8122). Block-bodied lambdas, async lambdas, and anonymous methods
        /// can never be compiled to expression trees (CS0834/CS1989/CS1946), so they are always safe; an
        /// expression-bodied non-async lambda or a query-expression clause cannot be proven safe without a
        /// semantic model, so both are conservatively treated as unsafe.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>True if rewriting to a pattern-matching operator here could break compilation; otherwise, false.</returns>
        private static bool IsUnsafeForPatternMatching(SyntaxNode node)
        {
            foreach (var ancestor in node.Ancestors())
            {
                switch (ancestor)
                {
                    case LambdaExpressionSyntax lambda:
                        return lambda.ExpressionBody is not null && !lambda.Modifiers.Any(SyntaxKind.AsyncKeyword);

                    case QueryClauseSyntax:
                    case SelectOrGroupClauseSyntax:
                        return true;

                    case AnonymousMethodExpressionSyntax:
                    case LocalFunctionStatementSyntax:
                    case BaseMethodDeclarationSyntax:
                    case AccessorDeclarationSyntax:
                    case BasePropertyDeclarationSyntax:
                        return false;
                }
            }

            return false;
        }
    }
}
