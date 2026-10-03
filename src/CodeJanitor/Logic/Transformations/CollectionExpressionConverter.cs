using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace CodeJanitor.Logic.Transformations;

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
public sealed class CollectionExpressionConverter : ISourceTransformation
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

    /// <summary>
    /// ExpressionRewriter is a syntax rewriter that transforms variable declarations and property declarations using collection initializer, object creation, or array creation expressions into equivalent C# 12 collection expressions for supported list types.
    /// </summary>
    private sealed class CollectionExpressionRewriter : CSharpSyntaxRewriter
    {
        /// <summary>
        /// This method visits a variable declarator, returns it unchanged unless its parent is a variable declaration with an initializer, and if a collection expression conversion succeeds, returns a new node with the initializer value replaced by that collection expression, otherwise returns the original node.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>A SyntaxNode value produced by this method.</returns>
        public override SyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            node = (VariableDeclaratorSyntax)base.VisitVariableDeclarator(node);

            if (!(node.Parent is VariableDeclarationSyntax declaration) || node.Initializer is null)
            {
                return node;
            }

            var replacement = TryConvertToCollectionExpression(declaration.Type, node.Initializer.Value);

            return replacement is null
                ? node
                : node.WithInitializer(node.Initializer.WithValue(replacement));
        }

        /// <summary>
        /// Visits a property declaration, first invoking base traversal, then if the property has an initializer, attempts to convert its value to a collection expression and replaces the initializer with the converted expression when successful, otherwise returns the original node.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>A SyntaxNode value produced by this method.</returns>
        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            node = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node);

            if (node.Initializer is null)
            {
                return node;
            }

            var replacement = TryConvertToCollectionExpression(node.Type, node.Initializer.Value);

            return replacement is null
                ? node
                : node.WithInitializer(node.Initializer.WithValue(replacement));
        }

        /// <summary>
        /// Attempts to convert an object creation, array creation, or implicit array creation initializer into a collection expression when compatible with the declared type, returning null otherwise without side effects.
        /// </summary>
        /// <param name="declaredType">The declared type.</param>
        /// <param name="initializer">The initializer.</param>
        /// <returns>A ExpressionSyntax value produced by this method.</returns>
        private static ExpressionSyntax TryConvertToCollectionExpression(TypeSyntax declaredType, ExpressionSyntax initializer)
        {
            switch (initializer)
            {
                case ObjectCreationExpressionSyntax objectCreation:
                    return TryConvertObjectCreation(declaredType, objectCreation);

                case ArrayCreationExpressionSyntax arrayCreation:
                    return TryConvertArrayCreation(declaredType, arrayCreation);

                case ImplicitArrayCreationExpressionSyntax implicitArrayCreation:
                    // The elements' best type may be a subtype of a reference element type (object[] a = new[] { "a" }
                    // creates a string[]), so only element types without covariant subtypes are converted.
                    return declaredType is ArrayTypeSyntax declaredArrayType &&
                           declaredArrayType.RankSpecifiers[0].Rank == 1 &&
                           implicitArrayCreation.Commas.Count == 0 &&
                           declaredArrayType.ElementType is PredefinedTypeSyntax elementType &&
                           !elementType.Keyword.IsKind(SyntaxKind.ObjectKeyword) &&
                           HasOnlyPlainElements(implicitArrayCreation.Initializer) &&
                           !WouldDiscardComments(implicitArrayCreation, implicitArrayCreation.Initializer)
                        ? BuildCollectionExpression(implicitArrayCreation.Initializer).WithTriviaFrom(implicitArrayCreation)
                        : null;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Attempts to convert an argument-less object creation of a supported list type matching the declared type into a collection expression using its initializer elements, preserving original trivia, and returns null if conversion isn&apos;t applicable.
        /// </summary>
        /// <param name="declaredType">The declared type.</param>
        /// <param name="objectCreation">The object creation.</param>
        /// <returns>A ExpressionSyntax value produced by this method.</returns>
        private static ExpressionSyntax TryConvertObjectCreation(TypeSyntax declaredType, ObjectCreationExpressionSyntax objectCreation)
        {
            if (objectCreation.ArgumentList is not null && objectCreation.ArgumentList.Arguments.Count > 0)
            {
                // e.g. new List<T>(capacity) or new List<T>(otherCollection) - not a plain
                // empty/initializer creation, leave untouched.
                return null;
            }

            if (!IsSupportedListType(objectCreation.Type) || objectCreation.Type.ToString() != declaredType.ToString())
            {
                return null;
            }

            return HasOnlyPlainElements(objectCreation.Initializer) && !WouldDiscardComments(objectCreation, objectCreation.Initializer)
                ? BuildCollectionExpression(objectCreation.Initializer).WithTriviaFrom(objectCreation)
                : null;
        }

        /// <summary>
        /// Attempts to convert an array creation expression to a collection expression only when the declared type is a matching array type and either an initializer is present or the array is explicitly zero-length, otherwise returns null with no side effects.
        /// </summary>
        /// <param name="declaredType">The declared type.</param>
        /// <param name="arrayCreation">The array creation.</param>
        /// <returns>A ExpressionSyntax value produced by this method.</returns>
        private static ExpressionSyntax TryConvertArrayCreation(TypeSyntax declaredType, ArrayCreationExpressionSyntax arrayCreation)
        {
            if (!(declaredType is ArrayTypeSyntax declaredArrayType)
                || declaredArrayType.ElementType.ToString() != arrayCreation.Type.ElementType.ToString()
                || !IsSingleDimensionalArrayOfSameShape(declaredArrayType, arrayCreation.Type))
            {
                return null;
            }

            if (arrayCreation.Initializer is not null)
            {
                return HasOnlyPlainElements(arrayCreation.Initializer) && !WouldDiscardComments(arrayCreation, arrayCreation.Initializer)
                    ? BuildCollectionExpression(arrayCreation.Initializer).WithTriviaFrom(arrayCreation)
                    : null;
            }

            // No initializer - only safe to convert an explicitly zero-length array (e.g.
            // 'new T[0]'), since a sized-but-empty array ('new T[5]') has different semantics.
            var rankSize = arrayCreation.Type.RankSpecifiers.FirstOrDefault()?.Sizes.FirstOrDefault();

            return rankSize is LiteralExpressionSyntax literal && literal.Token.ValueText == "0" &&
                   !WouldDiscardComments(arrayCreation, null)
                ? BuildCollectionExpression(null).WithTriviaFrom(arrayCreation)
                : null;
        }

        /// <summary>
        /// Returns true only if the given type syntax is a generic name (such as `List&lt;T&gt;`) whose base identifier is &quot;List&quot;, performing a pure syntactic check with no side effects or exceptions.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>A bool value produced by this method.</returns>
        private static bool IsSupportedListType(TypeSyntax type) =>
                    type is GenericNameSyntax genericName && genericName.Identifier.ValueText == "List";

        /// <summary>
        /// Determines whether the initializer can be expressed as collection expression elements: member initializers
        /// (<c>Capacity = 5</c>, <c>[0] = 1</c>) and complex element initializers (<c>{ 1, 2 }</c>) have no element form,
        /// and a range element without a start (<c>..3</c>) would read as a spread element (<c>[..3]</c>).
        /// </summary>
        private static bool HasOnlyPlainElements(InitializerExpressionSyntax initializer)
        {
            return initializer is null ||
                !initializer.Expressions.Any(e => e is AssignmentExpressionSyntax || e is InitializerExpressionSyntax ||
                                                  e is RangeExpressionSyntax { LeftOperand: null });
        }

        /// <summary>
        /// Determines whether replacing the creation expression would discard a comment or directive: everything outside
        /// the initializer's braces (and the creation's own leading and trailing trivia) is thrown away.
        /// </summary>
        private static bool WouldDiscardComments(ExpressionSyntax creation, InitializerExpressionSyntax initializer)
        {
            var first = creation.GetFirstToken();
            var last = creation.GetLastToken();

            return creation.DescendantTokens().Any(token =>
                (token != first && HasCommentOutside(token.LeadingTrivia, initializer)) ||
                (token != last && HasCommentOutside(token.TrailingTrivia, initializer)));
        }

        private static bool HasCommentOutside(SyntaxTriviaList trivia, InitializerExpressionSyntax initializer)
        {
            return trivia.Any(t => !t.IsKind(SyntaxKind.WhitespaceTrivia) &&
                !t.IsKind(SyntaxKind.EndOfLineTrivia) &&
                (initializer is null || !initializer.Span.Contains(t.Span)));
        }

        /// <summary>
        /// Builds a collection expression from the initializer's elements (an empty one for <see langword="null" />).
        /// When comments or preprocessor directives appear between the braces, the braces become brackets and every
        /// token and trivia in between is kept; otherwise the elements are joined on one line.
        /// </summary>
        /// <param name="initializer">The initializer, or <see langword="null" /> for an empty collection.</param>
        /// <returns>The collection expression.</returns>
        private static ExpressionSyntax BuildCollectionExpression(InitializerExpressionSyntax initializer)
        {
            var elements = initializer?.Expressions ?? default;

            if (initializer is not null && initializer.DescendantTrivia().Any(trivia =>
                    initializer.Span.Contains(trivia.Span) &&
                    !trivia.IsKind(SyntaxKind.WhitespaceTrivia) &&
                    !trivia.IsKind(SyntaxKind.EndOfLineTrivia)))
            {
                var elementsWithSeparators = elements.GetWithSeparators().Select(item => item.IsNode
                    ? (SyntaxNodeOrToken)SyntaxFactory.ExpressionElement((ExpressionSyntax)item.AsNode())
                    : item);

                return SyntaxFactory.CollectionExpression(
                    SyntaxFactory.Token(SyntaxKind.OpenBracketToken).WithTrailingTrivia(initializer.OpenBraceToken.TrailingTrivia),
                    SyntaxFactory.SeparatedList<CollectionElementSyntax>(elementsWithSeparators),
                    SyntaxFactory.Token(SyntaxKind.CloseBracketToken).WithLeadingTrivia(initializer.CloseBraceToken.LeadingTrivia));
            }

            var elementsText = string.Join(", ", elements.Select(e => e.ToString()));

            return SyntaxFactory.ParseExpression("[" + elementsText + "]");
        }

        /// <summary>
        /// Determines whether the declared array type can be the target of a collection expression built from the
        /// created array: a single-dimensional array whose ranks (including those of jagged element arrays) match.
        /// </summary>
        private static bool IsSingleDimensionalArrayOfSameShape(ArrayTypeSyntax declaredArrayType, ArrayTypeSyntax createdArrayType)
        {
            return declaredArrayType.RankSpecifiers[0].Rank == 1 &&
                declaredArrayType.RankSpecifiers.Select(r => r.Rank).SequenceEqual(createdArrayType.RankSpecifiers.Select(r => r.Rank));
        }
    }
}
