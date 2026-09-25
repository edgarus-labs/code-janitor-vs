using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Finds the conditional compilation symbols that decide how a document compiles, and the build variants (assignments
/// of those symbols) other than the active one, in which a change to the document has to be verified as well. A build
/// configuration defines its symbols in every project, so a variant assigns them in the document's project and in every
/// C# project it references, directly or transitively.
/// </summary>
internal static class ConditionalCompilationVariants
{
    /// <summary>
    /// Gets, ordered, every symbol used in an <c>#if</c>/<c>#elif</c> condition of <paramref name="root" />, and every
    /// symbol of such a condition in another document of the project or of a referenced project whose toggling changes
    /// the namespaces, types or members that document declares, or their signatures. A symbol that only changes code
    /// inside members cannot change how the document binds.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="root">The syntax root of <paramref name="document" />.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The relevant symbols in ordinal order.</returns>
    public static async Task<IReadOnlyList<string>> GetRelevantSymbolsAsync(Document document, CompilationUnitSyntax root, CancellationToken cancellationToken)
    {
        var symbols = new SortedSet<string>(GetConditionSymbols(root), StringComparer.Ordinal);

        foreach (var otherDocument in GetProjects(document.Project).SelectMany(project => project.Documents))
        {
            if (otherDocument.Id == document.Id)
            {
                continue;
            }

            var tree = await otherDocument.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            var otherRoot = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            var candidates = GetConditionSymbols(otherRoot).Where(symbol => !symbols.Contains(symbol)).ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            var text = await tree.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var options = (CSharpParseOptions)tree.Options;
            var declarations = GetDeclarations(otherRoot);
            foreach (var symbol in candidates)
            {
                var toggledTree = CSharpSyntaxTree.ParseText(text, options.WithPreprocessorSymbols(Toggle(options.PreprocessorSymbolNames, symbol)), cancellationToken: cancellationToken);
                if (!declarations.SequenceEqual(GetDeclarations(toggledTree.GetRoot(cancellationToken)), StringComparer.Ordinal))
                {
                    symbols.Add(symbol);
                }
            }
        }

        return symbols.ToList();
    }

    /// <summary>
    /// Gets every assignment of <paramref name="symbols" /> that differs from the current configuration: the solution in
    /// which the project of <paramref name="document" /> and every C# project it references define exactly the assigned
    /// symbols (and keep their other symbols), and a description such as <c>DEBUG defined, TRACE undefined</c>.
    /// </summary>
    /// <param name="document">The document, in the solution of the active configuration.</param>
    /// <param name="symbols">The symbols to assign.</param>
    /// <returns>The other variants.</returns>
    public static IEnumerable<(Solution Solution, string Description)> GetOtherVariants(Document document, IReadOnlyList<string> symbols)
    {
        var activeSolution = document.Project.Solution;
        var projects = GetProjects(document.Project).ToList();

        for (var assignment = 0; assignment < 1 << symbols.Count; assignment++)
        {
            var defined = symbols.Where((symbol, index) => (assignment & (1 << index)) != 0).ToList();

            var solution = activeSolution;
            foreach (var project in projects)
            {
                var options = (CSharpParseOptions)project.ParseOptions;
                var variantSymbols = options.PreprocessorSymbolNames.Except(symbols, StringComparer.Ordinal).Concat(defined).ToList();
                if (!new HashSet<string>(options.PreprocessorSymbolNames, StringComparer.Ordinal).SetEquals(variantSymbols))
                {
                    solution = solution.WithProjectParseOptions(project.Id, options.WithPreprocessorSymbols(variantSymbols));
                }
            }

            if (solution != activeSolution)
            {
                yield return (solution, string.Join(", ", symbols.Select(symbol => symbol + (defined.Contains(symbol) ? " defined" : " undefined"))));
            }
        }
    }

    /// <summary>
    /// Gets <paramref name="project" /> and every C# project it references, directly or transitively: the projects whose
    /// conditional compilation decides what the document can bind to.
    /// </summary>
    private static IEnumerable<Project> GetProjects(Project project) =>
        new[] { project }.Concat(
            project.Solution.GetProjectDependencyGraph()
                .GetProjectsThatThisProjectTransitivelyDependsOn(project.Id)
                .Select(project.Solution.GetProject)
                .Where(referenced => referenced?.ParseOptions is CSharpParseOptions));

    private static IEnumerable<string> GetConditionSymbols(SyntaxNode root)
    {
        if (!root.ContainsDirectives)
        {
            return Enumerable.Empty<string>();
        }

        var symbols = new List<string>();
        for (var directive = root.GetFirstDirective(); directive != null; directive = directive.GetNextDirective())
        {
            if (directive is ConditionalDirectiveTriviaSyntax conditional)
            {
                symbols.AddRange(conditional.Condition.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Select(name => name.Identifier.ValueText));
            }
        }

        return symbols.Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<string> Toggle(IEnumerable<string> symbols, string symbol)
    {
        var toggled = symbols.ToList();
        if (!toggled.Remove(symbol))
        {
            toggled.Add(symbol);
        }

        return toggled;
    }

    /// <summary>
    /// Gets the namespace, type and member declarations of a document, each identified by its containing declarations,
    /// its kind and its signature, in ordinal order (a multiset: an added overload changes it).
    /// </summary>
    private static List<string> GetDeclarations(SyntaxNode root)
    {
        var declarations = new List<string>();
        if (root is CompilationUnitSyntax compilationUnit)
        {
            AddDeclarations(compilationUnit.Members, string.Empty, declarations);
        }

        declarations.Sort(StringComparer.Ordinal);

        return declarations;
    }

    private static void AddDeclarations(IEnumerable<MemberDeclarationSyntax> members, string container, List<string> declarations)
    {
        foreach (var member in members)
        {
            // Top-level statements declare nothing another document can refer to.
            if (member is GlobalStatementSyntax || member is IncompleteMemberSyntax)
            {
                continue;
            }

            var declaration = container + "/" + member.Kind() + " " + GetSignature(member);
            declarations.Add(declaration);

            switch (member)
            {
                case BaseNamespaceDeclarationSyntax namespaceDeclaration:
                    AddDeclarations(namespaceDeclaration.Members, declaration, declarations);
                    break;

                case TypeDeclarationSyntax typeDeclaration:
                    AddDeclarations(typeDeclaration.Members, declaration, declarations);
                    break;

                case EnumDeclarationSyntax enumDeclaration:
                    AddDeclarations(enumDeclaration.Members, declaration, declarations);
                    break;
            }
        }
    }

    /// <summary>
    /// Gets the signature of a declaration: its tokens without trivia, bodies, initializers and nested declarations,
    /// i.e. its attributes, modifiers, name, type parameters, parameters (with default values), return type, base list,
    /// constraints, accessors and record primary-constructor parameters.
    /// </summary>
    private static string GetSignature(MemberDeclarationSyntax member) =>
        string.Join(" ", member.DescendantTokens(node => node == member || !IsOutsideSignature(node)).Select(token => token.Text));

    private static bool IsOutsideSignature(SyntaxNode node) =>
        node is MemberDeclarationSyntax
        || node is UsingDirectiveSyntax
        || node is ExternAliasDirectiveSyntax
        || node is BlockSyntax
        || node is ArrowExpressionClauseSyntax
        || node is ConstructorInitializerSyntax
        || (node is EqualsValueClauseSyntax && !(node.Parent is ParameterSyntax));
}
