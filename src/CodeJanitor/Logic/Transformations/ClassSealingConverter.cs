using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
/// <item>no cast, <c>as</c>, <c>is</c>, type pattern, <c>case</c> type label, <c>foreach</c> or reference equality
/// (<c>==</c>, <c>!=</c>) of the solution converts it to or from an interface it does not implement, directly or as an
/// array element, a tuple element or a covariant delegate type argument (sealing removes that explicit conversion);</item>
/// <item>its declaration, leading trivia included, contains no code excluded by a preprocessor directive, and no such
/// code of the solution names it (the semantic model cannot see inactive code, which other build configurations
/// compile);</item>
/// <item>no project written in another language than C# references its project (the checks above only read C#).</item>
/// </list>
/// A file compiled by several projects or target frameworks is analyzed in each of them, and a class is sealed only
/// when it is safe in every one. Classes outside the solution (other repositories, published packages) cannot be
/// seen: sealing a public class remains an API change, which is why the cleanup setting is an explicit opt-in.
/// The solution-wide scans are computed once per immutable <see cref="Solution" /> snapshot and reused for every file
/// analyzed against that snapshot. Pure logic over Roslyn workspace types, unit-testable without Visual Studio.
/// </remarks>
public sealed class ClassSealingConverter
{
    /// <summary>
    /// The solution-wide data of each solution snapshot analyzed by this converter, released with the snapshot.
    /// </summary>
    private readonly ConditionalWeakTable<Solution, SolutionAnalysis> _analyses = new ConditionalWeakTable<Solution, SolutionAnalysis>();

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
    private async Task<HashSet<int>> GetSafeToSealAsync(Document document, CancellationToken cancellationToken)
    {
        var solution = document.Project.Solution;
        if (solution.GetProjectDependencyGraph().GetProjectsThatTransitivelyDependOnThisProject(document.Project.Id)
            .Any(projectId => solution.GetProject(projectId)?.Language != LanguageNames.CSharp))
        {
            return new HashSet<int>();
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var analysis = _analyses.GetValue(solution, _ => new SolutionAnalysis());
        var inactiveIdentifiers = await analysis.GetInactiveCodeIdentifiersAsync(solution, cancellationToken).ConfigureAwait(false);
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
            var converted = await GetExplicitlyConvertedWithInterfacesAsync(sealable.Keys.ToList(), document.Project, analysis, cancellationToken).ConfigureAwait(false);
            foreach (var type in converted)
            {
                sealable.Remove(type);
            }
        }

        return new HashSet<int>(sealable.Values);
    }

    /// <summary>
    /// Determines whether the declaration, its leading trivia included, contains code excluded by a preprocessor
    /// directive (<c>#if</c>/<c>#elif</c>/<c>#else</c> branches inactive in this project flavor). The semantic model
    /// cannot see that code, which other build configurations compile and which may declare modifiers or members that
    /// forbid sealing.
    /// </summary>
    private static bool ContainsInactiveCode(TypeDeclarationSyntax declaration) =>
        declaration.DescendantTrivia(descendIntoTrivia: true)
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
    /// Gets the <paramref name="candidates" /> that an explicit conversion site of <paramref name="project" /> or of a
    /// project depending on it converts to or from an interface the class does not implement. That explicit reference
    /// conversion exists only while the class is not sealed: sealing would report CS0030, CS0039, CS0019 or CS8121, or
    /// turn an <c>is</c> test into CS0184.
    /// </summary>
    private static async Task<List<INamedTypeSymbol>> GetExplicitlyConvertedWithInterfacesAsync(
        IReadOnlyCollection<INamedTypeSymbol> candidates,
        Project project,
        SolutionAnalysis analysis,
        CancellationToken cancellationToken)
    {
        var solution = project.Solution;
        var projectIds = new List<ProjectId> { project.Id };
        projectIds.AddRange(solution.GetProjectDependencyGraph().GetProjectsThatTransitivelyDependOnThisProject(project.Id));
        var converted = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var scanned in projectIds.Select(solution.GetProject).Where(scanned => scanned?.Language == LanguageNames.CSharp))
        {
            var conversions = await analysis.GetConvertedClassesAsync(scanned, cancellationToken).ConfigureAwait(false);
            if (conversions is null)
            {
                continue;
            }

            foreach (var candidate in candidates.Where(candidate => !converted.Contains(candidate)))
            {
                // The candidate as seen by the compilation of the scanned project.
                var similar = SymbolFinder.FindSimilarSymbols(candidate, conversions.Compilation, cancellationToken).FirstOrDefault();
                if (similar != null && conversions.Classes.Contains(similar.OriginalDefinition))
                {
                    converted.Add(candidate);
                }
            }
        }

        return converted.ToList();
    }

    /// <summary>
    /// Gets the classes (original definitions) that an explicit conversion site of <paramref name="project" />
    /// converts to or from an interface they do not implement, or null when the project has no C# compilation.
    /// </summary>
    private static async Task<ConvertedClasses> FindConvertedClassesAsync(Project project, CancellationToken cancellationToken)
    {
        if (!(await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) is CSharpCompilation compilation))
        {
            return null;
        }

        var classes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var document in project.Documents)
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
                AddClassesConvertedWithInterfaces(source, target, compilation, classes);
            }
        }

        return new ConvertedClasses(compilation, classes);
    }

    private static bool IsExplicitConversionSite(SyntaxNode node) =>
        node is CastExpressionSyntax ||
        node is PatternSyntax ||
        node is CaseSwitchLabelSyntax ||
        node is ForEachStatementSyntax ||
        node.IsKind(SyntaxKind.AsExpression) ||
        node.IsKind(SyntaxKind.IsExpression) ||
        node.IsKind(SyntaxKind.EqualsExpression) ||
        node.IsKind(SyntaxKind.NotEqualsExpression);

    /// <summary>
    /// Gets the type converted from and the type converted to at an explicit conversion site (for a reference
    /// equality, the types of its operands, one of which the other must convert to).
    /// </summary>
    private static (ITypeSymbol Source, ITypeSymbol Target) GetConversionTypes(SyntaxNode node, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        switch (node)
        {
            case CastExpressionSyntax cast:
                return (semanticModel.GetTypeInfo(cast.Expression, cancellationToken).Type, semanticModel.GetTypeInfo(cast.Type, cancellationToken).Type);
            case BinaryExpressionSyntax equality when equality.IsKind(SyntaxKind.EqualsExpression) || equality.IsKind(SyntaxKind.NotEqualsExpression):
                return semanticModel.GetOperation(equality, cancellationToken) is IBinaryOperation { OperatorMethod: null }
                    ? (semanticModel.GetTypeInfo(equality.Left, cancellationToken).Type, semanticModel.GetTypeInfo(equality.Right, cancellationToken).Type)
                    : (null, null);
            case BinaryExpressionSyntax binary:
                return (semanticModel.GetTypeInfo(binary.Left, cancellationToken).Type, semanticModel.GetTypeInfo(binary.Right, cancellationToken).Type);
            case ForEachStatementSyntax forEach:
                return (semanticModel.GetForEachStatementInfo(forEach).ElementType, semanticModel.GetTypeInfo(forEach.Type, cancellationToken).Type);
            case PatternSyntax pattern when semanticModel.GetOperation(pattern, cancellationToken) is IPatternOperation operation:
                return (operation.InputType, operation.NarrowedType);
            case CaseSwitchLabelSyntax label when label.Parent?.Parent is SwitchStatementSyntax switchStatement &&
                                                  semanticModel.GetSymbolInfo(label.Value, cancellationToken).Symbol is ITypeSymbol type:
                return (semanticModel.GetTypeInfo(switchStatement.Expression, cancellationToken).Type, type);
            default:
                return (null, null);
        }
    }

    /// <summary>
    /// Adds to <paramref name="classes" /> the class that the conversion converts to or from an interface the class does
    /// not implicitly convert to, a conversion that sealing the class removes. The conversion is followed into array
    /// elements, tuple elements and covariant type arguments of the same generic delegate, which convert only when their
    /// types do (interfaces and contravariant type arguments convert explicitly whatever the class).
    /// </summary>
    private static void AddClassesConvertedWithInterfaces(ITypeSymbol source, ITypeSymbol target, CSharpCompilation compilation, HashSet<INamedTypeSymbol> classes)
    {
        if (source is null || target is null)
        {
            return;
        }

        if (source is IArrayTypeSymbol sourceArray && target is IArrayTypeSymbol targetArray)
        {
            AddClassesConvertedWithInterfaces(sourceArray.ElementType, targetArray.ElementType, compilation, classes);

            return;
        }

        if (!(source is INamedTypeSymbol sourceType) || !(target is INamedTypeSymbol targetType))
        {
            return;
        }

        if (sourceType.IsTupleType && targetType.IsTupleType)
        {
            if (sourceType.TupleElements.Length == targetType.TupleElements.Length)
            {
                for (var index = 0; index < sourceType.TupleElements.Length; index++)
                {
                    AddClassesConvertedWithInterfaces(sourceType.TupleElements[index].Type, targetType.TupleElements[index].Type, compilation, classes);
                }
            }

            return;
        }

        if (sourceType.TypeKind == TypeKind.Delegate && sourceType.IsGenericType &&
            SymbolEqualityComparer.Default.Equals(sourceType.OriginalDefinition, targetType.OriginalDefinition))
        {
            var typeParameters = sourceType.OriginalDefinition.TypeParameters;
            for (var index = 0; index < typeParameters.Length; index++)
            {
                if (typeParameters[index].Variance == VarianceKind.Out)
                {
                    AddClassesConvertedWithInterfaces(sourceType.TypeArguments[index], targetType.TypeArguments[index], compilation, classes);
                }
            }

            return;
        }

        if (targetType.TypeKind == TypeKind.Interface && sourceType.TypeKind == TypeKind.Class &&
            !compilation.ClassifyConversion(sourceType, targetType).IsImplicit)
        {
            classes.Add(sourceType.OriginalDefinition);
        }
        else if (sourceType.TypeKind == TypeKind.Interface && targetType.TypeKind == TypeKind.Class &&
                 !compilation.ClassifyConversion(targetType, sourceType).IsImplicit)
        {
            classes.Add(targetType.OriginalDefinition);
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

    /// <summary>
    /// The classes a project converts explicitly with interfaces, as symbols of <see cref="Compilation" />.
    /// </summary>
    private sealed class ConvertedClasses
    {
        public ConvertedClasses(CSharpCompilation compilation, HashSet<INamedTypeSymbol> classes)
        {
            Compilation = compilation;
            Classes = classes;
        }

        public CSharpCompilation Compilation { get; }

        public HashSet<INamedTypeSymbol> Classes { get; }
    }

    /// <summary>
    /// The solution-wide data of one immutable solution snapshot, computed on first use. A computation that fails or is
    /// canceled is not kept: the next call computes it again.
    /// </summary>
    private sealed class SolutionAnalysis
    {
        private readonly ConcurrentDictionary<ProjectId, Task<ConvertedClasses>> _convertedClasses =
            new ConcurrentDictionary<ProjectId, Task<ConvertedClasses>>();

        private readonly ConcurrentDictionary<SolutionId, Task<HashSet<string>>> _inactiveCodeIdentifiers =
            new ConcurrentDictionary<SolutionId, Task<HashSet<string>>>();

        public Task<HashSet<string>> GetInactiveCodeIdentifiersAsync(Solution solution, CancellationToken cancellationToken) =>
            GetOrComputeAsync(_inactiveCodeIdentifiers, solution.Id, token => ClassSealingConverter.GetInactiveCodeIdentifiersAsync(solution, token), cancellationToken);

        public Task<ConvertedClasses> GetConvertedClassesAsync(Project project, CancellationToken cancellationToken) =>
            GetOrComputeAsync(_convertedClasses, project.Id, token => FindConvertedClassesAsync(project, token), cancellationToken);

        private static async Task<TValue> GetOrComputeAsync<TKey, TValue>(
            ConcurrentDictionary<TKey, Task<TValue>> cache,
            TKey key,
            Func<CancellationToken, Task<TValue>> computeAsync,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                var completion = new TaskCompletionSource<TValue>(TaskCreationOptions.RunContinuationsAsynchronously);
                var entry = cache.GetOrAdd(key, completion.Task);
                if (entry == completion.Task)
                {
                    try
                    {
                        var value = await computeAsync(cancellationToken).ConfigureAwait(false);
                        completion.SetResult(value);

                        return value;
                    }
                    catch (Exception ex)
                    {
                        ((ICollection<KeyValuePair<TKey, Task<TValue>>>)cache).Remove(new KeyValuePair<TKey, Task<TValue>>(key, entry));
                        if (ex is OperationCanceledException)
                        {
                            completion.SetCanceled();
                        }
                        else
                        {
                            completion.SetException(ex);
                        }

                        throw;
                    }
                }

                try
                {
                    return await entry.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Canceled by the token of the caller computing it: compute again with this one.
                }
            }
        }
    }
}
