using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeJanitor.Logic.SourceControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.SourceControl;

/// <summary>
/// Unit tests for <see cref="GitStatusParser" />.
/// Pure parsing of `git status --porcelain` output (no process / VS required).
/// </summary>
[TestClass]
public sealed class GitStatusParserTests
{
    /// <summary>
    /// The root.
    /// </summary>
    private const string Root = @"C:\repo";

    private IGitStatusParser _parser;

    [TestInitialize]
    public void TestInitialize() => _parser = new GitStatusParser();

    private static string Combine(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void ParsesModifiedFile()
    {
        IReadOnlyList<string> result = _parser.Parse(" M src/A.cs\r\n", Root);

        Assert.AreSequenceEqual(new List<string> { Combine("src/A.cs") }, result.ToList());
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void ParsesStagedAddedFile()
    {
        IReadOnlyList<string> result = _parser.Parse("A  src/B.cs\r\n", Root);

        Assert.AreSequenceEqual(new List<string> { Combine("src/B.cs") }, result.ToList());
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void ParsesUntrackedFile()
    {
        IReadOnlyList<string> result = _parser.Parse("?? src/C.cs\r\n", Root);

        Assert.AreSequenceEqual(new List<string> { Combine("src/C.cs") }, result.ToList());
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void ExcludesDeletedFile()
    {
        IReadOnlyList<string> result = _parser.Parse(" D src/D.cs\r\n", Root);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void ExcludesIgnoredFile()
    {
        IReadOnlyList<string> result = _parser.Parse("!! bin/obj.cs\r\n", Root);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void ParsesRenamedFile_ReturnsNewPath()
    {
        IReadOnlyList<string> result = _parser.Parse("R  src/old.cs -> src/new.cs\r\n", Root);

        Assert.AreSequenceEqual(new List<string> { Combine("src/new.cs") }, result.ToList());
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void IgnoresEmptyLines()
    {
        IReadOnlyList<string> result = _parser.Parse("\r\n M src/A.cs\r\n\r\n", Root);

        Assert.AreSequenceEqual(new List<string> { Combine("src/A.cs") }, result.ToList());
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void ParsesMultipleEntries()
    {
        string output = " M src/A.cs\r\nA  src/B.cs\r\n?? src/C.cs\r\n D src/D.cs\r\n";
        IReadOnlyList<string> result = _parser.Parse(output, Root);

        Assert.AreSequenceEqual(
            new List<string> { Combine("src/A.cs"), Combine("src/B.cs"), Combine("src/C.cs") }, result.ToList());
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void ParsesQuotedPathWithSpaces()
    {
        IReadOnlyList<string> result = _parser.Parse("?? \"src/with space.cs\"\r\n", Root);

        Assert.AreSequenceEqual(new List<string> { Combine("src/with space.cs") }, result.ToList());
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void NullOrEmpty_ReturnsEmpty()
    {
        Assert.IsEmpty(_parser.Parse(null, Root));
        Assert.IsEmpty(_parser.Parse(string.Empty, Root));
    }
}
