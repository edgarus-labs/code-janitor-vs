using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeJanitor.Helpers;

/// <summary>
/// Reads and writes source files on disk so that their encoding and line endings follow .editorconfig
/// (<c>charset</c>, <c>end_of_line</c>) and otherwise stay as the file had them. A missing or <c>unset</c> option
/// means "do not change", not "use the tool default".
/// </summary>
internal static class FileTextStyle
{
    private const string CharsetKey = "charset";
    private const string EndOfLineKey = "end_of_line";
    private const string Lf = "\n";
    private const string CrLf = "\r\n";
    private const string Cr = "\r";
    private const int Latin1CodePage = 28591;

    private static readonly Regex LineBreak = new Regex("\r\n|\r|\n", RegexOptions.Compiled);

    /// <summary>
    /// Reads a file as text, detecting its encoding from the byte order mark. A file without one is read with the
    /// encoding .editorconfig <c>charset</c> names when it is <c>latin1</c>, <c>utf-16le</c> or <c>utf-16be</c>, and
    /// otherwise as UTF-8 reported without a byte order mark, so writing it back does not add one.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="encoding">The encoding the file had, including whether it had a byte order mark.</param>
    /// <returns>The file text.</returns>
    internal static string ReadAllText(string filePath, out Encoding encoding)
    {
        var charset = ParseCharset(EditorConfigHelper.LoadOptions(filePath));
        var encodingWithoutByteOrderMark = charset is null || charset is UTF8Encoding ? new UTF8Encoding(false) : charset;

        using (var reader = new StreamReader(filePath, encodingWithoutByteOrderMark, detectEncodingFromByteOrderMarks: true))
        {
            var text = reader.ReadToEnd();
            encoding = reader.CurrentEncoding;

            return text;
        }
    }

    /// <summary>
    /// Writes text to a file. The encoding is the one .editorconfig <c>charset</c> names when it can represent every
    /// character of the text, otherwise <paramref name="encoding" />. The line endings are the ones
    /// <c>end_of_line</c> names; without that option the text takes the line endings of
    /// <paramref name="originalText" /> when it uses a single kind, and is written as it is when the original mixed
    /// several kinds.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="text">The text to write.</param>
    /// <param name="encoding">The encoding to use when .editorconfig does not name one.</param>
    /// <param name="originalText">The text the file had before the change, or null for a new file.</param>
    /// <param name="styleFilePath">
    /// The path whose .editorconfig applies, when the text is first written to a temporary file next to its final
    /// location; defaults to <paramref name="filePath" />.
    /// </param>
    /// <exception cref="IOException">
    /// Neither encoding can represent every character of the text (for example <c>€</c> in a Latin-1 file); the file is
    /// left unchanged.
    /// </exception>
    internal static void WriteAllText(string filePath, string text, Encoding encoding, string originalText, string styleFilePath = null)
    {
        var options = EditorConfigHelper.LoadOptions(styleFilePath ?? filePath);
        var lineEnding = ParseEndOfLine(options) ?? GetUniformLineEnding(originalText);
        var styledText = lineEnding is null ? text : NormalizeLineEndings(text, lineEnding);
        var charset = ParseCharset(options);
        var targetEncoding = charset is not null && EncodesWithoutLoss(charset, styledText) ? charset : encoding;
        if (!EncodesWithoutLoss(targetEncoding, styledText))
        {
            throw new IOException(
                $"'{styleFilePath ?? filePath}' cannot be written as {targetEncoding.WebName} without replacing characters that encoding cannot represent; it was left unchanged.");
        }

        File.WriteAllText(filePath, styledText, targetEncoding);
    }

    /// <summary>
    /// Determines whether writing <paramref name="text" /> with <paramref name="encoding" /> keeps every character:
    /// Unicode encodings represent all of them, an encoding whose fallback throws reports a loss itself, and any other
    /// encoding would silently replace what it cannot represent.
    /// </summary>
    private static bool EncodesWithoutLoss(Encoding encoding, string text)
    {
        if (encoding is UTF8Encoding || encoding is UnicodeEncoding || encoding is UTF32Encoding || encoding.EncoderFallback is EncoderExceptionFallback)
        {
            return true;
        }

        try
        {
            Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback).GetByteCount(text);

            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Replaces the text of an existing file as <see cref="WriteAllText" /> writes it, but atomically: the text is
    /// written to a temporary file next to it first, which then replaces the file, keeping a backup of it until the
    /// replace succeeded. A write that fails leaves the file as it was.
    /// </summary>
    /// <param name="filePath">The path of the existing file.</param>
    /// <param name="text">The text to write.</param>
    /// <param name="encoding">The encoding to use when .editorconfig does not name one.</param>
    /// <param name="originalText">The text the file had before the change.</param>
    internal static void ReplaceAllText(string filePath, string text, Encoding encoding, string originalText) =>
        ReplaceAllText(filePath, text, encoding, originalText, File.Replace);

    /// <summary>
    /// Replaces the text of an existing file as <see cref="ReplaceAllText(string, string, Encoding, string)" /> does,
    /// with <paramref name="replaceFile" /> in place of <see cref="File.Replace(string, string, string)" />.
    /// </summary>
    /// <param name="filePath">The path of the existing file.</param>
    /// <param name="text">The text to write.</param>
    /// <param name="encoding">The encoding to use when .editorconfig does not name one.</param>
    /// <param name="originalText">The text the file had before the change.</param>
    /// <param name="replaceFile">Replaces a file (source, destination, backup) as <see cref="File.Replace(string, string, string)" /> does.</param>
    internal static void ReplaceAllText(string filePath, string text, Encoding encoding, string originalText, Action<string, string, string> replaceFile)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var tempFilePath = filePath + ".codejanitor.tmp." + suffix;
        var backupFilePath = filePath + ".codejanitor.bak." + suffix;
        try
        {
            WriteAllText(tempFilePath, text, encoding, originalText, filePath);
            replaceFile(tempFilePath, filePath, backupFilePath);
        }
        catch
        {
            // A replace can fail after it moved the file to the backup; the file is restored before the new text is
            // dropped, and the new text is kept when the file cannot be restored.
            if (!File.Exists(filePath) && File.Exists(backupFilePath))
            {
                File.Move(backupFilePath, filePath);
            }

            if (File.Exists(filePath) && File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }

            throw;
        }

        try
        {
            File.Delete(backupFilePath);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            OutputWindowHelper.WarningWriteLine($"The new text of '{filePath}' was written, but its backup '{backupFilePath}' could not be deleted: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the encoding a write to the file uses: the one .editorconfig <c>charset</c> names, otherwise
    /// <paramref name="encoding" />.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="encoding">The encoding used when .editorconfig does not name one.</param>
    /// <returns>The encoding.</returns>
    internal static Encoding ResolveEncoding(string filePath, Encoding encoding) => ParseCharset(EditorConfigHelper.LoadOptions(filePath)) ?? encoding;

    /// <summary>
    /// Gets the line ending to use for text generated for a file: .editorconfig <c>end_of_line</c>, otherwise the
    /// dominant line ending of the text, otherwise <see cref="Environment.NewLine" />.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="text">The current text of the file.</param>
    /// <returns>The line ending.</returns>
    internal static string ResolveLineEnding(string filePath, string text) => ParseEndOfLine(EditorConfigHelper.LoadOptions(filePath)) ?? GetDominantLineEnding(text);

    /// <summary>
    /// Gets the line ending used by most lines of a text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The dominant line ending, or <see cref="Environment.NewLine" /> when the text has no line break.</returns>
    internal static string GetDominantLineEnding(string text)
    {
        CountLineEndings(text, out var lf, out var crLf, out var cr);
        var dominant = Environment.NewLine;
        var dominantCount = dominant == CrLf ? crLf : lf;
        foreach (var candidate in new[] { (Lf, lf), (CrLf, crLf), (Cr, cr) })
        {
            if (candidate.Item2 > dominantCount)
            {
                dominant = candidate.Item1;
                dominantCount = candidate.Item2;
            }
        }

        return dominant;
    }

    /// <summary>
    /// Replaces every line break of a text with the specified line ending.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="lineEnding">The line ending.</param>
    /// <returns>The text with uniform line endings.</returns>
    internal static string NormalizeLineEndings(string text, string lineEnding) => string.IsNullOrEmpty(text) ? text : LineBreak.Replace(text, _ => lineEnding);

    /// <summary>
    /// Gets the line ending of a text that uses exactly one kind.
    /// </summary>
    /// <param name="text">The text, or null.</param>
    /// <returns>The line ending, or null when the text is null, has no line break or mixes several kinds.</returns>
    private static string GetUniformLineEnding(string text)
    {
        if (text is null)
        {
            return null;
        }

        CountLineEndings(text, out var lf, out var crLf, out var cr);
        if (lf > 0 && crLf == 0 && cr == 0)
        {
            return Lf;
        }

        if (crLf > 0 && lf == 0 && cr == 0)
        {
            return CrLf;
        }

        return cr > 0 && lf == 0 && crLf == 0 ? Cr : null;
    }

    private static void CountLineEndings(string text, out int lf, out int crLf, out int cr)
    {
        lf = 0;
        crLf = 0;
        cr = 0;
        if (text is null)
        {
            return;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lf++;
            }
            else if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crLf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
        }
    }

    private static string ParseEndOfLine(IReadOnlyDictionary<string, string> options)
    {
        switch (ReadOption(options, EndOfLineKey))
        {
            case "lf":
                return Lf;

            case "crlf":
                return CrLf;

            case "cr":
                return Cr;

            default:
                return null;
        }
    }

    private static Encoding ParseCharset(IReadOnlyDictionary<string, string> options)
    {
        switch (ReadOption(options, CharsetKey))
        {
            case "utf-8":
                return new UTF8Encoding(false);

            case "utf-8-bom":
                return new UTF8Encoding(true);

            case "utf-16le":
                return new UnicodeEncoding(false, true);

            case "utf-16be":
                return new UnicodeEncoding(true, true);

            case "latin1":
                return Encoding.GetEncoding(Latin1CodePage);

            default:
                return null;
        }
    }

    private static string ReadOption(IReadOnlyDictionary<string, string> options, string key) => options.TryGetValue(key, out var value) && value is not null ? value.Trim().ToLowerInvariant() : null;
}
