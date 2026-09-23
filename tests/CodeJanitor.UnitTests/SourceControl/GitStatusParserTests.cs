using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.SourceControl;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
    public void TestInitialize()
    {
        _parser = new GitStatusParser();
    }

    private static string Combine(string relative)
    {
        return Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void ParsesModifiedFile()
    {
        var result = _parser.Parse(" M src/A.cs\r\n", Root);

        CollectionAssert.AreEqual(new List<string> { Combine("src/A.cs") }, result.ToList());
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void ParsesStagedAddedFile()
    {
        var result = _parser.Parse("A  src/B.cs\r\n", Root);

        CollectionAssert.AreEqual(new List<string> { Combine("src/B.cs") }, result.ToList());
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void ParsesUntrackedFile()
    {
        var result = _parser.Parse("?? src/C.cs\r\n", Root);

        CollectionAssert.AreEqual(new List<string> { Combine("src/C.cs") }, result.ToList());
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void ExcludesDeletedFile()
    {
        var result = _parser.Parse(" D src/D.cs\r\n", Root);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void ExcludesIgnoredFile()
    {
        var result = _parser.Parse("!! bin/obj.cs\r\n", Root);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void ParsesRenamedFile_ReturnsNewPath()
    {
        var result = _parser.Parse("R  src/old.cs -> src/new.cs\r\n", Root);

        CollectionAssert.AreEqual(new List<string> { Combine("src/new.cs") }, result.ToList());
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void IgnoresEmptyLines()
    {
        var result = _parser.Parse("\r\n M src/A.cs\r\n\r\n", Root);

        CollectionAssert.AreEqual(new List<string> { Combine("src/A.cs") }, result.ToList());
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void ParsesMultipleEntries()
    {
        var output = " M src/A.cs\r\nA  src/B.cs\r\n?? src/C.cs\r\n D src/D.cs\r\n";
        var result = _parser.Parse(output, Root);

        CollectionAssert.AreEqual(
            new List<string> { Combine("src/A.cs"), Combine("src/B.cs"), Combine("src/C.cs") },
            result.ToList());
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void ParsesQuotedPathWithSpaces()
    {
        var result = _parser.Parse("?? \"src/with space.cs\"\r\n", Root);

        CollectionAssert.AreEqual(new List<string> { Combine("src/with space.cs") }, result.ToList());
    }

    [TestMethod]
    [TestCategory("SourceControl UnitTests")]
    public void NullOrEmpty_ReturnsEmpty()
    {
        Assert.AreEqual(0, _parser.Parse(null, Root).Count);
        Assert.AreEqual(0, _parser.Parse(string.Empty, Root).Count);
    }
}
