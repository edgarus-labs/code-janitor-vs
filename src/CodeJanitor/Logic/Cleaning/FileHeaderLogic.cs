using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Properties;
using CodeJanitor.UI.Enumerations;
using System;
using System.Collections.Generic;
using System.ComponentModel;

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

    internal static FileHeaderLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new FileHeaderLogic(package));
    }

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

    internal void UpdateFileHeader(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var settingsFileHeader = FileHeaderHelper.GetFileHeaderFromSettings(textDocument);
        if (string.IsNullOrWhiteSpace(settingsFileHeader))
        {
            return;
        }

        if (!settingsFileHeader.EndsWith(Environment.NewLine))
        {
            settingsFileHeader += Environment.NewLine;
        }

        switch ((HeaderUpdateMode)Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode)
        {
            case HeaderUpdateMode.Insert:
                InsertFileHeader(textDocument, settingsFileHeader);
                break;

            case HeaderUpdateMode.Replace:
                ReplaceFileHeader(textDocument, settingsFileHeader);
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
        var headerBlock = ReadTextBlock(textDocument);
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
    /// Inserts the file header into the given text document at the position determined by settings (document start or after usings), throwing InvalidEnumArgumentException for an invalid position, and requires the UI thread.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <param name="settingsFileHeader">The settings file header.</param>
    /// <exception cref="InvalidEnumArgumentException">Thrown when method validation or execution fails for this exception type.</exception>

    private void InsertFileHeader(TextDocument textDocument, string settingsFileHeader)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        switch (FileHeaderHelper.GetFileHeaderPositionFromSettings(textDocument))
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
        var existingFileHeader = cursor.GetText(settingsFileHeader.Length);

        if (!existingFileHeader.StartsWith(settingsFileHeader.Trim()))
        {
            cursor.Insert(settingsFileHeader);
        }
    }

    /// <summary>
    /// Reads the first lines of a document
    /// </summary>
    /// <param name="textDocument">The document to read</param>
    /// <returns>A string representing the first <see cref="HeaderMaxNbLines"/> lines of the document</returns>

    private string ReadTextBlock(TextDocument textDocument)
    {
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

        var maxNbLines = Math.Min(HeaderMaxNbLines, textDocument.EndPoint.Line);
        var blockStart = textDocument.StartPoint.CreateEditPoint();

        return blockStart.GetLines(1, maxNbLines);
    }

    /// <summary>
    /// Replaces the file header at the configured position (document start or after usings) after removing any existing header from the alternate position, throws InvalidEnumArgumentException for invalid settings, and must run on the UI thread.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <param name="settingsFileHeader">The settings file header.</param>
    /// <exception cref="InvalidEnumArgumentException">Thrown when method validation or execution fails for this exception type.</exception>

    private void ReplaceFileHeader(TextDocument textDocument, string settingsFileHeader)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        switch (FileHeaderHelper.GetFileHeaderPositionFromSettings(textDocument))
        {
            case HeaderPosition.DocumentStart:
                ReplaceFileHeaderAfterUsings(textDocument, string.Empty); // Removes header after usings if present
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
        var currentHeaderLength = GetHeaderLength(textDocument, false);

        headerBlockStart.ReplaceText(currentHeaderLength, settingsFileHeader, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
    }
}
