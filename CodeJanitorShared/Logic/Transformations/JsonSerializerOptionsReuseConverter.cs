using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeJanitor.Logic.Transformations
{
    /// <summary>
    /// Applies a conservative CA1869-style optimization by replacing direct allocations of
    /// <c>JsonSerializerOptions</c> in <c>JsonSerializer.*</c> call arguments with <c>null</c>.
    /// </summary>
    /// <remarks>
    /// This avoids per-call options allocations while preserving the selected overload. The
    /// transformation is intentionally narrow and skips configured options instances.
    /// </remarks>
    public class JsonSerializerOptionsReuseConverter : ISourceTransformation
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

        private sealed class JsonSerializerOptionsReuseRewriter : CSharpSyntaxRewriter
        {
            public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                node = (InvocationExpressionSyntax)base.VisitInvocationExpression(node);

                if (node.ArgumentList == null || node.ArgumentList.Arguments.Count == 0 || !IsJsonSerializerCall(node))
                {
                    return node;
                }

                var updatedArgumentList = node.ArgumentList;
                var changed = false;

                for (var i = 0; i < updatedArgumentList.Arguments.Count; i++)
                {
                    var argument = updatedArgumentList.Arguments[i];
                    if (!IsPlainJsonSerializerOptionsCreation(argument.Expression))
                    {
                        continue;
                    }

                    var replacement = argument.WithExpression(
                        SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
                                     .WithTriviaFrom(argument.Expression));

                    updatedArgumentList = updatedArgumentList.WithArguments(
                        updatedArgumentList.Arguments.Replace(argument, replacement));
                    changed = true;
                }

                return changed ? node.WithArgumentList(updatedArgumentList) : node;
            }

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

            private static bool IsPlainJsonSerializerOptionsCreation(ExpressionSyntax expression)
            {
                if (!(expression is ObjectCreationExpressionSyntax creation))
                {
                    return false;
                }

                if (creation.Initializer != null)
                {
                    return false;
                }

                if (creation.ArgumentList != null && creation.ArgumentList.Arguments.Count > 0)
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
}
