using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Simplifies lambda bodies that contain exactly one statement from a block body to an
/// expression body.
/// </summary>
/// <remarks>
/// For example: <c>() =&gt; { return Compute(); }</c> becomes <c>() =&gt; Compute()</c>, and
/// <c>() =&gt; { DoWork(); }</c> becomes <c>() =&gt; DoWork()</c>.
/// </remarks>
public sealed class SingleStatementLambdaConverter : ISourceTransformation
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

    /// <summary>
    /// SingleStatementLambdaRewriter is a rewriter that converts single-statement lambdas into expression-bodied equivalents across anonymous methods, simple lambdas, and parenthesized lambdas.
    /// </summary>
    private sealed class SingleStatementLambdaRewriter : CSharpSyntaxRewriter
    {
        /// <summary>
        /// Converts an anonymous method with a single expression body into an equivalent parenthesized lambda expression, preserving async modifier and syntax trivia, while returning the original node if the body cannot be reduced.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>A SyntaxNode value produced by this method.</returns>
        public override SyntaxNode VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            var canConvert = HasFixedTargetType(node) &&
                             (node.ParameterList is not null || InitializesParameterlessDelegate(node));
            node = (AnonymousMethodExpressionSyntax)base.VisitAnonymousMethodExpression(node);

            var expression = canConvert ? TryExtractSingleExpression(node.Block) : null;
            if (expression is null)
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

        /// <summary>
        /// Overrides the simple lambda expression visitor to first run the base visit and then return the result of attempting to simplify the lambda expression, potentially rewriting the node.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>A SyntaxNode value produced by this method.</returns>
        public override SyntaxNode VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            var hasFixedTargetType = HasFixedTargetType(node);
            node = (SimpleLambdaExpressionSyntax)base.VisitSimpleLambdaExpression(node);

            return hasFixedTargetType ? TrySimplifySimpleLambda(node) : node;
        }

        /// <summary>
        /// Visits a parenthesized lambda expression, then attempts to simplify it and returns the resulting (potentially replaced) syntax node.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>A SyntaxNode value produced by this method.</returns>
        public override SyntaxNode VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            var hasFixedTargetType = HasFixedTargetType(node);
            node = (ParenthesizedLambdaExpressionSyntax)base.VisitParenthesizedLambdaExpression(node);

            return hasFixedTargetType ? TrySimplifyParenthesizedLambda(node) : node;
        }

        /// <summary>
        /// Attempts to simplify a simple lambda by replacing its block body with a single extracted expression when possible, preserving trivia, otherwise returns the original node unchanged.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>A SyntaxNode value produced by this method.</returns>
        private static SyntaxNode TrySimplifySimpleLambda(SimpleLambdaExpressionSyntax node)
        {
            var expression = TryExtractSingleExpression(node.Body as BlockSyntax);

            return expression is null ? node : node.WithBody(expression.WithTriviaFrom(node.Body));
        }

        /// <summary>
        /// Attempts to replace a parenthesized lambda&apos;s block body with a single extracted expression when possible, preserving trivia, otherwise returns the original node with no side effects.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>A SyntaxNode value produced by this method.</returns>
        private static SyntaxNode TrySimplifyParenthesizedLambda(ParenthesizedLambdaExpressionSyntax node)
        {
            var expression = TryExtractSingleExpression(node.Body as BlockSyntax);

            return expression is null ? node : node.WithBody(expression.WithTriviaFrom(node.Body));
        }

        /// <summary>
        /// Returns the single expression from a block containing exactly one expression or return statement, or null if the block is null, has multiple statements, or the statement is unsupported, with no side effects.
        /// </summary>
        /// <param name="block">The block.</param>
        /// <returns>A ExpressionSyntax value produced by this method.</returns>
        private static ExpressionSyntax TryExtractSingleExpression(BlockSyntax block)
        {
            if (block is null || block.Statements.Count != 1)
            {
                return null;
            }

            ExpressionSyntax expression;
            var statement = block.Statements[0];
            switch (statement)
            {
                case ExpressionStatementSyntax expressionStatement:
                    expression = expressionStatement.Expression;
                    break;

                case ReturnStatementSyntax returnStatement when returnStatement.Expression is not null:
                    expression = returnStatement.Expression;
                    break;

                default:
                    return null;
            }

            // Comments and preprocessor directives between the braces but outside the kept expression would be lost.
            var losesTrivia = block.DescendantTrivia().Any(trivia =>
                block.Span.Contains(trivia.Span) &&
                !expression.Span.Contains(trivia.Span) &&
                !trivia.IsKind(SyntaxKind.WhitespaceTrivia) &&
                !trivia.IsKind(SyntaxKind.EndOfLineTrivia));

            return losesTrivia ? null : expression;
        }

        /// <summary>
        /// Determines whether the lambda's delegate type is fixed regardless of its body's form. A lambda that is (or is
        /// nested in) an argument or a collection initializer element takes its type from overload resolution, where
        /// an expression body can pick another overload than a block body (for example <c>Func&lt;Task&gt;</c> instead
        /// of <c>Action</c>, or an expression-tree overload of <c>IQueryable</c>), silently changing behavior.
        /// </summary>
        private static bool HasFixedTargetType(ExpressionSyntax lambda)
        {
            foreach (var ancestor in lambda.Ancestors())
            {
                switch (ancestor)
                {
                    case ArgumentSyntax _:
                        return false;
                    case InitializerExpressionSyntax initializer when !initializer.IsKind(SyntaxKind.ObjectInitializerExpression):
                        return false;
                    case StatementSyntax _:
                    case MemberDeclarationSyntax _:
                        return true;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether an anonymous method without a parameter list (which converts to a delegate type with any
        /// parameters) initializes a variable whose declared type is a delegate without parameters (<c>Action</c> or
        /// <c>Func&lt;TResult&gt;</c>), so that the equivalent <c>() =&gt;</c> lambda converts to it as well.
        /// </summary>
        private static bool InitializesParameterlessDelegate(AnonymousMethodExpressionSyntax node)
        {
            if (!(node.Parent is EqualsValueClauseSyntax equalsValue) ||
                !(equalsValue.Parent is VariableDeclaratorSyntax declarator) ||
                !(declarator.Parent is VariableDeclarationSyntax declaration))
            {
                return false;
            }

            var type = declaration.Type;
            if (type is QualifiedNameSyntax qualified &&
                qualified.Left is IdentifierNameSyntax left &&
                left.Identifier.ValueText == "System")
            {
                type = qualified.Right;
            }

            switch (type)
            {
                case IdentifierNameSyntax identifier:
                    return identifier.Identifier.ValueText == "Action";
                case GenericNameSyntax generic:
                    return generic.Identifier.ValueText == "Func" && generic.TypeArgumentList.Arguments.Count == 1;
                default:
                    return false;
            }
        }
    }
}
