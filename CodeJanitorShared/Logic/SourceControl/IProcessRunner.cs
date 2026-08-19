namespace CodeJanitor.Logic.SourceControl;

/// <summary>
/// Abstraction over running an external process and capturing its standard output.
/// Enables unit testing of source-control logic without a real process.
/// </summary>

public interface IProcessRunner
{
    /// <summary>
    /// Runs the specified executable and returns its standard output.
    /// </summary>
    /// <param name="fileName">The executable to run (e.g. "git").</param>
    /// <param name="arguments">The command-line arguments.</param>
    /// <param name="workingDirectory">The working directory.</param>
    /// <returns>The captured standard output.</returns>

    string Run(string fileName, string arguments, string workingDirectory);
}