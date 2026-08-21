using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// A source transformation that inlines separate uninitialized local variable declarations
/// preceding out argument usages into modern 'out var ...' syntax.
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

    private sealed class OutVarRewriter : CSharpSyntaxRewriter
    {
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
                    if (variable.Initializer == null)
                    {
                        var varName = variable.Identifier.Text;
                        var nextStatement = statements[i + 1];

                        var outArg = nextStatement.DescendantNodes()
                            .OfType<ArgumentSyntax>()
                            .FirstOrDefault(a => a.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword) &&
                                                 a.Expression is IdentifierNameSyntax id &&
                                                 id.Identifier.Text == varName);

                        if (outArg != null)
                        {
                            // Check that varName is not used in nextStatement before outArg
                            var outArgSpanStart = outArg.SpanStart;
                            var priorUsages = nextStatement.DescendantNodes()
                                .OfType<IdentifierNameSyntax>()
                                .Where(id => id.Identifier.Text == varName && id.SpanStart < outArgSpanStart)
                                .Any();

                            if (!priorUsages)
                            {
                                var varKeywordToken = SyntaxFactory.Identifier(
                                    SyntaxFactory.TriviaList(),
                                    "var",
                                    SyntaxFactory.TriviaList(SyntaxFactory.Space));

                                var designation = SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(varName));
                                var declExpr = SyntaxFactory.DeclarationExpression(
                                    SyntaxFactory.IdentifierName(varKeywordToken),
                                    designation);

                                var newOutArg = outArg.WithExpression(declExpr);
                                var updatedNextStatement = nextStatement.ReplaceNode(outArg, newOutArg)
                                    .WithLeadingTrivia(localDecl.GetLeadingTrivia());

                                statements.RemoveAt(i);
                                statements[i] = updatedNextStatement;
                                changed = true;
                                i--; // Re-check at current index
                            }
                        }
                    }
                }
            }

            return changed ? visitedBlock.WithStatements(SyntaxFactory.List(statements)) : visitedBlock;
        }
    }
}
