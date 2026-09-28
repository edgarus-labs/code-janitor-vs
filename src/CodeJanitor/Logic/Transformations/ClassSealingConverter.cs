using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Operations;
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
/// <item>no generic type constraint of the solution names it (a sealed type in a constraint is CS0701);</item>
/// <item>no cast, <c>as</c>, <c>is</c>, type pattern or <c>foreach</c> of the solution converts it to or from an
/// interface it does not implement (sealing removes that explicit conversion);</item>
/// <item>its declaration contains no code excluded by a preprocessor directive, and no such code of the solution
/// names it (the semantic model cannot see inactive code, which other build configurations compile).</item>
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
        var inactiveIdentifiers = await GetInactiveCodeIdentifiersAsync(solution, cancellationToken).ConfigureAwait(false);
        var sealable = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);

        foreach (var declaration in GetCandidates(root))
        {
            if (!ContainsInactiveCode(declaration) &&
                semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is INamedTypeSymbol type &&
                !inactiveIdentifiers.Contains(type.Name) &&
                IsDeclaredForSealing(type) &&
                !await IsInheritedOrConstrainedAsync(type, solution, cancellationToken).ConfigureAwait(false))
            {
                sealable[type] = declaration.SpanStart;
            }
        }

        if (sealable.Count > 0)
        {
            var converted = await GetExplicitlyConvertedWithInterfacesAsync(sealable.Keys.ToList(), document.Project, cancellationToken).ConfigureAwait(false);
            foreach (var type in converted)
            {
                sealable.Remove(type);
            }
        }

        return new HashSet<int>(sealable.Values);
    }

    /// <summary>
    /// Determines whether the declaration contains code excluded by a preprocessor directive (<c>#if</c>/<c>#elif</c>/
    /// <c>#else</c> branches inactive in this project flavor). The semantic model cannot see that code, which other
    /// build configurations compile and which may declare members that forbid sealing.
    /// </summary>
    private static bool ContainsInactiveCode(TypeDeclarationSyntax declaration) =>
        declaration.DescendantTrivia(declaration.Span, descendIntoTrivia: true)
            .Any(trivia => trivia.IsKind(SyntaxKind.DisabledTextTrivia));

    /// <summary>
    /// Gets the identifiers that occur in code excluded by a preprocessor directive anywhere in the solution. Such code
    /// is compiled in other build configurations and may derive from, constrain or convert a class the semantic model
    /// sees as safe to seal.
    /// </summary>
    private static async Task<HashSet<string>> GetInactiveCodeIdentifiersAsync(Solution solution, CancellationToken cancellationToken)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var document in solution.Projects.SelectMany(project => project.Documents))
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root is null || !root.ContainsDirectives)
            {
                continue;
            }

            foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
            {
                if (!trivia.IsKind(SyntaxKind.DisabledTextTrivia))
                {
                    continue;
                }

                foreach (var token in SyntaxFactory.ParseTokens(trivia.ToString()))
                {
                    if (token.IsKind(SyntaxKind.IdentifierToken))
                    {
                        identifiers.Add(token.ValueText);
                    }
                }
            }
        }

        return identifiers;
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
    /// Gets the <paramref name="candidates" /> that a cast, <c>as</c>, <c>is</c>, type pattern or <c>foreach</c>
    /// iteration variable of <paramref name="project" /> or of a project depending on it explicitly converts to or from
    /// an interface the class does not implement. That explicit reference conversion exists only while the class is
    /// not sealed: sealing would report CS0030, CS0039 or CS8121, or turn an <c>is</c> test into CS0184.
    /// </summary>
    private static async Task<List<INamedTypeSymbol>> GetExplicitlyConvertedWithInterfacesAsync(
        IReadOnlyCollection<INamedTypeSymbol> candidates,
        Project project,
        CancellationToken cancellationToken)
    {
        var solution = project.Solution;
        var projectIds = new List<ProjectId> { project.Id };
        projectIds.AddRange(solution.GetProjectDependencyGraph().GetProjectsThatTransitivelyDependOnThisProject(project.Id));
        var converted = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var scanned in projectIds.Select(solution.GetProject).Where(scanned => scanned?.Language == LanguageNames.CSharp))
        {
            if (!(await scanned.GetCompilationAsync(cancellationToken).ConfigureAwait(false) is CSharpCompilation compilation))
            {
                continue;
            }

            // The candidate as seen by this compilation, mapped back to the candidate of the analyzed project.
            var candidateInCompilation = new Dictionary<INamedTypeSymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var candidate in candidates.Where(candidate => !converted.Contains(candidate)))
            {
                var similar = SymbolFinder.FindSimilarSymbols(candidate, compilation, cancellationToken).FirstOrDefault();
                if (similar != null)
                {
                    candidateInCompilation[similar.OriginalDefinition] = candidate;
                }
            }

            if (candidateInCompilation.Count == 0)
            {
                continue;
            }

            foreach (var document in scanned.Documents)
            {
                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                SemanticModel semanticModel = null;

                foreach (var node in root.DescendantNodes())
                {
                    if (!IsExplicitConversionSite(node))
                    {
                        continue;
                    }

                    semanticModel ??= await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                    var (source, target) = GetConversionTypes(node, semanticModel, cancellationToken);
                    if (TryGetConvertedCandidate(source, target, candidateInCompilation, compilation, out var candidate))
                    {
                        converted.Add(candidate);
                    }
                }
            }
        }

        return converted.ToList();
    }

    private static bool IsExplicitConversionSite(SyntaxNode node) =>
        node is CastExpressionSyntax ||
        node is PatternSyntax ||
        node is ForEachStatementSyntax ||
        node.IsKind(SyntaxKind.AsExpression) ||
        node.IsKind(SyntaxKind.IsExpression);

    /// <summary>
    /// Gets the type converted from and the type converted to at an explicit conversion site.
    /// </summary>
    private static (ITypeSymbol Source, ITypeSymbol Target) GetConversionTypes(SyntaxNode node, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        switch (node)
        {
            case CastExpressionSyntax cast:
                return (semanticModel.GetTypeInfo(cast.Expression, cancellationToken).Type, semanticModel.GetTypeInfo(cast.Type, cancellationToken).Type);
            case BinaryExpressionSyntax binary:
                return (semanticModel.GetTypeInfo(binary.Left, cancellationToken).Type, semanticModel.GetTypeInfo(binary.Right, cancellationToken).Type);
            case ForEachStatementSyntax forEach:
                return (semanticModel.GetForEachStatementInfo(forEach).ElementType, semanticModel.GetTypeInfo(forEach.Type, cancellationToken).Type);
            case PatternSyntax pattern when semanticModel.GetOperation(pattern, cancellationToken) is IPatternOperation operation:
                return (operation.InputType, operation.NarrowedType);
            default:
                return (null, null);
        }
    }

    /// <summary>
    /// Determines whether the conversion goes between a candidate (or an array of it) and an interface (or an array of
    /// it) that the candidate does not implicitly convert to, a conversion that sealing the candidate removes.
    /// </summary>
    private static bool TryGetConvertedCandidate(
        ITypeSymbol source,
        ITypeSymbol target,
        IReadOnlyDictionary<INamedTypeSymbol, INamedTypeSymbol> candidateInCompilation,
        CSharpCompilation compilation,
        out INamedTypeSymbol candidate)
    {
        while (source is IArrayTypeSymbol sourceArray && target is IArrayTypeSymbol targetArray)
        {
            source = sourceArray.ElementType;
            target = targetArray.ElementType;
        }

        candidate = null;
        if (source is null || target is null)
        {
            return false;
        }

        if (target.TypeKind == TypeKind.Interface && source is INamedTypeSymbol sourceClass &&
            candidateInCompilation.TryGetValue(sourceClass.OriginalDefinition, out candidate))
        {
            return !compilation.ClassifyConversion(source, target).IsImplicit;
        }

        if (source.TypeKind == TypeKind.Interface && target is INamedTypeSymbol targetClass &&
            candidateInCompilation.TryGetValue(targetClass.OriginalDefinition, out candidate))
        {
            return !compilation.ClassifyConversion(target, source).IsImplicit;
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
