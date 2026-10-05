using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using CodeJanitor.UI.Enumerations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeJanitor.Helpers;

/// <summary>
/// A utility class that provides methods for analyzing, extracting, and processing file headers, including determining header length, position, line skipping, and handling using directives.
/// </summary>
internal static class FileHeaderHelper
{
    /// <summary>
    /// Gets the file header configured for the specified language in the effective cleanup settings of a document.
    /// </summary>
    /// <param name="language">The code language of the document.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>
    /// <returns>The configured file header, or null for a language without a file header setting.</returns>
    internal static string GetFileHeaderFromSettings(CodeLanguage language, EffectiveCleanupSettings settings)
    {
        switch (language)
        {
            case CodeLanguage.CPlusPlus: return settings.GetString(nameof(Settings.Cleaning_UpdateFileHeaderCPlusPlus));
            case CodeLanguage.CSharp: return settings.GetString(nameof(Settings.Cleaning_UpdateFileHeaderCSharp));
            case CodeLanguage.CSS: return settings.GetString(nameof(Settings.Cleaning_UpdateFileHeaderCSS));
            case CodeLanguage.FSharp: return settings.GetString(nameof(Settings.Cleaning_UpdateFileHeaderFSharp));
            case CodeLanguage.HTML: return settings.GetString(nameof(Settings.Cleaning_UpdateFileHeaderHTML));
            case CodeLanguage.JavaScript: return settings.GetString(nameof(Settings.Cleaning_UpdateFileHeaderJavaScript));
            case CodeLanguage.JSON: return settings.GetString(nameof(Settings.Cleaning_UpdateFileHeaderJSON));
            case CodeLanguage.LESS: return settings.GetString(nameof(Settings.Cleaning_UpdateFileHeaderLESS));
            case CodeLanguage.PHP: return settings.GetString(nameof(Settings.Cleaning_UpdateFileHeaderPHP));
            case CodeLanguage.PowerShell: return settings.GetString(nameof(Settings.Cleaning_UpdateFileHeaderPowerShell));
            case CodeLanguage.R: return settings.GetString(nameof(Settings.Cleaning_UpdateFileHeaderR));
            case CodeLanguage.SCSS: return settings.GetString(nameof(Settings.Cleaning_UpdateFileHeaderSCSS));
            case CodeLanguage.TypeScript: return settings.GetString(nameof(Settings.Cleaning_UpdateFileHeaderTypeScript));
            case CodeLanguage.VisualBasic: return settings.GetString(nameof(Settings.Cleaning_UpdateFileHeaderVB));
            case CodeLanguage.XAML: return settings.GetString(nameof(Settings.Cleaning_UpdateFileHeaderXAML));
            case CodeLanguage.XML: return settings.GetString(nameof(Settings.Cleaning_UpdateFileHeaderXML));
            default: return null;
        }
    }

    /// <summary>
    /// Returns the header position for the specified language: the configured position from the effective cleanup
    /// settings of the document for C#, and <see cref="HeaderPosition.DocumentStart" /> for every other language.
    /// </summary>
    /// <param name="language">The code language of the document.</param>
    /// <param name="settings">The effective cleanup settings of the document.</param>
    /// <returns>The header position.</returns>
    internal static HeaderPosition GetFileHeaderPositionFromSettings(CodeLanguage language, EffectiveCleanupSettings settings)
    {
        switch (language)
        {
            case CodeLanguage.CSharp:
                return (HeaderPosition)settings.GetInt32(nameof(Settings.Cleaning_UpdateFileHeader_HeaderPosition));

            default:
                return HeaderPosition.DocumentStart;
        }
    }

    /// <summary>
    /// Returns the total character length of leading comment headers in the given text based on the specified language, summing lengths for each applicable comment syntax, and for C# optionally skipping using directives.
    /// </summary>
    /// <param name="language">The language.</param>
    /// <param name="text">The text.</param>
    /// <param name="skipUsings">The skip usings.</param>
    /// <returns>A int value produced by this method.</returns>
    internal static int GetHeaderLength(CodeLanguage language, string text, bool skipUsings = false)
    {
        switch (language)
        {
            case CodeLanguage.CSharp:
                return GetHeaderLength(text, "//", skipUsings) +
                    GetHeaderLength(text, "/*", "*/", skipUsings);

            case CodeLanguage.CPlusPlus:
            case CodeLanguage.JavaScript:
            case CodeLanguage.JSON:
            case CodeLanguage.LESS:
            case CodeLanguage.SCSS:
            case CodeLanguage.TypeScript:
                return GetHeaderLength(text, "//") +
                    GetHeaderLength(text, "/*", "*/");

            case CodeLanguage.HTML:
            case CodeLanguage.XAML:
            case CodeLanguage.XML:
                return GetHeaderLength(text, "<!--", "-->");

            case CodeLanguage.CSS:
                return GetHeaderLength(text, "/*", "*/");

            case CodeLanguage.PHP:
                return GetHeaderLength(text, "//") +
                    GetHeaderLength(text, "#") +
                    GetHeaderLength(text, "/*", "*/");

            case CodeLanguage.PowerShell:
                return GetHeaderLength(text, "#") +
                    GetHeaderLength(text, "<#", "#>");

            case CodeLanguage.R:
                return GetHeaderLength(text, "#");

            case CodeLanguage.FSharp:
                return GetHeaderLength(text, "//") +
                    GetHeaderLength(text, "(*", "*)");

            case CodeLanguage.VisualBasic:
                return GetHeaderLength(text, "'");

            case CodeLanguage.Unknown:
            default:
                return 0;
        }
    }

    /// <summary>
    /// Gets the length of the lines at the start of the text that must stay the first lines of the file, so that a
    /// file header goes below them: a shebang line (<c>#!</c>), the XML declaration of XML, XAML and HTML, and the
    /// opening tag of PHP.
    /// </summary>
    /// <param name="language">The language of the text.</param>
    /// <param name="text">The text of the file; line breaks may be CR LF or LF.</param>
    /// <returns>The number of characters of those lines, including their line breaks.</returns>
    internal static int GetPrologLength(CodeLanguage language, string text)
    {
        var length = 0;

        if (TryReadLine(text, length, out var line, out var nextLine) && line.StartsWith("#!", StringComparison.Ordinal))
        {
            length = nextLine;
        }

        switch (language)
        {
            case CodeLanguage.XML:
            case CodeLanguage.XAML:
            case CodeLanguage.HTML:
                if (TryReadLine(text, length, out line, out nextLine) && line.TrimStart().StartsWith("<?xml", StringComparison.Ordinal))
                {
                    var declarationEnd = length;
                    while (TryReadLine(text, declarationEnd, out line, out nextLine))
                    {
                        declarationEnd = nextLine;
                        if (line.Contains("?>"))
                        {
                            return declarationEnd;
                        }
                    }
                }

                break;

            case CodeLanguage.PHP:
                if (TryReadLine(text, length, out line, out nextLine) && (line.Trim() == "<?php" || line.Trim() == "<?"))
                {
                    length = nextLine;
                }

                break;
        }

        return length;
    }

    /// <summary>
    /// Reads the line of the text that starts at the index.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="index">The index of the first character of the line.</param>
    /// <param name="line">The line without its line break.</param>
    /// <param name="nextLine">The index of the first character after the line break, or the length of the text for the last line.</param>
    /// <returns>True if the text has a line at the index, otherwise false.</returns>
    private static bool TryReadLine(string text, int index, out string line, out int nextLine)
    {
        line = null;
        nextLine = index;
        if (index >= text.Length)
        {
            return false;
        }

        var lineBreak = text.IndexOf('\n', index);
        nextLine = lineBreak < 0 ? text.Length : lineBreak + 1;
        line = text.Substring(index, (lineBreak < 0 ? text.Length : lineBreak) - index).TrimEnd('\r');

        return true;
    }

    /// <summary>
    /// Determines the header length in the given text by delegating to a specialized overload that skips usings when skipUsings is true, or to the standard header-length calculation otherwise, with no exceptions thrown.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="commentSyntax">The comment syntax.</param>
    /// <param name="skipUsings">The skip usings.</param>
    /// <returns>A int value produced by this method.</returns>
    internal static int GetHeaderLength(string text, string commentSyntax, bool skipUsings)
    {
        if (skipUsings)
        {
            return GetHeaderLengthSkipUsings(text, commentSyntax);
        }

        return GetHeaderLength(text, commentSyntax);
    }

    /// <summary>
    /// Determines the header length in the given text using the provided comment delimiters, delegating to a variant that skips using directives when skipUsings is true, with no side effects.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="commentSyntaxStart">The comment syntax start.</param>
    /// <param name="commentSyntaxEnd">The comment syntax end.</param>
    /// <param name="skipUsings">The skip usings.</param>
    /// <returns>A int value produced by this method.</returns>
    internal static int GetHeaderLength(string text, string commentSyntaxStart, string commentSyntaxEnd, bool skipUsings)
    {
        if (skipUsings)
        {
            return GetHeaderLengthSkipUsings(text, commentSyntaxStart, commentSyntaxEnd);
        }

        return GetHeaderLength(text, commentSyntaxStart, commentSyntaxEnd);
    }

    /// <summary>
    /// Gets the number of lines to skip to pass occurrences of <paramref name="startOfLine"/>
    /// </summary>
    /// <param name="startOfLine">The pattern of the start of lines we want to skip</param>
    /// <param name="text">The document to search</param>
    /// <param name="limits">The limits not to pass. <paramref name="startOfLine"/> beyond those limits are ignored</param>
    /// <returns>The number of lines to skip</returns>
    internal static int GetNbLinesToSkip(string startOfLine, string text, IEnumerable<string> limits)
    {
        var max = GetLowestIndex(text, limits);
        var potentialTextBlock = text.Substring(0, max);
        var lastIndex = potentialTextBlock.LastIndexOf(startOfLine);

        if (lastIndex == -1)
        {
            return 0;
        }

        var relevantTextBlock = potentialTextBlock.Substring(0, lastIndex);

        return Regex.Matches(relevantTextBlock, Environment.NewLine).Count + 1;
    }

    /// <summary>
    /// Returns the sequence of empty or whitespace-only lines encountered after skipping the specified number of lines, stopping at the first non-empty line and producing no side effects.
    /// </summary>
    /// <param name="lines">The lines.</param>
    /// <param name="nbLinesToSkip">The nb lines to skip.</param>
    /// <returns>A IEnumerable&lt;string&gt; value produced by this method.</returns>
    private static IEnumerable<string> GetEmptyLines(IEnumerable<string> lines, int nbLinesToSkip)
    {
        var result = new List<string>();

        foreach (var line in lines.Skip(nbLinesToSkip))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                break;
            }

            result.Add(line);
        }

        return result;
    }

    /// <summary>
    /// Computes the length of the header
    /// </summary>
    /// <param name="text">The beginning of the document containing the header</param>
    /// <param name="commentSyntax">The syntax of the comment tag in the processed language</param>
    /// <returns>The header length</returns>
    /// <remarks>EnvDTE API only counts 1 character per end of line (\r\n counts for 1)</remarks>
    private static int GetHeaderLength(string text, string commentSyntax)
    {
        if (!text.TrimStart().StartsWith(commentSyntax))
        {
            return 0;
        }

        var lines = SplitLines(text);
        var header = new List<string>();

        header.AddRange(GetEmptyLines(lines, 0));
        header.AddRange(GetLinesStartingWith(commentSyntax, lines, header.Count));

        var nbChar = 0;

        if (header.Count() == 0)
        {
            return 0;
        }

        header.ToList().ForEach(x => nbChar += x.Length + 1);

        return nbChar;
    }

    /// <summary>
    /// Computes the length of the header
    /// </summary>
    /// <param name="text">The beginning of the document containing the header</param>
    /// <param name="commentSyntaxStart">The syntax of the comment tag start in the processed language</param>
    /// <param name="commentSyntaxEnd">The syntax of the comment tag end in the processed language</param>
    /// <returns>The header length</returns>
    /// <remarks>EnvDTE API only counts 1 character per end of line (\r\n counts for 1)</remarks>
    private static int GetHeaderLength(string text, string commentSyntaxStart, string commentSyntaxEnd)
    {
        if (!text.TrimStart().StartsWith(commentSyntaxStart) || text.IndexOf(commentSyntaxEnd) == -1)
        {
            return 0;
        }

        var lines = SplitLines(text);
        var header = new List<string>();

        header.AddRange(GetEmptyLines(lines, 0));

        foreach (var line in lines.Skip(header.Count()))
        {
            header.Add(line);

            if (line.TrimEnd().EndsWith(commentSyntaxEnd))
            {
                break;
            }
        }

        var nbChar = 0;

        if (header.Count() == 0)
        {
            return 0;
        }

        header.ToList().ForEach(x => nbChar += x.Length + 1);

        return nbChar;
    }

    /// <summary>
    /// Removes using directives, then computes the total character length of consecutive comment-prefixed lines (including one newline per line) and returns that length plus one, or zero if no such header lines exist.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="commentSyntax">The comment syntax.</param>
    /// <returns>A int value produced by this method.</returns>
    private static int GetHeaderLengthSkipUsings(string text, string commentSyntax)
    {
        text = SkipUsings(text);

        var lines = SplitLines(text);
        var header = new List<string>();
        header.AddRange(GetLinesStartingWith(commentSyntax, lines));

        var nbChar = 0;
        header.ToList().ForEach(x => nbChar += x.Length + 1);

        return nbChar == 0 ? 0 : nbChar + 1;
    }

    /// <summary>
    /// We need to produce exactly one concise summary sentence, plain text, no XML, no quotes. Mention key behavior and side effects. The method: skips usings via SkipUsings (side effect? modifies local copy? It reassigns text, but SkipUsings might have side effects? We don&apos;t know, but likely pure. Then finds comment syntax start and end, if missing returns 0. Computes header substring length, counts newlines, returns computed length. We need mention behavior: returns length of header comment after skipping usings, compensating for newlines and end delimiter. Side effects: none (or modifies local text only). We&apos;ll produce a concise sentence. Let&apos;s craft: &quot;After skipping using directives, this method returns the length of the header comment delimited by the given syntax (or 0 if markers are absent or empty), adjusting for newline characters and including the end delimiter length.&quot; But need to be accurate: It returns header.Length + commentSyntaxEnd.Length - nbNewLines + 1. Since endIndex is index of commentSyntaxEnd, substring from startIndex to endIndex-startIndex excludes the end delimiter. Then adds end delimiter length, subtracts newlines, plus 1. Why plus 1? Possibly to include.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="commentSyntaxStart">The comment syntax start.</param>
    /// <param name="commentSyntaxEnd">The comment syntax end.</param>
    /// <returns>A int value produced by this method.</returns>
    private static int GetHeaderLengthSkipUsings(string text, string commentSyntaxStart, string commentSyntaxEnd)
    {
        text = SkipUsings(text);

        var startIndex = text.IndexOf(commentSyntaxStart);
        var endIndex = text.IndexOf(commentSyntaxEnd);

        if (startIndex == -1 || endIndex == -1)
        {
            return 0;
        }

        var header = text.Substring(startIndex, endIndex - startIndex);
        var nbNewLines = Regex.Matches(header, Environment.NewLine).Count;

        if (header.Length == 0 && nbNewLines == 0)
        {
            return 0;
        }

        return header.Length + commentSyntaxEnd.Length - nbNewLines + 1;
    }

    /// <summary>
    /// Skips the given number of lines and returns a list of consecutive subsequent lines that start with the pattern, stopping at the first non-matching line, with no side effects.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="lines">The lines.</param>
    /// <param name="nbLinesToSkip">The nb lines to skip.</param>
    /// <returns>A IEnumerable&lt;string&gt; value produced by this method.</returns>
    private static IEnumerable<string> GetLinesStartingWith(string pattern, IEnumerable<string> lines, int nbLinesToSkip = 0)
    {
        var result = new List<string>();

        foreach (var line in lines.Skip(nbLinesToSkip))
        {
            if (!line.StartsWith(pattern))
            {
                break;
            }

            result.Add(line);
        }

        return result;
    }

    /// <summary>
    /// Looks for the index of the first limit found in the text
    /// </summary>
    /// <param name="text">The text to search in</param>
    /// <param name="limits">The limits to search for</param>
    /// <returns>Lowest index of all the limits found</returns>
    private static int GetLowestIndex(string text, IEnumerable<string> limits)
    {
        var indexes = new List<int>();

        foreach (var limit in limits)
        {
            var limitIndex = text.IndexOf(limit);

            if (limitIndex > -1)
            {
                indexes.Add(limitIndex);
            }
        }

        if (indexes.Count == 0)
        {
            return text.Length;
        }

        return indexes.Min();
    }

    /// <summary>
    /// We need to produce exactly one concise summary sentence, plain text, no XML, no quotes. Mention key behavior and side effects. The method removes using directives from a C# document by locating the last &quot;using &quot; before &quot;namespace &quot; and returning the substring after that, trimming leading whitespace. Potential issue: if no using found, startIndex goes -1 but loop condition? Let&apos;s analyze: namespaceIndex = document.IndexOf(&quot;namespace &quot;). If -1, loop not entered, lastUsingIndex 0, afterUsingIndex 0, returns whole document trim. If namespace found, loop runs. startIndex initially 0. while startIndex &lt; namespaceIndex. lastUsingIndex = startIndex (initially 0). Then startIndex = IndexOf(&quot;using &quot;, startIndex). If found, returns index &gt;=0, then startIndex++ increments to index+1. If not found, returns -1, then startIndex++ increments to 0 (since -1+1=0). Then break. So if no using, lastUsingIndex remains 0. afterUsingIndex = 0 if lastUsingIndex &lt;= 0. Returns document.Substring(0).TrimStart() = whole document. If using found, lastUsingIndex set to previous start.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string SkipUsings(string document)
    {
        // we cannot simply look for the last using since it can be used inside the code
        // so we look for the last using before namespace
        var namespaceIndex = document.IndexOf("namespace ");
        var startIndex = 0;
        var lastUsingIndex = 0;

        while (startIndex < namespaceIndex)
        {
            lastUsingIndex = startIndex;
            startIndex = document.IndexOf("using ", startIndex);

            if (startIndex++ == -1)
            {
                break;
            }
        }

        var afterUsingIndex = 0;

        if (lastUsingIndex > 0)
        {
            afterUsingIndex = document.IndexOf($"{Environment.NewLine}", lastUsingIndex) + 1;
        }

        return document.Substring(afterUsingIndex).TrimStart();
    }

    /// <summary>
    /// Splits the input text on every occurrence of Environment.NewLine and returns all resulting substrings, including empty entries, without altering the original string.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A IEnumerable&lt;string&gt; value produced by this method.</returns>
    private static IEnumerable<string> SplitLines(string text)
    {
        var separator = new string[] { Environment.NewLine };

        return text.Split(separator, StringSplitOptions.None);
    }
}
