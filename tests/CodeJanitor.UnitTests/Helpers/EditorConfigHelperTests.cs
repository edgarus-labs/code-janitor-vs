using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CodeJanitor.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Helpers;

/// <summary>
/// Behavioral tests for <see cref="EditorConfigHelper" />: the options that apply to a file on disk, resolved the way
/// Roslyn resolves them (nearest file wins, <c>root = true</c>, section globs), and the file that defines an option.
/// </summary>
[TestClass]
public sealed class EditorConfigHelperTests
{
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
    public void LoadOptions_NearestFileWins_AndInheritsTheParentKeys()
    {
        WriteConfig(_tempDirectory, "root = true", "[*.cs]", "indent_style = space", "indent_size = 4");
        string nested = CreateDirectory("src", "App");
        WriteConfig(Path.Combine(_tempDirectory, "src"), "[*.cs]", "indent_size = 2");

        IReadOnlyDictionary<string, string> options = EditorConfigHelper.LoadOptions(Path.Combine(nested, "Program.cs"));

        Assert.AreEqual("2", options["indent_size"]);
        Assert.AreEqual("space", options["indent_style"]);
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void LoadOptions_RootFile_HidesTheFilesAboveIt()
    {
        WriteConfig(_tempDirectory, "root = true", "[*.cs]", "indent_style = tab", "tab_width = 8");
        string nested = CreateDirectory("src");
        WriteConfig(nested, "root = true", "[*.cs]", "indent_size = 2");

        IReadOnlyDictionary<string, string> options = EditorConfigHelper.LoadOptions(Path.Combine(nested, "Sample.cs"));

        CollectionAssert.AreEquivalent(
            new Dictionary<string, string> { ["indent_size"] = "2" },
            options.ToDictionary(entry => entry.Key, entry => entry.Value));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    [DataRow("[*.{cs,csx}]", "Sample.cs", true, DisplayName = "brace list matches .cs")]
    [DataRow("[*.{cs,csx}]", "Script.csx", true, DisplayName = "brace list matches .csx")]
    [DataRow("[*.{cs,csx}]", "Module.vb", false, DisplayName = "brace list does not match .vb")]
    [DataRow("[{Program,Startup}.cs]", "Program.cs", true, DisplayName = "brace list of names matches Program.cs")]
    [DataRow("[{Program,Startup}.cs]", "Sample.cs", false, DisplayName = "brace list of names does not match another file")]
    [DataRow("[*]", "Sample.cs", true, DisplayName = "star matches every file")]
    [DataRow("[*.cs]", "Sample.CS", false, DisplayName = "globs are case-sensitive")]
    [DataRow("[*.vb]", "Sample.cs", false, DisplayName = "other extension")]
    [DataRow("[Sample.cs]", "Sample.cs", true, DisplayName = "exact file name")]
    [DataRow("[src/**.cs]", "Sample.cs", false, DisplayName = "directory glob does not match the config directory")]
    public void LoadOptions_AppliesOnlyTheSectionsWhoseGlobMatchesTheFile(string section, string fileName, bool applies)
    {
        WriteConfig(_tempDirectory, "root = true", section, "indent_style = tab");

        IReadOnlyDictionary<string, string> options = EditorConfigHelper.LoadOptions(Path.Combine(_tempDirectory, fileName));

        Assert.AreEqual(applies, options.ContainsKey("indent_style"));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void LoadOptions_LaterSectionOfTheSameFile_WinsOverAnEarlierOne()
    {
        WriteConfig(_tempDirectory, "root = true", "[*.cs]", "indent_size = 2", "[*]", "indent_size = 8", "[Program.cs]", "indent_size = 3");

        Assert.AreEqual("3", EditorConfigHelper.LoadOptions(Path.Combine(_tempDirectory, "Program.cs"))["indent_size"]);
        Assert.AreEqual("8", EditorConfigHelper.LoadOptions(Path.Combine(_tempDirectory, "Sample.cs"))["indent_size"]);
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void LoadOptions_LowerCasesKeys_AndKeepsTheSeveritySuffixOfValues()
    {
        WriteConfig(_tempDirectory, "root = true", "[*.cs]", "CSharp_Prefer_Braces = when_multiline:warning", "file_header_template = Copyright {fileName}");

        IReadOnlyDictionary<string, string> options = EditorConfigHelper.LoadOptions(Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.AreEqual("when_multiline:warning", options["csharp_prefer_braces"]);
        Assert.AreEqual("Copyright {fileName}", options["file_header_template"]);
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    [DataRow("\r\n", DisplayName = "CRLF")]
    [DataRow("\n", DisplayName = "LF")]
    public void LoadOptions_IgnoresCommentsAndWhitespace_WithAnyLineEnding(string newLine)
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"), string.Join(newLine,
            "# top comment",
            "root = true",
            "; another comment",
            string.Empty,
            "[*.cs]",
            "\tindent_style\t=\ttab\t",
            "  # indent_size = 8",
            "  tab_width   =   6  ",
            string.Empty), new UTF8Encoding(true));

        IReadOnlyDictionary<string, string> options = EditorConfigHelper.LoadOptions(Path.Combine(_tempDirectory, "Sample.cs"));

        CollectionAssert.AreEquivalent(
            new Dictionary<string, string> { ["indent_style"] = "tab", ["tab_width"] = "6" },
            options.ToDictionary(entry => entry.Key, entry => entry.Value));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    [DataRow("none", "none")]
    [DataRow("silent", "silent")]
    [DataRow("refactoring", "silent")]
    [DataRow("suggestion", "suggestion")]
    [DataRow("warning", "warning")]
    [DataRow("error", "error")]
    [DataRow("WARNING", "warning", DisplayName = "severity is case-insensitive")]
    public void LoadOptions_ReportsDiagnosticSeverities_UnderTheLowerCasedIdKey(string configured, string expected)
    {
        WriteConfig(_tempDirectory, "root = true", "[*.cs]", "dotnet_diagnostic.IDE0011.severity = " + configured);

        IReadOnlyDictionary<string, string> options = EditorConfigHelper.LoadOptions(Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.AreEqual(expected, options[EditorConfigHelper.DiagnosticSeverityKey("IDE0011")]);
        Assert.AreEqual("dotnet_diagnostic.ide0011.severity", EditorConfigHelper.DiagnosticSeverityKey("IDE0011"));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    [DataRow("default", DisplayName = "default severity")]
    [DataRow("loud", DisplayName = "invalid severity")]
    public void LoadOptions_DefaultOrInvalidDiagnosticSeverity_IsNotReported(string configured)
    {
        WriteConfig(_tempDirectory, "root = true", "[*.cs]", "dotnet_diagnostic.IDE0011.severity = " + configured, "indent_size = 2");

        IReadOnlyDictionary<string, string> options = EditorConfigHelper.LoadOptions(Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.IsFalse(options.ContainsKey(EditorConfigHelper.DiagnosticSeverityKey("IDE0011")));
        Assert.AreEqual("2", options["indent_size"]);
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    [DataRow(null, DisplayName = "null path")]
    [DataRow("", DisplayName = "empty path")]
    [DataRow("   ", DisplayName = "whitespace path")]
    [DataRow("Sample\0.cs", DisplayName = "path with a null character")]
    public void BlankOrInvalidPath_YieldsNoOptions_AndNoDefiningFile(string filePath)
    {
        Assert.AreEqual(0, EditorConfigHelper.LoadOptions(filePath).Count);
        Assert.IsNull(EditorConfigHelper.FindDefiningConfigPath(filePath, "indent_size"));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void LoadOptions_DeletedEditorConfig_NoLongerApplies()
    {
        WriteConfig(_tempDirectory, "root = true", "[*.cs]", "indent_size = 2");
        string nested = CreateDirectory("src");
        WriteConfig(nested, "[*.cs]", "indent_size = 3");
        string filePath = Path.Combine(nested, "Sample.cs");
        Assert.AreEqual("3", EditorConfigHelper.LoadOptions(filePath)["indent_size"]);

        File.Delete(Path.Combine(nested, ".editorconfig"));

        Assert.AreEqual("2", EditorConfigHelper.LoadOptions(filePath)["indent_size"]);
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void LoadOptions_EditedEditorConfigOfTheSameLength_IsReadAgain()
    {
        string configPath = Path.Combine(_tempDirectory, ".editorconfig");
        WriteConfig(_tempDirectory, "root = true", "[*.cs]", "indent_size = 2");
        string filePath = Path.Combine(_tempDirectory, "Sample.cs");
        Assert.AreEqual("2", EditorConfigHelper.LoadOptions(filePath)["indent_size"]);

        WriteConfig(_tempDirectory, "root = true", "[*.cs]", "indent_size = 3");
        File.SetLastWriteTimeUtc(configPath, File.GetLastWriteTimeUtc(configPath).AddSeconds(5));

        Assert.AreEqual("3", EditorConfigHelper.LoadOptions(filePath)["indent_size"]);
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void LoadOptions_EditorConfigLockedByAnotherWriter_IsSkipped_AndTheOthersStillApply()
    {
        WriteConfig(_tempDirectory, "root = true", "[*.cs]", "indent_size = 2", "indent_style = space");
        string nested = CreateDirectory("src");
        WriteConfig(nested, "[*.cs]", "indent_size = 3");
        string filePath = Path.Combine(nested, "Sample.cs");

        using (new FileStream(Path.Combine(nested, ".editorconfig"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            IReadOnlyDictionary<string, string> options = EditorConfigHelper.LoadOptions(filePath);

            Assert.AreEqual("2", options["indent_size"]);
            Assert.AreEqual("space", options["indent_style"]);
        }
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void FindDefiningConfigPath_ReturnsTheNearestFileDefiningTheOption()
    {
        string parentConfig = WriteConfig(_tempDirectory, "root = true", "[*.cs]", "indent_size = 2", "indent_style = space");
        string nested = CreateDirectory("src", "App");
        string childConfig = WriteConfig(Path.Combine(_tempDirectory, "src"), "[*.cs]", "indent_size = 3");
        string filePath = Path.Combine(nested, "Program.cs");

        Assert.AreEqual(childConfig, EditorConfigHelper.FindDefiningConfigPath(filePath, "indent_size"));
        Assert.AreEqual(parentConfig, EditorConfigHelper.FindDefiningConfigPath(filePath, "indent_style"));
        Assert.IsNull(EditorConfigHelper.FindDefiningConfigPath(filePath, "tab_width"));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void FindDefiningConfigPath_IgnoresSectionsThatDoNotMatchTheFile()
    {
        string parentConfig = WriteConfig(_tempDirectory, "root = true", "[*.cs]", "indent_size = 2");
        string nested = CreateDirectory("src");
        WriteConfig(nested, "[*.vb]", "indent_size = 3", "[{Program,Startup}.cs]", "indent_size = 5");

        Assert.AreEqual(parentConfig, EditorConfigHelper.FindDefiningConfigPath(Path.Combine(nested, "Sample.cs"), "indent_size"));
        Assert.AreEqual(Path.Combine(nested, ".editorconfig"), EditorConfigHelper.FindDefiningConfigPath(Path.Combine(nested, "Program.cs"), "indent_size"));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void FindDefiningConfigPath_OptionOnlyAboveARootFile_IsNotApplied()
    {
        WriteConfig(_tempDirectory, "root = true", "[*.cs]", "indent_size = 2");
        string nested = CreateDirectory("src");
        WriteConfig(nested, "root = true", "[*.cs]", "indent_style = tab");

        Assert.IsNull(EditorConfigHelper.FindDefiningConfigPath(Path.Combine(nested, "Sample.cs"), "indent_size"));
    }

    [TestMethod]
    [TestCategory("Helpers UnitTests")]
    public void FindDefiningConfigPath_FindsTheFileConfiguringADiagnosticSeverity()
    {
        string parentConfig = WriteConfig(_tempDirectory, "root = true", "[*.cs]", "dotnet_diagnostic.IDE0011.severity = warning");
        string nested = CreateDirectory("src");
        WriteConfig(nested, "[*.cs]", "dotnet_diagnostic.IDE0040.severity = none");

        Assert.AreEqual(
            parentConfig,
            EditorConfigHelper.FindDefiningConfigPath(Path.Combine(nested, "Sample.cs"), EditorConfigHelper.DiagnosticSeverityKey("IDE0011")));
    }

    /// <summary>
    /// Writes an .editorconfig with the specified lines (CRLF) into the specified directory.
    /// </summary>
    /// <returns>The full path of the written file.</returns>
    private static string WriteConfig(string directory, params string[] lines)
    {
        string path = Path.Combine(directory, ".editorconfig");
        File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n");

        return path;
    }

    private string CreateDirectory(params string[] segments)
    {
        return Directory.CreateDirectory(Path.Combine(new[] { _tempDirectory }.Concat(segments).ToArray())).FullName;
    }
}
