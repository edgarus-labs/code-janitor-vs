using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodeJanitor.Properties;
using System.Collections.Generic;
using System.Linq;

namespace CodeJanitor.Logic.Transformations
{
    /// <summary>
    /// Inserts blank lines before/after declarations (classes, methods, properties, fields, etc.)
    /// based on individual Cleaning_InsertBlankLinePaddingBefore/After* settings. Replicates
    /// the decision logic of <c>InsertBlankLinePaddingLogic</c> in headless-Roslyn form.
    /// </summary>
    public class BlankLinePaddingConverter : ISourceTransformation
    {
        public string Name => "Blank Line Padding";

        public string Apply(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source;
            }

            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetRoot();
            var rewriter = new BlankLinePaddingRewriter(source);
            var newRoot = rewriter.Visit(root);
            return newRoot.ToFullString();
        }

        private sealed class BlankLinePaddingRewriter : CSharpSyntaxRewriter
        {
            private readonly string _source;

            internal BlankLinePaddingRewriter(string source)
            {
                _source = source;
            }

            private SyntaxNode WithPaddingBefore(SyntaxNode node, bool shouldPad)
            {
                if (!shouldPad) return node;
                var leadingTrivia = node.GetLeadingTrivia();
                // Check if we already have at least one newline; if not, add one.
                var lastTriviaBeforeCode = -1;
                for (int i = leadingTrivia.Count - 1; i >= 0; i--)
                {
                    if (leadingTrivia[i].IsKind(SyntaxKind.EndOfLineTrivia))
                    {
                        lastTriviaBeforeCode = i;
                        break;
                    }
                    if (leadingTrivia[i].IsKind(SyntaxKind.WhitespaceTrivia))
                    {
                        continue;
                    }
                    // Non-whitespace, non-EOL trivia found.
                    break;
                }
                
                // Only add newline if we don't already have one at the end.
                if (lastTriviaBeforeCode == leadingTrivia.Count - 1)
                {
                    // Already ends with EOL, don't add another.
                    return node;
                }
                
                var newLine = SyntaxFactory.EndOfLine("\r\n");
                return node.WithLeadingTrivia(leadingTrivia.Insert(0, newLine));
            }

            private SyntaxNode WithPaddingAfter(SyntaxNode node, bool shouldPad)
            {
                if (!shouldPad) return node;
                var trailingTrivia = node.GetTrailingTrivia();
                // Check if we already have at least one newline; if not, add one.
                var firstTriviaAfterCode = -1;
                for (int i = 0; i < trailingTrivia.Count; i++)
                {
                    if (trailingTrivia[i].IsKind(SyntaxKind.EndOfLineTrivia))
                    {
                        firstTriviaAfterCode = i;
                        break;
                    }
                }
                
                // Only add newline if we don't already have one at the start.
                if (firstTriviaAfterCode == 0)
                {
                    // Already starts with EOL, don't add another.
                    return node;
                }
                
                var newLine = SyntaxFactory.EndOfLine("\r\n");
                return node.WithTrailingTrivia(trailingTrivia.Add(newLine));
            }

            private bool IsMultiLine(SyntaxNode node)
            {
                return node.GetFirstToken().GetLocation().GetLineSpan().StartLinePosition.Line <
                       node.GetLastToken().GetLocation().GetLineSpan().EndLinePosition.Line;
            }

            // ── Type declarations ──────────────────────────────────────────────────

            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);
                var before = Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses;
                var after = Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses;
                visited = (ClassDeclarationSyntax)WithPaddingBefore(visited, before);
                visited = (ClassDeclarationSyntax)WithPaddingAfter(visited, after);
                return visited;
            }

            public override SyntaxNode VisitStructDeclaration(StructDeclarationSyntax node)
            {
                var visited = (StructDeclarationSyntax)base.VisitStructDeclaration(node);
                var before = Settings.Default.Cleaning_InsertBlankLinePaddingBeforeStructs;
                var after = Settings.Default.Cleaning_InsertBlankLinePaddingAfterStructs;
                visited = (StructDeclarationSyntax)WithPaddingBefore(visited, before);
                visited = (StructDeclarationSyntax)WithPaddingAfter(visited, after);
                return visited;
            }

            public override SyntaxNode VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
            {
                var visited = (InterfaceDeclarationSyntax)base.VisitInterfaceDeclaration(node);
                var before = Settings.Default.Cleaning_InsertBlankLinePaddingBeforeInterfaces;
                var after = Settings.Default.Cleaning_InsertBlankLinePaddingAfterInterfaces;
                visited = (InterfaceDeclarationSyntax)WithPaddingBefore(visited, before);
                visited = (InterfaceDeclarationSyntax)WithPaddingAfter(visited, after);
                return visited;
            }

            public override SyntaxNode VisitEnumDeclaration(EnumDeclarationSyntax node)
            {
                var visited = (EnumDeclarationSyntax)base.VisitEnumDeclaration(node);
                var before = Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEnumerations;
                var after = Settings.Default.Cleaning_InsertBlankLinePaddingAfterEnumerations;
                visited = (EnumDeclarationSyntax)WithPaddingBefore(visited, before);
                visited = (EnumDeclarationSyntax)WithPaddingAfter(visited, after);
                return visited;
            }

            public override SyntaxNode VisitRecordDeclaration(RecordDeclarationSyntax node)
            {
                var visited = (RecordDeclarationSyntax)base.VisitRecordDeclaration(node);
                // Records use Class padding settings.
                var before = Settings.Default.Cleaning_InsertBlankLinePaddingBeforeClasses;
                var after = Settings.Default.Cleaning_InsertBlankLinePaddingAfterClasses;
                visited = (RecordDeclarationSyntax)WithPaddingBefore(visited, before);
                visited = (RecordDeclarationSyntax)WithPaddingAfter(visited, after);
                return visited;
            }

            public override SyntaxNode VisitDelegateDeclaration(DelegateDeclarationSyntax node)
            {
                var visited = (DelegateDeclarationSyntax)base.VisitDelegateDeclaration(node);
                var before = Settings.Default.Cleaning_InsertBlankLinePaddingBeforeDelegates;
                var after = Settings.Default.Cleaning_InsertBlankLinePaddingAfterDelegates;
                visited = (DelegateDeclarationSyntax)WithPaddingBefore(visited, before);
                visited = (DelegateDeclarationSyntax)WithPaddingAfter(visited, after);
                return visited;
            }

            // ── Namespace ──────────────────────────────────────────────────────────

            public override SyntaxNode VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
            {
                var visited = (NamespaceDeclarationSyntax)base.VisitNamespaceDeclaration(node);
                var before = Settings.Default.Cleaning_InsertBlankLinePaddingBeforeNamespaces;
                var after = Settings.Default.Cleaning_InsertBlankLinePaddingAfterNamespaces;
                visited = (NamespaceDeclarationSyntax)WithPaddingBefore(visited, before);
                visited = (NamespaceDeclarationSyntax)WithPaddingAfter(visited, after);
                return visited;
            }

            public override SyntaxNode VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
            {
                var visited = (FileScopedNamespaceDeclarationSyntax)base.VisitFileScopedNamespaceDeclaration(node);
                var before = Settings.Default.Cleaning_InsertBlankLinePaddingBeforeNamespaces;
                var after = Settings.Default.Cleaning_InsertBlankLinePaddingAfterNamespaces;
                visited = (FileScopedNamespaceDeclarationSyntax)WithPaddingBefore(visited, before);
                visited = (FileScopedNamespaceDeclarationSyntax)WithPaddingAfter(visited, after);
                return visited;
            }

            // ── Members ────────────────────────────────────────────────────────────

            public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);
                var before = Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods;
                var after = Settings.Default.Cleaning_InsertBlankLinePaddingAfterMethods;
                visited = (MethodDeclarationSyntax)WithPaddingBefore(visited, before);
                visited = (MethodDeclarationSyntax)WithPaddingAfter(visited, after);
                return visited;
            }

            public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
            {
                var visited = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node);
                var before = Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods;
                var after = Settings.Default.Cleaning_InsertBlankLinePaddingAfterMethods;
                visited = (ConstructorDeclarationSyntax)WithPaddingBefore(visited, before);
                visited = (ConstructorDeclarationSyntax)WithPaddingAfter(visited, after);
                return visited;
            }

            public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
            {
                var visited = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node);
                var isMultiLine = IsMultiLine(visited);
                var before = isMultiLine
                    ? Settings.Default.Cleaning_InsertBlankLinePaddingBeforePropertiesMultiLine
                    : Settings.Default.Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine;
                var after = isMultiLine
                    ? Settings.Default.Cleaning_InsertBlankLinePaddingAfterPropertiesMultiLine
                    : Settings.Default.Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine;
                visited = (PropertyDeclarationSyntax)WithPaddingBefore(visited, before);
                visited = (PropertyDeclarationSyntax)WithPaddingAfter(visited, after);
                return visited;
            }

            public override SyntaxNode VisitIndexerDeclaration(IndexerDeclarationSyntax node)
            {
                var visited = (IndexerDeclarationSyntax)base.VisitIndexerDeclaration(node);
                var isMultiLine = IsMultiLine(visited);
                var before = isMultiLine
                    ? Settings.Default.Cleaning_InsertBlankLinePaddingBeforePropertiesMultiLine
                    : Settings.Default.Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine;
                var after = isMultiLine
                    ? Settings.Default.Cleaning_InsertBlankLinePaddingAfterPropertiesMultiLine
                    : Settings.Default.Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine;
                visited = (IndexerDeclarationSyntax)WithPaddingBefore(visited, before);
                visited = (IndexerDeclarationSyntax)WithPaddingAfter(visited, after);
                return visited;
            }

            public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
            {
                var visited = (FieldDeclarationSyntax)base.VisitFieldDeclaration(node);
                var isMultiLine = IsMultiLine(visited);
                var before = isMultiLine
                    ? Settings.Default.Cleaning_InsertBlankLinePaddingBeforeFieldsMultiLine
                    : Settings.Default.Cleaning_InsertBlankLinePaddingBeforeFieldsSingleLine;
                var after = isMultiLine
                    ? Settings.Default.Cleaning_InsertBlankLinePaddingAfterFieldsMultiLine
                    : Settings.Default.Cleaning_InsertBlankLinePaddingAfterFieldsSingleLine;
                visited = (FieldDeclarationSyntax)WithPaddingBefore(visited, before);
                visited = (FieldDeclarationSyntax)WithPaddingAfter(visited, after);
                return visited;
            }

            public override SyntaxNode VisitEventDeclaration(EventDeclarationSyntax node)
            {
                var visited = (EventDeclarationSyntax)base.VisitEventDeclaration(node);
                var before = Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEvents;
                var after = Settings.Default.Cleaning_InsertBlankLinePaddingAfterEvents;
                visited = (EventDeclarationSyntax)WithPaddingBefore(visited, before);
                visited = (EventDeclarationSyntax)WithPaddingAfter(visited, after);
                return visited;
            }

            public override SyntaxNode VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
            {
                var visited = (EventFieldDeclarationSyntax)base.VisitEventFieldDeclaration(node);
                var before = Settings.Default.Cleaning_InsertBlankLinePaddingBeforeEvents;
                var after = Settings.Default.Cleaning_InsertBlankLinePaddingAfterEvents;
                visited = (EventFieldDeclarationSyntax)WithPaddingBefore(visited, before);
                visited = (EventFieldDeclarationSyntax)WithPaddingAfter(visited, after);
                return visited;
            }

            public override SyntaxNode VisitUsingStatement(UsingStatementSyntax node)
            {
                var visited = (UsingStatementSyntax)base.VisitUsingStatement(node);
                var before = Settings.Default.Cleaning_InsertBlankLinePaddingBeforeUsingStatementBlocks;
                var after = Settings.Default.Cleaning_InsertBlankLinePaddingAfterUsingStatementBlocks;
                visited = (UsingStatementSyntax)WithPaddingBefore(visited, before);
                visited = (UsingStatementSyntax)WithPaddingAfter(visited, after);
                return visited;
            }
        }
    }
}
