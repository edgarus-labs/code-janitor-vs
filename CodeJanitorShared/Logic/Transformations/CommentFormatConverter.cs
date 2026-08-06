using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CodeJanitor.Properties;

namespace CodeJanitor.Logic.Transformations
{
    /// <summary>
    /// Formats comments in C# source code by applying consistent spacing and alignment rules.
    /// Handles both single-line (//) and multi-line (/* */) comments.
    /// </summary>
    public class CommentFormatConverter : ISourceTransformation
    {
        private static readonly Regex SingleLineCommentRegex = new Regex(@"^(\s*)//\s*(.*)$", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex MultiLineCommentStartRegex = new Regex(@"^(\s*)/\*", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex MultiLineCommentEndRegex = new Regex(@"\*/$", RegexOptions.Multiline | RegexOptions.Compiled);

        public string Name => "Format comments";

        public string Apply(string source)
        {
            if (string.IsNullOrEmpty(source) || !Settings.Default.Formatting_CommentRunDuringCleanup)
            {
                return source;
            }

            var lines = source.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var result = new List<string>();
            var inMultiLineComment = false;
            var multiLineCommentIndentation = "";

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Handle multi-line comments
                if (inMultiLineComment)
                {
                    if (line.Contains("*/"))
                    {
                        inMultiLineComment = false;
                        result.Add(line);
                    }
                    else
                    {
                        // Normalize continuation lines in multi-line comments
                        result.Add(NormalizeMultiLineCommentLine(line, multiLineCommentIndentation));
                    }
                    continue;
                }

                // Check for multi-line comment start
                var multiLineStartMatch = MultiLineCommentStartRegex.Match(line);
                if (multiLineStartMatch.Success)
                {
                    inMultiLineComment = !line.Contains("*/");
                    multiLineCommentIndentation = multiLineStartMatch.Groups[1].Value;
                    result.Add(line);
                    continue;
                }

                // Handle single-line comments
                var singleLineMatch = SingleLineCommentRegex.Match(line);
                if (singleLineMatch.Success)
                {
                    var indentation = singleLineMatch.Groups[1].Value;
                    var commentText = singleLineMatch.Groups[2].Value;

                    // Format: preserve indentation, ensure single space after //
                    var formattedLine = string.IsNullOrWhiteSpace(commentText)
                        ? indentation + "//"
                        : indentation + "// " + commentText.TrimStart();

                    result.Add(formattedLine);
                    continue;
                }

                // Not a comment line, keep as-is
                result.Add(line);
            }

            var newline = source.Contains("\r\n") ? "\r\n" : (source.Contains("\r") ? "\r" : "\n");
            return string.Join(newline, result);
        }

        private string NormalizeMultiLineCommentLine(string line, string baseIndentation)
        {
            var trimmed = line.TrimStart();

            // If line starts with *, align it with base indentation
            if (trimmed.StartsWith("*"))
            {
                return baseIndentation + " " + trimmed;
            }

            // Otherwise preserve as-is (content lines)
            return line;
        }
    }
}
