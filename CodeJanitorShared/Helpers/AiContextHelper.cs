using EnvDTE;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Model.CodeItems;
using System;
using System.IO;
using System.Linq;

namespace CodeJanitor.Helpers;

/// <summary>
/// Encapsulates the extracted code context for AI operations.
/// </summary>
internal sealed class AiCodeContext
{
    public string TargetName { get; set; }
    public string CodeSnippet { get; set; }
    public string FilePath { get; set; }
    public Action<string> ReplaceAction { get; set; }
    public Action<string> InsertAction { get; set; }
}

/// <summary>
/// Helper for extracting active code context, methods, selections, and applying AI modifications.
/// </summary>
internal static class AiContextHelper
{
    /// <summary>
    /// Extracts code context from the active document (selection, cursor enclosing method/type, or entire file).
    /// </summary>
    internal static AiCodeContext GetActiveCodeContext(CodeJanitorPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var document = package.ActiveDocument;
        if (document == null)
        {
            return null;
        }

        var textDocument = document.Object("TextDocument") as TextDocument;
        if (textDocument == null)
        {
            return null;
        }

        var selection = textDocument.Selection;
        var selectedText = selection?.Text;

        // If user explicitly highlighted text, use it
        if (!string.IsNullOrWhiteSpace(selectedText))
        {
            return new AiCodeContext
            {
                TargetName = $"Selection ({document.Name})",
                CodeSnippet = selectedText,
                FilePath = document.FullName,
                ReplaceAction = newCode =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    using (new UndoTransactionHelper(package, "AI Replace Selection"))
                    {
                        selection.Delete();
                        selection.Insert(newCode);
                    }
                },
                InsertAction = newCode =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    using (new UndoTransactionHelper(package, "AI Insert Code"))
                    {
                        selection.Insert(newCode);
                    }
                }
            };
        }

        var spadeItem = package.Spade?.SelectedItems?.FirstOrDefault();
        if (spadeItem is BaseCodeItemElement element && element.StartPoint != null && element.EndPoint != null)
        {
            return GetCodeItemContext(package, element);
        }

        // Get full file text
        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var endPoint = textDocument.EndPoint.CreateEditPoint();
        var fullText = startPoint.GetText(endPoint);
        var cursorLine = selection?.ActivePoint?.Line ?? 1;

        // Use Roslyn AST to identify enclosing method or type
        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(fullText);
            var root = syntaxTree.GetRoot();
            var linePosition = syntaxTree.GetText().Lines[Math.Max(0, cursorLine - 1)].Start;
            var token = root.FindToken(linePosition);
            var node = token.Parent;

            var method = node?.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (method != null)
            {
                var methodSpan = method.Span;
                var methodText = method.ToFullString();
                var methodName = method.Identifier.Text;

                return new AiCodeContext
                {
                    TargetName = $"Method: {methodName}()",
                    CodeSnippet = methodText,
                    FilePath = document.FullName,
                    ReplaceAction = newCode =>
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();
                        using (new UndoTransactionHelper(package, $"AI Replace Method {methodName}"))
                        {
                            var methodStart = textDocument.CreateEditPoint();
                            methodStart.MoveToAbsoluteOffset(methodSpan.Start + 1);
                            var methodEnd = textDocument.CreateEditPoint();
                            methodEnd.MoveToAbsoluteOffset(methodSpan.End + 1);
                            methodStart.Delete(methodEnd);
                            methodStart.Insert(newCode);
                        }
                    },
                    InsertAction = newCode =>
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();
                        using (new UndoTransactionHelper(package, "AI Insert Code"))
                        {
                            selection.Insert(newCode);
                        }
                    }
                };
            }

            var typeDecl = node?.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (typeDecl != null)
            {
                return new AiCodeContext
                {
                    TargetName = $"Type: {typeDecl.Identifier.Text}",
                    CodeSnippet = typeDecl.ToFullString(),
                    FilePath = document.FullName,
                    ReplaceAction = null,
                    InsertAction = newCode =>
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();
                        using (new UndoTransactionHelper(package, "AI Insert Code"))
                        {
                            selection.Insert(newCode);
                        }
                    }
                };
            }
        }
        catch
        {
            // Fall back to entire document if AST parsing encounters errors
        }

        return new AiCodeContext
        {
            TargetName = Path.GetFileName(document.FullName),
            CodeSnippet = fullText,
            FilePath = document.FullName,
            ReplaceAction = null,
            InsertAction = newCode =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                using (new UndoTransactionHelper(package, "AI Insert Code"))
                {
                    selection.Insert(newCode);
                }
            }
        };
    }

    /// <summary>
    /// Extracts code context from a Spade ICodeItem element.
    /// </summary>
    internal static AiCodeContext GetCodeItemContext(CodeJanitorPackage package, ICodeItem codeItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (codeItem is BaseCodeItemElement element && element.StartPoint != null && element.EndPoint != null)
        {
            var code = element.StartPoint.GetText(element.EndPoint);
            var itemName = element.Name ?? "Member";

            return new AiCodeContext
            {
                TargetName = $"{itemName}",
                CodeSnippet = code,
                FilePath = package.ActiveDocument?.FullName,
                ReplaceAction = newCode =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    using (new UndoTransactionHelper(package, $"AI Replace {itemName}"))
                    {
                        element.StartPoint.Delete(element.EndPoint);
                        element.StartPoint.Insert(newCode);
                    }
                },
                InsertAction = newCode =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    using (new UndoTransactionHelper(package, $"AI Insert {itemName}"))
                    {
                        element.EndPoint.Insert(Environment.NewLine + newCode);
                    }
                }
            };
        }

        // Fallback to active document context
        return GetActiveCodeContext(package);
    }
}
