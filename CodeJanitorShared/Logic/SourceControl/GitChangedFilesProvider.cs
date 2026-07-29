using System.Collections.Generic;
using System.IO;

namespace SteveCadwallader.CodeJanitor.Logic.SourceControl
{
    /// <summary>
    /// Determines changed files via git, by resolving the repository root and parsing
    /// <c>git status --porcelain</c>. The process execution is injected for testability.
    /// </summary>
    /// <remarks>See ADR-0005 / ADR-0007 (dependency injection, unit-testability).</remarks>
    public class GitChangedFilesProvider : IChangedFilesProvider
    {
        private readonly IProcessRunner _processRunner;
        private readonly IGitStatusParser _parser;

        /// <summary>
        /// Initializes a new instance of the <see cref="GitChangedFilesProvider" /> class.
        /// </summary>
        /// <param name="processRunner">The process runner used to execute git.</param>
        /// <param name="parser">The parser for git status output.</param>
        public GitChangedFilesProvider(IProcessRunner processRunner, IGitStatusParser parser)
        {
            _processRunner = processRunner;
            _parser = parser;
        }

        /// <inheritdoc />
        public IReadOnlyList<string> GetChangedFiles(string workingDirectory)
        {
            var topLevel = _processRunner.Run("git", "rev-parse --show-toplevel", workingDirectory);
            if (string.IsNullOrWhiteSpace(topLevel))
            {
                return new List<string>();
            }

            var repositoryRoot = topLevel.Trim().Replace('/', Path.DirectorySeparatorChar);
            var status = _processRunner.Run("git", "status --porcelain", workingDirectory);
            return _parser.Parse(status, repositoryRoot);
        }
    }
}
