using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using System;
using System.IO;
using System.Text;

namespace CodeJanitor.UnitTests.Cleaning;

/// <summary>
/// Unit tests for <see cref="RemoveByteOrderMarkLogic" />.
/// </summary>
[TestClass]
public class RemoveByteOrderMarkLogicTests
{
    private string _tempDirectory;
    private RemoveByteOrderMarkLogic _logic;

    [TestInitialize]
    public void TestInitialize()
    {
        Settings.Default.Reset();
        Settings.Default.Cleaning_RemoveByteOrderMark = true;
        _logic = RemoveByteOrderMarkLogic.GetInstance(null);
        _tempDirectory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Settings.Default.Reset();

        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void HasByteOrderMark_Utf8Bom_ReturnsTrue()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, 0x41, 0x42];

        Assert.IsTrue(RemoveByteOrderMarkLogic.HasByteOrderMark(bytes));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void HasByteOrderMark_Utf16LeBom_ReturnsTrue()
    {
        byte[] bytes = [0xFF, 0xFE, 0x41, 0x00];

        Assert.IsTrue(RemoveByteOrderMarkLogic.HasByteOrderMark(bytes));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void HasByteOrderMark_Utf16BeBom_ReturnsTrue()
    {
        byte[] bytes = [0xFE, 0xFF, 0x00, 0x41];

        Assert.IsTrue(RemoveByteOrderMarkLogic.HasByteOrderMark(bytes));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void HasByteOrderMark_Utf32LeBom_ReturnsTrue()
    {
        byte[] bytes = [0xFF, 0xFE, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00];

        Assert.IsTrue(RemoveByteOrderMarkLogic.HasByteOrderMark(bytes));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void HasByteOrderMark_Utf32BeBom_ReturnsTrue()
    {
        byte[] bytes = [0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, 0x41];

        Assert.IsTrue(RemoveByteOrderMarkLogic.HasByteOrderMark(bytes));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void HasByteOrderMark_NoBom_ReturnsFalse()
    {
        byte[] bytes = [0x41, 0x42, 0x43];

        Assert.IsFalse(RemoveByteOrderMarkLogic.HasByteOrderMark(bytes));
        Assert.IsFalse(RemoveByteOrderMarkLogic.HasByteOrderMark((byte[])null));
        Assert.IsFalse(RemoveByteOrderMarkLogic.HasByteOrderMark(new byte[] { 0x01 }));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void StripByteOrderMark_Utf8Bom_StripsPrefix()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, 0x41, 0x42];
        byte[] expected = [0x41, 0x42];

        var stripped = RemoveByteOrderMarkLogic.StripByteOrderMark(bytes);

        CollectionAssert.AreEqual(expected, stripped);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void StripByteOrderMark_Utf16LeBom_ConvertsToUtf8WithoutBom()
    {
        var text = "Hello World";
        var utf16WithBom = Encoding.Unicode.GetPreamble();
        var utf16Bytes = Encoding.Unicode.GetBytes(text);
        var combined = new byte[utf16WithBom.Length + utf16Bytes.Length];
        Buffer.BlockCopy(utf16WithBom, 0, combined, 0, utf16WithBom.Length);
        Buffer.BlockCopy(utf16Bytes, 0, combined, utf16WithBom.Length, utf16Bytes.Length);

        var stripped = RemoveByteOrderMarkLogic.StripByteOrderMark(combined);
        var resultText = Encoding.UTF8.GetString(stripped);

        Assert.AreEqual(text, resultText);
        Assert.IsFalse(RemoveByteOrderMarkLogic.HasByteOrderMark(stripped));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void HasByteOrderMark_String_DetectsLeadingBom()
    {
        Assert.IsTrue(RemoveByteOrderMarkLogic.HasByteOrderMark("\uFEFFtext"));
        Assert.IsFalse(RemoveByteOrderMarkLogic.HasByteOrderMark("text"));
        Assert.IsFalse(RemoveByteOrderMarkLogic.HasByteOrderMark((string)null));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void StripByteOrderMark_String_StripsLeadingBom()
    {
        Assert.AreEqual("text", RemoveByteOrderMarkLogic.StripByteOrderMark("\uFEFFtext"));
        Assert.AreEqual("text", RemoveByteOrderMarkLogic.StripByteOrderMark("text"));
        Assert.IsNull(RemoveByteOrderMarkLogic.StripByteOrderMark((string)null));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void RemoveByteOrderMark_FileOnDiskWithUtf8Bom_RemovesBom()
    {
        var filePath = Path.Combine(_tempDirectory, "Utf8WithBom.cs");
        var content = "namespace Demo;\r\n\r\npublic class C { }\r\n";
        File.WriteAllText(filePath, content, new UTF8Encoding(true));

        Assert.IsTrue(RemoveByteOrderMarkLogic.HasByteOrderMark(File.ReadAllBytes(filePath)));

        var modified = _logic.RemoveByteOrderMark(filePath);

        Assert.IsTrue(modified);
        var bytesAfter = File.ReadAllBytes(filePath);
        Assert.IsFalse(RemoveByteOrderMarkLogic.HasByteOrderMark(bytesAfter));
        Assert.AreEqual(content, File.ReadAllText(filePath));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void RemoveByteOrderMark_FileOnDiskWithoutBom_DoesNotModify()
    {
        var filePath = Path.Combine(_tempDirectory, "Utf8WithoutBom.cs");
        var content = "namespace Demo;\r\n\r\npublic class C { }\r\n";
        File.WriteAllText(filePath, content, new UTF8Encoding(false));

        var modified = _logic.RemoveByteOrderMark(filePath);

        Assert.IsFalse(modified);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void RemoveByteOrderMark_WhenDisabled_DoesNotModifyFile()
    {
        Settings.Default.Cleaning_RemoveByteOrderMark = false;

        var filePath = Path.Combine(_tempDirectory, "DisabledTest.cs");
        var content = "namespace Demo;\r\n\r\npublic class C { }\r\n";
        File.WriteAllText(filePath, content, new UTF8Encoding(true));

        var modified = _logic.RemoveByteOrderMark(filePath);

        Assert.IsFalse(modified);
        Assert.IsTrue(RemoveByteOrderMarkLogic.HasByteOrderMark(File.ReadAllBytes(filePath)));
    }
}
