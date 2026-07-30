using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeJanitor.Logic.Transformations
{
    /// <summary>
    /// Adds the <c>readonly</c> modifier to fields only when provably safe to do so from a single
    /// syntax tree, without a full solution-wide semantic analysis (see ADR-0007).
    /// </summary>
    /// <remarks>
    /// Scope is intentionally conservative: only <c>private</c> fields of non-partial types with a
    /// single declarator are considered, and only when every write to the field occurs directly in
    /// the declaring type's own constructor (instance fields) or static constructor (static
    /// fields) - never in a regular method, accessor, local function, or nested lambda, since those
    /// could execute after construction. Pure logic, unit-testable without Visual Studio.
    /// </remarks>
    public class ReadonlyFieldConverter : IFieldMutabilityConverter, ISourceTransformation
    {
        /// <inheritdoc />
        public string Name => "Readonly Field";

        /// <inheritdoc />
        public string Apply(string source) => AddReadonlyWhenSafe(source);

        /// <inheritdoc />
        public string AddReadonlyWhenSafe(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source;
            }

            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetRoot();

            var fieldsToConvert = new List<FieldDeclarationSyntax>();

            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (typeDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                {
                    continue;
                }

                foreach (var fieldDecl in typeDecl.Members.OfType<FieldDeclarationSyntax>())
                {
                    if (IsSafeToMakeReadonly(typeDecl, fieldDecl))
                    {
                        fieldsToConvert.Add(fieldDecl);
                    }
                }
            }

            if (fieldsToConvert.Count == 0)
            {
                return source;
            }

            var newRoot = root.ReplaceNodes(fieldsToConvert, (original, _) => WithReadonlyModifier(original));
            return newRoot.ToFullString();
        }

        private static bool IsSafeToMakeReadonly(TypeDeclarationSyntax typeDecl, FieldDeclarationSyntax fieldDecl)
        {
            if (fieldDecl.Declaration.Variables.Count != 1)
            {
                return false;
            }

            var modifiers = fieldDecl.Modifiers;
            if (modifiers.Any(m => m.IsKind(SyntaxKind.ReadOnlyKeyword) ||
                                    m.IsKind(SyntaxKind.ConstKeyword) ||
                                    m.IsKind(SyntaxKind.VolatileKeyword)))
            {
                return false;
            }

            // Only private (explicit or implicit) fields: external writes to
            // public/internal/protected fields cannot be ruled out from a single file.
            if (modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword) ||
                                    m.IsKind(SyntaxKind.InternalKeyword) ||
                                    m.IsKind(SyntaxKind.ProtectedKeyword)))
            {
                return false;
            }

            var isStatic = modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
            var fieldName = fieldDecl.Declaration.Variables[0].Identifier.Text;

            var scopeNodes = typeDecl.DescendantNodes(n => n == typeDecl || !(n is TypeDeclarationSyntax));

            var writeNodes = new List<SyntaxNode>();

            foreach (var assignment in scopeNodes.OfType<AssignmentExpressionSyntax>())
            {
                if (IsFieldReference(assignment.Left, fieldName))
                {
                    writeNodes.Add(assignment);
                }
            }

            foreach (var unary in scopeNodes.OfType<PostfixUnaryExpressionSyntax>())
            {
                if ((unary.IsKind(SyntaxKind.PostIncrementExpression) || unary.IsKind(SyntaxKind.PostDecrementExpression)) &&
                    IsFieldReference(unary.Operand, fieldName))
                {
                    writeNodes.Add(unary);
                }
            }

            foreach (var unary in scopeNodes.OfType<PrefixUnaryExpressionSyntax>())
            {
                if ((unary.IsKind(SyntaxKind.PreIncrementExpression) || unary.IsKind(SyntaxKind.PreDecrementExpression)) &&
                    IsFieldReference(unary.Operand, fieldName))
                {
                    writeNodes.Add(unary);
                }
            }

            foreach (var argument in scopeNodes.OfType<ArgumentSyntax>())
            {
                if ((argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) &&
                    IsFieldReference(argument.Expression, fieldName))
                {
                    writeNodes.Add(argument);
                }
            }

            foreach (var writeNode in writeNodes)
            {
                if (!IsWriteInMatchingConstructor(writeNode, typeDecl, isStatic))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsFieldReference(ExpressionSyntax expression, string fieldName)
        {
            if (expression is IdentifierNameSyntax identifier)
            {
                return identifier.Identifier.Text == fieldName;
            }

            if (expression is MemberAccessExpressionSyntax memberAccess && memberAccess.Expression is ThisExpressionSyntax)
            {
                return memberAccess.Name.Identifier.Text == fieldName;
            }

            return false;
        }

        private static bool IsWriteInMatchingConstructor(SyntaxNode writeNode, TypeDeclarationSyntax typeDecl, bool isStatic)
        {
            foreach (var ancestor in writeNode.Ancestors())
            {
                switch (ancestor)
                {
                    case AnonymousFunctionExpressionSyntax _:
                    case LocalFunctionStatementSyntax _:
                    case MethodDeclarationSyntax _:
                    case AccessorDeclarationSyntax _:
                    case DestructorDeclarationSyntax _:
                    case OperatorDeclarationSyntax _:
                    case ConversionOperatorDeclarationSyntax _:
                        // Any of these boundaries reached before a constructor means the write
                        // could execute after construction (or in an unrelated member) - unsafe.
                        return false;

                    case ConstructorDeclarationSyntax constructor:
                        var constructorIsStatic = constructor.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
                        return ReferenceEquals(constructor.Parent, typeDecl) && constructorIsStatic == isStatic;

                    case TypeDeclarationSyntax _:
                        // Reached the type boundary (e.g. a field initializer) without finding a
                        // constructor context - conservatively unsafe.
                        return false;
                }
            }

            return false;
        }

        private static FieldDeclarationSyntax WithReadonlyModifier(FieldDeclarationSyntax fieldDecl)
        {
            var readonlyToken = SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword).WithTrailingTrivia(SyntaxFactory.Space);
            return fieldDecl.WithModifiers(fieldDecl.Modifiers.Add(readonlyToken));
        }
    }
}
