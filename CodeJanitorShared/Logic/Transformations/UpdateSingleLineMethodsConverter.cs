using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodeJanitor.Properties;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Spreads single-line method declarations onto multiple lines by placing the opening brace
/// on a new line, method body content on separate lines, and closing brace on its own line.
/// </summary>

public class UpdateSingleLineMethodsConverter : ISourceTransformation
{
    public string Name => "Update single-line methods";

    public string Apply(string source)
    {
        if (string.IsNullOrEmpty(source) || !Settings.Default.Cleaning_UpdateSingleLineMethods)
        {
            return source;
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var rewriter = new SingleLineMethodRewriter();
        var newRoot = rewriter.Visit(root);

        return newRoot.ToFullString();
    }

    private sealed class SingleLineMethodRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            // First visit children
            var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);

            // Don't process abstract methods or methods in interfaces
            if (visited.Body == null || visited.Modifiers.Any(SyntaxKind.AbstractKeyword))
            {
                return visited;
            }

            // Check if it's a single-line method (return statement or throw)
            if (!IsSingleLineMethodBody(visited.Body))
            {
                return visited;
            }

            // Spread it onto multiple lines

            return SpreadMethodOntoMultipleLines(visited);
        }

        private bool IsSingleLineMethodBody(BlockSyntax body)
        {
            if (body == null || body.Statements.Count == 0)
                return false;

            // Check if all statements fit on one line (simple heuristic)
            // A single-line method body would have minimal whitespace/newlines
            var bodyText = body.ToFullString();
            var lineCount = bodyText.Split('\n').Length;

            // If body spans only 1-2 lines, consider it single-line
            // (1 for opening brace, 2 includes closing brace)

            return lineCount <= 2;
        }

        private MethodDeclarationSyntax SpreadMethodOntoMultipleLines(MethodDeclarationSyntax method)
        {
            if (method.Body == null)
                return method;

            var newline = "\r\n";
            var indent = "    ";

            // Reconstruct the method body with proper formatting
            var statements = method.Body.Statements;

            // Build formatted body text
            var bodyLines = new System.Collections.Generic.List<string> { "{" };

            foreach (var statement in statements)
            {
                bodyLines.Add(indent + statement.ToString().Trim());
            }

            bodyLines.Add("}");

            var formattedBody = string.Join(newline, bodyLines);

            // Parse the new body
            var newBodySyntax = SyntaxFactory.ParseStatement(formattedBody) as BlockSyntax;
            if (newBodySyntax == null)
            {
                return method;
            }

            return method.WithBody(newBodySyntax);
        }
    }
}