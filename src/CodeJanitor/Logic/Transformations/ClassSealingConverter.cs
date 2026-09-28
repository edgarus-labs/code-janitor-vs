using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Adds the <c>sealed</c> modifier to the top-level classes and record classes of a C# file that the semantic model
/// proves are neither designed nor used for inheritance anywhere in the solution.
/// </summary>
/// <remarks>
/// A class is sealed only when all of these hold:
/// <list type="bullet">
/// <item>it is not already sealed, abstract, static or partial;</item>
/// <item>it declares no virtual or abstract member;</item>
/// <item>it declares no <c>protected</c>, <c>protected internal</c> or <c>private protected</c> member, constructors,
/// accessors, fields and nested types included (overrides excepted): such members only make sense for derived
/// classes, and sealing would report CS0628;</item>
/// <item>no class of the solution derives from it;</item>
/// <item>no generic type constraint of the solution names it (a sealed type in a constraint is CS0701).</item>
/// </list>
/// A file compiled by several projects or target frameworks is analyzed in each of them, and a class is sealed only
/// when it is safe in every one. Classes outside the solution (other repositories, published packages) cannot be
/// seen: sealing a public class remains an API change, which is why the cleanup setting is an explicit opt-in.
/// Pure logic over Roslyn workspace types, unit-testable without Visual Studio.
/// </remarks>
public sealed class ClassSealingConverter
{
    /// <summary>
    /// Seals the classes of the file that are safe to seal in every document of <paramref name="documents" />.
    /// </summary>
    /// <param name="documents">The documents of one file, one per project flavor compiling it, all with the same text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The text of the file with the safe classes sealed, or its unchanged text when none is.</returns>
    public async Task<string> SealWhenSafeAsync(IReadOnlyList<Document> documents, CancellationToken cancellationToken)
    {
        if (documents is null || documents.Count == 0)
        {
            throw new ArgumentException("At least one document is required.", nameof(documents));
        }

        var root = await documents[0].GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var candidates = GetCandidates(root).Select(declaration => declaration.SpanStart).ToList();

        foreach (var document in documents)
        {
            if (candidates.Count == 0)
            {
                break;
            }

            var safeInDocument = await GetSafeToSealAsync(document, cancellationToken).ConfigureAwait(false);
            candidates.RemoveAll(position => !safeInDocument.Contains(position));
        }

        if (candidates.Count == 0)
        {
            return root.ToFullString();
        }

        var toSeal = GetCandidates(root).Where(declaration => candidates.Contains(declaration.SpanStart)).ToList();

        return root.ReplaceNodes(toSeal, (original, _) => WithSealedModifier(original)).ToFullString();
    }

    /// <summary>
    /// Gets the start positions of the candidate declarations of <paramref name="document" /> that are safe to seal
    /// in its project.
    /// </summary>
    private static async Task<HashSet<int>> GetSafeToSealAsync(Document document, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var solution = document.Project.Solution;
        var safe = new HashSet<int>();

        foreach (var declaration in GetCandidates(root))
        {
            if (semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is INamedTypeSymbol type &&
                IsDeclaredForSealing(type) &&
                !await IsInheritedOrConstrainedAsync(type, solution, cancellationToken).ConfigureAwait(false))
            {
                safe.Add(declaration.SpanStart);
            }
        }

        return safe;
    }

    /// <summary>
    /// Gets the top-level class and record class declarations that the modifiers written on them allow to seal.
    /// </summary>
    private static IEnumerable<TypeDeclarationSyntax> GetCandidates(SyntaxNode root) =>
        root.DescendantNodes(node => node is CompilationUnitSyntax || node is BaseNamespaceDeclarationSyntax)
            .OfType<TypeDeclarationSyntax>()
            .Where(declaration =>
                (declaration is ClassDeclarationSyntax ||
                 (declaration is RecordDeclarationSyntax record && !record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword))) &&
                !declaration.Modifiers.Any(modifier =>
                    modifier.IsKind(SyntaxKind.SealedKeyword) ||
                    modifier.IsKind(SyntaxKind.AbstractKeyword) ||
                    modifier.IsKind(SyntaxKind.StaticKeyword) ||
                    modifier.IsKind(SyntaxKind.PartialKeyword)));

    /// <summary>
    /// Determines whether the members the class declares show it is not designed for inheritance. Members the compiler
    /// declares implicitly (for example the virtual equality members of a record) are ignored: sealing adjusts them.
    /// </summary>
    private static bool IsDeclaredForSealing(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class || type.IsSealed || type.IsAbstract || type.IsStatic || type.DeclaringSyntaxReferences.Length != 1)
        {
            return false;
        }

        return !type.GetMembers().Any(member =>
            !member.IsImplicitlyDeclared &&
            (member.IsVirtual || member.IsAbstract || (!member.IsOverride && IsProtected(member.DeclaredAccessibility))));
    }

    private static bool IsProtected(Accessibility accessibility) =>
        accessibility == Accessibility.Protected ||
        accessibility == Accessibility.ProtectedOrInternal ||
        accessibility == Accessibility.ProtectedAndInternal;

    /// <summary>
    /// Determines whether a class of the solution derives from <paramref name="type" /> or a generic type constraint of
    /// the solution names it.
    /// </summary>
    private static async Task<bool> IsInheritedOrConstrainedAsync(INamedTypeSymbol type, Solution solution, CancellationToken cancellationToken)
    {
        var derived = await SymbolFinder.FindDerivedClassesAsync(type, solution, transitive: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (derived.Any())
        {
            return true;
        }

        var references = await SymbolFinder.FindReferencesAsync(type, solution, cancellationToken).ConfigureAwait(false);
        foreach (var location in references.SelectMany(reference => reference.Locations))
        {
            var referenceRoot = await location.Document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var node = referenceRoot.FindNode(location.Location.SourceSpan, getInnermostNodeForTie: true);
            if (node.AncestorsAndSelf().Any(ancestor => ancestor is TypeConstraintSyntax))
            {
                return true;
            }
        }

        return false;
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
