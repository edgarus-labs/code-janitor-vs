using CodeJanitor.Helpers;
using CodeJanitor.Properties;
using CodeJanitor.UI.Enumerations;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A class for encapsulating the logic of file header updates.
/// </summary>
internal sealed class FileHeaderLogic
{
    private readonly CodeJanitorPackage _package;

    /// <summary>
    /// The header max nb lines.
    /// </summary>
    private const int HeaderMaxNbLines = 60;

    /// <summary>
    /// The singleton instance of the <see cref="FileHeaderLogic" /> class.
    /// </summary>
    private static FileHeaderLogic _instance;

    /// <summary>
    /// Gets an instance of the <see cref="FileHeaderLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>An instance of the <see cref="FileHeaderLogic" /> class.</returns>
    internal static FileHeaderLogic GetInstance(CodeJanitorPackage package) => _instance ?? (_instance = new FileHeaderLogic(package));

    /// <summary>
    /// Initializes a new instance of the <see cref="FileHeaderLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    private FileHeaderLogic(CodeJanitorPackage package)
    {
        _package = package;
    }

    /// <summary>
    /// Updates the file header for the specified text document.
    /// </summary>
    /// <param name="textDocument">The text document to update.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>
    internal void UpdateFileHeader(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var language = textDocument.GetCodeLanguage();
        var settingsFileHeader = FileHeaderHelper.GetFileHeaderFromSettings(language, settings);
        if (string.IsNullOrWhiteSpace(settingsFileHeader))
        {
            return;
        }

        if (!settingsFileHeader.EndsWith(Environment.NewLine))
        {
            settingsFileHeader += Environment.NewLine;
        }

        var headerPosition = FileHeaderHelper.GetFileHeaderPositionFromSettings(language, settings);
        switch ((HeaderUpdateMode)settings.GetInt32(nameof(Settings.Cleaning_UpdateFileHeader_HeaderUpdateMode)))
        {
            case HeaderUpdateMode.Insert:
                InsertFileHeader(textDocument, settingsFileHeader, headerPosition);
                break;

            case HeaderUpdateMode.Replace:
                ReplaceFileHeader(textDocument, settingsFileHeader, headerPosition);
                break;

            default:
                throw new InvalidEnumArgumentException("Invalid file header update mode retrieved from settings");
        }
    }

    /// <summary>
    /// Ensures the caller is on the UI thread, reads the document&apos;s text block and code language, then returns the header length computed by FileHeaderHelper, optionally skipping usings, with a side effect of throwing if not on the UI thread.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <param name="skipUsings">The skip usings.</param>
    /// <returns>A int value produced by this method.</returns>
    private int GetHeaderLength(TextDocument textDocument, bool skipUsings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var headerBlock = ReadTextBlock(textDocument, skipUsings ? 1 : GetPrologLineCount(textDocument) + 1);
        var language = textDocument.GetCodeLanguage();

        return FileHeaderHelper.GetHeaderLength(language, headerBlock, skipUsings);
    }

    /// <summary>
    /// Retrieves.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <param name="skipUsings">The skip usings.</param>
    /// <returns>A string value produced by this method.</returns>
    private string GetCurrentHeader(TextDocument textDocument, bool skipUsings)
    {
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

        var currentHeaderLength = GetHeaderLength(textDocument, skipUsings);

        var headerBlockStart = textDocument.StartPoint.CreateEditPoint();

        if (skipUsings)
        {
            var nbLinesToSkip = GetNbLinesToSkip(textDocument);

            headerBlockStart.MoveToLineAndOffset(nbLinesToSkip + 1, 1);
        }
        else
        {
            MoveBelowProlog(textDocument, headerBlockStart);
        }

        return headerBlockStart.GetText(currentHeaderLength + 1).Trim();
    }

    /// <summary>
    /// This method enforces UI-thread execution, reads the document&apos;s head block, and delegates to FileHeaderHelper to return the number of lines to skip based on &quot;using &quot; prefixes and exclusions for namespace/assembly attributes, with no additional side effects.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <returns>A int value produced by this method.</returns>
    private int GetNbLinesToSkip(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var docHeadBlock = ReadTextBlock(textDocument);

        return FileHeaderHelper.GetNbLinesToSkip("using ", docHeadBlock, new List<string> { "namespace ", "[assembly:" });
    }

    /// <summary>
    /// Inserts the file header into the given text document at the configured position (document start or after usings), throwing InvalidEnumArgumentException for an invalid position, and requires the UI thread.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <param name="settingsFileHeader">The settings file header.</param>
    /// <param name="headerPosition">The configured header position.</param>
    /// <exception cref="InvalidEnumArgumentException">Thrown when method validation or execution fails for this exception type.</exception>
    private void InsertFileHeader(TextDocument textDocument, string settingsFileHeader, HeaderPosition headerPosition)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        switch (headerPosition)
        {
            case HeaderPosition.DocumentStart:
                InsertFileHeaderDocumentStart(textDocument, settingsFileHeader);
                return;

            case HeaderPosition.AfterUsings:
                InsertFileHeaderAfterUsings(textDocument, settingsFileHeader);
                return;

            default:
                throw new InvalidEnumArgumentException("Invalid file header position retrieved from settings");
        }
    }

    /// <summary>
    /// Inserts a file header located after the first block of "using" lines
    /// </summary>
    /// <param name="textDocument">The document to update</param>
    /// <param name="settingsFileHeader">The new file header read from the settings</param>
    /// <remarks>Only valid for languages containing "using" directive</remarks>
    private void InsertFileHeaderAfterUsings(TextDocument textDocument, string settingsFileHeader)
    {
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

        var currentHeader = GetCurrentHeader(textDocument, true).Trim();

        if (currentHeader.StartsWith(settingsFileHeader.Trim()))
        {
            return;
        }

        var headerBlockStart = textDocument.StartPoint.CreateEditPoint();
        var nbLinesToSkip = GetNbLinesToSkip(textDocument);

        headerBlockStart.MoveToLineAndOffset(nbLinesToSkip + 1, 1);

        if (!settingsFileHeader.StartsWith(Environment.NewLine))
        {
            settingsFileHeader = Environment.NewLine + settingsFileHeader;
        }

        headerBlockStart.Insert(settingsFileHeader);
    }

    /// <summary>
    /// Inserts the specified file header at the start of the document only if the existing text does not already begin with the trimmed header, modifying the document as a side effect.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <param name="settingsFileHeader">The settings file header.</param>
    private void InsertFileHeaderDocumentStart(TextDocument textDocument, string settingsFileHeader)
    {
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

        var cursor = textDocument.StartPoint.CreateEditPoint();
        MoveBelowProlog(textDocument, cursor);
        var existingFileHeader = cursor.GetText(settingsFileHeader.Length);

        if (!existingFileHeader.StartsWith(settingsFileHeader.Trim()))
        {
            cursor.Insert(settingsFileHeader);
        }
    }

    /// <summary>
    /// Reads the first lines of a document, from the specified line on
    /// </summary>
    /// <param name="textDocument">The document to read</param>
    /// <param name="firstLine">The first line to read, one-based</param>
    /// <returns>A string representing the first <see cref="HeaderMaxNbLines"/> lines of the document from the first line on</returns>
    private string ReadTextBlock(TextDocument textDocument, int firstLine = 1)
    {
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

        var endLine = Math.Min(firstLine - 1 + HeaderMaxNbLines, textDocument.EndPoint.Line);
        var blockStart = textDocument.StartPoint.CreateEditPoint();

        return blockStart.GetLines(firstLine, endLine);
    }

    /// <summary>
    /// Gets the number of lines at the start of the document that must stay its first lines: a shebang, the XML
    /// declaration or the opening tag of PHP.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <returns>The number of lines, zero for a document that starts with its content.</returns>
    private int GetPrologLineCount(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var headBlock = ReadTextBlock(textDocument);
        var prologLength = FileHeaderHelper.GetPrologLength(textDocument.GetCodeLanguage(), headBlock);

        return headBlock.Substring(0, prologLength).Count(c => c == '\n');
    }

    /// <summary>
    /// Moves the edit point to the first line below the prolog of the document, which is where a header at the
    /// document start goes.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <param name="editPoint">The edit point to move.</param>
    private void MoveBelowProlog(TextDocument textDocument, EditPoint editPoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var prologLineCount = GetPrologLineCount(textDocument);
        if (prologLineCount > 0)
        {
            editPoint.MoveToLineAndOffset(prologLineCount + 1, 1);
        }
    }

    /// <summary>
    /// Replaces the file header at the configured position (document start or after usings) after removing any existing header from the alternate position, throws InvalidEnumArgumentException for invalid settings, and must run on the UI thread.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <param name="settingsFileHeader">The settings file header.</param>
    /// <param name="headerPosition">The configured header position.</param>
    /// <exception cref="InvalidEnumArgumentException">Thrown when method validation or execution fails for this exception type.</exception>
    private void ReplaceFileHeader(TextDocument textDocument, string settingsFileHeader, HeaderPosition headerPosition)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        switch (headerPosition)
        {
            case HeaderPosition.DocumentStart:
                // Without usings the header after the usings is the one at the document start, which is replaced below.
                if (HasUsings(textDocument))
                {
                    ReplaceFileHeaderAfterUsings(textDocument, string.Empty); // Removes header after usings if present
                }

                ReplaceFileHeaderDocumentStart(textDocument, settingsFileHeader);
                return;

            case HeaderPosition.AfterUsings:
                ReplaceFileHeaderDocumentStart(textDocument, string.Empty); // Removes header at document start if present
                ReplaceFileHeaderAfterUsings(textDocument, settingsFileHeader);
                return;

            default:
                throw new InvalidEnumArgumentException("Invalid file header position retrieved from settings");
        }
    }

    /// <summary>
    /// Determines whether the document is a C# document that starts with using directives, so that a header after the
    /// usings is not the header at the document start.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <returns>True if the document has using directives, otherwise false.</returns>
    private bool HasUsings(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        return textDocument.GetCodeLanguage() == CodeLanguage.CSharp && GetNbLinesToSkip(textDocument) > 0;
    }

    /// <summary>
    /// Updates the file header located after the first block of "using" lines
    /// </summary>
    /// <param name="textDocument">The document to update</param>
    /// <param name="settingsFileHeader">The new file header read from the settings</param>
    /// <remarks>Only valid for languages containing "using" directive</remarks>
    private void ReplaceFileHeaderAfterUsings(TextDocument textDocument, string settingsFileHeader)
    {
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

        var currentHeader = GetCurrentHeader(textDocument, true).Trim();
        var newHeader = settingsFileHeader.Trim();

        if (string.Equals(currentHeader, newHeader))
        {
            return;
        }

        var headerBlockStart = textDocument.StartPoint.CreateEditPoint();
        var nbLinesToSkip = GetNbLinesToSkip(textDocument);

        headerBlockStart.MoveToLineAndOffset(nbLinesToSkip + 1, 1);

        var currentHeaderLength = GetHeaderLength(textDocument, true);

        if (!settingsFileHeader.StartsWith(Environment.NewLine))
        {
            settingsFileHeader = Environment.NewLine + settingsFileHeader;
        }

        headerBlockStart.ReplaceText(currentHeaderLength, settingsFileHeader, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
    }

    /// <summary>
    /// Replaces the trimmed file header at the document&apos;s start with the provided settings header only if they differ, using a UI-thread check and replacing text while preserving markers.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <param name="settingsFileHeader">The settings file header.</param>
    private void ReplaceFileHeaderDocumentStart(TextDocument textDocument, string settingsFileHeader)
    {
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

        var currentHeader = GetCurrentHeader(textDocument, false).Trim();
        var newHeader = settingsFileHeader.Trim();

        if (string.Equals(currentHeader, newHeader))
        {
            return;
        }

        var headerBlockStart = textDocument.StartPoint.CreateEditPoint();
        MoveBelowProlog(textDocument, headerBlockStart);
        var currentHeaderLength = GetHeaderLength(textDocument, false);

        headerBlockStart.ReplaceText(currentHeaderLength, settingsFileHeader, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
    }
}
