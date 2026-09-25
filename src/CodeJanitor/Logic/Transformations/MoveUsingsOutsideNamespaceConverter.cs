using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
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
/// directive cannot be resolved, preprocessor directives are interleaved with the using directives, the moved
/// document has compile errors the original did not have, or any name in it would bind to a different symbol, the
/// document is left unchanged and the reason is reported.
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

        if (HasInterleavedPreprocessorDirectives(root))
        {
            return MoveUsingsOutsideNamespaceResult.Skipped("the using directives are interleaved with preprocessor directives");
        }

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var targets = new Dictionary<UsingDirectiveSyntax, ISymbol>();
        foreach (var usingDirective in namespaceUsings)
        {
            var target = GetTarget(usingDirective, semanticModel, cancellationToken);
            if (!IsResolved(target))
            {
                return MoveUsingsOutsideNamespaceResult.Skipped(
                    $"'{usingDirective.WithoutTrivia().ToFullString()}' cannot be resolved semantically");
            }

            targets.Add(usingDirective, target);
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

        var newErrors = FindNewErrors(semanticModel.GetDiagnostics(cancellationToken: cancellationToken), movedModel.GetDiagnostics(cancellationToken: cancellationToken));
        if (newErrors.Count > 0)
        {
            return MoveUsingsOutsideNamespaceResult.Skipped(
                $"moving them would introduce {newErrors.Count} new compile error(s): {string.Join("; ", newErrors.Take(3))}");
        }

        var bindingChange = FindChangedBinding(semanticModel, movedModel, cancellationToken);
        if (bindingChange != null)
        {
            return MoveUsingsOutsideNamespaceResult.Skipped(bindingChange);
        }

        return MoveUsingsOutsideNamespaceResult.Moved(movedText);
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
    /// Determines whether a preprocessor directive sits before, between or directly after the using directives of a
    /// namespace. Moving the directives would separate them from their <c>#if</c>/<c>#region</c> brackets.
    /// </summary>
    private static bool HasInterleavedPreprocessorDirectives(CompilationUnitSyntax root) =>
        GetNamespacesWithUsings(root).Any(ns =>
            ns.Usings.Any(u => u.GetLeadingTrivia().Any(t => t.IsDirective) || u.GetTrailingTrivia().Any(t => t.IsDirective))
            || ns.Usings.Last().GetLastToken().GetNextToken().LeadingTrivia.Any(t => t.IsDirective));

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
    private static string Identify(ISymbol symbol) =>
        symbol == null ? null : symbol.Kind + ":" + symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>
    /// Returns <paramref name="usingDirective" /> with its name or alias target replaced by the fully qualified name
    /// of <paramref name="target" />.
    /// </summary>
    private static UsingDirectiveSyntax Qualify(UsingDirectiveSyntax usingDirective, ISymbol target) =>
        usingDirective.WithNamespaceOrType(
            SyntaxFactory.ParseTypeName(target.ToDisplayString(QualifiedNameFormat)).WithTriviaFrom(usingDirective.NamespaceOrType));

    /// <summary>
    /// Finds the first name outside the using directives whose binding differs after the move (for example an import
    /// that is now searched after a same-named type of an enclosing namespace) and returns the skip reason; null when
    /// all names bind as before. Only using directives move, so the names of both trees correspond in document order.
    /// </summary>
    private static string FindChangedBinding(SemanticModel before, SemanticModel after, CancellationToken cancellationToken)
    {
        var beforeNames = GetNamesOutsideUsings(before, cancellationToken);
        var afterNames = GetNamesOutsideUsings(after, cancellationToken);
        if (beforeNames.Count != afterNames.Count)
        {
            return "the names in the moved file could not be matched with the original, so the move could not be verified";
        }

        for (var i = 0; i < beforeNames.Count; i++)
        {
            var beforeSymbol = GetBinding(before, beforeNames[i], cancellationToken);
            var afterSymbol = GetBinding(after, afterNames[i], cancellationToken);
            if (Identify(beforeSymbol) != Identify(afterSymbol))
            {
                return $"moving them would change what '{beforeNames[i].Identifier.ValueText}' refers to ({Describe(beforeSymbol)} -> {Describe(afterSymbol)})";
            }
        }

        return null;
    }

    private static List<SimpleNameSyntax> GetNamesOutsideUsings(SemanticModel semanticModel, CancellationToken cancellationToken) =>
        semanticModel.SyntaxTree.GetCompilationUnitRoot(cancellationToken)
            .DescendantNodes(node => !(node is UsingDirectiveSyntax))
            .OfType<SimpleNameSyntax>()
            .ToList();

    private static ISymbol GetBinding(SemanticModel semanticModel, SimpleNameSyntax name, CancellationToken cancellationToken)
    {
        var info = semanticModel.GetSymbolInfo(name, cancellationToken);

        return info.Symbol ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] : null);
    }

    private static string Describe(ISymbol symbol) => symbol?.ToDisplayString() ?? "nothing";

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
    /// file header and line endings and deduplicating against the existing top-level directives.
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

        var newUsingsToAdd = new List<UsingDirectiveSyntax>();
        foreach (var u in extractedUsings)
        {
            var key = GetUsingKey(u);
            if (existingKeys.Add(key))
            {
                var cleanUsing = u.WithoutTrivia().WithTrailingTrivia(lineEndingTrivia);
                newUsingsToAdd.Add(cleanUsing);
            }
        }

        // Remove usings from namespaces
        var rewriter = new RemoveNamespaceUsingsRewriter();
        var updatedRoot = (CompilationUnitSyntax)rewriter.Visit(root);

        // Build merged usings list
        var allUsings = new List<UsingDirectiveSyntax>();
        if (existingTopUsings.Count > 0)
        {
            for (int i = 0; i < updatedRoot.Usings.Count; i++)
            {
                allUsings.Add(updatedRoot.Usings[i].WithoutTrailingTrivia().WithTrailingTrivia(lineEndingTrivia));
            }

            foreach (var u in newUsingsToAdd)
            {
                allUsings.Add(u.WithoutTrailingTrivia().WithTrailingTrivia(lineEndingTrivia));
            }
        }
        else
        {
            // When there were no top-level usings, transfer leading trivia from the first token (e.g. file header)
            // to the first using directive.
            var firstToken = updatedRoot.GetFirstToken();
            var leadingTrivia = firstToken.LeadingTrivia;

            if (newUsingsToAdd.Count > 0)
            {
                var firstUsing = newUsingsToAdd[0].WithLeadingTrivia(leadingTrivia).WithoutTrailingTrivia().WithTrailingTrivia(lineEndingTrivia);
                allUsings.Add(firstUsing);
                for (int i = 1; i < newUsingsToAdd.Count; i++)
                {
                    allUsings.Add(newUsingsToAdd[i].WithoutTrailingTrivia().WithTrailingTrivia(lineEndingTrivia));
                }

                updatedRoot = updatedRoot.ReplaceToken(firstToken, firstToken.WithLeadingTrivia(SyntaxFactory.TriviaList()));
            }
        }

        // Ensure there is an empty line between usings and the next code element
        if (allUsings.Count > 0)
        {
            var lastIndex = allUsings.Count - 1;
            var lastUsing = allUsings[lastIndex];
            allUsings[lastIndex] = lastUsing.WithTrailingTrivia(lineEndingTrivia, lineEndingTrivia);
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
    /// method generates a normalized string key from a `UsingDirectiveSyntax` node by applying default whitespace formatting, converting the node to its full text representation, and trimming the resulting string.
    /// </summary>
    /// <param name="u">The u.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string GetUsingKey(UsingDirectiveSyntax u)
    {
        return u.NormalizeWhitespace().ToFullString().Trim();
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
