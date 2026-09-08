using System.Diagnostics;

namespace CodeJanitor.Logic.SourceControl;

/// <summary>
/// Runs an external process and captures its standard output.
/// </summary>

public sealed class ProcessRunner : IProcessRunner
{
    /// <inheritdoc />

    public string Run(string fileName, string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using (var process = Process.Start(startInfo))
        {
            if (process is null)
            {
                return string.Empty;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return output;
        }
    }
}
