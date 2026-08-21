using EnvDTE;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using CodeJanitor.Model.CodeItems;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeJanitor.Helpers;

/// <summary>
/// A static helper class for working with text documents.
/// </summary>
/// <remarks>
///
/// Note: All POSIXRegEx text replacements search against '\n' but insert/replace with
///       Environment.NewLine. This handles line endings correctly.
/// </remarks>

internal static class TextDocumentHelper
{
    /// <summary>
    /// The common set of options to be used for find and replace patterns.
    /// </summary>
    internal const FindOptions StandardFindOptions = FindOptions.UseRegularExpressions;

    /// <summary>
    /// Finds all matches of the specified pattern within the specified text document.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <param name="patternString">The pattern string.</param>
    /// <returns>The set of matches.</returns>

    internal static IEnumerable<EditPoint> FindMatches(TextDocument textDocument, string patternString)
    {
        var matches = new List<EditPoint>();
        UIThread.Run(() =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (TryGetTextBufferAt(textDocument.Parent.FullName, out ITextBuffer textBuffer))
            {
                IFinder finder = GetFinder(patternString, textBuffer);
                var findMatches = finder.FindAll();
                foreach (var match in findMatches)
                {
                    matches.Add(GetEditPointForSnapshotPosition(textDocument, textBuffer.CurrentSnapshot, match.Start));
                }
            }
        });

        return matches;
    }

    /// <summary>
    /// Finds all matches of the specified pattern within the specified text selection.
    /// </summary>
    /// <param name="textSelection">The text selection.</param>
    /// <param name="patternString">The pattern string.</param>
    /// <returns>The set of matches.</returns>

    internal static IEnumerable<EditPoint> FindMatches(TextSelection textSelection, string patternString)
    {
        var matches = new List<EditPoint>();
        UIThread.Run(() =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (TryGetTextBufferAt(textSelection.Parent.Parent.FullName, out ITextBuffer textBuffer))
            {
                IFinder finder = GetFinder(patternString, textBuffer);
                var findMatches = finder.FindAll(GetSnapshotSpanForTextSelection(textBuffer.CurrentSnapshot, textSelection));
                foreach (var match in findMatches)
                {
                    matches.Add(GetEditPointForSnapshotPosition(textSelection.Parent, textBuffer.CurrentSnapshot, match.Start));
                }
            }
        });

        return matches;
    }

    /// <summary>
    /// Finds the first match of the specified pattern within the specified text document, otherwise null.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <param name="patternString">The pattern string.</param>
    /// <returns>The first match, otherwise null.</returns>

    internal static EditPoint FirstOrDefaultMatch(TextDocument textDocument, string patternString)
    {
        EditPoint result = null;
        UIThread.Run(() =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (TryGetTextBufferAt(textDocument.Parent.FullName, out ITextBuffer textBuffer))
            {
                IFinder finder = GetFinder(patternString, textBuffer);
                if (finder.TryFind(out Span match))
                {
                    result = GetEditPointForSnapshotPosition(textDocument, textBuffer.CurrentSnapshot, match.Start);
                }
            }
        });

        return result;
    }

    /// <summary>
    /// Attempts to find next match starting with <paramref name="startPoint"/> and if sucessful,
    /// updates <paramref name="endPoint"/> to point to the end position of the match.
    /// </summary>
    /// <param name="startPoint"></param>
    /// <param name="endPoint"></param>
    /// <param name="patternString"></param>
    /// <returns>True if successful, false if no match found.</returns>

    internal static bool TryFindNextMatch(EditPoint startPoint, ref EditPoint endPoint, string patternString)
    {
        bool result = false;
        EditPoint resultEndPoint = null;

        UIThread.Run(() =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (TryGetTextBufferAt(startPoint.Parent.Parent.FullName, out ITextBuffer textBuffer))
            {
                IFinder finder = GetFinder(patternString, textBuffer);

                if (finder.TryFind(GetSnapshotPositionForTextPoint(textBuffer.CurrentSnapshot, startPoint), out Span match))
                {
                    resultEndPoint = GetEditPointForSnapshotPosition(startPoint.Parent, textBuffer.CurrentSnapshot, match.End);
                    result = true;
                }
            }
        });

        endPoint = resultEndPoint;

        return result;
    }

    /// <summary>
    /// Gets the text between the specified start point and the first match.
    /// </summary>
    /// <param name="startPoint">The start point.</param>
    /// <param name="matchString">The match string.</param>
    /// <returns>The matching text, otherwise null.</returns>

    internal static string GetTextToFirstMatch(TextPoint startPoint, string matchString)
    {
        string result = null;
        UIThread.Run(() =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (TryGetTextBufferAt(startPoint.Parent.Parent.FullName, out ITextBuffer textBuffer))
            {
                IFinder finder = GetFinder(matchString, textBuffer);
                if (finder.TryFind(GetSnapshotPositionForTextPoint(textBuffer.CurrentSnapshot, startPoint), out Span match))
                {
                    result = textBuffer.CurrentSnapshot.GetText(startPoint.AbsoluteCharOffset, match.Start - startPoint.AbsoluteCharOffset);
                }
            }
        });

        return result;
    }

    /// <summary>
    /// Inserts a blank line before the specified point except where adjacent to a brace.
    /// </summary>
    /// <param name="point">The point.</param>

    internal static void InsertBlankLineBeforePoint(EditPoint point)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (point.Line <= 1) return;

        point.LineUp(1);
        point.StartOfLine();

        string text = point.GetLine();
        if (RegexNullSafe.IsMatch(text, @"^\s*[^\s\{]")) // If it is not a scope boundary, insert newline.
        {
            point.EndOfLine();
            point.Insert(Environment.NewLine);
        }
    }

    /// <summary>
    /// Inserts a blank line after the specified point except where adjacent to a brace.
    /// </summary>
    /// <param name="point">The point.</param>

    internal static void InsertBlankLineAfterPoint(EditPoint point)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (point.AtEndOfDocument) return;

        point.LineDown(1);
        point.StartOfLine();

        string text = point.GetLine();
        if (RegexNullSafe.IsMatch(text, @"^\s*[^\s\}]"))
        {
            point.Insert(Environment.NewLine);
        }
    }

    /// <summary>
    /// Attempts to move the cursor to the specified code item.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="codeItem">The code item.</param>
    /// <param name="centerOnWhole">True if the whole element should be used for centering.</param>

    internal static void MoveToCodeItem(Document document, BaseCodeItem codeItem, bool centerOnWhole)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var textDocument = document.GetTextDocument();
        if (textDocument == null) return;

        try
        {
            object viewRangeEnd = null;
            TextPoint navigatePoint = null;

            codeItem.RefreshCachedPositionAndName();
            textDocument.Selection.MoveToPoint(codeItem.StartPoint, false);

            if (centerOnWhole)
            {
                viewRangeEnd = codeItem.EndPoint;
            }

            var codeItemElement = codeItem as BaseCodeItemElement;
            if (codeItemElement != null)
            {
                navigatePoint = codeItemElement.CodeElement.GetStartPoint(vsCMPart.vsCMPartNavigate);
            }

            textDocument.Selection.AnchorPoint.TryToShow(vsPaneShowHow.vsPaneShowCentered, viewRangeEnd);

            if (navigatePoint != null)
            {
                textDocument.Selection.MoveToPoint(navigatePoint, false);
            }
            else
            {
                textDocument.Selection.FindText(codeItem.Name, (int)vsFindOptions.vsFindOptionsMatchInHiddenText);
                textDocument.Selection.MoveToPoint(textDocument.Selection.AnchorPoint, false);
            }
        }
        catch (Exception)
        {
            // Move operation may fail if element is no longer available.
        }
        finally
        {
            // Always set focus within the code editor window.
            document.Activate();
        }
    }

    /// <summary>
    /// Attempts to select the text of the specified code item.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="codeItem">The code item.</param>

    internal static void SelectCodeItem(Document document, BaseCodeItem codeItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var textDocument = document.GetTextDocument();
        if (textDocument == null) return;

        try
        {
            codeItem.RefreshCachedPositionAndName();
            textDocument.Selection.MoveToPoint(codeItem.StartPoint, false);
            textDocument.Selection.MoveToPoint(codeItem.EndPoint, true);

            textDocument.Selection.SwapAnchor();
        }
        catch (Exception)
        {
            // Select operation may fail if element is no longer available.
        }
        finally
        {
            // Always set focus within the code editor window.
            document.Activate();
        }
    }

    /// <summary>
    /// Substitutes all occurrences in the specified text document of the specified pattern
    /// string with the specified replacement string.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <param name="patternString">The pattern string.</param>
    /// <param name="replacementString">The replacement string.</param>

    internal static void SubstituteAllStringMatches(TextDocument textDocument, string patternString, string replacementString)
    {
        UIThread.Run(() =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (TryGetTextBufferAt(textDocument.Parent.FullName, out ITextBuffer textBuffer))
            {
                IFinder finder = GetFinder(patternString, replacementString, textBuffer);
                ReplaceAll(textBuffer, finder.FindForReplaceAll());
            }
        });
    }

    /// <summary>
    /// Substitutes all occurrences in the specified text selection of the specified pattern
    /// string with the specified replacement string.
    /// </summary>
    /// <param name="textSelection">The text selection.</param>
    /// <param name="patternString">The pattern string.</param>
    /// <param name="replacementString">The replacement string.</param>

    internal static void SubstituteAllStringMatches(TextSelection textSelection, string patternString, string replacementString)
    {
        UIThread.Run(() =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (TryGetTextBufferAt(textSelection.Parent.Parent.FullName, out ITextBuffer textBuffer))
            {
                IFinder finder = GetFinder(patternString, replacementString, textBuffer);
                ReplaceAll(textBuffer, finder.FindForReplaceAll(GetSnapshotSpanForTextSelection(textBuffer.CurrentSnapshot, textSelection)));
            }
        });
    }

    /// <summary>
    /// Substitutes all occurrences between the specified start and end points of the specified
    /// pattern string with the specified replacement string.
    /// </summary>
    /// <param name="startPoint">The start point.</param>
    /// <param name="endPoint">The end point.</param>
    /// <param name="patternString">The pattern string.</param>
    /// <param name="replacementString">The replacement string.</param>

    internal static void SubstituteAllStringMatches(EditPoint startPoint, EditPoint endPoint, string patternString, string replacementString)
    {
        UIThread.Run(() =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (TryGetTextBufferAt(startPoint.Parent.Parent.FullName, out ITextBuffer textBuffer))
            {
                IFinder finder = GetFinder(patternString, replacementString, textBuffer);
                ReplaceAll(textBuffer, finder.FindForReplaceAll(GetSnapshotSpanForExtent(textBuffer.CurrentSnapshot, startPoint, endPoint)));
            }
        });
    }

    /// <summary>
    /// We need to analyze the C# method and produce exactly one concise summary sentence. Plain text only, no XML, no quotes. Mention key behavior and side effects. Method: GetEditPointForSnapshotPosition takes TextDocument, ITextSnapshot, int position. It throws ThreadHelper.ThrowIfNotOnUIThread() (so it ensures UI thread). Creates an edit point from textDocument. Gets line from snapshot at position. Moves edit point to line (line number + 1) and offset (position - line start + 1). Returns edit point. Side effects: creates an EditPoint object, moves it, no modifications to document. It&apos;s a helper to convert snapshot position to EditPoint coordinates (1-based line and offset). The method assumes UI thread. We need one concise summary sentence. Mention key behavior and side effects. For example: &quot;Creates a TextDocument edit point and moves it to the 1-based line and offset corresponding to the given snapshot position, requiring UI thread access and returning the positioned edit point without modifying the document.&quot; But need to be concise. Let&apos;s craft. Ensure no XML, no quotes. Plain text. Final: &quot;Creates an EditPoint from the document, moves it to the line and character offset computed from.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <param name="textSnapshot">The text snapshot.</param>
    /// <param name="position">The position.</param>
    /// <returns>A EditPoint value produced by this method.</returns>

    private static EditPoint GetEditPointForSnapshotPosition(TextDocument textDocument, ITextSnapshot textSnapshot, int position)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var editPoint = textDocument.CreateEditPoint();
        var textSnapshotLine = textSnapshot.GetLineFromPosition(position);
        editPoint.MoveToLineAndOffset(textSnapshotLine.LineNumber + 1, position - textSnapshotLine.Start.Position + 1);

        return editPoint;
    }

    /// <summary>
    /// Creates an IFinder for the given text by obtaining the IFindService, building a finder factory with StandardFindOptions, and returning a finder bound to the buffer&apos;s current snapshot, with no side effects beyond that service retrieval.
    /// </summary>
    /// <param name="findWhat">The find what.</param>
    /// <param name="textBuffer">The text buffer.</param>
    /// <returns>A IFinder value produced by this method.</returns>

    private static IFinder GetFinder(string findWhat, ITextBuffer textBuffer)
    {
        var findService = CodeJanitorPackage.Instance.ComponentModel.GetService<IFindService>();
        var finderFactory = findService.CreateFinderFactory(findWhat, StandardFindOptions);

        return finderFactory.Create(textBuffer.CurrentSnapshot);
    }

    /// <summary>
    /// Retrieves the shared IFindService from the package&apos;s component model, uses it to create a finder factory with the given find/replace strings and standard options, then returns a finder bound to the buffer&apos;s current snapshot without modifying the buffer or throwing exceptions.
    /// </summary>
    /// <param name="findWhat">The find what.</param>
    /// <param name="replaceWith">The replace with.</param>
    /// <param name="textBuffer">The text buffer.</param>
    /// <returns>A IFinder value produced by this method.</returns>

    private static IFinder GetFinder(string findWhat, string replaceWith, ITextBuffer textBuffer)
    {
        var findService = CodeJanitorPackage.Instance.ComponentModel.GetService<IFindService>();
        var finderFactory = findService.CreateFinderFactory(findWhat, replaceWith, StandardFindOptions);

        return finderFactory.Create(textBuffer.CurrentSnapshot);
    }

    /// <summary>
    /// Converts the selection&apos;s anchor and active points into snapshot positions and returns a normalized Span covering the text range between them, ordering the start and end by position, while requiring execution on the UI thread via ThreadHelper.ThrowIfNotOnUIThread().
    /// </summary>
    /// <param name="textSnapshot">The text snapshot.</param>
    /// <param name="selection">The selection.</param>
    /// <returns>A Span value produced by this method.</returns>

    private static Span GetSnapshotSpanForTextSelection(ITextSnapshot textSnapshot, TextSelection selection)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var startPosition = GetSnapshotPositionForTextPoint(textSnapshot, selection.AnchorPoint);
        var endPosition = GetSnapshotPositionForTextPoint(textSnapshot, selection.ActivePoint);

        if (startPosition <= endPosition)
        {
            return new Span(startPosition, endPosition - startPosition);
        }
        else
        {
            return new Span(endPosition, startPosition - endPosition);
        }
    }

    /// <summary>
    /// Computes the absolute snapshot position for a given text point by subtracting 1 from the line number and character offset, and throws if not called on the UI thread.
    /// </summary>
    /// <param name="textSnapshot">The text snapshot.</param>
    /// <param name="textPoint">The text point.</param>
    /// <returns>A int value produced by this method.</returns>

    private static int GetSnapshotPositionForTextPoint(ITextSnapshot textSnapshot, TextPoint textPoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var textSnapshotLine = textSnapshot.GetLineFromLineNumber(textPoint.Line - 1);

        return textSnapshotLine.Start.Position + textPoint.LineCharOffset - 1;
    }

    /// <summary>
    /// Computes a normalized Span for the given snapshot and edit points, swapping endpoints if needed, and throws if not called on the UI thread.
    /// </summary>
    /// <param name="textSnapshot">The text snapshot.</param>
    /// <param name="startPoint">The start point.</param>
    /// <param name="endPoint">The end point.</param>
    /// <returns>A Span value produced by this method.</returns>

    private static Span GetSnapshotSpanForExtent(ITextSnapshot textSnapshot, EditPoint startPoint, EditPoint endPoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var startPosition = GetSnapshotPositionForTextPoint(textSnapshot, startPoint);
        var endPosition = GetSnapshotPositionForTextPoint(textSnapshot, endPoint);

        if (startPosition <= endPosition)
        {
            return new Span(startPosition, endPosition - startPosition);
        }
        else
        {
            return new Span(endPosition, startPosition - endPosition);
        }
    }

    /// <summary>
    /// Replaces all specified matches in the given text buffer within a single edit transaction, applying the changes only if replacements are present.
    /// </summary>
    /// <param name="textBuffer">The text buffer.</param>
    /// <param name="replacements">The replacements.</param>

    private static void ReplaceAll(ITextBuffer textBuffer, IEnumerable<FinderReplacement> replacements)
    {
        if (replacements.Any())
        {
            using (var edit = textBuffer.CreateEdit())
            {
                foreach (var match in replacements)
                {
                    edit.Replace(match.Match, match.Replace);
                }

                edit.Apply();
            }
        }
    }

    /// <summary>
    /// Attempts to retrieve an open document&apos;s ITextBuffer via the editor adapter factory, returning true and assigning the buffer on success, otherwise setting the out parameter to null and returning false with no exceptions thrown.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="textBuffer">The text buffer.</param>
    /// <returns>A bool value produced by this method.</returns>

    private static bool TryGetTextBufferAt(string filePath, out ITextBuffer textBuffer)
    {
        IVsWindowFrame windowFrame;
        if (VsShellUtilities.IsDocumentOpen(
          CodeJanitorPackage.Instance,
          filePath,
          Guid.Empty,
          out var _,
          out var _,
          out windowFrame))
        {
            IVsTextView view = VsShellUtilities.GetTextView(windowFrame);
            IVsTextLines lines;
            if (view.GetBuffer(out lines) == 0)
            {
                var buffer = lines as IVsTextBuffer;
                if (buffer != null)
                {
                    var editorAdapterFactoryService = CodeJanitorPackage.Instance.ComponentModel.GetService<IVsEditorAdaptersFactoryService>();
                    textBuffer = editorAdapterFactoryService.GetDataBuffer(buffer);

                    return true;
                }
            }
        }

        textBuffer = null;

        return false;
    }
}
