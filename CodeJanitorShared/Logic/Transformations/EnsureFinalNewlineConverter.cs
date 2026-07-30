using System;

namespace CodeJanitor.Logic.Transformations
{
    /// <summary>
    /// Ensures C# source ends with exactly one line break (the <c>insert_final_newline</c> convention),
    /// adding one when it is missing (headless-Roslyn-path block for BL-018). Pure and unit-testable.
    /// </summary>
    /// <remarks>
    /// Conservative: it only appends a line break when the source does not already end with one, and
    /// leaves any existing (including multiple) trailing line breaks untouched, so it never fights a
    /// separate blank-line rule. The line-break style (<c>\r\n</c> vs <c>\n</c>) matches the style
    /// already used in the file.
    /// </remarks>
    public class EnsureFinalNewlineConverter : ISourceTransformation
    {
        /// <inheritdoc />
        public string Name => "Ensure final newline";

        /// <inheritdoc />
        public string Apply(string source)
        {
            return Convert(source);
        }

        /// <summary>
        /// Appends a final line break to the given source when it does not already end with one.
        /// </summary>
        public string Convert(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source;
            }

            if (source.EndsWith("\n", StringComparison.Ordinal))
            {
                return source;
            }

            var newline = source.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            return source + newline;
        }
    }
}
