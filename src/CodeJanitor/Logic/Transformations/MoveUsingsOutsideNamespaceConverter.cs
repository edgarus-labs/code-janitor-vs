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
/// Moves using directives from inside namespace declarations (both block-scoped and file-scoped)
/// to the top-level compilation unit (outside namespace), preserving file headers and deduplicating directives.
/// </summary>
/// <remarks>
/// Inside a namespace, a using directive's name is resolved against the enclosing namespaces first
/// (<c>using Services;</c> in <c>namespace Company.App</c> may mean <c>Company.App.Services</c>); at file level
/// only the global namespace is searched. Syntax alone cannot tell which namespace a name refers to, so every
/// moved directive is resolved with the document's semantic model. A directive that means the same at file level
/// keeps its exact text; any other directive is written fully qualified. The move is all-or-nothing: when a
/// directive cannot be resolved, the directives would move across preprocessor directives, a moved directive imports
/// an extension method that the file calls implicitly where the call cannot be verified, the moved document has
/// compile errors the original did not have, or any name, member or implicitly called member (for example the
/// GetEnumerator of a foreach) would bind to a different symbol, the document is left unchanged and the reason is
/// reported. These checks run in the active configuration and in every other assignment of the conditional
/// compilation symbols the document depends on (see <see cref="ConditionalCompilationVariants" />); a document that
/// depends on more than <see cref="MaxConditionalCompilationSymbols" /> of them is left unchanged.
/// </remarks>
public sealed class MoveUsingsOutsideNamespaceConverter
{
    /// <summary>
    /// Fully qualified names without the <c>global::</c> prefix: at file level a name already starts at the
    /// global namespace, so the prefix adds nothing but noise. <c>Nullable&lt;T&gt;</c> is written out because the
    /// <c>T?</c> shorthand is not valid as an alias target before C# 12.
    /// </summary>
    private static readonly SymbolDisplayFormat QualifiedNameFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)
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

    /// <summary>
    /// Determines, from syntax alone, whether <paramref name="source" /> has using directives inside a namespace,
    /// i.e. whether <see cref="MoveUsingsOutsideAsync" /> has anything to do. Hosts use it to skip the semantic work.
    /// </summary>
    /// <param name="source">The C# source code.</param>
    /// <returns>True when at least one namespace declaration contains a using directive.</returns>
    public static bool HasUsingsInsideNamespace(string source) =>
        !string.IsNullOrEmpty(source) && GetNamespaceUsings(CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot()).Any();

    /// <summary>
    /// Moves the using directives located inside the namespaces of <paramref name="document" /> to the top of the
    /// compilation unit, fully qualifying names that were resolved relative to an enclosing namespace.
    /// </summary>
    /// <param name="document">The C# document, bound to the project it compiles in.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The moved text, or why the document was left unchanged.</returns>
    public async Task<MoveUsingsOutsideNamespaceResult> MoveUsingsOutsideAsync(Document document, CancellationToken cancellationToken)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (!(await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) is CompilationUnitSyntax root))
        {
            return MoveUsingsOutsideNamespaceResult.NoUsingsInsideNamespace;
        }

        var namespaceUsings = GetNamespaceUsings(root).ToList();

        if (namespaceUsings.Count == 0)
        {
            return MoveUsingsOutsideNamespaceResult.NoUsingsInsideNamespace;
        }

        if (HasInterleavedPreprocessorDirectives(root, namespaceUsings))
        {
            return MoveUsingsOutsideNamespaceResult.Skipped("the using directives are interleaved with preprocessor directives");
        }

        var symbols = await ConditionalCompilationVariants.GetRelevantSymbolsAsync(document, root, cancellationToken).ConfigureAwait(false);
        if (symbols.Count > MaxConditionalCompilationSymbols)
        {
            return MoveUsingsOutsideNamespaceResult.Skipped(
                $"the move depends on the conditional compilation symbols {string.Join(", ", symbols)}, and more than {MaxConditionalCompilationSymbols} conditional-compilation symbols are too many variants to verify");
        }

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var unsafeDirective = ResolveTargets(namespaceUsings, semanticModel, cancellationToken, out var targets)
            ?? FindUnverifiableImplicitCall(root, targets, semanticModel, cancellationToken);
        if (unsafeDirective != null)
        {
            return MoveUsingsOutsideNamespaceResult.Skipped(unsafeDirective);
        }

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var newline = text.ToString().Contains("\r\n") ? "\r\n" : "\n";

        // Move the directives verbatim first: every directive that still means the same at file level (already fully
        // qualified, keyword/tuple syntax, global:: or extern-alias qualified) keeps its exact text. Only the others are
        // rewritten fully qualified.
        var verbatimText = MoveUsingsOutside(root, newline);
        var verbatimModel = await GetSemanticModelAsync(document, verbatimText, text, cancellationToken).ConfigureAwait(false);
        var fileLevelTargets = verbatimModel.SyntaxTree.GetCompilationUnitRoot(cancellationToken).Usings
            .GroupBy(GetUsingKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Identify(GetTarget(group.First(), verbatimModel, cancellationToken)), StringComparer.Ordinal);

        var qualifiedUsings = targets.ToDictionary(
            pair => pair.Key,
            pair => fileLevelTargets.TryGetValue(GetUsingKey(pair.Key), out var fileLevelTarget) && fileLevelTarget == Identify(pair.Value)
                ? pair.Key
                : Qualify(pair.Key, pair.Value));

        var movedText = MoveUsingsOutside(root.ReplaceNodes(qualifiedUsings.Keys, (original, _) => qualifiedUsings[original]), newline);
        var movedModel = movedText == verbatimText
            ? verbatimModel
            : await GetSemanticModelAsync(document, movedText, text, cancellationToken).ConfigureAwait(false);

        var unsafeChange = FindUnsafeChange(semanticModel, movedModel, cancellationToken);
        if (unsafeChange != null)
        {
            return MoveUsingsOutsideNamespaceResult.Skipped(unsafeChange);
        }

        foreach (var variant in ConditionalCompilationVariants.GetOtherVariants(document, symbols))
        {
            var unsafeVariantChange = await FindUnsafeChangeInVariantAsync(variant.Solution.GetDocument(document.Id), movedText, text, cancellationToken).ConfigureAwait(false);
            if (unsafeVariantChange != null)
            {
                return MoveUsingsOutsideNamespaceResult.Skipped($"with {variant.Description}: {unsafeVariantChange}");
            }
        }

        return MoveUsingsOutsideNamespaceResult.Moved(movedText);
    }

    /// <summary>
    /// Verifies <paramref name="movedText" /> in a build variant: <paramref name="variant" /> is the original document in
    /// the solution parsed with the variant's symbols, against which the checks of the active configuration are run.
    /// Returns the skip reason, or null when the move is safe in the variant.
    /// </summary>
    private static async Task<string> FindUnsafeChangeInVariantAsync(Document variant, string movedText, SourceText originalText, CancellationToken cancellationToken)
    {
        var root = (CompilationUnitSyntax)await variant.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await variant.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var unsafeDirective = ResolveTargets(GetNamespaceUsings(root), semanticModel, cancellationToken, out var targets)
            ?? FindUnverifiableImplicitCall(root, targets, semanticModel, cancellationToken);
        if (unsafeDirective != null)
        {
            return unsafeDirective;
        }

        var movedModel = await GetSemanticModelAsync(variant, movedText, originalText, cancellationToken).ConfigureAwait(false);

        return FindUnsafeChange(semanticModel, movedModel, cancellationToken);
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
    /// when the move adds compile errors or changes what a node binds to, otherwise null.
    /// </summary>
    private static string FindUnsafeChange(SemanticModel before, SemanticModel after, CancellationToken cancellationToken)
    {
        var newErrors = FindNewErrors(before.GetDiagnostics(cancellationToken: cancellationToken), after.GetDiagnostics(cancellationToken: cancellationToken));

        return newErrors.Count > 0
            ? $"moving them would introduce {newErrors.Count} new compile error(s): {string.Join("; ", newErrors.Take(3))}"
            : FindChangedBinding(before, after, cancellationToken);
    }

    private static Task<SemanticModel> GetSemanticModelAsync(Document document, string newText, SourceText originalText, CancellationToken cancellationToken) =>
        document.WithText(SourceText.From(newText, originalText.Encoding, originalText.ChecksumAlgorithm)).GetSemanticModelAsync(cancellationToken);

    /// <summary>
    /// Gets the using directives of every (nested) namespace declaration; namespaces can only be declared in the
    /// compilation unit or in another namespace, so no other nodes are visited.
    /// </summary>
    private static IEnumerable<UsingDirectiveSyntax> GetNamespaceUsings(CompilationUnitSyntax root) =>
        GetNamespacesWithUsings(root).SelectMany(ns => ns.Usings);

    private static IEnumerable<BaseNamespaceDeclarationSyntax> GetNamespacesWithUsings(CompilationUnitSyntax root) =>
        root.DescendantNodes(node => node is CompilationUnitSyntax || node is BaseNamespaceDeclarationSyntax)
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Where(ns => ns.Usings.Count > 0);

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
    /// Identifies a symbol independently of the compilation it was bound in.
    /// </summary>
    private static string Identify(ISymbol symbol)
    {
        var normalized = Normalize(symbol);

        return normalized == null ? null : normalized.Kind + ":" + normalized.ToDisplayString(IdentityFormat);
    }

    /// <summary>
    /// Returns a reduced extension method (the <c>Twice()</c> of <c>1.Twice()</c>) as the method it calls
    /// (<c>E.Twice(int)</c>), whose identity includes the declaring class and the receiver parameter.
    /// </summary>
    private static ISymbol Normalize(ISymbol symbol) =>
        symbol is IMethodSymbol method && method.ReducedFrom != null ? method.GetConstructedReducedFrom() : symbol;

    /// <summary>
    /// Returns <paramref name="usingDirective" /> with its name or alias target replaced by the fully qualified name
    /// of <paramref name="target" />.
    /// </summary>
    private static UsingDirectiveSyntax Qualify(UsingDirectiveSyntax usingDirective, ISymbol target) =>
        usingDirective.WithNamespaceOrType(
            SyntaxFactory.ParseTypeName(target.ToDisplayString(QualifiedNameFormat)).WithTriviaFrom(usingDirective.NamespaceOrType));

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
                    return $"moving them would change what '{DescribeSite(beforeNodes[i])}' refers to ({beforeBinding} -> {afterBinding})";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the nodes outside the using directives in document order, including those of documentation comments
    /// (<c>cref</c> names bind like code) but not those of other structured trivia such as preprocessor directives, nor
    /// those of a documentation comment in front of a namespace that starts the file: that comment documents nothing,
    /// it is the file header, which the move hands to the first moved directive when the file has no top-level using or
    /// extern alias directive. A documentation comment in front of a type that starts the file is compared, so moving it
    /// to a directive is detected.
    /// </summary>
    private static List<SyntaxNode> GetNodesOutsideUsings(SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var root = semanticModel.SyntaxTree.GetCompilationUnitRoot(cancellationToken);
        var firstToken = root.GetFirstToken();
        var headerEnd = firstToken.Parent is BaseNamespaceDeclarationSyntax ? firstToken.SpanStart : 0;

        bool IsCompared(SyntaxNode node) =>
            !(node is UsingDirectiveSyntax)
            && (!(node is StructuredTriviaSyntax) || (node is DocumentationCommentTriviaSyntax && node.SpanStart >= headerEnd));

        return root.DescendantNodes(IsCompared, descendIntoTrivia: true).Where(IsCompared).ToList();
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

    /// <summary>
    /// Returns the compile errors of <paramref name="after" /> that <paramref name="before" /> does not have,
    /// compared by ID and message (their locations shift when the directives move).
    /// </summary>
    private static List<string> FindNewErrors(IEnumerable<Diagnostic> before, IEnumerable<Diagnostic> after)
    {
        var remaining = before
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .GroupBy(Describe, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var newErrors = new List<string>();
        foreach (var description in after.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(Describe))
        {
            if (remaining.TryGetValue(description, out var count) && count > 0)
            {
                remaining[description] = count - 1;
            }
            else
            {
                newErrors.Add(description);
            }
        }

        return newErrors;
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
    /// Reduces the leading trivia of a moved directive to its comments, each followed by a line ending, or by a space
    /// when it shares the line with the directive. The indentation and blank lines of the namespace are dropped.
    /// </summary>
    private static SyntaxTriviaList KeepLeadingComments(SyntaxTriviaList trivia, SyntaxTrivia lineEnding)
    {
        var kept = new List<SyntaxTrivia>();
        for (var i = 0; i < trivia.Count; i++)
        {
            if (!IsComment(trivia[i]))
            {
                continue;
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

        public override string ToString() =>
            _symbols.Length == 0 ? "nothing" : string.Join(" or ", _symbols.Select(symbol => Normalize(symbol).ToDisplayString()));
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
}
