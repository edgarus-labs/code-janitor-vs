using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Simplifies lambda bodies that contain exactly one statement from a block body to an
/// expression body.
/// </summary>
/// <remarks>
/// For example: <c>() =&gt; { return Compute(); }</c> becomes <c>() =&gt; Compute()</c>, and
/// <c>() =&gt; { DoWork(); }</c> becomes <c>() =&gt; DoWork()</c>.
/// </remarks>

public class SingleStatementLambdaConverter : ISourceTransformation
{
    /// <inheritdoc />
    public string Name => "Single Statement Lambda";

    /// <inheritdoc />

    public string Apply(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return source;
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var rewritten = new SingleStatementLambdaRewriter().Visit(root);

        return rewritten.ToFullString();
    }

    private sealed class SingleStatementLambdaRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            node = (AnonymousMethodExpressionSyntax)base.VisitAnonymousMethodExpression(node);

            var expression = TryExtractSingleExpression(node.Block);
            if (expression == null)
            {
                return node;
            }

            var parameterList = node.ParameterList ??
                                SyntaxFactory.ParameterList(
                                    SyntaxFactory.SeparatedList<ParameterSyntax>());

            var lambda = SyntaxFactory.ParenthesizedLambdaExpression(
                parameterList,
                expression.WithTriviaFrom(node.Block));

            var leadingArrowTrivia = parameterList.Parameters.Count == 0
                ? SyntaxFactory.TriviaList(SyntaxFactory.Space)
                : SyntaxTriviaList.Empty;

            lambda = lambda.WithArrowToken(
                SyntaxFactory.Token(
                    leadingArrowTrivia,
                    SyntaxKind.EqualsGreaterThanToken,
                    SyntaxFactory.TriviaList(SyntaxFactory.Space)));

            if (node.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword))
            {
                lambda = lambda.WithAsyncKeyword(node.AsyncKeyword);
            }

            return lambda.WithTriviaFrom(node);
        }

        public override SyntaxNode VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            node = (SimpleLambdaExpressionSyntax)base.VisitSimpleLambdaExpression(node);

            return TrySimplifySimpleLambda(node);
        }

        public override SyntaxNode VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            node = (ParenthesizedLambdaExpressionSyntax)base.VisitParenthesizedLambdaExpression(node);

            return TrySimplifyParenthesizedLambda(node);
        }

        private static SyntaxNode TrySimplifySimpleLambda(SimpleLambdaExpressionSyntax node)
        {
            var expression = TryExtractSingleExpression(node.Body as BlockSyntax);

            return expression == null ? node : node.WithBody(expression.WithTriviaFrom(node.Body));
        }

        private static SyntaxNode TrySimplifyParenthesizedLambda(ParenthesizedLambdaExpressionSyntax node)
        {
            var expression = TryExtractSingleExpression(node.Body as BlockSyntax);

            return expression == null ? node : node.WithBody(expression.WithTriviaFrom(node.Body));
        }

        private static ExpressionSyntax TryExtractSingleExpression(BlockSyntax block)
        {
            if (block == null || block.Statements.Count != 1)
            {
                return null;
            }

            var statement = block.Statements[0];
            switch (statement)
            {
                case ExpressionStatementSyntax expressionStatement:
                    return expressionStatement.Expression;

                case ReturnStatementSyntax returnStatement when returnStatement.Expression != null:
                    return returnStatement.Expression;

                default:
                    return null;
            }
        }
    }
}