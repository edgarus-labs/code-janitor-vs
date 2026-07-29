using System.Collections.Generic;

namespace SteveCadwallader.CodeJanitor.Logic.SourceControl
{
    /// <summary>
    /// Parses the output of <c>git status --porcelain</c> into a set of absolute file paths
    /// representing changed files that exist on disk (modified, added, renamed, untracked).
    /// </summary>
    public interface IGitStatusParser
    {
        /// <summary>
        /// Parses porcelain status output into absolute paths of changed, on-disk files.
        /// </summary>
        /// <param name="porcelainOutput">The output of <c>git status --porcelain</c>.</param>
        /// <param name="repositoryRoot">The absolute path of the repository root.</param>
        /// <returns>The absolute paths of changed files (deleted and ignored files excluded).</returns>
        IReadOnlyList<string> Parse(string porcelainOutput, string repositoryRoot);
    }
}
