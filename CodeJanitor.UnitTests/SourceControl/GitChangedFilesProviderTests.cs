using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.SourceControl;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeJanitor.UnitTests.SourceControl;

/// <summary>
/// Unit tests for <see cref="GitChangedFilesProvider" /> using a fake process runner
/// (no real git process required).
/// </summary>

[TestClass]
public class GitChangedFilesProviderTests
{
    private sealed class FakeProcessRunner : IProcessRunner
    {
        public string TopLevel = "C:/repo";
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
        var runner = new FakeProcessRunner { TopLevel = "C:/repo", Status = " M src/A.cs\r\n" };
        var provider = new GitChangedFilesProvider(runner, new GitStatusParser());

        var result = provider.GetChangedFiles(@"C:\repo\src");

        CollectionAssert.AreEqual(
            new List<string> { Path.Combine(@"C:\repo", "src\\A.cs") },
            result.ToList());
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void NotAGitRepo_ReturnsEmpty()
    {
        var runner = new FakeProcessRunner { TopLevel = string.Empty, Status = " M src/A.cs\r\n" };
        var provider = new GitChangedFilesProvider(runner, new GitStatusParser());

        Assert.AreEqual(0, provider.GetChangedFiles(@"C:\repo").Count);
    }
}
