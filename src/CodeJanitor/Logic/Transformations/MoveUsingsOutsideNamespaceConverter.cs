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
/// moved directive is resolved with the document's semantic model and written fully qualified. The move is
/// all-or-nothing: when a directive cannot be resolved, or the moved document has compile errors the original
/// did not have, the document is left unchanged and the reason is reported.
/// </remarks>
public sealed class MoveUsingsOutsideNamespaceConverter
{
    /// <summary>
    /// Fully qualified names without the <c>global::</c> prefix: at file level a name already starts at the
    /// global namespace, so the prefix adds nothing but noise.
    /// </summary>
    private static readonly SymbolDisplayFormat QualifiedNameFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

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

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var qualifiedUsings = new Dictionary<UsingDirectiveSyntax, UsingDirectiveSyntax>();
        foreach (var usingDirective in namespaceUsings)
        {
            var qualified = Qualify(usingDirective, semanticModel, cancellationToken);
            if (qualified == null)
            {
                return MoveUsingsOutsideNamespaceResult.Skipped(
                    $"'{usingDirective.WithoutTrivia().ToFullString()}' cannot be resolved semantically");
            }

            qualifiedUsings.Add(usingDirective, qualified);
        }

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var newline = text.ToString().Contains("\r\n") ? "\r\n" : "\n";
        var qualifiedRoot = root.ReplaceNodes(qualifiedUsings.Keys, (original, _) => qualifiedUsings[original]);
        var movedText = MoveUsingsOutside(qualifiedRoot, newline);

        var movedDocument = document.WithText(SourceText.From(movedText, text.Encoding, text.ChecksumAlgorithm));
        var movedSemanticModel = await movedDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var newErrors = FindNewErrors(semanticModel.GetDiagnostics(cancellationToken: cancellationToken), movedSemanticModel.GetDiagnostics(cancellationToken: cancellationToken));
        if (newErrors.Count > 0)
        {
            return MoveUsingsOutsideNamespaceResult.Skipped(
                $"moving them would introduce {newErrors.Count} new compile error(s): {string.Join("; ", newErrors.Take(3))}");
        }

        return MoveUsingsOutsideNamespaceResult.Moved(movedText);
    }

    /// <summary>
    /// Gets the using directives of every (nested) namespace declaration; namespaces can only be declared in the
    /// compilation unit or in another namespace, so no other nodes are visited.
    /// </summary>
    private static IEnumerable<UsingDirectiveSyntax> GetNamespaceUsings(CompilationUnitSyntax root) =>
        root.DescendantNodes(node => node is CompilationUnitSyntax || node is BaseNamespaceDeclarationSyntax)
            .OfType<BaseNamespaceDeclarationSyntax>()
            .SelectMany(ns => ns.Usings);

    /// <summary>
    /// Returns <paramref name="usingDirective" /> with its name or alias target fully qualified, the directive itself
    /// when it is already written that way, or null when it does not resolve to a namespace or a valid type.
    /// </summary>
    private static UsingDirectiveSyntax Qualify(UsingDirectiveSyntax usingDirective, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var target = usingDirective.NamespaceOrType;
        var symbol = usingDirective.Alias != null
            ? (semanticModel.GetDeclaredSymbol(usingDirective, cancellationToken) as IAliasSymbol)?.Target
            : semanticModel.GetSymbolInfo(target, cancellationToken).Symbol;

        if (!IsResolved(symbol))
        {
            return null;
        }

        var qualifiedName = symbol.ToDisplayString(QualifiedNameFormat);
        if (string.Equals(qualifiedName, target.NormalizeWhitespace().ToFullString(), StringComparison.Ordinal))
        {
            return usingDirective;
        }

        return usingDirective.WithNamespaceOrType(SyntaxFactory.ParseTypeName(qualifiedName).WithTriviaFrom(target));
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
