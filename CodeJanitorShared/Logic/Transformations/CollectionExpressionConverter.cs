using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace CodeJanitor.Logic.Transformations
{
    /// <summary>
    /// Converts <c>List&lt;T&gt;</c> and array initializations to the C# 12 collection expression
    /// syntax (<c>[]</c> / <c>[a, b, c]</c>) when the declared type is explicit and textually
    /// matches the created type.
    /// </summary>
    /// <remarks>
    /// Uses a textual type match (rather than the semantic model) so the transformation is safe
    /// without a full compilation, mirroring <see cref="VarWhenApparentConverter" /> (see ADR-0007).
    /// Pure logic, unit-testable without Visual Studio.
    /// </remarks>
    public class CollectionExpressionConverter : ISourceTransformation
    {
        /// <inheritdoc />
        public string Name => "Collection Expression";

        /// <inheritdoc />
        public string Apply(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source;
            }

            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetRoot();
            var rewritten = new CollectionExpressionRewriter().Visit(root);
            return rewritten.ToFullString();
        }

        private sealed class CollectionExpressionRewriter : CSharpSyntaxRewriter
        {
            public override SyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node)
            {
                node = (VariableDeclaratorSyntax)base.VisitVariableDeclarator(node);

                if (!(node.Parent is VariableDeclarationSyntax declaration) || node.Initializer == null)
                {
                    return node;
                }

                var replacement = TryConvertToCollectionExpression(declaration.Type, node.Initializer.Value);
                return replacement == null
                    ? node
                    : node.WithInitializer(node.Initializer.WithValue(replacement));
            }

            public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
            {
                node = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node);

                if (node.Initializer == null)
                {
                    return node;
                }

                var replacement = TryConvertToCollectionExpression(node.Type, node.Initializer.Value);
                return replacement == null
                    ? node
                    : node.WithInitializer(node.Initializer.WithValue(replacement));
            }

            private static ExpressionSyntax TryConvertToCollectionExpression(TypeSyntax declaredType, ExpressionSyntax initializer)
            {
                switch (initializer)
                {
                    case ObjectCreationExpressionSyntax objectCreation:
                        return TryConvertObjectCreation(declaredType, objectCreation);

                    case ArrayCreationExpressionSyntax arrayCreation:
                        return TryConvertArrayCreation(declaredType, arrayCreation);

                    case ImplicitArrayCreationExpressionSyntax implicitArrayCreation:
                        return declaredType is ArrayTypeSyntax
                            ? BuildCollectionExpression(implicitArrayCreation.Initializer.Expressions).WithTriviaFrom(implicitArrayCreation)
                            : null;

                    default:
                        return null;
                }
            }

            private static ExpressionSyntax TryConvertObjectCreation(TypeSyntax declaredType, ObjectCreationExpressionSyntax objectCreation)
            {
                if (objectCreation.ArgumentList != null && objectCreation.ArgumentList.Arguments.Count > 0)
                {
                    // e.g. new List<T>(capacity) or new List<T>(otherCollection) - not a plain
                    // empty/initializer creation, leave untouched.
                    return null;
                }

                if (!IsSupportedListType(objectCreation.Type) || objectCreation.Type.ToString() != declaredType.ToString())
                {
                    return null;
                }

                var elements = objectCreation.Initializer?.Expressions ?? default;
                return BuildCollectionExpression(elements).WithTriviaFrom(objectCreation);
            }

            private static ExpressionSyntax TryConvertArrayCreation(TypeSyntax declaredType, ArrayCreationExpressionSyntax arrayCreation)
            {
                if (!(declaredType is ArrayTypeSyntax declaredArrayType)
                    || declaredArrayType.ElementType.ToString() != arrayCreation.Type.ElementType.ToString())
                {
                    return null;
                }

                if (arrayCreation.Initializer != null)
                {
                    return BuildCollectionExpression(arrayCreation.Initializer.Expressions).WithTriviaFrom(arrayCreation);
                }

                // No initializer - only safe to convert an explicitly zero-length array (e.g.
                // 'new T[0]'), since a sized-but-empty array ('new T[5]') has different semantics.
                var rankSize = arrayCreation.Type.RankSpecifiers.FirstOrDefault()?.Sizes.FirstOrDefault();
                return rankSize is LiteralExpressionSyntax literal && literal.Token.ValueText == "0"
                    ? BuildCollectionExpression(default).WithTriviaFrom(arrayCreation)
                    : null;
            }

            private static bool IsSupportedListType(TypeSyntax type) =>
                type is GenericNameSyntax genericName && genericName.Identifier.ValueText == "List";

            private static ExpressionSyntax BuildCollectionExpression(SeparatedSyntaxList<ExpressionSyntax> elements)
            {
                var elementsText = string.Join(", ", elements.Select(e => e.ToString()));
                return SyntaxFactory.ParseExpression("[" + elementsText + "]");
            }
        }
    }
}
