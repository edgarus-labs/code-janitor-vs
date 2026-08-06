using System.Text.RegularExpressions;

namespace CodeJanitor.Logic.Transformations
{
    /// <summary>
    /// Removes C# #region and #endregion directive lines while preserving other preprocessor directives.
    /// </summary>
    public class RegionDirectiveRemover : ISourceTransformation
    {
        private static readonly Regex RegionDirectiveRegex = new Regex(
            @"^[ \t]*#(?:end)?region\b[^\r\n]*(?:\r?\n)?",
            RegexOptions.Multiline | RegexOptions.Compiled);

        public string Name => "Remove region directives";

        public string Apply(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source;
            }

            return RegionDirectiveRegex.Replace(source, string.Empty);
        }
    }
}
