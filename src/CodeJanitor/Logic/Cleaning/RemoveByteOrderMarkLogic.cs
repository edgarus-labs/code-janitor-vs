using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Properties;
using System;
using System.IO;
using System.Text;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// Encapsulates the logic of removing Byte Order Mark (BOM) from source files and text documents.
/// </summary>
internal sealed class RemoveByteOrderMarkLogic
{
    /// <summary>
    /// The bom char.
    /// </summary>
    private const char BomChar = '\uFEFF';

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf32LeBom = [0xFF, 0xFE, 0x00, 0x00];
    private static readonly byte[] Utf32BeBom = [0x00, 0x00, 0xFE, 0xFF];
    private static readonly byte[] Utf16LeBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BeBom = [0xFE, 0xFF];

    private readonly CodeJanitorPackage _package;
    private static RemoveByteOrderMarkLogic _instance;

    /// <summary>
    /// Gets the singleton instance of the <see cref="RemoveByteOrderMarkLogic" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>An instance of <see cref="RemoveByteOrderMarkLogic" />.</returns>
    internal static RemoveByteOrderMarkLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new RemoveByteOrderMarkLogic(package));
    }

    private RemoveByteOrderMarkLogic(CodeJanitorPackage package)
    {
        _package = package;
    }

    /// <summary>
    /// Removes the Byte Order Mark character from the beginning of the open text document if present.
    /// </summary>
    /// <param name="textDocument">The text document to clean.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>
    internal void RemoveByteOrderMark(TextDocument textDocument, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (textDocument is null || !settings.GetBoolean(nameof(Settings.Cleaning_RemoveByteOrderMark)))
        {
            return;
        }

        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var fullText = startPoint.GetText(textDocument.EndPoint);

        if (!string.IsNullOrEmpty(fullText) && fullText[0] == BomChar)
        {
            var endPoint = textDocument.EndPoint.CreateEditPoint();
            var cleanedText = fullText.Substring(1);
            startPoint.ReplaceText(endPoint, cleanedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
        }
    }

    /// <summary>
    /// Removes the Byte Order Mark from the document.
    /// </summary>
    /// <param name="document">The document.</param>
    internal void RemoveByteOrderMark(Document document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (document is null)
        {
            return;
        }

        var textDocument = document.GetTextDocument();
        if (textDocument is not null)
        {
            RemoveByteOrderMark(textDocument, EffectiveCleanupSettings.For(document.FullName));
        }
    }

    /// <summary>
    /// Removes the Byte Order Mark from the project item.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>True if the file on disk was modified, otherwise false.</returns>
    internal bool RemoveByteOrderMark(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (projectItem is null)
        {
            return false;
        }

        if (projectItem.Document is not null)
        {
            RemoveByteOrderMark(projectItem.Document);

            return false;
        }

        var filePath = projectItem.GetFileName();
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            return RemoveByteOrderMark(filePath);
        }

        return false;
    }

    /// <summary>
    /// Removes the Byte Order Mark bytes from the file on disk, writing it back as UTF-8 without BOM.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>True if the file was modified, otherwise false.</returns>
    internal bool RemoveByteOrderMark(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath) ||
            !EffectiveCleanupSettings.For(filePath).GetBoolean(nameof(Settings.Cleaning_RemoveByteOrderMark)))
        {
            return false;
        }

        var bytes = File.ReadAllBytes(filePath);
        if (!HasByteOrderMark(bytes))
        {
            return false;
        }

        var cleanedBytes = StripByteOrderMark(bytes);
        File.WriteAllBytes(filePath, cleanedBytes);

        return true;
    }

    /// <summary>
    /// Checks whether the given byte array begins with any known Unicode Byte Order Mark (UTF-8, UTF-16 LE/BE, UTF-32 LE/BE).
    /// </summary>
    /// <param name="bytes">The byte array to check.</param>
    /// <returns>True if a BOM prefix is detected, otherwise false.</returns>
    internal static bool HasByteOrderMark(byte[] bytes)
    {
        if (bytes is null || bytes.Length < 2)
        {
            return false;
        }

        if (bytes.Length >= 3 && bytes[0] == Utf8Bom[0] && bytes[1] == Utf8Bom[1] && bytes[2] == Utf8Bom[2])
        {
            return true;
        }

        if (bytes.Length >= 4 &&
            ((bytes[0] == Utf32LeBom[0] && bytes[1] == Utf32LeBom[1] && bytes[2] == Utf32LeBom[2] && bytes[3] == Utf32LeBom[3]) ||
             (bytes[0] == Utf32BeBom[0] && bytes[1] == Utf32BeBom[1] && bytes[2] == Utf32BeBom[2] && bytes[3] == Utf32BeBom[3])))
        {
            return true;
        }

        if ((bytes[0] == Utf16LeBom[0] && bytes[1] == Utf16LeBom[1]) ||
            (bytes[0] == Utf16BeBom[0] && bytes[1] == Utf16BeBom[1]))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Strips any Byte Order Mark from the byte array and returns the resulting UTF-8 bytes without BOM.
    /// </summary>
    /// <param name="bytes">The byte array.</param>
    /// <returns>A byte array encoded in UTF-8 without BOM.</returns>
    internal static byte[] StripByteOrderMark(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return bytes ?? [];
        }

        // UTF-8 BOM
        if (bytes.Length >= 3 && bytes[0] == Utf8Bom[0] && bytes[1] == Utf8Bom[1] && bytes[2] == Utf8Bom[2])
        {
            var result = new byte[bytes.Length - 3];
            Buffer.BlockCopy(bytes, 3, result, 0, result.Length);

            return result;
        }

        // UTF-32 LE
        if (bytes.Length >= 4 && bytes[0] == Utf32LeBom[0] && bytes[1] == Utf32LeBom[1] && bytes[2] == Utf32LeBom[2] && bytes[3] == Utf32LeBom[3])
        {
            var text = Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4);

            return new UTF8Encoding(false).GetBytes(text);
        }

        // UTF-32 BE
        if (bytes.Length >= 4 && bytes[0] == Utf32BeBom[0] && bytes[1] == Utf32BeBom[1] && bytes[2] == Utf32BeBom[2] && bytes[3] == Utf32BeBom[3])
        {
            var text = new UTF32Encoding(true, false).GetString(bytes, 4, bytes.Length - 4);

            return new UTF8Encoding(false).GetBytes(text);
        }

        // UTF-16 LE
        if (bytes.Length >= 2 && bytes[0] == Utf16LeBom[0] && bytes[1] == Utf16LeBom[1])
        {
            var text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

            return new UTF8Encoding(false).GetBytes(text);
        }

        // UTF-16 BE
        if (bytes.Length >= 2 && bytes[0] == Utf16BeBom[0] && bytes[1] == Utf16BeBom[1])
        {
            var text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            return new UTF8Encoding(false).GetBytes(text);
        }

        return bytes;
    }

    /// <summary>
    /// Checks whether the string starts with the Unicode BOM character U+FEFF.
    /// </summary>
    /// <param name="text">The text to check.</param>
    /// <returns>True if text begins with U+FEFF, otherwise false.</returns>
    internal static bool HasByteOrderMark(string text)
    {
        return !string.IsNullOrEmpty(text) && text[0] == BomChar;
    }

    /// <summary>
    /// Strips the Unicode BOM character U+FEFF from the start of the string if present.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The text without leading BOM.</returns>
    internal static string StripByteOrderMark(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return text[0] == BomChar ? text.Substring(1) : text;
    }
}
