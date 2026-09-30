using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// A source transformation that inlines separate uninitialized local variable declarations
/// preceding out argument usages into inline 'out T x' declarations (the declared type is kept, because the call may depend on it).
/// </summary>
public sealed class OutVarInliningConverter : ISourceTransformation
{
    /// <inheritdoc />
    public string Name => "Inline out Variable Declarations";

    /// <inheritdoc />
    public string Apply(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return source;
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var rewriter = new OutVarRewriter();
        var newRoot = rewriter.Visit(root);

        return newRoot.ToFullString();
    }

    /// <summary>
    /// syntax rewriter that processes `out var` variable declarations within code blocks.
    /// </summary>
    private sealed class OutVarRewriter : CSharpSyntaxRewriter
    {
        /// <summary>
        /// Overrides `VisitBlock` to merge a preceding uninitialized local variable declaration with a subsequent matching `out` argument into a single inline `out var` declaration.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>A SyntaxNode value produced by this method.</returns>
        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            var visitedBlock = (BlockSyntax)base.VisitBlock(node);
            var statements = visitedBlock.Statements.ToList();
            var changed = false;

            for (var i = 0; i < statements.Count - 1; i++)
            {
                if (statements[i] is LocalDeclarationStatementSyntax localDecl &&
                    localDecl.Declaration.Variables.Count == 1)
                {
                    var variable = localDecl.Declaration.Variables[0];
                    if (variable.Initializer is null)
                    {
                        var varName = variable.Identifier.Text;
                        var nextStatement = statements[i + 1];
                        var leakingPart = GetPartWhoseVariablesLeakToTheBlock(nextStatement);

                        var outArg = leakingPart is null || HasDirective(nextStatement.GetLeadingTrivia())
                            ? null
                            : leakingPart.DescendantNodesAndSelf(n => !StartsOwnVariableScope(n))
                                .OfType<ArgumentSyntax>()
                                .FirstOrDefault(a => a.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword) &&
                                                     a.Expression is IdentifierNameSyntax id &&
                                                     id.Identifier.Text == varName);

                        if (outArg is not null)
                        {
                            // Check that varName is not used in nextStatement before outArg
                            var outArgSpanStart = outArg.SpanStart;
                            var priorUsages = nextStatement.DescendantNodes()
                                .OfType<IdentifierNameSyntax>()
                                .Where(id => id.Identifier.Text == varName && id.SpanStart < outArgSpanStart)
                                .Any();

                            if (!priorUsages)
                            {
                                // Keep the declared type: 'var' would change what the call binds to (out int vs. out long
                                // overloads, generic type arguments inferred from the argument, dynamic becoming object).
                                var declaredType = localDecl.Declaration.Type
                                    .WithoutTrivia()
                                    .WithTrailingTrivia(SyntaxFactory.Space);

                                var designation = SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(varName));
                                var declExpr = SyntaxFactory.DeclarationExpression(declaredType, designation);

                                var newOutArg = outArg.WithExpression(declExpr);
                                var updatedNextStatement = nextStatement.ReplaceNode(outArg, newOutArg)
                                    .WithLeadingTrivia(MergeLeadingTrivia(localDecl, nextStatement));

                                statements.RemoveAt(i);
                                statements[i] = updatedNextStatement;
                                changed = true;
                                i = Math.Max(i - 2, -1); // Re-check the declaration right before the rewritten call
                            }
                        }
                    }
                }
            }

            return changed ? visitedBlock.WithStatements(SyntaxFactory.List(statements)) : visitedBlock;
        }

        /// <summary>
        /// Returns the part of a statement whose expression variables are scoped to the enclosing block (so an
        /// <c>out var</c> declared there stays visible to the statements that follow), or <see langword="null" /> when
        /// the statement's expression variables would be scoped to the statement itself (loops, <c>using</c>,
        /// <c>lock</c>, ...).
        /// </summary>
        private static SyntaxNode GetPartWhoseVariablesLeakToTheBlock(StatementSyntax statement)
        {
            switch (statement)
            {
                case ExpressionStatementSyntax expressionStatement:
                    return expressionStatement.Expression;
                case LocalDeclarationStatementSyntax localDeclaration:
                    return localDeclaration.Declaration;
                case ReturnStatementSyntax returnStatement:
                    return returnStatement.Expression;
                case IfStatementSyntax ifStatement:
                    return ifStatement.Condition;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Determines whether expression variables declared inside the node are scoped to the node itself rather than to
        /// the enclosing statement.
        /// </summary>
        private static bool StartsOwnVariableScope(SyntaxNode node) =>
            node is AnonymousFunctionExpressionSyntax ||
            node is QueryExpressionSyntax ||
            node is SwitchExpressionArmSyntax;

        /// <summary>
        /// Determines whether the trivia contains a preprocessor directive (or code a directive disabled).
        /// </summary>
        private static bool HasDirective(SyntaxTriviaList trivia) =>
            trivia.Any(t => t.IsDirective || t.IsKind(SyntaxKind.DisabledTextTrivia));

        /// <summary>
        /// Builds the leading trivia of the call that replaces the removed declaration: the declaration's own leading
        /// trivia, followed by any comments that trailed the declaration or led the call, with the call's indentation.
        /// </summary>
        private static SyntaxTriviaList MergeLeadingTrivia(LocalDeclarationStatementSyntax declaration, StatementSyntax call)
        {
            var declarationTrailing = declaration.GetTrailingTrivia();
            var callLeading = call.GetLeadingTrivia();
            var merged = declaration.GetLeadingTrivia();

            if (declarationTrailing.Any(IsComment))
            {
                merged = merged.AddRange(declarationTrailing.SkipWhile(t => t.IsKind(SyntaxKind.WhitespaceTrivia)));
                return merged.AddRange(callLeading);
            }

            if (callLeading.Any(IsComment))
            {
                var callTrivia = callLeading.SkipWhile(t => t.IsKind(SyntaxKind.WhitespaceTrivia)).ToList();
                if (callTrivia[0].IsKind(SyntaxKind.EndOfLineTrivia))
                {
                    // A blank line follows: the declaration's indentation would otherwise be left on an empty line.
                    while (merged.Count > 0 && merged.Last().IsKind(SyntaxKind.WhitespaceTrivia))
                    {
                        merged = merged.RemoveAt(merged.Count - 1);
                    }
                }

                return merged.AddRange(callTrivia);
            }

            return merged;
        }

        /// <summary>
        /// Determines whether the trivia is a comment.
        /// </summary>
        private static bool IsComment(SyntaxTrivia trivia) =>
            trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
            trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
            trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
            trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
    }
}
