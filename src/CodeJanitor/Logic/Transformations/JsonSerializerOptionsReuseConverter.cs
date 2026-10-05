using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Applies a conservative CA1869-style optimization by replacing direct allocations of
/// <c>JsonSerializerOptions</c> in <c>JsonSerializer.*</c> call arguments with <c>null</c>.
/// </summary>
/// <remarks>
/// This avoids per-call options allocations while preserving the selected overload. The
/// transformation is intentionally narrow and skips configured options instances.
/// </remarks>
public sealed class JsonSerializerOptionsReuseConverter : ISourceTransformation
{
    /// <inheritdoc />
    public string Name => "CA1869 JsonSerializerOptions Reuse";

    /// <inheritdoc />
    public string Apply(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return source;
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var rewritten = new JsonSerializerOptionsReuseRewriter().Visit(root);

        return rewritten.ToFullString();
    }

    /// <summary>
    /// A syntax rewriter that analyzes and transforms C# code to detect JsonSerializer method invocations and direct JsonSerializerOptions instantiations in order to enable reuse of serializer options.
    /// </summary>
    private sealed class JsonSerializerOptionsReuseRewriter : CSharpSyntaxRewriter
    {
        /// <summary>
        /// This method overrides invocation visiting to replace plain JsonSerializerOptions creation arguments with null literals (preserving trivia) in JsonSerializer calls, returning the original node if unchanged or no such arguments exist.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>A SyntaxNode value produced by this method.</returns>
        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var original = node;
            node = (InvocationExpressionSyntax)base.VisitInvocationExpression(node);

            if (node.ArgumentList is null || node.ArgumentList.Arguments.Count == 0 || !IsJsonSerializerCall(node))
            {
                return node;
            }

            var updatedArgumentList = node.ArgumentList;
            var changed = false;
            var namesOptionsParameter = IsSystemTextJsonSerializer(original);

            for (var i = 0; i < updatedArgumentList.Arguments.Count; i++)
            {
                var argument = updatedArgumentList.Arguments[i];
                if (!IsPlainJsonSerializerOptionsCreation(argument.Expression))
                {
                    continue;
                }

                // The first positional argument is the value, JSON text or stream: the options never come first.
                if (i == 0 && argument.NameColon is null)
                {
                    continue;
                }

                // A positional null is ambiguous between the JsonSerializerOptions and JsonTypeInfo/JsonSerializerContext
                // overloads (CS0121); naming the argument keeps the options overload selected. A named argument followed by
                // a positional one needs C# 7.2, and another JsonSerializer type may name its parameter differently, so
                // there the null is cast to the options type as written instead.
                var nullLiteral = SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
                var replacement = argument.WithExpression(nullLiteral.WithTriviaFrom(argument.Expression));
                if (argument.NameColon is null && (!namesOptionsParameter || IsFollowedByPositionalArgument(updatedArgumentList, i)))
                {
                    var optionsType = ((ObjectCreationExpressionSyntax)argument.Expression).Type.WithoutTrivia();
                    replacement = argument.WithExpression(
                        SyntaxFactory.CastExpression(optionsType, nullLiteral).WithTriviaFrom(argument.Expression));
                }
                else if (argument.NameColon is null)
                {
                    replacement = replacement
                        .WithNameColon(SyntaxFactory.NameColon(SyntaxFactory.IdentifierName("options"))
                                                    .WithLeadingTrivia(argument.Expression.GetLeadingTrivia())
                                                    .WithTrailingTrivia(SyntaxFactory.Space))
                        .WithExpression(replacement.Expression.WithoutLeadingTrivia());
                }

                updatedArgumentList = updatedArgumentList.WithArguments(
                    updatedArgumentList.Arguments.Replace(argument, replacement));
                changed = true;
            }

            return changed ? node.WithArgumentList(updatedArgumentList) : node;
        }

        /// <summary>
        /// Determines whether an argument after the one at <paramref name="index" /> is passed by position.
        /// </summary>
        /// <param name="argumentList">The argument list.</param>
        /// <param name="index">The index of the argument.</param>
        /// <returns>True when a later argument has no name.</returns>
        private static bool IsFollowedByPositionalArgument(ArgumentListSyntax argumentList, int index) => argumentList.Arguments.Skip(index + 1).Any(argument => argument.NameColon is null);

        /// <summary>
        /// Determines whether the given invocation expression is a call to JsonSerializer by checking if its member access receiver is exactly one of the expected fully qualified names, returning false otherwise with no side effects.
        /// </summary>
        /// <param name="invocation">The invocation.</param>
        /// <returns>A bool value produced by this method.</returns>
        private static bool IsJsonSerializerCall(InvocationExpressionSyntax invocation)
        {
            if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
            {
                return false;
            }

            var receiver = memberAccess.Expression.ToString();

            return receiver == "JsonSerializer"
                   || receiver == "System.Text.Json.JsonSerializer"
                   || receiver == "global::System.Text.Json.JsonSerializer";
        }

        /// <summary>
        /// Determines whether the call is known to be on <c>System.Text.Json.JsonSerializer</c>, whose options parameter
        /// is named <c>options</c>: the receiver is qualified with <c>System.Text.Json</c>, or the file imports
        /// <c>System.Text.Json</c> and declares no other type named <c>JsonSerializer</c>.
        /// </summary>
        private static bool IsSystemTextJsonSerializer(InvocationExpressionSyntax invocation)
        {
            var receiver = ((MemberAccessExpressionSyntax)invocation.Expression).Expression.ToString();
            if (receiver != "JsonSerializer")
            {
                return true;
            }

            if (!(invocation.SyntaxTree.GetRoot() is CompilationUnitSyntax root))
            {
                return false;
            }

            return root.DescendantNodes(node => node is CompilationUnitSyntax || node is BaseNamespaceDeclarationSyntax)
                    .OfType<UsingDirectiveSyntax>()
                    .Any(directive => directive.Alias is null &&
                                      directive.StaticKeyword.IsKind(SyntaxKind.None) &&
                                      directive.Name?.ToString() == "System.Text.Json") &&
                !root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Any(type => type.Identifier.ValueText == "JsonSerializer") &&
                !root.DescendantNodes().OfType<DelegateDeclarationSyntax>().Any(type => type.Identifier.ValueText == "JsonSerializer");
        }

        /// <summary>
        /// Determines whether the given expression is a parameterless object creation of JsonSerializerOptions (with no initializer or arguments), returning true only for the specified type name variants and false otherwise.
        /// </summary>
        /// <param name="expression">The expression.</param>
        /// <returns>A bool value produced by this method.</returns>
        private static bool IsPlainJsonSerializerOptionsCreation(ExpressionSyntax expression)
        {
            if (!(expression is ObjectCreationExpressionSyntax creation))
            {
                return false;
            }

            if (creation.Initializer is not null)
            {
                return false;
            }

            if (creation.ArgumentList is not null && creation.ArgumentList.Arguments.Count > 0)
            {
                return false;
            }

            var createdType = creation.Type.ToString();

            return createdType == "JsonSerializerOptions"
                   || createdType == "System.Text.Json.JsonSerializerOptions"
                   || createdType == "global::System.Text.Json.JsonSerializerOptions";
        }
    }
}
