using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeJanitor.Logic.SourceControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.SourceControl;

/// <summary>
/// Unit tests for <see cref="GitChangedFilesProvider" /> using a fake process runner
/// (no real git process required).
/// </summary>
[TestClass]
public sealed class GitChangedFilesProviderTests
{
    /// <summary>
    /// FakeProcessRunner represents a test double implementation used to simulate the execution and status reporting of an external process without actually launching one.
    /// </summary>
    private sealed class FakeProcessRunner : IProcessRunner
    {
        /// <summary>
        /// The top level.
        /// </summary>
        public string TopLevel = "C:/repo";

        /// <summary>
        /// The status.
        /// </summary>
        public string Status = string.Empty;

        /// <summary>
        /// Returns a predefined constant (TopLevel for &quot;rev-parse&quot;, Status for &quot;status&quot;) based solely on substring checks of the arguments parameter, otherwise an empty string, ignoring the other parameters and producing no side effects.
        /// </summary>
        /// <param name="fileName">The file name.</param>
        /// <param name="arguments">The arguments.</param>
        /// <param name="workingDirectory">The working directory.</param>
        /// <returns>A string value produced by this method.</returns>
        public string Run(string fileName, string arguments, string workingDirectory)
        {
            if (arguments.Contains("rev-parse"))
            {
                return TopLevel;
            }

            if (arguments.Contains("status"))
            {
                return Status;
            }

            return string.Empty;
        }
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void ReturnsChangedFiles_FromGitOutput()
    {
        FakeProcessRunner runner = new FakeProcessRunner { TopLevel = "C:/repo", Status = " M src/A.cs\r\n" };
        GitChangedFilesProvider provider = new GitChangedFilesProvider(runner, new GitStatusParser());

        IReadOnlyList<string> result = provider.GetChangedFiles(@"C:\repo\src");

        Assert.AreSequenceEqual(
            new List<string> { Path.Combine(@"C:\repo", "src\\A.cs") }, result.ToList());
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void NotAGitRepo_ReturnsEmpty()
    {
        FakeProcessRunner runner = new FakeProcessRunner { TopLevel = string.Empty, Status = " M src/A.cs\r\n" };
        GitChangedFilesProvider provider = new GitChangedFilesProvider(runner, new GitStatusParser());

        Assert.IsEmpty(provider.GetChangedFiles(@"C:\repo"));
    }
}
