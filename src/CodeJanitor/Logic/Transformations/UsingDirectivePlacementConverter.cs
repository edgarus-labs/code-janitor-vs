using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Moves using directives between the namespace declarations (block-scoped or file-scoped) and the compilation unit of a
/// document: outside the namespace (<see cref="MoveUsingsOutsideAsync" />) or inside it
/// (<see cref="MoveUsingsInsideAsync" />), preserving file headers, comments and line endings, and deduplicating directives.
/// </summary>
/// <remarks>
/// Inside a namespace, a using directive's name is resolved against the enclosing namespaces first
/// (<c>using Services;</c> in <c>namespace Company.App</c> may mean <c>Company.App.Services</c>); at file level
/// only the global namespace is searched. Syntax alone cannot tell which namespace a name refers to, so every
/// moved directive is resolved with the document's semantic model. A directive that means the same at its new place
/// keeps its exact text; any other directive is written fully qualified (with <c>global::</c> inside a namespace). The
/// move is all-or-nothing: when a directive cannot be resolved, the directives would move across preprocessor
/// directives, there is no single namespace to move them into, a moved directive imports an extension method that the
/// file calls implicitly where the call cannot be verified, a fully qualified directive would refer to something else
/// (a target that only an extern alias inside the namespace reaches cannot be named at file level), the moved document
/// has compile errors the original did not have, or any name, member or implicitly called member (for example the
/// GetEnumerator of a foreach) would bind to a different symbol (symbols are told apart by their assemblies as well),
/// the document is left unchanged and the reason is reported. These checks run in the active
/// configuration and in every other assignment of the conditional compilation symbols the document depends on (see
/// <see cref="ConditionalCompilationVariants" />); a document that depends on more than
/// <see cref="MaxConditionalCompilationSymbols" /> of them is left unchanged.
/// </remarks>
public sealed class UsingDirectivePlacementConverter
{
    /// <summary>
    /// Fully qualified names at file level, without the <c>global::</c> prefix: at file level a name already starts at
    /// the global namespace, so the prefix adds nothing but noise. Special types are written by name
    /// (<c>System.String</c>, not <c>string</c>) and <c>Nullable&lt;T&gt;</c> is written out, because neither a keyword
    /// nor the <c>T?</c> shorthand is valid as an alias target before C# 12; neither is tuple syntax, which
    /// <see cref="Qualify" /> writes out.
    /// </summary>
    private static readonly SymbolDisplayFormat FileLevelNameFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)
        .RemoveMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes)
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier | SymbolDisplayMiscellaneousOptions.ExpandNullable);

    /// <summary>
    /// Fully qualified names inside a namespace, with the <c>global::</c> prefix: without it, the first identifier of
    /// the name would be looked up in the enclosing namespaces first. Special types and <c>Nullable&lt;T&gt;</c> are
    /// written out as by <see cref="FileLevelNameFormat" />.
    /// </summary>
    private static readonly SymbolDisplayFormat NamespaceLevelNameFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .RemoveMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes)
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier | SymbolDisplayMiscellaneousOptions.ExpandNullable);

    /// <summary>
    /// Identifies symbols independently of the compilation they were bound in. Members include their containing type,
    /// explicit interface and parameter types, so that same-named members of different types (for example two
    /// <c>Twice(this int)</c> extension methods) are told apart.
    /// </summary>
    private static readonly SymbolDisplayFormat IdentityFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMemberOptions(SymbolDisplayMemberOptions.IncludeContainingType | SymbolDisplayMemberOptions.IncludeExplicitInterface | SymbolDisplayMemberOptions.IncludeParameters)
        .AddParameterOptions(SymbolDisplayParameterOptions.IncludeParamsRefOut | SymbolDisplayParameterOptions.IncludeType);

    /// <summary>
    /// The most conditional compilation symbols whose variants are verified: each one doubles the number of variants,
    /// and each variant compiles the project twice.
    /// </summary>
    private const int MaxConditionalCompilationSymbols = 4;

    /// <summary>
    /// Extension methods the compiler calls implicitly without the semantic model exposing which method a call binds
    /// to, with the syntax that makes it call them.
    /// </summary>
    private static readonly (string Name, string DisplayName, string CalledBy, Func<SyntaxNode, SemanticModel, CancellationToken, bool> IsCaller)[] UnverifiableImplicitCalls =
    {
        ("Add", "Add", "collection expressions", (node, _, _) => node is CollectionExpressionSyntax),
        ("GetEnumerator", "GetEnumerator", "spread elements", (node, _, _) => node is SpreadElementSyntax),
        ("GetPinnableReference", "GetPinnableReference", "fixed statements", (node, _, _) => node is FixedStatementSyntax),
        ("op_Equality", "operator ==", "tuple == comparisons", (node, semanticModel, cancellationToken) => IsTupleComparison(node, SyntaxKind.EqualsExpression, semanticModel, cancellationToken)),
        ("op_Inequality", "operator !=", "tuple != comparisons", (node, semanticModel, cancellationToken) => IsTupleComparison(node, SyntaxKind.NotEqualsExpression, semanticModel, cancellationToken)),
    };

    private const string UnmatchedNodes = "the names in the moved file could not be matched with the original, so the move could not be verified";

    private const string InterleavedPreprocessorDirectives = "the using directives are interleaved with preprocessor directives";

    /// <summary>
    /// Determines, from syntax alone, whether <paramref name="source" /> may have using directives inside a namespace,
    /// i.e. whether <see cref="MoveUsingsOutsideAsync" /> may have anything to do. Hosts use it to skip the semantic work.
    /// The source is parsed without the project's conditional compilation symbols, so a using directive in an
    /// <c>#if</c> block may be disabled text here but active in the project: disabled text in the using section of a
    /// namespace that contains a using directive counts as well, and the semantic move decides with the project's parse
    /// options.
    /// </summary>
    /// <param name="source">The C# source code.</param>
    /// <returns>
    /// True when at least one namespace declaration contains a using directive, active or in disabled conditional text
    /// before its first member.
    /// </returns>
    public static bool HasUsingsInsideNamespace(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return false;
        }

        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var namespaces = GetNamespaces(root).ToList();
        if (namespaces.Any(ns => ns.Usings.Count > 0))
        {
            return true;
        }

        var disabledUsings = root.DescendantTrivia()
            .Where(trivia => trivia.IsKind(SyntaxKind.DisabledTextTrivia) && ContainsUsingDirective(trivia.ToString()))
            .ToList();

        return disabledUsings.Count > 0
            && namespaces.Select(ns => GetUsingSection(root, ns)).Any(section => disabledUsings.Any(trivia => section.Contains(trivia.SpanStart)));
    }

    /// <summary>
    /// Determines, from syntax alone, whether <paramref name="source" /> may have using directives to move inside its
    /// namespace, i.e. whether <see cref="MoveUsingsInsideAsync" /> may have anything to do. Hosts use it to skip the
    /// semantic work. Only a file whose top level holds nothing but using and extern alias directives and a single
    /// namespace counts: with several namespaces there is no single namespace to move them into, and a type, delegate,
    /// top-level statement or assembly attribute outside the namespace would lose the file-level directives it may need,
    /// so hosts leave such files as they are without analyzing them. As for <see cref="HasUsingsInsideNamespace" />, the
    /// source is parsed without the project's conditional compilation symbols, so a conditional directive in front of
    /// the namespace counts as well.
    /// </summary>
    /// <param name="source">The C# source code.</param>
    /// <returns>
    /// True when the file declares a single top-level namespace and nothing else outside it, and has a using directive
    /// other than <c>global using</c> at file level, or an <c>#if</c>, <c>#elif</c>, <c>#else</c> or <c>#endif</c>
    /// directive in front of the namespace.
    /// </returns>
    public static bool HasUsingsOutsideNamespace(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return false;
        }

        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        if (root.AttributeLists.Count > 0 || root.Members.Count != 1 || !(root.Members[0] is BaseNamespaceDeclarationSyntax ns))
        {
            return false;
        }

        return GetFileLevelUsings(root).Any()
            || GetDirectives(root).Any(directive => (directive is BranchingDirectiveTriviaSyntax || directive is EndIfDirectiveTriviaSyntax) && directive.SpanStart < ns.SpanStart);
    }

    /// <summary>
    /// Determines whether disabled conditional text contains a using directive (in a namespace it may declare as well).
    /// </summary>
    private static bool ContainsUsingDirective(string disabledText) =>
        SyntaxFactory.ParseCompilationUnit(disabledText).DescendantNodes(node => node is CompilationUnitSyntax || node is BaseNamespaceDeclarationSyntax)
            .OfType<UsingDirectiveSyntax>()
            .Any();

    /// <summary>
    /// Moves the using directives located inside the namespaces of <paramref name="document" /> to the top of the
    /// compilation unit, fully qualifying names that were resolved relative to an enclosing namespace.
    /// </summary>
    /// <param name="document">The C# document, bound to the project it compiles in.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The moved text, or why the document was left unchanged.</returns>
    public Task<UsingDirectivePlacementResult> MoveUsingsOutsideAsync(Document document, CancellationToken cancellationToken) =>
        MoveAsync(document, OutwardMove.Instance, cancellationToken);

    /// <summary>
    /// Moves the file-level using directives of <paramref name="document" /> into its only top-level namespace, in front
    /// of the directives already there, writing <c>global::</c>-qualified the names that would otherwise be resolved
    /// relative to an enclosing namespace. <c>global using</c> directives import into every file of the project and
    /// extern alias directives must come first in the file, so both stay at file level. A moved directive that imports
    /// what a directive of the namespace already imports is dropped.
    /// </summary>
    /// <param name="document">The C# document, bound to the project it compiles in.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The moved text, or why the document was left unchanged.</returns>
    public Task<UsingDirectivePlacementResult> MoveUsingsInsideAsync(Document document, CancellationToken cancellationToken) =>
        MoveAsync(document, InwardMove.Instance, cancellationToken);

    /// <summary>
    /// Moves the using directives of <paramref name="document" /> in the direction of <paramref name="move" /> and
    /// verifies the move in the active configuration and in every other relevant build variant.
    /// </summary>
    private static async Task<UsingDirectivePlacementResult> MoveAsync(Document document, UsingMove move, CancellationToken cancellationToken)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (!(await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) is CompilationUnitSyntax root))
        {
            return UsingDirectivePlacementResult.NothingToMove;
        }

        var movedUsings = move.GetMovedUsings(root);
        if (movedUsings.Count == 0)
        {
            return UsingDirectivePlacementResult.NothingToMove;
        }

        var unsupportedLayout = move.FindUnsupportedLayout(root, movedUsings);
        if (unsupportedLayout != null)
        {
            return UsingDirectivePlacementResult.Skipped(unsupportedLayout);
        }

        var symbols = await ConditionalCompilationVariants.GetRelevantSymbolsAsync(document, root, cancellationToken).ConfigureAwait(false);
        if (symbols.Count > MaxConditionalCompilationSymbols)
        {
            return UsingDirectivePlacementResult.Skipped(
                $"the move depends on the conditional compilation symbols {string.Join(", ", symbols)}, and more than {MaxConditionalCompilationSymbols} conditional-compilation symbols are too many variants to verify");
        }

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var unsafeDirective = ResolveTargets(movedUsings, semanticModel, cancellationToken, out var targets)
            ?? FindUnverifiableImplicitCall(root, targets, semanticModel, cancellationToken);
        if (unsafeDirective != null)
        {
            return UsingDirectivePlacementResult.Skipped(unsafeDirective);
        }

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var newline = text.ToString().Contains("\r\n") ? "\r\n" : "\n";

        // Move the directives verbatim first: every directive that still means the same at its new place (already fully
        // qualified, keyword/tuple syntax, global:: or extern-alias qualified, or not shadowed there) keeps its exact
        // text. Only the others are rewritten fully qualified.
        var verbatimText = move.Move(root, targets, new Dictionary<UsingDirectiveSyntax, UsingDirectiveSyntax>(), semanticModel, newline, cancellationToken);
        var verbatimModel = await GetSemanticModelAsync(document, verbatimText, text, cancellationToken).ConfigureAwait(false);
        var placedTargets = GetPlacedTargets(move, verbatimModel, cancellationToken);

        var qualifiedUsings = targets
            .Where(pair => !(placedTargets.TryGetValue(GetUsingKey(pair.Key), out var placedTarget) && placedTarget == Identify(pair.Value)))
            .ToDictionary(pair => pair.Key, pair => Qualify(pair.Key, pair.Value, move.QualifiedNameFormat));

        var movedText = qualifiedUsings.Count == 0
            ? verbatimText
            : move.Move(root, targets, qualifiedUsings, semanticModel, newline, cancellationToken);
        var movedModel = movedText == verbatimText
            ? verbatimModel
            : await GetSemanticModelAsync(document, movedText, text, cancellationToken).ConfigureAwait(false);

        var unsafeChange = FindMisqualifiedUsing(move, targets, qualifiedUsings, movedModel, cancellationToken)
            ?? FindUnsafeChange(semanticModel, movedModel, cancellationToken);
        if (unsafeChange != null)
        {
            return UsingDirectivePlacementResult.Skipped(unsafeChange);
        }

        foreach (var variant in ConditionalCompilationVariants.GetOtherVariants(document, symbols))
        {
            var unsafeVariantChange = await FindUnsafeChangeInVariantAsync(variant.Solution.GetDocument(document.Id), move, movedText, text, cancellationToken).ConfigureAwait(false);
            if (unsafeVariantChange != null)
            {
                return UsingDirectivePlacementResult.Skipped($"with {variant.Description}: {unsafeVariantChange}");
            }
        }

        return UsingDirectivePlacementResult.Moved(movedText);
    }

    /// <summary>
    /// Verifies <paramref name="movedText" /> in a build variant: <paramref name="variant" /> is the original document in
    /// the solution parsed with the variant's symbols, against which the checks of the active configuration are run. A
    /// directive that only the variant compiles (in an <c>#if</c> block the move did not touch) would not move with the
    /// others, which is reported as well. Returns the skip reason, or null when the move is safe in the variant.
    /// </summary>
    private static async Task<string> FindUnsafeChangeInVariantAsync(Document variant, UsingMove move, string movedText, SourceText originalText, CancellationToken cancellationToken)
    {
        var root = (CompilationUnitSyntax)await variant.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await variant.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var unsafeDirective = ResolveTargets(move.GetMovedUsings(root), semanticModel, cancellationToken, out var targets)
            ?? FindUnverifiableImplicitCall(root, targets, semanticModel, cancellationToken);
        if (unsafeDirective != null)
        {
            return unsafeDirective;
        }

        var movedModel = await GetSemanticModelAsync(variant, movedText, originalText, cancellationToken).ConfigureAwait(false);
        var leftBehind = move.GetMovedUsings(movedModel.SyntaxTree.GetCompilationUnitRoot(cancellationToken)).FirstOrDefault();

        return leftBehind != null
            ? $"'{leftBehind.WithoutTrivia().ToFullString()}' would stay {move.Origin}"
            : FindUnsafeChange(semanticModel, movedModel, cancellationToken);
    }

    /// <summary>
    /// Gets what the directives where the moved directives end up import or alias in a moved document, identified (see
    /// <see cref="Identify" />) and keyed by their tokens (see <see cref="GetUsingKey" />).
    /// </summary>
    private static Dictionary<string, string> GetPlacedTargets(UsingMove move, SemanticModel movedModel, CancellationToken cancellationToken) =>
        move.GetPlacedUsings(movedModel.SyntaxTree.GetCompilationUnitRoot(cancellationToken))
            .GroupBy(GetUsingKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Identify(GetTarget(group.First(), movedModel, cancellationToken)), StringComparer.Ordinal);

    /// <summary>
    /// Finds a moved directive whose fully qualified replacement refers to something other than the directive's target
    /// where it moves and returns the skip reason; null when every replacement refers to its target. A fully qualified
    /// name cannot carry an extern alias, so a target that only an extern alias staying behind (inside the namespace)
    /// reaches would be written as the same-named namespace or type of another assembly, or as nothing.
    /// </summary>
    private static string FindMisqualifiedUsing(
        UsingMove move,
        IReadOnlyDictionary<UsingDirectiveSyntax, ISymbol> targets,
        IReadOnlyDictionary<UsingDirectiveSyntax, UsingDirectiveSyntax> qualifiedUsings,
        SemanticModel movedModel,
        CancellationToken cancellationToken)
    {
        if (qualifiedUsings.Count == 0)
        {
            return null;
        }

        // A replacement that is not placed was dropped for a directive that imports the same.
        var placedTargets = GetPlacedTargets(move, movedModel, cancellationToken);
        foreach (var pair in qualifiedUsings)
        {
            if (placedTargets.TryGetValue(GetUsingKey(pair.Value), out var placedTarget) && placedTarget != Identify(targets[pair.Key]))
            {
                return $"'{pair.Key.WithoutTrivia().ToFullString()}' refers to a namespace or type that cannot be named where it moves: '{pair.Value.WithoutTrivia().ToFullString()}' would refer to something else there";
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the namespace or type each using directive imports or aliases. Returns the skip reason for the first
    /// directive that cannot be resolved, otherwise null.
    /// </summary>
    private static string ResolveTargets(
        IEnumerable<UsingDirectiveSyntax> usingDirectives,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out Dictionary<UsingDirectiveSyntax, ISymbol> targets)
    {
        targets = new Dictionary<UsingDirectiveSyntax, ISymbol>();
        foreach (var usingDirective in usingDirectives)
        {
            var target = GetTarget(usingDirective, semanticModel, cancellationToken);
            if (!IsResolved(target))
            {
                return $"'{usingDirective.WithoutTrivia().ToFullString()}' cannot be resolved semantically";
            }

            targets.Add(usingDirective, target);
        }

        return null;
    }

    /// <summary>
    /// Compares the moved document with the original, both compiled in the same configuration. Returns the skip reason
    /// when the move adds compile errors (matched by message, see <see cref="CompilerErrorMatching.MatchByMessage" />:
    /// their locations shift when the directives move) or changes what a node binds to, otherwise null.
    /// </summary>
    private static string FindUnsafeChange(SemanticModel before, SemanticModel after, CancellationToken cancellationToken)
    {
        var newErrors = CompilerErrorMatching.MatchByMessage(GetErrors(before, cancellationToken), GetErrors(after, cancellationToken)).New;

        return newErrors.Count > 0
            ? $"moving them would introduce {newErrors.Count} new compile error(s): {string.Join("; ", newErrors.Take(3).Select(Describe))}"
            : FindChangedBinding(before, after, cancellationToken);
    }

    private static IEnumerable<Diagnostic> GetErrors(SemanticModel semanticModel, CancellationToken cancellationToken) =>
        semanticModel.GetDiagnostics(cancellationToken: cancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    private static Task<SemanticModel> GetSemanticModelAsync(Document document, string newText, SourceText originalText, CancellationToken cancellationToken) =>
        document.WithText(SourceText.From(newText, originalText.Encoding, originalText.ChecksumAlgorithm)).GetSemanticModelAsync(cancellationToken);

    /// <summary>
    /// Gets the using directives of every (nested) namespace declaration; namespaces can only be declared in the
    /// compilation unit or in another namespace, so no other nodes are visited.
    /// </summary>
    private static IEnumerable<UsingDirectiveSyntax> GetNamespaceUsings(CompilationUnitSyntax root) =>
        GetNamespacesWithUsings(root).SelectMany(ns => ns.Usings);

    private static IEnumerable<BaseNamespaceDeclarationSyntax> GetNamespacesWithUsings(CompilationUnitSyntax root) =>
        GetNamespaces(root).Where(ns => ns.Usings.Count > 0);

    private static IEnumerable<BaseNamespaceDeclarationSyntax> GetNamespaces(CompilationUnitSyntax root) =>
        root.DescendantNodes(node => node is CompilationUnitSyntax || node is BaseNamespaceDeclarationSyntax)
            .OfType<BaseNamespaceDeclarationSyntax>();

    /// <summary>
    /// Gets the span in which the extern alias and using directives of <paramref name="ns" /> are written: from its
    /// opening brace or semicolon to its first member, otherwise to its closing brace or the end of the file.
    /// </summary>
    private static TextSpan GetUsingSection(CompilationUnitSyntax root, BaseNamespaceDeclarationSyntax ns)
    {
        var start = ns is NamespaceDeclarationSyntax blockNamespace
            ? blockNamespace.OpenBraceToken.Span.End
            : ((FileScopedNamespaceDeclarationSyntax)ns).SemicolonToken.Span.End;
        var end = ns.Members.Count > 0 ? ns.Members[0].SpanStart
            : ns is NamespaceDeclarationSyntax block ? block.CloseBraceToken.SpanStart
            : root.FullSpan.End;

        return TextSpan.FromBounds(start, Math.Max(start, end));
    }

    /// <summary>
    /// Determines whether a preprocessor directive lies between the place the moved directives are inserted and the
    /// end of the using directives of the last namespace, including the directives directly after them. Moving the
    /// directives across it would take them into or out of an <c>#if</c> or <c>#region</c> block, or into another
    /// <c>#nullable</c> or <c>#pragma warning</c> state.
    /// </summary>
    private static bool HasInterleavedPreprocessorDirectives(CompilationUnitSyntax root, IReadOnlyList<UsingDirectiveSyntax> namespaceUsings)
    {
        var start = GetInsertionPosition(root);
        var end = namespaceUsings[namespaceUsings.Count - 1].GetLastToken().GetNextToken().SpanStart;

        return GetDirectives(root).Any(directive => directive.SpanStart >= start && directive.SpanStart < end);
    }

    /// <summary>
    /// Gets the position the moved directives are inserted at (see <see cref="MoveUsingsOutside" />): after the last
    /// top-level using or extern alias directive, otherwise before the first token, after the file header it keeps.
    /// </summary>
    private static int GetInsertionPosition(CompilationUnitSyntax root) =>
        root.Usings.Count > 0 ? root.Usings.Last().FullSpan.End
        : root.Externs.Count > 0 ? root.Externs.Last().FullSpan.End
        : root.GetFirstToken().SpanStart;

    /// <summary>
    /// Gets the using directives of the compilation unit that a move inside the namespace takes along: all but the
    /// <c>global using</c> directives, which import into every file of the project and stay at file level.
    /// </summary>
    private static IEnumerable<UsingDirectiveSyntax> GetFileLevelUsings(CompilationUnitSyntax root) =>
        root.Usings.Where(usingDirective => usingDirective.GlobalKeyword.IsKind(SyntaxKind.None));

    private static IEnumerable<BaseNamespaceDeclarationSyntax> GetTopLevelNamespaces(CompilationUnitSyntax root) =>
        root.Members.OfType<BaseNamespaceDeclarationSyntax>();

    /// <summary>
    /// Determines whether a preprocessor directive lies between the first directive moved inside
    /// <paramref name="ns" /> and the place in <paramref name="ns" /> the directives are inserted at (see
    /// <see cref="GetInsertionToken" />). The directive's leading trivia counts, unless the directive starts the file:
    /// then its leading trivia is the file header, which stays at the top of the file. Moving the directives across a
    /// preprocessor directive would take them into or out of an <c>#if</c> or <c>#region</c> block, or into another
    /// <c>#nullable</c> or <c>#pragma warning</c> state.
    /// </summary>
    private static bool HasInterleavedPreprocessorDirectives(CompilationUnitSyntax root, IReadOnlyList<UsingDirectiveSyntax> fileLevelUsings, BaseNamespaceDeclarationSyntax ns)
    {
        var first = fileLevelUsings[0];
        var start = StartsFile(root, first) ? first.SpanStart : first.FullSpan.Start;
        var end = GetInsertionToken(ns).FullSpan.End;

        return GetDirectives(root).Any(directive => directive.SpanStart >= start && directive.SpanStart < end);
    }

    /// <summary>
    /// Gets the token of <paramref name="ns" /> after which the moved directives are inserted: its last extern alias
    /// directive, otherwise its opening brace or semicolon.
    /// </summary>
    private static SyntaxToken GetInsertionToken(BaseNamespaceDeclarationSyntax ns) =>
        ns.Externs.Count > 0 ? ns.Externs.Last().GetLastToken()
        : ns is NamespaceDeclarationSyntax blockNamespace ? blockNamespace.OpenBraceToken
        : ((FileScopedNamespaceDeclarationSyntax)ns).SemicolonToken;

    private static bool StartsFile(CompilationUnitSyntax root, UsingDirectiveSyntax usingDirective) =>
        root.GetFirstToken() == usingDirective.GetFirstToken();

    private static IEnumerable<DirectiveTriviaSyntax> GetDirectives(CompilationUnitSyntax root)
    {
        for (var directive = root.GetFirstDirective(); directive != null; directive = directive.GetNextDirective())
        {
            yield return directive;
        }
    }

    /// <summary>
    /// Gets the namespace or type a using directive imports or aliases.
    /// </summary>
    private static ISymbol GetTarget(UsingDirectiveSyntax usingDirective, SemanticModel semanticModel, CancellationToken cancellationToken) =>
        usingDirective.Alias != null
            ? (semanticModel.GetDeclaredSymbol(usingDirective, cancellationToken) as IAliasSymbol)?.Target
            : semanticModel.GetSymbolInfo(usingDirective.NamespaceOrType, cancellationToken).Symbol;

    /// <summary>
    /// Identifies a symbol independently of the compilation it was bound in: by its kind, its name and the assemblies
    /// that declare it (see <see cref="GetDeclaringAssemblies" />).
    /// </summary>
    private static string Identify(ISymbol symbol)
    {
        var normalized = Normalize(symbol);
        if (normalized == null)
        {
            return null;
        }

        var parts = normalized.ToDisplayParts(IdentityFormat);

        return normalized.Kind + ":" + parts.ToDisplayString() + " in " + string.Join(", ", GetDeclaringAssemblies(normalized, parts));
    }

    /// <summary>
    /// Gets, in ordinal order, the identities of the assemblies that declare <paramref name="symbol" /> and the types
    /// its display <paramref name="parts" /> name (containing types, type arguments, parameter types). A name omits
    /// extern aliases, so the same name declared by an assembly referenced with an extern alias and by another one
    /// reads the same. A namespace is declared by every assembly whose namespaces it merges.
    /// </summary>
    private static IEnumerable<string> GetDeclaringAssemblies(ISymbol symbol, ImmutableArray<SymbolDisplayPart> parts)
    {
        var assemblies = symbol is INamespaceSymbol namespaceSymbol
            ? namespaceSymbol.ConstituentNamespaces.Select(constituent => constituent.ContainingAssembly)
            : parts.Select(part => part.Symbol).Where(named => named != null && !(named is INamespaceSymbol)).Select(named => named.ContainingAssembly);

        return assemblies
            .Select(assembly => assembly?.Identity.GetDisplayName())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(identity => identity, StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns a reduced extension method (the <c>Twice()</c> of <c>1.Twice()</c>) as the method it calls
    /// (<c>E.Twice(int)</c>), whose identity includes the declaring class and the receiver parameter.
    /// </summary>
    private static ISymbol Normalize(ISymbol symbol) =>
        symbol is IMethodSymbol method && method.ReducedFrom != null ? method.GetConstructedReducedFrom() : symbol;

    /// <summary>
    /// Returns <paramref name="usingDirective" /> with its name or alias target replaced by the fully qualified name
    /// of <paramref name="target" />, written in <paramref name="format" />. A tuple without element names is written as
    /// the <c>ValueTuple</c> it stands for, because before C# 12 neither an alias target nor the type of a
    /// <c>using static</c> directive can be written in tuple syntax. Its type arguments keep the tuple syntax, which is
    /// valid there and keeps their element names, as does a tuple with element names, which only C# 12 accepts as an
    /// alias target.
    /// </summary>
    private static UsingDirectiveSyntax Qualify(UsingDirectiveSyntax usingDirective, ISymbol target, SymbolDisplayFormat format)
    {
        var name = target is INamedTypeSymbol tuple && tuple.IsTupleType && !tuple.TupleElements.Any(element => element.IsExplicitlyNamedTupleElement)
            ? tuple.ContainingNamespace.ToDisplayString(format) + "." + tuple.Name + "<" + string.Join(", ", tuple.TypeArguments.Select(argument => argument.ToDisplayString(format))) + ">"
            : target.ToDisplayString(format);

        return usingDirective.WithNamespaceOrType(SyntaxFactory.ParseTypeName(name).WithTriviaFrom(usingDirective.NamespaceOrType));
    }

    /// <summary>
    /// Gets a key that identifies what a using directive imports: its alias name, <c>static</c> or a plain import,
    /// and its target, so that directives written differently that import the same (<c>using Services;</c> inside
    /// <c>namespace Company.App</c> and <c>using Company.App.Services;</c> at file level) are considered equal. A
    /// directive that cannot be resolved is identified by its tokens.
    /// </summary>
    private static string GetImportKey(UsingDirectiveSyntax usingDirective, ISymbol target)
    {
        if (!IsResolved(target))
        {
            return "text " + GetUsingKey(usingDirective);
        }

        var kind = usingDirective.Alias != null ? "alias " + usingDirective.Alias.Name.Identifier.ValueText
            : usingDirective.StaticKeyword.IsKind(SyntaxKind.None) ? "import"
            : "static";

        return kind + " " + Identify(target);
    }

    /// <summary>
    /// Finds the first node outside the using directives whose binding differs after the move (for example an import
    /// that is now searched after a same-named type or extension method of an enclosing namespace) and returns the skip
    /// reason; null when everything binds as before. Only using directives move, so the nodes of both trees correspond
    /// in document order.
    /// </summary>
    private static string FindChangedBinding(SemanticModel before, SemanticModel after, CancellationToken cancellationToken)
    {
        var beforeNodes = GetNodesOutsideUsings(before, cancellationToken);
        var afterNodes = GetNodesOutsideUsings(after, cancellationToken);
        if (beforeNodes.Count != afterNodes.Count)
        {
            return UnmatchedNodes;
        }

        for (var i = 0; i < beforeNodes.Count; i++)
        {
            if (beforeNodes[i].RawKind != afterNodes[i].RawKind)
            {
                return UnmatchedNodes;
            }

            var beforeBindings = GetBindings(before, beforeNodes[i], cancellationToken).ToList();
            var afterBindings = GetBindings(after, afterNodes[i], cancellationToken).ToList();
            for (var j = 0; j < Math.Max(beforeBindings.Count, afterBindings.Count); j++)
            {
                var beforeBinding = j < beforeBindings.Count ? beforeBindings[j] : Binding.None;
                var afterBinding = j < afterBindings.Count ? afterBindings[j] : Binding.None;
                if (beforeBinding.Identity != afterBinding.Identity)
                {
                    // Symbols that read the same are told apart by their assemblies.
                    var withAssemblies = beforeBinding.Describe(withAssemblies: false) == afterBinding.Describe(withAssemblies: false);
                    return $"moving them would change what '{DescribeSite(beforeNodes[i])}' refers to ({beforeBinding.Describe(withAssemblies)} -> {afterBinding.Describe(withAssemblies)})";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the nodes outside the using directives in document order, including those of documentation comments
    /// (<c>cref</c> names bind like code) but not those of other structured trivia such as preprocessor directives, nor
    /// those of a documentation comment in front of a top-level namespace: that comment documents nothing, and it may
    /// be the file header, which the outward move hands to the first moved directive when the file has no top-level
    /// using or extern alias directive, and which the inward move leaves in front of the namespace. It is excluded
    /// whether or not the namespace starts the file, so that both trees are compared alike. A documentation comment in
    /// front of a type that starts the file is compared, so moving it to a directive is detected.
    /// </summary>
    private static List<SyntaxNode> GetNodesOutsideUsings(SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var root = semanticModel.SyntaxTree.GetCompilationUnitRoot(cancellationToken);

        bool IsCompared(SyntaxNode node) =>
            !(node is UsingDirectiveSyntax)
            && (!(node is StructuredTriviaSyntax trivia) || (trivia is DocumentationCommentTriviaSyntax && !DocumentsTopLevelNamespace(trivia)));

        return root.DescendantNodes(IsCompared, descendIntoTrivia: true).Where(IsCompared).ToList();
    }

    /// <summary>
    /// Determines whether a documentation comment is in front of the <c>namespace</c> keyword of a top-level namespace.
    /// </summary>
    private static bool DocumentsTopLevelNamespace(StructuredTriviaSyntax documentationComment)
    {
        var token = documentationComment.ParentTrivia.Token;

        return token.IsKind(SyntaxKind.NamespaceKeyword) && token.Parent?.Parent is CompilationUnitSyntax;
    }

    /// <summary>
    /// Gets what <paramref name="node" /> binds to: its symbol and the members the compiler calls implicitly for it
    /// (the GetEnumerator of a foreach, the GetAwaiter of an await, the Deconstruct of a deconstruction or
    /// <c>var (a, b)</c> pattern, the methods of a query clause, the Add of a collection initializer element, the
    /// Length/Count, indexer and Slice of a list pattern, slice pattern or implicit Index/Range element access, the
    /// operator true of a condition), which extension members of the moved imports can provide just like explicitly
    /// called ones.
    /// </summary>
    private static IEnumerable<Binding> GetBindings(SemanticModel semanticModel, SyntaxNode node, CancellationToken cancellationToken)
    {
        yield return Binding.Of(semanticModel.GetSymbolInfo(node, cancellationToken));

        switch (node)
        {
            case CommonForEachStatementSyntax forEach:
                var forEachInfo = semanticModel.GetForEachStatementInfo(forEach);
                yield return Binding.Of(forEachInfo.GetEnumeratorMethod);
                yield return Binding.Of(forEachInfo.MoveNextMethod);
                yield return Binding.Of(forEachInfo.CurrentProperty);
                yield return Binding.Of(forEachInfo.DisposeMethod);
                if (forEach is ForEachVariableStatementSyntax deconstructingForEach)
                {
                    foreach (var method in GetDeconstructMethods(semanticModel.GetDeconstructionInfo(deconstructingForEach)))
                    {
                        yield return Binding.Of(method);
                    }
                }

                break;

            case AssignmentExpressionSyntax assignment:
                foreach (var method in GetDeconstructMethods(semanticModel.GetDeconstructionInfo(assignment)))
                {
                    yield return Binding.Of(method);
                }

                break;

            case AwaitExpressionSyntax awaitExpression:
                var awaitInfo = semanticModel.GetAwaitExpressionInfo(awaitExpression);
                yield return Binding.Of(awaitInfo.GetAwaiterMethod);
                yield return Binding.Of(awaitInfo.IsCompletedProperty);
                yield return Binding.Of(awaitInfo.GetResultMethod);
                break;

            case ParenthesizedVariableDesignationSyntax designation:
                yield return Binding.Of((semanticModel.GetOperation(designation, cancellationToken) as IRecursivePatternOperation)?.DeconstructSymbol);
                break;

            case QueryClauseSyntax queryClause:
                var queryInfo = semanticModel.GetQueryClauseInfo(queryClause, cancellationToken);
                yield return Binding.Of(queryInfo.CastInfo);
                yield return Binding.Of(queryInfo.OperationInfo);
                break;

            case ListPatternSyntax listPattern:
                var listPatternOperation = semanticModel.GetOperation(listPattern, cancellationToken) as IListPatternOperation;
                yield return Binding.Of(listPatternOperation?.LengthSymbol);
                yield return Binding.Of(listPatternOperation?.IndexerSymbol);
                break;

            case SlicePatternSyntax slicePattern:
                yield return Binding.Of((semanticModel.GetOperation(slicePattern, cancellationToken) as ISlicePatternOperation)?.SliceSymbol);
                break;

            case ElementAccessExpressionSyntax _:
            case ElementBindingExpressionSyntax _:
                var implicitIndexer = semanticModel.GetOperation(node, cancellationToken) as IImplicitIndexerReferenceOperation;
                yield return Binding.Of(implicitIndexer?.LengthSymbol);
                yield return Binding.Of(implicitIndexer?.IndexerSymbol);
                break;

            case IfStatementSyntax _:
            case ConditionalExpressionSyntax _:
            case WhileStatementSyntax _:
            case DoStatementSyntax _:
            case ForStatementSyntax _:
            case CasePatternSwitchLabelSyntax _:
            case SwitchExpressionArmSyntax _:
            case CatchClauseSyntax _:
                yield return Binding.Of(GetTruthOperator(semanticModel.GetOperation(node, cancellationToken)));
                break;
        }

        if (node is ExpressionSyntax element
            && element.Parent is InitializerExpressionSyntax initializer
            && initializer.IsKind(SyntaxKind.CollectionInitializerExpression))
        {
            yield return Binding.Of(semanticModel.GetCollectionInitializerSymbolInfo(element, cancellationToken));
        }
    }

    /// <summary>
    /// Gets the Deconstruct methods of a (nested) deconstruction; null where a part needs none (a tuple).
    /// </summary>
    private static IEnumerable<IMethodSymbol> GetDeconstructMethods(DeconstructionInfo deconstruction) =>
        new[] { deconstruction.Method }.Concat(deconstruction.Nested.SelectMany(GetDeconstructMethods));

    /// <summary>
    /// Gets the <c>operator true</c> the compiler calls on the condition, guard or exception filter of
    /// <paramref name="operation" /> when its type does not convert to bool; null when there is none.
    /// </summary>
    private static IMethodSymbol GetTruthOperator(IOperation operation)
    {
        IOperation condition;
        switch (operation)
        {
            case IConditionalOperation conditional:
                condition = conditional.Condition;
                break;

            case IWhileLoopOperation whileLoop:
                condition = whileLoop.Condition;
                break;

            case IForLoopOperation forLoop:
                condition = forLoop.Condition;
                break;

            case IPatternCaseClauseOperation caseClause:
                condition = caseClause.Guard;
                break;

            case ISwitchExpressionArmOperation arm:
                condition = arm.Guard;
                break;

            case ICatchClauseOperation catchClause:
                condition = catchClause.Filter;
                break;

            default:
                condition = null;
                break;
        }

        return condition is IUnaryOperation unary && unary.OperatorKind == UnaryOperatorKind.True ? unary.OperatorMethod : null;
    }

    /// <summary>
    /// Describes a node for a skip reason: a name by its identifier, anything else by its (shortened) text.
    /// </summary>
    private static string DescribeSite(SyntaxNode node)
    {
        if (node is SimpleNameSyntax name)
        {
            return name.Identifier.ValueText;
        }

        var text = string.Join(" ", node.ToString().Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        return text.Length <= 40 ? text : text.Substring(0, 40) + "...";
    }

    /// <summary>
    /// Finds a using directive that imports an extension method the document calls implicitly where the call cannot be
    /// compared (see <see cref="UnverifiableImplicitCalls" />) and returns the skip reason; null when there is none. The
    /// move changes where the imported extension methods are looked up, so such a call could silently bind to another
    /// extension method.
    /// </summary>
    private static string FindUnverifiableImplicitCall(
        CompilationUnitSyntax root,
        IReadOnlyDictionary<UsingDirectiveSyntax, ISymbol> targets,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var call in UnverifiableImplicitCalls)
        {
            var importingDirective = targets.FirstOrDefault(pair => ImportsExtensionMethod(pair.Key, pair.Value, call.Name)).Key;
            if (importingDirective != null && root.DescendantNodes().Any(node => call.IsCaller(node, semanticModel, cancellationToken)))
            {
                return $"'{importingDirective.WithoutTrivia().ToFullString()}' imports an extension method '{call.DisplayName}', which {call.CalledBy} call implicitly in a way that cannot be verified";
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether <paramref name="node" /> compares two tuples element by element with the operator of
    /// <paramref name="kind" />; the semantic model does not expose the operators it calls for the elements.
    /// </summary>
    private static bool IsTupleComparison(SyntaxNode node, SyntaxKind kind, SemanticModel semanticModel, CancellationToken cancellationToken) =>
        node.IsKind(kind) && semanticModel.GetOperation(node, cancellationToken) is ITupleBinaryOperation;

    /// <summary>
    /// Determines whether a using directive imports an extension method named <paramref name="name" />, declared (as
    /// a classic extension method or in an extension block) by a static class of an imported namespace or by the type
    /// of a <c>using static</c> directive. Alias directives import no extension methods.
    /// </summary>
    private static bool ImportsExtensionMethod(UsingDirectiveSyntax usingDirective, ISymbol target, string name)
    {
        if (usingDirective.Alias != null)
        {
            return false;
        }

        var types = target is INamespaceSymbol importedNamespace
            ? importedNamespace.GetTypeMembers()
            : target is INamedTypeSymbol importedType ? ImmutableArray.Create(importedType) : ImmutableArray<INamedTypeSymbol>.Empty;

        return types.Any(type => type.IsStatic
            && (type.GetMembers(name).Any(member => member is IMethodSymbol method && method.IsExtensionMethod)
                || type.GetTypeMembers().Any(block => block.IsExtension && block.GetMembers(name).Any(member => member is IMethodSymbol))));
    }

    private static bool IsResolved(ISymbol symbol)
    {
        switch (symbol)
        {
            case INamespaceSymbol _:
                return true;

            case ITypeSymbol type:
                return !ContainsErrorType(type);

            default:
                return false;
        }
    }

    private static bool ContainsErrorType(ITypeSymbol type)
    {
        switch (type)
        {
            case IErrorTypeSymbol _:
                return true;

            case IArrayTypeSymbol array:
                return ContainsErrorType(array.ElementType);

            case IPointerTypeSymbol pointer:
                return ContainsErrorType(pointer.PointedAtType);

            case INamedTypeSymbol named:
                return named.TypeArguments.Any(ContainsErrorType)
                    || (named.ContainingType != null && ContainsErrorType(named.ContainingType));

            default:
                return false;
        }
    }

    private static string Describe(Diagnostic diagnostic) =>
        $"{diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Moves all using directives located inside namespaces to the top of the compilation unit, preserving the
    /// file header, the comments of the directives and line endings, and deduplicating against the existing top-level
    /// directives.
    /// </summary>
    private static string MoveUsingsOutside(CompilationUnitSyntax root, string newline)
    {
        var namespacesWithUsings = root.DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Where(ns => ns.Usings.Count > 0)
            .ToList();

        var lineEndingTrivia = newline == "\r\n" ? SyntaxFactory.CarriageReturnLineFeed : SyntaxFactory.LineFeed;

        // Collect all using directives inside namespaces
        var extractedUsings = new List<UsingDirectiveSyntax>();
        foreach (var ns in namespacesWithUsings)
        {
            extractedUsings.AddRange(ns.Usings);
        }

        // Collect existing top-level usings
        var existingTopUsings = root.Usings.ToList();
        var existingKeys = new HashSet<string>(existingTopUsings.Select(GetUsingKey), StringComparer.Ordinal);

        // A directive that duplicates a top-level or an earlier moved directive is dropped; its comments go to the
        // directive that stays.
        var droppedComments = new Dictionary<string, (List<SyntaxTrivia> Leading, List<SyntaxTrivia> SameLine)>(StringComparer.Ordinal);
        var newUsingsToAdd = new List<UsingDirectiveSyntax>();
        foreach (var u in extractedUsings)
        {
            var key = GetUsingKey(u);
            if (existingKeys.Add(key))
            {
                newUsingsToAdd.Add(u.WithLeadingTrivia(KeepLeadingComments(u.GetLeadingTrivia(), lineEndingTrivia)));
                continue;
            }

            if (!droppedComments.TryGetValue(key, out var comments))
            {
                comments = (new List<SyntaxTrivia>(), new List<SyntaxTrivia>());
                droppedComments.Add(key, comments);
            }

            comments.Leading.AddRange(KeepLeadingComments(u.GetLeadingTrivia(), lineEndingTrivia));
            comments.SameLine.AddRange(GetSameLineComments(u.GetTrailingTrivia()));
        }

        // Ends a directive that stays with the comments on its line and those of the duplicates dropped for it.
        UsingDirectiveSyntax WithComments(UsingDirectiveSyntax usingDirective)
        {
            var leading = usingDirective.GetLeadingTrivia();
            var sameLine = GetSameLineComments(usingDirective.GetTrailingTrivia());
            if (droppedComments.TryGetValue(GetUsingKey(usingDirective), out var dropped))
            {
                leading = leading.AddRange(dropped.Leading);
                sameLine.AddRange(dropped.SameLine);
            }

            return usingDirective.WithLeadingTrivia(leading).WithTrailingTrivia(SyntaxFactory.TriviaList(sameLine).Add(lineEndingTrivia));
        }

        // Remove usings from namespaces
        var rewriter = new RemoveNamespaceUsingsRewriter();
        var updatedRoot = (CompilationUnitSyntax)rewriter.Visit(root);

        // Build merged usings list
        var allUsings = updatedRoot.Usings.Concat(newUsingsToAdd).Select(WithComments).ToList();

        // When there were no top-level usings, the first directive takes over the leading trivia of the first token (e.g.
        // the file header) - unless extern alias directives come first: the directives follow them, and the header stays
        // in front of them.
        if (existingTopUsings.Count == 0 && allUsings.Count > 0 && updatedRoot.Externs.Count == 0)
        {
            var firstToken = updatedRoot.GetFirstToken();
            allUsings[0] = allUsings[0].WithLeadingTrivia(firstToken.LeadingTrivia.AddRange(allUsings[0].GetLeadingTrivia()));
            updatedRoot = updatedRoot.ReplaceToken(firstToken, firstToken.WithLeadingTrivia(SyntaxFactory.TriviaList()));
        }

        // Ensure there is an empty line between usings and the next code element
        if (allUsings.Count > 0)
        {
            var lastIndex = allUsings.Count - 1;
            var lastUsing = allUsings[lastIndex];
            allUsings[lastIndex] = lastUsing.WithTrailingTrivia(lastUsing.GetTrailingTrivia().Add(lineEndingTrivia));
        }

        if (updatedRoot.Members.Count > 0)
        {
            var firstMemberToken = updatedRoot.Members[0].GetFirstToken();
            var leadingTrivia = firstMemberToken.LeadingTrivia;
            var trimmedTrivia = leadingTrivia.SkipWhile(t => t.IsKind(SyntaxKind.EndOfLineTrivia) || t.IsKind(SyntaxKind.WhitespaceTrivia)).ToList();
            if (trimmedTrivia.Count != leadingTrivia.Count)
            {
                updatedRoot = updatedRoot.ReplaceToken(firstMemberToken, firstMemberToken.WithLeadingTrivia(trimmedTrivia));
            }
        }

        updatedRoot = updatedRoot.WithUsings(SyntaxFactory.List(allUsings));

        return updatedRoot.ToFullString();
    }

    /// <summary>
    /// Moves the file-level using directives (see <see cref="GetFileLevelUsings" />) into the only top-level namespace,
    /// in front of the directives already there (after its extern alias directives), with their comments, indented as
    /// <see cref="GetMemberIndentation" /> says and followed by a blank line when members follow. A directive that
    /// imports what a directive already in the namespace or an earlier moved one imports (see
    /// <see cref="GetImportKey" />) is dropped; its comments go to the directive that stays. When the first moved
    /// directive starts the file, its leading trivia is the file header, which stays at the top of the file.
    /// </summary>
    private static string MoveUsingsInside(
        CompilationUnitSyntax root,
        IReadOnlyDictionary<UsingDirectiveSyntax, ISymbol> targets,
        IReadOnlyDictionary<UsingDirectiveSyntax, UsingDirectiveSyntax> qualifiedUsings,
        SemanticModel semanticModel,
        string newline,
        CancellationToken cancellationToken)
    {
        var lineEnding = newline == "\r\n" ? SyntaxFactory.CarriageReturnLineFeed : SyntaxFactory.LineFeed;
        var ns = GetTopLevelNamespaces(root).Single();
        var fileLevelUsings = GetFileLevelUsings(root).ToList();
        var headerOwner = StartsFile(root, fileLevelUsings[0]) ? fileLevelUsings[0] : null;
        var indentation = GetMemberIndentation(ns);

        var existingUsings = ns.Usings
            .Select(usingDirective => (Directive: usingDirective, Key: GetImportKey(usingDirective, GetTarget(usingDirective, semanticModel, cancellationToken))))
            .ToList();
        var keptKeys = new HashSet<string>(existingUsings.Select(existing => existing.Key), StringComparer.Ordinal);
        var droppedComments = new Dictionary<string, (List<SyntaxTrivia> Leading, List<SyntaxTrivia> SameLine)>(StringComparer.Ordinal);
        var movedUsings = new List<(UsingDirectiveSyntax Directive, string Key, SyntaxTriviaList Comments)>();
        foreach (var usingDirective in fileLevelUsings)
        {
            var key = GetImportKey(usingDirective, targets[usingDirective]);
            var comments = usingDirective == headerOwner
                ? SyntaxFactory.TriviaList()
                : KeepLeadingComments(usingDirective.GetLeadingTrivia(), lineEnding, indentation);
            if (keptKeys.Add(key))
            {
                movedUsings.Add((qualifiedUsings.TryGetValue(usingDirective, out var qualified) ? qualified : usingDirective, key, comments));
                continue;
            }

            if (!droppedComments.TryGetValue(key, out var dropped))
            {
                dropped = (new List<SyntaxTrivia>(), new List<SyntaxTrivia>());
                droppedComments.Add(key, dropped);
            }

            dropped.Leading.AddRange(comments);
            dropped.SameLine.AddRange(GetSameLineComments(usingDirective.GetTrailingTrivia()));
        }

        // A moved directive starts with its comments and those of the duplicates dropped for it, then the indentation,
        // and ends with the comments on its line and those of the dropped duplicates.
        UsingDirectiveSyntax Place((UsingDirectiveSyntax Directive, string Key, SyntaxTriviaList Comments) moved)
        {
            var leading = moved.Comments;
            var sameLine = GetSameLineComments(moved.Directive.GetTrailingTrivia());
            if (droppedComments.TryGetValue(moved.Key, out var dropped))
            {
                leading = leading.AddRange(dropped.Leading);
                sameLine.AddRange(dropped.SameLine);
            }

            if (indentation.Length > 0 && EndsLine(leading))
            {
                leading = leading.Add(SyntaxFactory.Whitespace(indentation));
            }

            return moved.Directive.WithLeadingTrivia(leading).WithTrailingTrivia(SyntaxFactory.TriviaList(sameLine).Add(lineEnding));
        }

        // A directive already in the namespace receives the comments of the moved duplicates dropped for it, between its
        // own leading comments and the indentation of its line.
        UsingDirectiveSyntax KeepInPlace((UsingDirectiveSyntax Directive, string Key) existing)
        {
            if (!droppedComments.TryGetValue(existing.Key, out var dropped))
            {
                return existing.Directive;
            }

            var leading = existing.Directive.GetLeadingTrivia();
            var lineStart = leading.Count > 0 && leading.Last().IsKind(SyntaxKind.WhitespaceTrivia) ? leading.Count - 1 : leading.Count;
            var sameLine = GetSameLineComments(existing.Directive.GetTrailingTrivia());
            sameLine.AddRange(dropped.SameLine);

            return existing.Directive
                .WithLeadingTrivia(SyntaxFactory.TriviaList(leading.Take(lineStart).Concat(dropped.Leading).Concat(leading.Skip(lineStart))))
                .WithTrailingTrivia(SyntaxFactory.TriviaList(sameLine).Add(lineEnding));
        }

        var placedUsings = movedUsings.Select(Place).ToList();
        var keptUsings = existingUsings.Select(KeepInPlace).ToList();
        var updatedNamespace = ns;
        if (placedUsings.Count > 0)
        {
            var insertionToken = GetInsertionToken(ns);
            var firstMemberToken = default(SyntaxToken);
            if (keptUsings.Count > 0)
            {
                // The moved directives and those already there form one block.
                keptUsings[0] = keptUsings[0].WithLeadingTrivia(TrimLeadingBlankLines(keptUsings[0].GetLeadingTrivia()));
            }
            else if (ns.Members.Count > 0)
            {
                // One blank line between the directives and the first member.
                firstMemberToken = ns.Members[0].GetFirstToken();
                var last = placedUsings[placedUsings.Count - 1];
                placedUsings[placedUsings.Count - 1] = last.WithTrailingTrivia(last.GetTrailingTrivia().Add(lineEnding));
            }

            if (ns is FileScopedNamespaceDeclarationSyntax && ns.Externs.Count == 0)
            {
                // One blank line between "namespace N;" and the directives.
                placedUsings[0] = placedUsings[0].WithLeadingTrivia(placedUsings[0].GetLeadingTrivia().Insert(0, lineEnding));
            }

            updatedNamespace = ns.ReplaceTokens(
                new[] { insertionToken, firstMemberToken }.Where(token => !token.IsKind(SyntaxKind.None)),
                (original, _) => original == insertionToken
                    ? EndLine(original, lineEnding)
                    : original.WithLeadingTrivia(TrimLeadingBlankLines(original.LeadingTrivia)));
        }

        updatedNamespace = updatedNamespace.WithUsings(SyntaxFactory.List(placedUsings.Concat(keptUsings)));

        var updatedRoot = root.ReplaceNode(ns, updatedNamespace);
        updatedRoot = updatedRoot.WithUsings(SyntaxFactory.List(updatedRoot.Usings.Where(usingDirective => !usingDirective.GlobalKeyword.IsKind(SyntaxKind.None))));

        if (headerOwner != null)
        {
            // The file header stays at the top of the file, in front of what is now the first token.
            var firstToken = updatedRoot.GetFirstToken();
            updatedRoot = updatedRoot.ReplaceToken(
                firstToken,
                firstToken.WithLeadingTrivia(headerOwner.GetLeadingTrivia().AddRange(TrimLeadingBlankLines(firstToken.LeadingTrivia))));
        }

        return updatedRoot.ToFullString();
    }

    /// <summary>
    /// Gets the indentation of the directives moved into <paramref name="ns" />: none in a file-scoped namespace; in a
    /// block-scoped namespace, that of the first extern alias directive, using directive or member that starts its line,
    /// so tabs or any indentation size of the file are kept. Only when there is none, one level (four spaces) deeper than
    /// the line of the namespace keyword.
    /// </summary>
    private static string GetMemberIndentation(BaseNamespaceDeclarationSyntax ns)
    {
        if (ns is FileScopedNamespaceDeclarationSyntax)
        {
            return string.Empty;
        }

        var text = ns.SyntaxTree.GetText();
        foreach (var node in ns.Externs.Cast<SyntaxNode>().Concat(ns.Usings).Concat(ns.Members))
        {
            var lineStart = text.Lines.GetLineFromPosition(node.SpanStart).Start;
            var indentation = text.ToString(TextSpan.FromBounds(lineStart, node.SpanStart));
            if (indentation.All(char.IsWhiteSpace))
            {
                return indentation;
            }
        }

        var leading = ns.GetFirstToken().LeadingTrivia;
        var lineIndentation = leading.Count > 0
            && leading[leading.Count - 1].IsKind(SyntaxKind.WhitespaceTrivia)
            && (leading.Count == 1 || leading[leading.Count - 2].IsKind(SyntaxKind.EndOfLineTrivia))
                ? leading[leading.Count - 1].ToString()
                : string.Empty;

        return lineIndentation + "    ";
    }

    /// <summary>
    /// Removes the blank lines at the start of leading trivia, keeping the indentation of its first line with content.
    /// </summary>
    private static SyntaxTriviaList TrimLeadingBlankLines(SyntaxTriviaList trivia)
    {
        var lastLineEnd = -1;
        for (var i = 0; i < trivia.Count && (trivia[i].IsKind(SyntaxKind.WhitespaceTrivia) || trivia[i].IsKind(SyntaxKind.EndOfLineTrivia)); i++)
        {
            if (trivia[i].IsKind(SyntaxKind.EndOfLineTrivia))
            {
                lastLineEnd = i;
            }
        }

        return lastLineEnd < 0 ? trivia : SyntaxFactory.TriviaList(trivia.Skip(lastLineEnd + 1));
    }

    /// <summary>
    /// Returns <paramref name="token" /> ending its line: unless its trailing trivia already ends the line, its trailing
    /// comments are followed by a line ending.
    /// </summary>
    private static SyntaxToken EndLine(SyntaxToken token, SyntaxTrivia lineEnding) =>
        token.TrailingTrivia.Any(SyntaxKind.EndOfLineTrivia)
            ? token
            : token.WithTrailingTrivia(SyntaxFactory.TriviaList(GetSameLineComments(token.TrailingTrivia)).Add(lineEnding));

    /// <summary>
    /// Reduces the leading trivia of a moved directive to its comments, each followed by a line ending, or by a space
    /// when it shares the line with the directive, and each that starts a line preceded by
    /// <paramref name="indentation" />. The original indentation and blank lines are dropped.
    /// </summary>
    private static SyntaxTriviaList KeepLeadingComments(SyntaxTriviaList trivia, SyntaxTrivia lineEnding, string indentation = "")
    {
        var kept = new List<SyntaxTrivia>();
        for (var i = 0; i < trivia.Count; i++)
        {
            if (!IsComment(trivia[i]))
            {
                continue;
            }

            if (indentation.Length > 0 && EndsLine(kept))
            {
                kept.Add(SyntaxFactory.Whitespace(indentation));
            }

            kept.Add(trivia[i]);

            // A /// comment ends with its own line ending.
            if (!trivia[i].IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
            {
                var next = trivia.Skip(i + 1).FirstOrDefault(t => !t.IsKind(SyntaxKind.WhitespaceTrivia));
                kept.Add(next.IsKind(SyntaxKind.EndOfLineTrivia) ? lineEnding : SyntaxFactory.Space);
            }
        }

        return SyntaxFactory.TriviaList(kept);
    }

    /// <summary>
    /// Determines whether what follows <paramref name="trivia" /> starts a line: the trivia is empty or ends with a line
    /// ending (a <c>///</c> comment includes its own).
    /// </summary>
    private static bool EndsLine(IReadOnlyList<SyntaxTrivia> trivia) =>
        trivia.Count == 0
        || trivia[trivia.Count - 1].IsKind(SyntaxKind.EndOfLineTrivia)
        || trivia[trivia.Count - 1].IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia);

    /// <summary>
    /// Gets the comments on the line of a directive from its trailing trivia, with the whitespace in front of them.
    /// </summary>
    private static List<SyntaxTrivia> GetSameLineComments(SyntaxTriviaList trailingTrivia)
    {
        var sameLine = trailingTrivia.TakeWhile(t => !t.IsKind(SyntaxKind.EndOfLineTrivia)).ToList();

        return sameLine.GetRange(0, sameLine.FindLastIndex(IsComment) + 1);
    }

    private static bool IsComment(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);

    /// <summary>
    /// Gets a key that identifies a using directive by its tokens alone, so that directives that differ only in
    /// whitespace or comments (for example the file header the first directive carries) are considered equal.
    /// </summary>
    private static string GetUsingKey(UsingDirectiveSyntax usingDirective) =>
        string.Join(" ", usingDirective.DescendantTokens().Select(token => token.Text));

    /// <summary>
    /// What a node binds to: a symbol, or the candidates (and why) when it does not bind to exactly one.
    /// </summary>
    private readonly struct Binding
    {
        private readonly ISymbol[] _symbols;
        private readonly CandidateReason _reason;

        private Binding(ISymbol[] symbols, CandidateReason reason)
        {
            _symbols = symbols;
            _reason = reason;
        }

        public static Binding None { get; } = new Binding(Array.Empty<ISymbol>(), CandidateReason.None);

        /// <summary>
        /// Gets the identity of the binding, comparable across compilations.
        /// </summary>
        public string Identity =>
            _reason + ":" + string.Join("|", _symbols.Select(Identify).OrderBy(identity => identity, StringComparer.Ordinal));

        public static Binding Of(ISymbol symbol) => symbol == null ? None : new Binding(new[] { symbol }, CandidateReason.None);

        public static Binding Of(SymbolInfo info) =>
            info.Symbol != null ? Of(info.Symbol) : new Binding(info.CandidateSymbols.ToArray(), info.CandidateReason);

        /// <summary>
        /// Describes the binding for a skip reason by the names of its symbols, each followed by the name of its
        /// assembly when <paramref name="withAssemblies" /> is set.
        /// </summary>
        /// <param name="withAssemblies">Whether to name the assemblies of the symbols.</param>
        /// <returns>The description, "nothing" when the binding has no symbol.</returns>
        public string Describe(bool withAssemblies) =>
            _symbols.Length == 0
                ? "nothing"
                : string.Join(" or ", _symbols.Select(Normalize).Select(symbol =>
                    symbol.ToDisplayString() + (withAssemblies && symbol.ContainingAssembly != null ? " from " + symbol.ContainingAssembly.Name : string.Empty)));
    }

    /// <summary>
    /// presents a syntax rewriter that removes using directives from namespace declarations, supporting both block-scoped and file-scoped namespace syntaxes.
    /// </summary>
    private sealed class RemoveNamespaceUsingsRewriter : CSharpSyntaxRewriter
    {
        /// <summary>
        /// Overrides `VisitNamespaceDeclaration` to strip all `using` directives from any namespace declaration that contains them, while leaving namespaces without usings untouched.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>A SyntaxNode value produced by this method.</returns>
        public override SyntaxNode VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            if (node.Usings.Count == 0)
            {
                return base.VisitNamespaceDeclaration(node);
            }

            var visited = (NamespaceDeclarationSyntax)base.VisitNamespaceDeclaration(node);

            return visited.WithUsings(SyntaxFactory.List<UsingDirectiveSyntax>());
        }

        /// <summary>
        /// the file-scoped namespace visitor to strip all `using` directives from the declaration, returning the node unchanged when it contains no usings.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>A SyntaxNode value produced by this method.</returns>
        public override SyntaxNode VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        {
            if (node.Usings.Count == 0)
            {
                return base.VisitFileScopedNamespaceDeclaration(node);
            }

            var visited = (FileScopedNamespaceDeclarationSyntax)base.VisitFileScopedNamespaceDeclaration(node);

            return visited.WithUsings(SyntaxFactory.List<UsingDirectiveSyntax>());
        }
    }

    /// <summary>
    /// One direction of the move: which using directives move, when the document's layout does not allow it, how they
    /// are moved, where they end up, and how a fully qualified name is written there. The resolution and verification
    /// of the move are shared (see <see cref="MoveAsync" />).
    /// </summary>
    private abstract class UsingMove
    {
        /// <summary>
        /// Gets how a fully qualified name is written where the directives go.
        /// </summary>
        public abstract SymbolDisplayFormat QualifiedNameFormat { get; }

        /// <summary>
        /// Gets where the moved directives come from, for skip reasons (for example "outside the namespace").
        /// </summary>
        public abstract string Origin { get; }

        /// <summary>
        /// Gets the directives that move, in document order.
        /// </summary>
        /// <param name="root">The compilation unit.</param>
        /// <returns>The directives.</returns>
        public abstract IReadOnlyList<UsingDirectiveSyntax> GetMovedUsings(CompilationUnitSyntax root);

        /// <summary>
        /// Gets why the layout of the document does not allow the move, or null when it does.
        /// </summary>
        /// <param name="root">The compilation unit.</param>
        /// <param name="movedUsings">The directives that move, at least one.</param>
        /// <returns>The skip reason, or null.</returns>
        public abstract string FindUnsupportedLayout(CompilationUnitSyntax root, IReadOnlyList<UsingDirectiveSyntax> movedUsings);

        /// <summary>
        /// Moves the directives and returns the new text.
        /// </summary>
        /// <param name="root">The compilation unit.</param>
        /// <param name="targets">The target of every moved directive, resolved where it is.</param>
        /// <param name="qualifiedUsings">The moved directives that are written as their fully qualified replacement.</param>
        /// <param name="semanticModel">The semantic model of <paramref name="root" />.</param>
        /// <param name="newline">The line ending of the document.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The moved text.</returns>
        public abstract string Move(
            CompilationUnitSyntax root,
            IReadOnlyDictionary<UsingDirectiveSyntax, ISymbol> targets,
            IReadOnlyDictionary<UsingDirectiveSyntax, UsingDirectiveSyntax> qualifiedUsings,
            SemanticModel semanticModel,
            string newline,
            CancellationToken cancellationToken);

        /// <summary>
        /// Gets the directives where the moved directives end up, in a moved document.
        /// </summary>
        /// <param name="root">The compilation unit of the moved document.</param>
        /// <returns>The directives.</returns>
        public abstract IEnumerable<UsingDirectiveSyntax> GetPlacedUsings(CompilationUnitSyntax root);
    }

    /// <summary>
    /// Moves the using directives of every namespace to the top of the compilation unit.
    /// </summary>
    private sealed class OutwardMove : UsingMove
    {
        public static OutwardMove Instance { get; } = new OutwardMove();

        public override SymbolDisplayFormat QualifiedNameFormat => FileLevelNameFormat;

        public override string Origin => "inside the namespace";

        public override IReadOnlyList<UsingDirectiveSyntax> GetMovedUsings(CompilationUnitSyntax root) =>
            GetNamespaceUsings(root).ToList();

        public override string FindUnsupportedLayout(CompilationUnitSyntax root, IReadOnlyList<UsingDirectiveSyntax> movedUsings) =>
            HasInterleavedPreprocessorDirectives(root, movedUsings) ? InterleavedPreprocessorDirectives : null;

        public override string Move(
            CompilationUnitSyntax root,
            IReadOnlyDictionary<UsingDirectiveSyntax, ISymbol> targets,
            IReadOnlyDictionary<UsingDirectiveSyntax, UsingDirectiveSyntax> qualifiedUsings,
            SemanticModel semanticModel,
            string newline,
            CancellationToken cancellationToken) =>
            MoveUsingsOutside(root.ReplaceNodes(qualifiedUsings.Keys, (original, _) => qualifiedUsings[original]), newline);

        public override IEnumerable<UsingDirectiveSyntax> GetPlacedUsings(CompilationUnitSyntax root) => root.Usings;
    }

    /// <summary>
    /// Moves the file-level using directives into the only top-level namespace.
    /// </summary>
    private sealed class InwardMove : UsingMove
    {
        public static InwardMove Instance { get; } = new InwardMove();

        public override SymbolDisplayFormat QualifiedNameFormat => NamespaceLevelNameFormat;

        public override string Origin => "outside the namespace";

        public override IReadOnlyList<UsingDirectiveSyntax> GetMovedUsings(CompilationUnitSyntax root) =>
            GetFileLevelUsings(root).ToList();

        public override string FindUnsupportedLayout(CompilationUnitSyntax root, IReadOnlyList<UsingDirectiveSyntax> movedUsings)
        {
            var namespaces = GetTopLevelNamespaces(root).ToList();
            if (namespaces.Count == 0)
            {
                return "the file declares no namespace to move them into";
            }

            if (namespaces.Count > 1)
            {
                return $"the file declares {namespaces.Count} namespaces, so there is no single namespace to move them into";
            }

            return HasInterleavedPreprocessorDirectives(root, movedUsings, namespaces[0]) ? InterleavedPreprocessorDirectives : null;
        }

        public override string Move(
            CompilationUnitSyntax root,
            IReadOnlyDictionary<UsingDirectiveSyntax, ISymbol> targets,
            IReadOnlyDictionary<UsingDirectiveSyntax, UsingDirectiveSyntax> qualifiedUsings,
            SemanticModel semanticModel,
            string newline,
            CancellationToken cancellationToken) =>
            MoveUsingsInside(root, targets, qualifiedUsings, semanticModel, newline, cancellationToken);

        public override IEnumerable<UsingDirectiveSyntax> GetPlacedUsings(CompilationUnitSyntax root) =>
            GetTopLevelNamespaces(root).Take(1).SelectMany(ns => ns.Usings);
    }
}
