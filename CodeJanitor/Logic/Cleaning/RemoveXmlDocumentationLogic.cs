using EnvDTE;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// Provides logic for detecting and removing XML documentation comments from C# source code.
/// </summary>
public sealed class RemoveXmlDocumentationLogic
{
    private readonly CodeJanitorPackage _package;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoveXmlDocumentationLogic"/> class.
    /// </summary>
    /// <param name="package">The package.</param>
    public RemoveXmlDocumentationLogic(CodeJanitorPackage package)
    {
        _package = package;
    }

    /// <summary>
    /// Gets an instance of <see cref="RemoveXmlDocumentationLogic"/>.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>An instance of <see cref="RemoveXmlDocumentationLogic"/>.</returns>
    public static RemoveXmlDocumentationLogic GetInstance(CodeJanitorPackage package)
    {
        return new RemoveXmlDocumentationLogic(package);
    }

    /// <summary>
    /// Determines whether a ProjectItem is a valid C# file that can be processed.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>True if project item can be processed, otherwise false.</returns>
    public bool CanRemoveXmlDocProjectItem(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (projectItem is null || !projectItem.IsPhysicalFile())
        {
            return false;
        }

        if (projectItem.Document is not null)
        {
            return projectItem.Document.GetCodeLanguage() == CodeLanguage.CSharp;
        }

        var path = projectItem.GetFileName();

        return !string.IsNullOrWhiteSpace(path) &&
               path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
               !path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) &&
               !path.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Removes all XML documentation comments from the specified C# source text.
    /// Returns the updated source text and whether any XML documentation comments were removed.
    /// </summary>
    /// <param name="sourceText">The source text.</param>
    /// <param name="updatedSourceText">The updated source text.</param>
    /// <returns>True if XML documentation comments were found and removed, otherwise false.</returns>
    public static bool TryRemoveXmlDocumentation(string sourceText, out string updatedSourceText)
    {
        updatedSourceText = sourceText;

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return false;
        }

        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = tree.GetRoot();

        if (!HasXmlDocumentation(root))
        {
            return false;
        }

        var rewriter = new RemoveXmlDocRewriter();
        var newRoot = rewriter.Visit(root);

        updatedSourceText = newRoot.ToFullString();

        return true;
    }

    /// <summary>
    /// Determines whether the syntax root contains any XML documentation trivia.
    /// </summary>
    /// <param name="root">The syntax node root.</param>
    /// <returns>True if any XML documentation trivia is present, otherwise false.</returns>
    public static bool HasXmlDocumentation(SyntaxNode root)
    {
        if (root is null)
        {
            return false;
        }

        return root.DescendantTrivia(descendIntoTrivia: true).Any(IsXmlDocTrivia);
    }

    /// <summary>
    /// Checks whether a syntax trivia represents an XML documentation comment.
    /// </summary>
    /// <param name="trivia">The trivia.</param>
    /// <returns>True if XML doc trivia, otherwise false.</returns>
    public static bool IsXmlDocTrivia(SyntaxTrivia trivia)
    {
        if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
            trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
        {
            return true;
        }

        if (trivia.HasStructure)
        {
            var structure = trivia.GetStructure();
            if (structure is not null &&
                (structure.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                 structure.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes XML documentation comments from a ProjectItem, updating the document in-memory if open or directly on disk.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>True if XML doc was removed, otherwise false.</returns>
    public bool RemoveXmlDoc(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!CanRemoveXmlDocProjectItem(projectItem))
        {
            return false;
        }

        // If the document is currently open in the editor:
        if (projectItem.Document is not null)
        {
            var textDocument = projectItem.Document.GetTextDocument();
            if (textDocument is null)
            {
                return false;
            }

            var editPoint = textDocument.StartPoint.CreateEditPoint();
            var originalText = editPoint.GetText(textDocument.EndPoint);

            if (!TryRemoveXmlDocumentation(originalText, out var updatedText))
            {
                return false;
            }

            editPoint.ReplaceText(textDocument.EndPoint, updatedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);

            return true;
        }

        // If the file is not open in the editor, process on disk:
        var filePath = projectItem.GetFileName();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            var originalText = File.ReadAllText(filePath);
            if (!TryRemoveXmlDocumentation(originalText, out var updatedText))
            {
                return false;
            }

            File.WriteAllText(filePath, updatedText);

            return true;
        }
        catch (Exception ex)
        {
            OutputWindowHelper.DiagnosticWriteLine($"Failed to remove XML documentation from '{filePath}'.", ex);

            return false;
        }
    }

    /// <summary>
    /// Rewrites C# syntax to remove XML documentation comments from the token stream by filtering relevant trivia.
    /// </summary>
    private sealed class RemoveXmlDocRewriter : CSharpSyntaxRewriter
    {
        /// <summary>
        /// its a syntax token and returns a new token with its leading and trailing trivia filtered when changes occur, otherwise delegating to the base visit implementation.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <returns>The syntax token result.</returns>
        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            var leading = token.LeadingTrivia;
            var trailing = token.TrailingTrivia;

            var newLeading = FilterTriviaList(leading);
            var newTrailing = FilterTriviaList(trailing);

            if (newLeading != leading || newTrailing != trailing)
            {
                return token.WithLeadingTrivia(newLeading).WithTrailingTrivia(newTrailing);
            }

            return base.VisitToken(token);
        }

        /// <summary>
        /// Filters XML documentation comment trivia from the specified syntax trivia list, removing preceding indentation whitespace and trailing end-of-line trivia associated with the stripped comments.
        /// </summary>
        /// <param name="triviaList">The trivia list.</param>
        /// <returns>The syntax trivia list result.</returns>
        private static SyntaxTriviaList FilterTriviaList(SyntaxTriviaList triviaList)
        {
            if (triviaList.Count == 0 || !triviaList.Any(IsXmlDocTrivia))
            {
                return triviaList;
            }

            var result = new List<SyntaxTrivia>();
            for (var i = 0; i < triviaList.Count; i++)
            {
                var trivia = triviaList[i];

                if (IsXmlDocTrivia(trivia))
                {
                    // If the trivia directly before this was whitespace on the same line (indentation of the XMLDoc comment),
                    // remove that preceding whitespace from result list.
                    if (result.Count > 0 && result[result.Count - 1].IsKind(SyntaxKind.WhitespaceTrivia))
                    {
                        result.RemoveAt(result.Count - 1);
                    }

                    // For MultiLineDocumentationCommentTrivia or cases where an EndOfLine immediately follows,
                    // skip the trailing EndOfLine if present.
                    if (i + 1 < triviaList.Count && triviaList[i + 1].IsKind(SyntaxKind.EndOfLineTrivia))
                    {
                        i++;
                    }

                    continue;
                }

                result.Add(trivia);
            }

            return SyntaxFactory.TriviaList(result);
        }
    }
}
