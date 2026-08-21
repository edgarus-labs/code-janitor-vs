using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Adds the <c>sealed</c> modifier to classes only when provably safe to do so from a single
/// syntax tree, without a full solution-wide semantic analysis (see ADR-0007).
/// </summary>
/// <remarks>
/// Sealing changes the public API surface (CA1852), so this is intentionally conservative:
/// only top-level classes that cannot be inherited from outside the assembly (no explicit
/// <c>public</c>/<c>protected</c> modifier) are considered, and only when no other type
/// declared in the same file derives from them. Nested types and partial classes are left
/// untouched. This cannot detect derived types declared in other files, which is why the
/// corresponding cleanup setting remains an explicit opt-in. Pure logic, unit-testable without
/// Visual Studio.
/// </remarks>

public class SealedClassConverter : IClassSealingConverter, ISourceTransformation
{
    /// <inheritdoc />
    public string Name => "Sealed Class";

    /// <inheritdoc />

    public string Apply(string source) => SealWhenSafe(source);

    /// <inheritdoc />

    public string SealWhenSafe(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return source;
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var derivedFromNames = new HashSet<string>(
            root.DescendantNodes()
                .OfType<BaseListSyntax>()
                .SelectMany(b => b.Types)
                .Select(t => GetSimpleName(t.Type)));

        var classesToSeal = new List<ClassDeclarationSyntax>();

        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (IsTopLevel(classDecl) && IsSafeToSeal(classDecl, derivedFromNames))
            {
                classesToSeal.Add(classDecl);
            }
        }

        if (classesToSeal.Count == 0)
        {
            return source;
        }

        var newRoot = root.ReplaceNodes(classesToSeal, (original, _) => WithSealedModifier(original));

        return newRoot.ToFullString();
    }

    /// <summary>
    /// Determines if a class declaration is top-level by returning true when its parent is a compilation unit, a namespace declaration, or a file-scoped namespace declaration, with no side effects or exceptions.
    /// </summary>
    /// <param name="classDecl">The class decl.</param>
    /// <returns>A bool value produced by this method.</returns>

    private static bool IsTopLevel(ClassDeclarationSyntax classDecl)
    {
        return classDecl.Parent is CompilationUnitSyntax ||
               classDecl.Parent is NamespaceDeclarationSyntax ||
               classDecl.Parent is FileScopedNamespaceDeclarationSyntax;
    }

    /// <summary>
    /// Determines if a class can be safely sealed by returning false for any class with sealed, abstract, static, or partial modifiers, for public or protected classes, or for classes listed in derivedFromNames, and true otherwise, with no side effects.
    /// </summary>
    /// <param name="classDecl">The class decl.</param>
    /// <param name="derivedFromNames">The derived from names.</param>
    /// <returns>A bool value produced by this method.</returns>

    private static bool IsSafeToSeal(ClassDeclarationSyntax classDecl, HashSet<string> derivedFromNames)
    {
        var modifiers = classDecl.Modifiers;

        if (modifiers.Any(m => m.IsKind(SyntaxKind.SealedKeyword) ||
                                m.IsKind(SyntaxKind.AbstractKeyword) ||
                                m.IsKind(SyntaxKind.StaticKeyword) ||
                                m.IsKind(SyntaxKind.PartialKeyword)))
        {
            return false;
        }

        // Only classes that cannot be inherited from outside the assembly: sealing a
        // public/protected type would be a breaking API change (CA1852).
        if (modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword)))
        {
            return false;
        }

        return !derivedFromNames.Contains(classDecl.Identifier.Text);
    }

    /// <summary>
    /// Recursively extracts the rightmost simple identifier from a TypeSyntax, falling back to the full type string for other syntax forms, with no side effects.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string GetSimpleName(TypeSyntax type)
    {
        switch (type)
        {
            case SimpleNameSyntax simple:
                return simple.Identifier.Text;

            case QualifiedNameSyntax qualified:
                return GetSimpleName(qualified.Right);

            default:
                return type.ToString();
        }
    }

    /// <summary>
    /// Adds a sealed modifier to the given class declaration, preserving leading trivia by moving it from the class keyword when no modifiers exist, otherwise appending the sealed token to the existing modifier list.
    /// </summary>
    /// <param name="classDecl">The class decl.</param>
    /// <returns>A ClassDeclarationSyntax value produced by this method.</returns>

    private static ClassDeclarationSyntax WithSealedModifier(ClassDeclarationSyntax classDecl)
    {
        var sealedToken = SyntaxFactory.Token(SyntaxKind.SealedKeyword).WithTrailingTrivia(SyntaxFactory.Space);

        if (classDecl.Modifiers.Count == 0)
        {
            // Move the class keyword's leading trivia (e.g. indentation) to the new modifier,
            // since it now becomes the first token of the declaration.
            sealedToken = sealedToken.WithLeadingTrivia(classDecl.Keyword.LeadingTrivia);
            var newKeyword = classDecl.Keyword.WithLeadingTrivia(SyntaxTriviaList.Empty);

            return classDecl.WithModifiers(SyntaxFactory.TokenList(sealedToken)).WithKeyword(newKeyword);
        }

        return classDecl.WithModifiers(classDecl.Modifiers.Add(sealedToken));
    }
}
