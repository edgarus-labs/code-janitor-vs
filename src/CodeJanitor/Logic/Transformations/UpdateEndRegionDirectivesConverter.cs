using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Updates #endregion directives to match the names of their corresponding #region directives,
/// and normalizes whitespace around region names.
/// </summary>
public sealed class UpdateEndRegionDirectivesConverter : ISourceTransformation
{
    private static readonly Regex RegionDirectiveRegex = new Regex(
        @"^[ \t]*#region\b[ \t]*(.*)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex EndRegionDirectiveRegex = new Regex(
        @"^[ \t]*#endregion\b[ \t]*(.*)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name => "Update end region directives";

    /// <summary>
    /// Processes the source line by line, rewriting each `#endregion` directive to carry the name of its matching
    /// `#region` (tracked on a stack, nameless regions included), preserving indentation and the file's line breaks,
    /// and leaving unmatched lines and lines inside multi-line string literals or comments unchanged.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>A string value produced by this method.</returns>
    public string Apply(string source)
    {
        if (string.IsNullOrEmpty(source) || source.IndexOf("#endregion", StringComparison.Ordinal) < 0)
        {
            return source;
        }

        var protectedSpans = RegionDirectiveRemover.FindMultiLineLiteralAndCommentSpans(source);
        var regionStack = new Stack<string>();
        var result = new StringBuilder(source.Length);
        int lineStart = 0;

        while (lineStart < source.Length)
        {
            int contentEnd = RegionDirectiveRemover.FindLineEnd(source, lineStart, out int nextLineStart);
            string line = source.Substring(lineStart, contentEnd - lineStart);
            bool isCode = !RegionDirectiveRemover.StartsInside(protectedSpans, lineStart);
            Match regionMatch = isCode ? RegionDirectiveRegex.Match(line) : Match.Empty;

            if (regionMatch.Success)
            {
                regionStack.Push(regionMatch.Groups[1].Value.Trim());
                result.Append(line);
            }
            else if (isCode && regionStack.Count > 0 && EndRegionDirectiveRegex.IsMatch(line))
            {
                string matchingRegionName = regionStack.Pop();

                // Build the new #endregion directive: "#endregion" + optional space + region name
                string newDirective = string.IsNullOrEmpty(matchingRegionName) ?
                    "#endregion" :
                    $"#endregion {matchingRegionName}";

                result.Append(GetIndentation(line)).Append(newDirective);
            }
            else
            {
                // Not a directive, or an #endregion without a matching #region: keep the line as-is
                result.Append(line);
            }

            result.Append(source, contentEnd, nextLineStart - contentEnd);
            lineStart = nextLineStart;
        }

        return result.ToString();
    }

    /// <summary>
    /// Returns the leading whitespace (spaces and tabs) from the input line by counting consecutive whitespace characters until the first non-whitespace character, then extracting that substring with no side effects or thrown exceptions.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string GetIndentation(string line)
    {
        int count = 0;
        foreach (char c in line)
        {
            if (c == ' ' || c == '\t')
                count++;
            else
                break;
        }

        return line.Substring(0, count);
    }
}
