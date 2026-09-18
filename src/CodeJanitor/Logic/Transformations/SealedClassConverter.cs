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

public sealed class SealedClassConverter : IClassSealingConverter, ISourceTransformation
{
    private readonly IReadOnlyCollection<string> _externalDisqualifiedTypeNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="SealedClassConverter" /> class.
    /// </summary>
    public SealedClassConverter()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SealedClassConverter" /> class with external disqualified type names.
    /// </summary>
    /// <param name="externalDisqualifiedTypeNames">Types that cannot be sealed (e.g. from solution-wide analysis).</param>
    public SealedClassConverter(IReadOnlyCollection<string> externalDisqualifiedTypeNames)
    {
        _externalDisqualifiedTypeNames = externalDisqualifiedTypeNames;
    }

    /// <inheritdoc />
    public string Name => "Sealed Class";

    /// <inheritdoc />
    public string Apply(string source) => SealWhenSafe(source, _externalDisqualifiedTypeNames);

    /// <inheritdoc />
    public string SealWhenSafe(string source) => SealWhenSafe(source, null);

    /// <inheritdoc />
    public string SealWhenSafe(string source, IReadOnlyCollection<string> externalDisqualifiedTypeNames)
    {
        if (string.IsNullOrEmpty(source))
        {
            return source;
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var disqualifiedTypeNames = new HashSet<string>(
            root.DescendantNodes()
                .OfType<BaseListSyntax>()
                .SelectMany(b => b.Types)
                .Select(t => GetSimpleName(t.Type)));

        foreach (var constraintType in root.DescendantNodes()
                     .OfType<TypeParameterConstraintClauseSyntax>()
                     .SelectMany(c => c.Constraints)
                     .OfType<TypeConstraintSyntax>()
                     .Select(tc => GetSimpleName(tc.Type)))
        {
            disqualifiedTypeNames.Add(constraintType);
        }

        if (externalDisqualifiedTypeNames != null)
        {
            foreach (var name in externalDisqualifiedTypeNames)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    disqualifiedTypeNames.Add(name);
                }
            }
        }

        var typesToSeal = new List<TypeDeclarationSyntax>();

        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (IsTypeEligibleForSealing(typeDecl) && IsTopLevel(typeDecl) && IsSafeToSeal(typeDecl, disqualifiedTypeNames))
            {
                typesToSeal.Add(typeDecl);
            }
        }

        if (typesToSeal.Count == 0)
        {
            return source;
        }

        var newRoot = root.ReplaceNodes(typesToSeal, (original, _) => WithSealedModifier(original));

        return newRoot.ToFullString();
    }

    /// <summary>
    /// Checks if the type declaration is a class or non-struct record.
    /// </summary>
    private static bool IsTypeEligibleForSealing(TypeDeclarationSyntax typeDecl)
    {
        if (typeDecl is ClassDeclarationSyntax)
        {
            return true;
        }

        if (typeDecl is RecordDeclarationSyntax recordDecl)
        {
            return !recordDecl.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword);
        }

        return false;
    }

    /// <summary>
    /// Determines if a type declaration is top-level by returning true when its parent is a compilation unit, a namespace declaration, or a file-scoped namespace declaration.
    /// </summary>
    private static bool IsTopLevel(TypeDeclarationSyntax typeDecl)
    {
        return typeDecl.Parent is CompilationUnitSyntax ||
               typeDecl.Parent is NamespaceDeclarationSyntax ||
               typeDecl.Parent is FileScopedNamespaceDeclarationSyntax;
    }

    /// <summary>
    /// Determines if a type can be safely sealed by returning false for any type with sealed, abstract, static, or partial modifiers, or for types listed in derivedFromNames, and true otherwise.
    /// </summary>
    private static bool IsSafeToSeal(TypeDeclarationSyntax typeDecl, HashSet<string> disqualifiedTypeNames)
    {
        var modifiers = typeDecl.Modifiers;

        if (modifiers.Any(m => m.IsKind(SyntaxKind.SealedKeyword) ||
                                m.IsKind(SyntaxKind.AbstractKeyword) ||
                                m.IsKind(SyntaxKind.StaticKeyword) ||
                                m.IsKind(SyntaxKind.PartialKeyword)))
        {
            return false;
        }

        if (HasVirtualMembers(typeDecl))
        {
            return false;
        }

        return !disqualifiedTypeNames.Contains(typeDecl.Identifier.Text);
    }

    /// <summary>
    /// Checks whether the type declaration declares any virtual member (methods, properties, indexers, or events).
    /// </summary>
    private static bool HasVirtualMembers(TypeDeclarationSyntax typeDecl)
    {
        return typeDecl.Members.Any(member =>
        {
            switch (member)
            {
                case MethodDeclarationSyntax method:
                    return method.Modifiers.Any(m => m.IsKind(SyntaxKind.VirtualKeyword));

                case PropertyDeclarationSyntax property:
                    return property.Modifiers.Any(m => m.IsKind(SyntaxKind.VirtualKeyword));

                case IndexerDeclarationSyntax indexer:
                    return indexer.Modifiers.Any(m => m.IsKind(SyntaxKind.VirtualKeyword));

                case EventDeclarationSyntax evt:
                    return evt.Modifiers.Any(m => m.IsKind(SyntaxKind.VirtualKeyword));

                case EventFieldDeclarationSyntax evtField:
                    return evtField.Modifiers.Any(m => m.IsKind(SyntaxKind.VirtualKeyword));

                default:
                    return false;
            }
        });
    }

    /// <summary>
    /// Recursively extracts the rightmost simple identifier from a TypeSyntax.
    /// </summary>
    private static string GetSimpleName(TypeSyntax type)
    {
        switch (type)
        {
            case SimpleNameSyntax simple:
                return simple.Identifier.Text;

            case QualifiedNameSyntax qualified:
                return GetSimpleName(qualified.Right);

            case AliasQualifiedNameSyntax alias:
                return GetSimpleName(alias.Name);

            default:
                return type.ToString();
        }
    }

    /// <summary>
    /// Adds a sealed modifier to the given type declaration.
    /// </summary>
    private static TypeDeclarationSyntax WithSealedModifier(TypeDeclarationSyntax typeDecl)
    {
        var sealedToken = SyntaxFactory.Token(SyntaxKind.SealedKeyword).WithTrailingTrivia(SyntaxFactory.Space);

        if (typeDecl.Modifiers.Count == 0)
        {
            sealedToken = sealedToken.WithLeadingTrivia(typeDecl.Keyword.LeadingTrivia);
            var newKeyword = typeDecl.Keyword.WithLeadingTrivia(SyntaxTriviaList.Empty);

            return typeDecl.WithModifiers(SyntaxFactory.TokenList(sealedToken)).WithKeyword(newKeyword);
        }

        return typeDecl.WithModifiers(typeDecl.Modifiers.Add(sealedToken));
    }
}
