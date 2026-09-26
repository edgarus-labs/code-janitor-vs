using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.CodeAnalysis.CSharp;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Model.CodeItems;
using CodeJanitor.Properties;
using CodeJanitor.UI.Enumerations;
using System;
using System.IO;

namespace CodeJanitor.UnitTests.Cleaning;

/// <summary>
/// Tests that the editor cleanup steps decide from the effective settings of the document: .editorconfig, then the
/// .codejanitor repository policy, then the Visual Studio settings.
/// </summary>
[TestClass]
public sealed class EditorCleanupSettingsTests
{
    private string _tempDirectory;

    [TestInitialize]
    public void TestInitialize()
    {
        Settings.Default.Reset();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        CSharpLanguageVersionSupport.SetLanguageVersionResolver(_ => new[] { LanguageVersion.CSharp12 });
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Settings.Default.Reset();
        CSharpLanguageVersionSupport.SetLanguageVersionResolver(null);

        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [TestMethod]
    public void NamespaceStep_ConvertsFileScopedNamespaceToBlockScoped_WhenEditorConfigRequiresIt_OverUserSetting()
    {
        WriteEditorConfig("csharp_style_namespace_declarations = block_scoped");
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = true;
        var filePath = Path.Combine(_tempDirectory, "Sample.cs");

        var output = FileScopedNamespaceLogic.GetInstance(null).ConvertNamespaceDeclarations(
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n}\r\n",
            filePath,
            EffectiveCleanupSettings.For(filePath));

        Assert.AreEqual("namespace Demo\r\n{\r\n    public class C\r\n    {\r\n    }\r\n}\r\n", output);
    }

    [TestMethod]
    [DataRow("indent_style = tab", "\t", DisplayName = "tabs")]
    [DataRow("indent_style = space\r\nindent_size = 2", "  ", DisplayName = "two spaces")]
    public void NamespaceStep_IndentsTheBlockScopedBody_AsEditorConfigRequires(string indentation, string level)
    {
        WriteEditorConfig("csharp_style_namespace_declarations = block_scoped", indentation);
        var filePath = Path.Combine(_tempDirectory, "Sample.cs");

        var output = FileScopedNamespaceLogic.GetInstance(null).ConvertNamespaceDeclarations(
            "namespace Demo;\r\n\r\npublic class C\r\n{\r\n}\r\n",
            filePath,
            EffectiveCleanupSettings.For(filePath));

        Assert.AreEqual($"namespace Demo\r\n{{\r\n{level}public class C\r\n{level}{{\r\n{level}}}\r\n}}\r\n", output);
    }

    [TestMethod]
    public void NamespaceStep_LeavesBlockScopedNamespace_WhenAProjectCompilingTheFileIsOlderThanCSharp10()
    {
        CSharpLanguageVersionSupport.SetLanguageVersionResolver(_ => new[] { LanguageVersion.CSharp9 });
        Settings.Default.Cleaning_ConvertToFileScopedNamespace = true;
        const string source = "namespace Demo\r\n{\r\n    public class C\r\n    {\r\n    }\r\n}\r\n";
        var filePath = Path.Combine(_tempDirectory, "Sample.cs");

        var output = FileScopedNamespaceLogic.GetInstance(null).ConvertNamespaceDeclarations(source, filePath, EffectiveCleanupSettings.For(filePath));

        Assert.AreEqual(source, output);
    }

    [TestMethod]
    public void FileHeader_ComesFromEditorConfigTemplate_OverPolicyAndUserSetting()
    {
        WriteEditorConfig("file_header_template = Copyright {fileName}");
        WriteRepositoryPolicy("\"fileHeaderCSharp\": \"// Policy header\"");
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// User header";

        var settings = EffectiveCleanupSettings.For(Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.AreEqual("// Copyright Sample.cs", FileHeaderHelper.GetFileHeaderFromSettings(CodeLanguage.CSharp, settings));
    }

    [TestMethod]
    public void FileHeaderPosition_ComesFromRepositoryPolicy_OverUserSetting_ForCSharpOnly()
    {
        WriteRepositoryPolicy("\"fileHeaderPosition\": \"afterUsings\"");
        Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition = (int)HeaderPosition.DocumentStart;

        var settings = EffectiveCleanupSettings.For(Path.Combine(_tempDirectory, "Sample.cs"));

        Assert.AreEqual(HeaderPosition.AfterUsings, FileHeaderHelper.GetFileHeaderPositionFromSettings(CodeLanguage.CSharp, settings));
        Assert.AreEqual(HeaderPosition.DocumentStart, FileHeaderHelper.GetFileHeaderPositionFromSettings(CodeLanguage.VisualBasic, settings));
    }

    [TestMethod]
    public void BlankLinePadding_ComesFromRepositoryPolicy_OverUserSetting()
    {
        WriteRepositoryPolicy("\"insertBlankLinePaddingBeforeMethods\": false, \"insertBlankLinePaddingAfterMethods\": true");
        Settings.Default.Cleaning_InsertBlankLinePaddingBeforeMethods = true;
        Settings.Default.Cleaning_InsertBlankLinePaddingAfterMethods = false;

        var settings = EffectiveCleanupSettings.For(Path.Combine(_tempDirectory, "Sample.cs"));
        var logic = InsertBlankLinePaddingLogic.GetInstance(null);
        var method = new TestCodeItem(KindCodeItem.Method);

        Assert.IsFalse(logic.ShouldBePrecededByBlankLine(method, settings));
        Assert.IsTrue(logic.ShouldBeFollowedByBlankLine(method, settings));
    }

    /// <summary>
    /// Writes a root .editorconfig with the specified C# options into the test directory.
    /// </summary>

    private void WriteEditorConfig(params string[] options)
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"),
            "root = true\r\n\r\n[*.cs]\r\n" + string.Join("\r\n", options) + "\r\n");
    }

    /// <summary>
    /// Writes a .codejanitor repository policy with the specified cleanup entries into the test directory.
    /// </summary>

    private void WriteRepositoryPolicy(string cleanupEntries)
    {
        File.WriteAllText(Path.Combine(_tempDirectory, RepositoryCleanupSettings.PrimaryConfigFileName),
            "{ \"cleanup\": { " + cleanupEntries + " } }");
    }

    private sealed class TestCodeItem : BaseCodeItem
    {
        private readonly KindCodeItem _kind;

        public TestCodeItem(KindCodeItem kind)
        {
            _kind = kind;
        }

        public override KindCodeItem Kind => _kind;
    }
}
