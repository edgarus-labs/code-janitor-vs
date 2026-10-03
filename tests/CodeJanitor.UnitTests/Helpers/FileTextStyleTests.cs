using System;
using System.IO;
using System.Linq;
using System.Text;
using CodeJanitor.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Helpers;

/// <summary>
/// Tests for reading and writing source files so that the encoding and the line endings follow .editorconfig
/// (<c>charset</c>, <c>end_of_line</c>) and otherwise stay as the file had them.
/// </summary>
[TestClass]
public sealed class FileTextStyleTests
{
    private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

    private string _tempDirectory;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void Write_NoEditorConfig_FileWithoutBom_StaysWithoutBom()
    {
        string filePath = CreateFile("class C { }\n", withBom: false);

        RewriteWith(filePath, "class D { }\n");

        Assert.AreSequenceEqual(Encoding.UTF8.GetBytes("class D { }\n"), File.ReadAllBytes(filePath));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void Write_NoEditorConfig_FileWithBom_KeepsBom()
    {
        string filePath = CreateFile("class C { }\n", withBom: true);

        RewriteWith(filePath, "class D { }\n");

        Assert.AreSequenceEqual(Bom.Concat(Encoding.UTF8.GetBytes("class D { }\n")).ToArray(), File.ReadAllBytes(filePath));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void Write_CharsetUtf8_RemovesBom()
    {
        WriteEditorConfig("charset = utf-8");
        string filePath = CreateFile("class C { }\n", withBom: true);

        RewriteWith(filePath, "class D { }\n");

        Assert.AreSequenceEqual(Encoding.UTF8.GetBytes("class D { }\n"), File.ReadAllBytes(filePath));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void Write_CharsetUtf8Bom_AddsBom()
    {
        WriteEditorConfig("charset = utf-8-bom");
        string filePath = CreateFile("class C { }\n", withBom: false);

        RewriteWith(filePath, "class D { }\n");

        Assert.AreSequenceEqual(Bom.Concat(Encoding.UTF8.GetBytes("class D { }\n")).ToArray(), File.ReadAllBytes(filePath));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    [DataRow(true)]
    [DataRow(false)]
    public void Write_CharsetUnset_KeepsWhatTheFileHad(bool hadBom)
    {
        WriteEditorConfig("charset = unset");
        string filePath = CreateFile("class C { }\n", hadBom);

        RewriteWith(filePath, "class D { }\n");

        byte[] expected = (hadBom ? Bom : new byte[0]).Concat(Encoding.UTF8.GetBytes("class D { }\n")).ToArray();
        Assert.AreSequenceEqual(expected, File.ReadAllBytes(filePath));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void Write_CharsetUtf16Le_WritesUtf16()
    {
        WriteEditorConfig("charset = utf-16le");
        string filePath = CreateFile("class C { }\n", withBom: false);

        RewriteWith(filePath, "class D { }\n");

        Assert.AreSequenceEqual(new UnicodeEncoding(false, true).GetPreamble().Concat(Encoding.Unicode.GetBytes("class D { }\n")).ToArray(), File.ReadAllBytes(filePath));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void Write_EndOfLineLf_ConvertsCrLfToLf()
    {
        WriteEditorConfig("end_of_line = lf");
        string filePath = CreateFile("class C\r\n{\r\n}\r\n", withBom: false);

        RewriteWith(filePath, "class D\r\n{\r\n}\r\n");

        Assert.AreEqual("class D\n{\n}\n", File.ReadAllText(filePath));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void Write_EndOfLineCrLf_ConvertsLfToCrLf()
    {
        WriteEditorConfig("end_of_line = crlf");
        string filePath = CreateFile("class C\n{\n}\n", withBom: false);

        RewriteWith(filePath, "class D\n{\n}\n");

        Assert.AreEqual("class D\r\n{\r\n}\r\n", File.ReadAllText(filePath));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void Write_EndOfLineUnset_FileWithLf_ConvertsInsertedCrLfToLf()
    {
        WriteEditorConfig("end_of_line = unset");
        string filePath = CreateFile("class C\n{\n}\n", withBom: false);

        RewriteWith(filePath, "/// <summary>\r\n/// x\r\n/// </summary>\nclass D\n{\n}\n");

        Assert.AreEqual("/// <summary>\n/// x\n/// </summary>\nclass D\n{\n}\n", File.ReadAllText(filePath));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void Write_NoEditorConfig_FileWithMixedLineEndings_LeavesTheTextUntouched()
    {
        string filePath = CreateFile("a\r\nb\nc\r\n", withBom: false);

        RewriteWith(filePath, "a\r\nb\nc\r\nd\n");

        Assert.AreEqual("a\r\nb\nc\r\nd\n", File.ReadAllText(filePath));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void ResolveLineEnding_EditorConfigValueWins()
    {
        WriteEditorConfig("end_of_line = lf");
        string filePath = CreateFile("a\r\nb\r\n", withBom: false);

        Assert.AreEqual("\n", FileTextStyle.ResolveLineEnding(filePath, "a\r\nb\r\n"));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void ResolveLineEnding_WithoutRule_UsesTheDominantSeparator()
    {
        string filePath = Path.Combine(_tempDirectory, "Sample.cs");

        Assert.AreEqual("\n", FileTextStyle.ResolveLineEnding(filePath, "a\nb\nc\r\n"));
        Assert.AreEqual("\r\n", FileTextStyle.ResolveLineEnding(filePath, "a\r\nb\r\nc\n"));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void ResolveLineEnding_WithoutRuleOrLineBreaks_UsesEnvironmentNewLine()
    {
        string filePath = Path.Combine(_tempDirectory, "Sample.cs");

        Assert.AreEqual(Environment.NewLine, FileTextStyle.ResolveLineEnding(filePath, "class C { }"));
    }

    private static void RewriteWith(string filePath, string newText)
    {
        string original = FileTextStyle.ReadAllText(filePath, out var encoding);

        FileTextStyle.WriteAllText(filePath, newText, encoding, original);
    }

    private string CreateFile(string text, bool withBom)
    {
        string filePath = Path.Combine(_tempDirectory, "Sample.cs");
        File.WriteAllText(filePath, text, new UTF8Encoding(withBom));

        return filePath;
    }

    private void WriteEditorConfig(string options)
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"), "root = true\n\n[*]\n" + options + "\n");
    }
}
