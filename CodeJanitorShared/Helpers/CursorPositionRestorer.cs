using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;

namespace CodeJanitor.Helpers;

/// <summary>
/// A class that handles tracking the cursor position and restoring it, typically in a using
/// statement context.
/// </summary>

internal sealed class CursorPositionRestorer : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CursorPositionRestorer" /> class.
    /// </summary>
    /// <param name="textDocument">The text document.</param>

    internal CursorPositionRestorer(TextDocument textDocument)
    {
        TextDocument = textDocument;

        CaptureCursorPosition();
    }

    /// <summary>
    /// Captures the current cursor position.
    /// </summary>

    internal void CaptureCursorPosition()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (TextDocument != null && TextDocument.Selection != null)
        {
            TrackedCursorPosition = new CursorPosition(TextDocument.Selection);
        }
    }

    /// <summary>
    /// Restores the cursor position.
    /// </summary>

    internal void RestoreCursorPosition()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (TextDocument != null && TextDocument.Selection != null)
        {
            if (IsCursorPositionReset() && TrackedCursorPosition.Line > 1)
            {
                TextDocument.Selection.MoveTo(TrackedCursorPosition.Line, TrackedCursorPosition.Column, false);
            }
        }
    }

    /// <summary>
    /// Determines whether the cursor position has been reset.
    /// </summary>
    /// <remarks>
    /// Currently using cursor being reset to the StartOfDocument as key that cursor position
    /// was lost. Tried using bookmarks to track locations but they are also lost on format
    /// document calls in CSS files.
    /// </remarks>
    /// <returns>True if the cursor position was reset, otherwise false.</returns>

    private bool IsCursorPositionReset()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        return TextDocument.Selection.ActivePoint.AtStartOfDocument ||
               TextDocument.Selection.ActivePoint.AtEndOfDocument;
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting
    /// unmanaged resources.
    /// </summary>

    public void Dispose()
    {
        RestoreCursorPosition();
    }

    /// <summary>
    /// Gets or sets the text document.
    /// </summary>
    private TextDocument TextDocument { get; set; }

    /// <summary>
    /// Gets or sets the tracked cursor position.
    /// </summary>
    private CursorPosition TrackedCursorPosition { get; set; }

    /// <summary>
    /// A structure for capturing cursor position.
    /// </summary>

    private struct CursorPosition
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CursorPosition" /> struct.
        /// </summary>
        /// <param name="textSelection">The text selection.</param>

        public CursorPosition(TextSelection textSelection)
        {
            Line = textSelection.CurrentLine;
            Column = textSelection.CurrentColumn;
        }

        public readonly int Line;
        public readonly int Column;
    }
}
