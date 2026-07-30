using System.Collections.Generic;

namespace CodeJanitor.Logic.SourceControl
{
    /// <summary>
    /// Provides the set of changed files (absolute paths) for a working directory.
    /// </summary>
    public interface IChangedFilesProvider
    {
        /// <summary>
        /// Gets the absolute paths of changed files for the repository containing the working directory.
        /// </summary>
        /// <param name="workingDirectory">A directory within the repository.</param>
        /// <returns>The absolute paths of changed files, or an empty list when not in a repository.</returns>
        IReadOnlyList<string> GetChangedFiles(string workingDirectory);
    }
}
