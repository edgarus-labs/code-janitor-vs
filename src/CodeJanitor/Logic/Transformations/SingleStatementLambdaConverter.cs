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

            var expression = canConvert && !LosesHeaderTrivia(node) ? TryExtractSingleExpression(node.Block) : null;
            if (expression is null)
            {
                return node;
            }

            var parameterList = node.ParameterList ??
                                SyntaxFactory.ParameterList(
                                    SyntaxFactory.SeparatedList<ParameterSyntax>());

            // Allman style: the line break between the header and the open brace moves behind the arrow, so the
            // arrow does not start a line and the block's indentation is not glued to it on the same line.
            var headerTrailing = node.Block.OpenBraceToken.GetPreviousToken().TrailingTrivia;
            var lineBreak = headerTrailing.Where(t => t.IsKind(SyntaxKind.EndOfLineTrivia)).Take(1).ToList();
            var arrowTrailing = lineBreak.Count > 0
                ? SyntaxFactory.TriviaList(lineBreak)
                : SyntaxFactory.TriviaList(SyntaxFactory.Space);

            if (lineBreak.Count > 0 && node.ParameterList is not null)
            {
                parameterList = parameterList.WithTrailingTrivia(SyntaxFactory.Space);
            }

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
                    arrowTrailing));

            if (node.Modifiers.Count > 0)
            {
                lambda = lambda.WithModifiers(node.Modifiers);
            }

            return lambda.WithTriviaFrom(node);
        }

        /// <summary>
        /// Determines whether the <c>delegate</c> keyword carries a comment or directive that a lambda has no place
        /// for. Trivia before the first token of the expression moves to the lambda and is not lost.
        /// </summary>
        private static bool LosesHeaderTrivia(AnonymousMethodExpressionSyntax node)
        {
            var keyword = node.DelegateKeyword;
            var trivia = node.Modifiers.Count > 0
                ? keyword.LeadingTrivia.Concat(keyword.TrailingTrivia)
                : keyword.TrailingTrivia;

            return trivia.Any(t => !t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia));
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
        /// Determines whether the lambda's delegate type is fixed regardless of its body's form: its target type is
        /// written in the source and is a well-known delegate or expression tree type. A lambda that is (or is nested
        /// in) an argument or a collection initializer element takes its type from overload resolution, where an
        /// expression body can pick another overload than a block body (for example <c>Func&lt;Task&gt;</c> instead of
        /// <c>Action</c>). A lambda assigned to <c>var</c>, <c>object</c> or <c>Delegate</c> takes its natural type,
        /// which changes from <c>Action</c> to <c>Func&lt;T&gt;</c> when an expression body returns a value.
        /// </summary>
        private static bool HasFixedTargetType(ExpressionSyntax lambda)
        {
            var targetType = GetTargetType(lambda);

            return targetType is not null && IsKnownDelegateType(targetType);
        }

        /// <summary>
        /// Gets the type written in the source that the lambda converts to: the declared type of the variable or
        /// property it initializes, the type of a cast, the return type of the member whose body returns it, or the
        /// result type of the enclosing <c>Func</c> lambda that returns it. Returns <see langword="null" /> when the
        /// target type is not visible in the syntax.
        /// </summary>
        private static TypeSyntax GetTargetType(ExpressionSyntax lambda)
        {
            SyntaxNode node = lambda;
            while (node.Parent is ParenthesizedExpressionSyntax)
            {
                node = node.Parent;
            }

            switch (node.Parent)
            {
                case EqualsValueClauseSyntax equalsValue:
                    switch (equalsValue.Parent)
                    {
                        case VariableDeclaratorSyntax declarator when declarator.Parent is VariableDeclarationSyntax declaration:
                            return declaration.Type;

                        case PropertyDeclarationSyntax property:
                            return property.Type;

                        default:
                            return null;
                    }

                case CastExpressionSyntax cast:
                    return cast.Type;

                case ArrowExpressionClauseSyntax arrow:
                    return GetReturnType(arrow.Parent);

                case ReturnStatementSyntax returnStatement:
                    return GetReturnType(returnStatement.Ancestors().FirstOrDefault(IsFunction));

                case AnonymousFunctionExpressionSyntax outerLambda when outerLambda.ExpressionBody == node:
                    return GetReturnType(outerLambda);

                default:
                    return null;
            }
        }

        /// <summary>
        /// Determines whether the node declares a function body that a <c>return</c> statement returns from.
        /// </summary>
        private static bool IsFunction(SyntaxNode node) =>
            node is BaseMethodDeclarationSyntax ||
            node is LocalFunctionStatementSyntax ||
            node is AccessorDeclarationSyntax ||
            node is AnonymousFunctionExpressionSyntax;

        /// <summary>
        /// Gets the declared return type of a synchronous method, operator, local function, property or indexer
        /// getter, or the result type of a lambda whose own target type is a <c>Func</c>; <see langword="null" />
        /// otherwise.
        /// </summary>
        private static TypeSyntax GetReturnType(SyntaxNode function)
        {
            switch (function)
            {
                case MethodDeclarationSyntax method when !method.Modifiers.Any(SyntaxKind.AsyncKeyword):
                    return method.ReturnType;

                case LocalFunctionStatementSyntax localFunction when !localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword):
                    return localFunction.ReturnType;

                case OperatorDeclarationSyntax @operator:
                    return @operator.ReturnType;

                case ConversionOperatorDeclarationSyntax conversion:
                    return conversion.Type;

                case BasePropertyDeclarationSyntax property when property is PropertyDeclarationSyntax || property is IndexerDeclarationSyntax:
                    return property.Type;

                case AccessorDeclarationSyntax accessor when accessor.IsKind(SyntaxKind.GetAccessorDeclaration):
                    return GetReturnType(accessor.Parent?.Parent);

                case AnonymousFunctionExpressionSyntax outerLambda when !outerLambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword):
                    var outerTarget = UnwrapTypeName(GetTargetType(outerLambda), out _);
                    return outerTarget is GenericNameSyntax func && func.Identifier.ValueText == "Func"
                        ? func.TypeArgumentList.Arguments.Last()
                        : null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Determines whether the type is syntactically a well-known delegate type (<c>Action</c>, <c>Func</c>,
        /// <c>Predicate</c>, <c>Comparison</c>, <c>Converter</c>, <c>EventHandler</c>) or an expression tree type
        /// (<c>Expression&lt;TDelegate&gt;</c>), optionally qualified or nullable.
        /// </summary>
        private static bool IsKnownDelegateType(TypeSyntax type)
        {
            var name = UnwrapTypeName(type, out var arity);
            if (name is null)
            {
                return false;
            }

            switch (name.Identifier.ValueText)
            {
                case "Action":
                case "EventHandler":
                    return true;

                case "Func":
                case "Predicate":
                case "Comparison":
                case "Converter":
                    return arity > 0;

                case "Expression":
                    return arity == 1;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Gets the simple name of a possibly nullable, qualified or alias-qualified type name, and its number of
        /// type arguments; <see langword="null" /> for other types (predefined types, arrays, tuples, <c>var</c>).
        /// </summary>
        private static SimpleNameSyntax UnwrapTypeName(TypeSyntax type, out int arity)
        {
            arity = 0;
            if (type is NullableTypeSyntax nullable)
            {
                type = nullable.ElementType;
            }

            SimpleNameSyntax name;
            switch (type)
            {
                case QualifiedNameSyntax qualified:
                    name = qualified.Right;
                    break;

                case AliasQualifiedNameSyntax aliasQualified:
                    name = aliasQualified.Name;
                    break;

                case SimpleNameSyntax simple:
                    name = simple;
                    break;

                default:
                    return null;
            }

            if (name is GenericNameSyntax generic)
            {
                arity = generic.TypeArgumentList.Arguments.Count;
            }

            return name;
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
