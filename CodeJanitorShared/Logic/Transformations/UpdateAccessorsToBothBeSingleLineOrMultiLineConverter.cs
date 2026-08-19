using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodeJanitor.Properties;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Updates property and event accessors to either both be single-line or both be multi-line,
/// ensuring consistency and readability.
/// </summary>

public class UpdateAccessorsToBothBeSingleLineOrMultiLineConverter : ISourceTransformation
{
    public string Name => "Update accessors to both be single line or multi-line";

    public string Apply(string source)
    {
        if (string.IsNullOrEmpty(source) || !Settings.Default.Cleaning_UpdateAccessorsToBothBeSingleLineOrMultiLine)
        {
            return source;
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var rewriter = new AccessorFormatRewriter();
        var newRoot = rewriter.Visit(root);

        return newRoot.ToFullString();
    }

    private sealed class AccessorFormatRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            // First visit children
            var visited = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node);

            if (visited.AccessorList == null || visited.AccessorList.Accessors.Count < 2)
            {
                return visited;
            }

            // Get first two accessors (get/set or set/get)
            var first = visited.AccessorList.Accessors[0];
            var second = visited.AccessorList.Accessors[1];

            // Check if they have bodies (can't format property shorthand or abstract properties)
            if (first.Body == null || second.Body == null)
            {
                return visited;
            }

            return UpdateAccessorConsistency(visited, first, second);
        }

        public override SyntaxNode VisitEventDeclaration(EventDeclarationSyntax node)
        {
            // First visit children
            var visited = (EventDeclarationSyntax)base.VisitEventDeclaration(node);

            if (visited.AccessorList == null || visited.AccessorList.Accessors.Count < 2)
            {
                return visited;
            }

            // Get first two accessors (add/remove)
            var first = visited.AccessorList.Accessors[0];
            var second = visited.AccessorList.Accessors[1];

            // Check if they have bodies
            if (first.Body == null || second.Body == null)
            {
                return visited;
            }

            return UpdateEventAccessorConsistency(visited, first, second);
        }

        private PropertyDeclarationSyntax UpdateAccessorConsistency(PropertyDeclarationSyntax prop, AccessorDeclarationSyntax first, AccessorDeclarationSyntax second)
        {
            bool isFirstSingleLine = IsSingleLine(first);
            bool isSecondSingleLine = IsSingleLine(second);

            // If they're already consistent, no change needed
            if (isFirstSingleLine == isSecondSingleLine)
            {
                return prop;
            }

            // Make both accessors the same format
            // Choose to make them both multi-line (preserves code style)
            var newAccessors = new SyntaxList<AccessorDeclarationSyntax>();

            foreach (var accessor in prop.AccessorList.Accessors)
            {
                if (isFirstSingleLine != IsSingleLine(accessor))
                {
                    // This accessor needs to be reformatted
                    newAccessors = newAccessors.Add(FormatAccessor(accessor, !isFirstSingleLine));
                }
                else
                {
                    newAccessors = newAccessors.Add(accessor);
                }
            }

            var newAccessorList = prop.AccessorList.WithAccessors(newAccessors);

            return prop.WithAccessorList(newAccessorList);
        }

        private EventDeclarationSyntax UpdateEventAccessorConsistency(EventDeclarationSyntax evt, AccessorDeclarationSyntax first, AccessorDeclarationSyntax second)
        {
            bool isFirstSingleLine = IsSingleLine(first);
            bool isSecondSingleLine = IsSingleLine(second);

            // If they're already consistent, no change needed
            if (isFirstSingleLine == isSecondSingleLine)
            {
                return evt;
            }

            // Make both accessors the same format
            var newAccessors = new SyntaxList<AccessorDeclarationSyntax>();

            foreach (var accessor in evt.AccessorList.Accessors)
            {
                if (isFirstSingleLine != IsSingleLine(accessor))
                {
                    // This accessor needs to be reformatted
                    newAccessors = newAccessors.Add(FormatAccessor(accessor, !isFirstSingleLine));
                }
                else
                {
                    newAccessors = newAccessors.Add(accessor);
                }
            }

            var newAccessorList = evt.AccessorList.WithAccessors(newAccessors);

            return evt.WithAccessorList(newAccessorList);
        }

        private bool IsSingleLine(AccessorDeclarationSyntax accessor)
        {
            if (accessor.Body == null)
                return true; // Expression-bodied accessors are considered single-line

            // Check if body spans only 2 lines (opening and closing brace)
            var bodyText = accessor.Body.ToFullString();
            var lines = bodyText.Split('\n');

            return lines.Length <= 2;
        }

        private AccessorDeclarationSyntax FormatAccessor(AccessorDeclarationSyntax accessor, bool makeMultiLine)
        {
            if (accessor.Body == null)
                return accessor;

            if (makeMultiLine)
            {
                // Expand to multi-line format
                var newline = "\r\n";
                var bodyStatements = new System.Collections.Generic.List<string> { "{" };

                foreach (var statement in accessor.Body.Statements)
                {
                    bodyStatements.Add("    " + statement.ToString().Trim());
                }

                bodyStatements.Add("}");
                var formattedBody = string.Join(newline, bodyStatements);

                var newBodySyntax = SyntaxFactory.ParseStatement(formattedBody) as BlockSyntax;

                return accessor.WithBody(newBodySyntax ?? accessor.Body);
            }
            else
            {
                // Compress to single-line format
                var statements = accessor.Body.Statements;
                if (statements.Count != 1)
                    return accessor;

                var statement = statements[0];
                var statementText = statement.ToString().Trim();

                // Create single-line body: { statement; } or similar
                var singleLineBody = $"{{ {statementText} }}";
                try
                {
                    var newBodySyntax = SyntaxFactory.ParseStatement(singleLineBody) as BlockSyntax;

                    return accessor.WithBody(newBodySyntax ?? accessor.Body);
                }
                catch
                {
                    return accessor;
                }
            }
        }
    }
}