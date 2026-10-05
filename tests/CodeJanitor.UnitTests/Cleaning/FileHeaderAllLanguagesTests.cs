using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using CodeJanitor.UI.Enumerations;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Threading;
using NSubstitute;
using EditPoint = EnvDTE.EditPoint;
using TextDocument = EnvDTE.TextDocument;
using TextPoint = EnvDTE.TextPoint;

namespace CodeJanitor.UnitTests.Cleaning;

/// <summary>
/// Behavioral tests for the file header update of every supported language: the editor-backed update of open files
/// (<see cref="FileHeaderLogic" />, run against an in-memory editor buffer) and the headless update of closed C# files.
/// </summary>
[TestClass]
public sealed class FileHeaderAllLanguagesTests
{
    private const string Crlf = "\r\n";
    private const string UsingsBody = "using System;\r\nusing System.Linq;\r\n\r\nnamespace Demo\r\n{\r\n}\r\n";
    private const string HeadlessBody = "namespace Demo;\r\n\r\npublic class C { }\r\n";

    private static readonly HeaderLanguage[] HeaderLanguages =
    [
        new("C#", "CSharp", ".cs", nameof(Settings.Cleaning_UpdateFileHeaderCSharp), "// Copyright (c) Contoso", "// Old header 1\r\n// Old header 2", "/* Old\r\n * header */", "namespace Demo;\r\n\r\npublic class C { }\r\n"),
        new("VB", "Basic", ".vb", nameof(Settings.Cleaning_UpdateFileHeaderVB), "' Copyright (c) Contoso", "' Old header 1\r\n' Old header 2", null, "Module M\r\nEnd Module\r\n"),
        new("C++", "C/C++", ".cpp", nameof(Settings.Cleaning_UpdateFileHeaderCPlusPlus), "// Copyright (c) Contoso", "// Old header 1\r\n// Old header 2", "/* Old\r\n * header */", "int main() { return 0; }\r\n"),
        new("CSS", "CSS", ".css", nameof(Settings.Cleaning_UpdateFileHeaderCSS), "/* Copyright (c) Contoso */", "/* Old header */", "/* Old\r\n header */", "body { color: red; }\r\n"),
        new("F#", "F#", ".fs", nameof(Settings.Cleaning_UpdateFileHeaderFSharp), "// Copyright (c) Contoso", "// Old header 1\r\n// Old header 2", "(* Old\r\n header *)", "module M\r\n\r\nlet x = 1\r\n"),
        new("HTML", "HTML", ".html", nameof(Settings.Cleaning_UpdateFileHeaderHTML), "<!-- Copyright (c) Contoso -->", "<!-- Old header -->", "<!-- Old\r\n header -->", "<html></html>\r\n"),
        new("JavaScript", "JavaScript", ".js", nameof(Settings.Cleaning_UpdateFileHeaderJavaScript), "// Copyright (c) Contoso", "// Old header 1\r\n// Old header 2", "/* Old\r\n * header */", "var a = 1;\r\n"),
        new("JSON", "JSON", ".json", nameof(Settings.Cleaning_UpdateFileHeaderJSON), "// Copyright (c) Contoso", "// Old header 1\r\n// Old header 2", "/* Old\r\n * header */", "{ \"a\": 1 }\r\n"),
        new("LESS", "LESS", ".less", nameof(Settings.Cleaning_UpdateFileHeaderLESS), "// Copyright (c) Contoso", "// Old header 1\r\n// Old header 2", "/* Old\r\n * header */", "@a: 1;\r\n"),
        new("PHP", "PHP", ".php", nameof(Settings.Cleaning_UpdateFileHeaderPHP), "// Copyright (c) Contoso", "// Old header 1\r\n// Old header 2", "/* Old\r\n * header */", "$a = 1;\r\n"),
        new("PowerShell", "PowerShell", ".ps1", nameof(Settings.Cleaning_UpdateFileHeaderPowerShell), "# Copyright (c) Contoso", "# Old header 1\r\n# Old header 2", "<# Old\r\n header #>", "Write-Host 1\r\n"),
        new("R", "R", ".r", nameof(Settings.Cleaning_UpdateFileHeaderR), "# Copyright (c) Contoso", "# Old header 1\r\n# Old header 2", null, "x <- 1\r\n"),
        new("SCSS", "SCSS", ".scss", nameof(Settings.Cleaning_UpdateFileHeaderSCSS), "// Copyright (c) Contoso", "// Old header 1\r\n// Old header 2", "/* Old\r\n * header */", "$a: 1;\r\n"),
        new("TypeScript", "TypeScript", ".ts", nameof(Settings.Cleaning_UpdateFileHeaderTypeScript), "// Copyright (c) Contoso", "// Old header 1\r\n// Old header 2", "/* Old\r\n * header */", "let a = 1;\r\n"),
        new("XAML", "XAML", ".xaml", nameof(Settings.Cleaning_UpdateFileHeaderXAML), "<!-- Copyright (c) Contoso -->", "<!-- Old header -->", "<!-- Old\r\n header -->", "<Page />\r\n"),
        new("XML", "XML", ".xml", nameof(Settings.Cleaning_UpdateFileHeaderXML), "<!-- Copyright (c) Contoso -->", "<!-- Old header -->", "<!-- Old\r\n header -->", "<root />\r\n"),
    ];

    private string _tempDirectory;

    public static IEnumerable<object[]> LanguageNames => HeaderLanguages.Select(language => new object[] { language.Name });

    public static IEnumerable<object[]> LanguageNamesWithBlockHeaders => HeaderLanguages.Where(language => language.BlockHeader is not null).Select(language => new object[] { language.Name });

    [TestInitialize]
    public void TestInitialize()
    {
        Settings.Default.Reset();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        // Isolates every test from configuration files above the temp directory.
        WriteEditorConfig();
        File.WriteAllText(Path.Combine(_tempDirectory, ".codejanitor"), "{ \"cleanup\": { } }");
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
    [DynamicData(nameof(LanguageNames))]
    public void InsertMode_AddsTheConfiguredHeaderAtTheStartOfTheDocument(string name)
    {
        HeaderLanguage language = Find(name);
        Configure(language, language.Header, HeaderUpdateMode.Insert);

        Assert.AreEqual(language.Header + Crlf + language.Body, UpdateInEditor(language, language.Body));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DynamicData(nameof(LanguageNames))]
    public void InsertMode_RunTwice_AddsTheHeaderOnce(string name)
    {
        HeaderLanguage language = Find(name);
        Configure(language, language.Header, HeaderUpdateMode.Insert);

        string once = UpdateInEditor(language, language.Body);

        Assert.AreEqual(once, UpdateInEditor(language, once));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DynamicData(nameof(LanguageNames))]
    public void InsertMode_DocumentThatStartsWithADifferentHeader_KeepsItBelowTheNewHeader(string name)
    {
        HeaderLanguage language = Find(name);
        Configure(language, language.Header, HeaderUpdateMode.Insert);
        string text = language.LineHeader + Crlf + language.Body;

        Assert.AreEqual(language.Header + Crlf + text, UpdateInEditor(language, text));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DynamicData(nameof(LanguageNames))]
    public void ReplaceMode_ReplacesTheExistingHeader(string name)
    {
        HeaderLanguage language = Find(name);
        Configure(language, language.Header, HeaderUpdateMode.Replace);

        Assert.AreEqual(language.Header + Crlf + language.Body, UpdateInEditor(language, language.LineHeader + Crlf + language.Body));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DynamicData(nameof(LanguageNamesWithBlockHeaders))]
    public void ReplaceMode_ReplacesAnExistingBlockHeader(string name)
    {
        HeaderLanguage language = Find(name);
        Configure(language, language.Header, HeaderUpdateMode.Replace);

        Assert.AreEqual(language.Header + Crlf + language.Body, UpdateInEditor(language, language.BlockHeader + Crlf + language.Body));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DynamicData(nameof(LanguageNames))]
    public void ReplaceMode_DocumentWithoutHeader_AddsTheHeader(string name)
    {
        HeaderLanguage language = Find(name);
        Configure(language, language.Header, HeaderUpdateMode.Replace);

        Assert.AreEqual(language.Header + Crlf + language.Body, UpdateInEditor(language, language.Body));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DynamicData(nameof(LanguageNames))]
    public void ReplaceMode_RunTwice_KeepsASingleHeader(string name)
    {
        HeaderLanguage language = Find(name);
        Configure(language, language.Header, HeaderUpdateMode.Replace);

        string fromHeaderless = UpdateInEditor(language, language.Body);
        string fromOldHeader = UpdateInEditor(language, language.LineHeader + Crlf + language.Body);

        Assert.AreEqual(fromHeaderless, UpdateInEditor(language, fromHeaderless), "Run on a document that got its header from the first run.");
        Assert.AreEqual(fromOldHeader, UpdateInEditor(language, fromOldHeader), "Run on a document whose old header was replaced by the first run.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DynamicData(nameof(LanguageNames))]
    public void ReplaceMode_DocumentAlreadyStartingWithTheHeader_IsLeftAsItIs(string name)
    {
        HeaderLanguage language = Find(name);
        Configure(language, language.Header, HeaderUpdateMode.Replace);
        string text = language.Header + Crlf + language.Body;

        Assert.AreEqual(text, UpdateInEditor(language, text));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DynamicData(nameof(LanguageNames))]
    public void ReplaceMode_HeaderFollowedByABlankLine_KeepsTheBlankLine(string name)
    {
        HeaderLanguage language = Find(name);
        Configure(language, language.Header, HeaderUpdateMode.Replace);
        string text = language.LineHeader + Crlf + Crlf + language.Body;

        Assert.AreEqual(language.Header + Crlf + Crlf + language.Body, UpdateInEditor(language, text));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DynamicData(nameof(LanguageNames))]
    public void BothModes_MultiLineHeader_IsWrittenLineByLine(string name)
    {
        HeaderLanguage language = Find(name);
        string multiLineHeader = language.Header + Crlf + language.Header;

        Configure(language, multiLineHeader, HeaderUpdateMode.Insert);
        Assert.AreEqual(multiLineHeader + Crlf + language.Body, UpdateInEditor(language, language.Body), "Insert");

        Configure(language, multiLineHeader, HeaderUpdateMode.Replace);
        Assert.AreEqual(multiLineHeader + Crlf + language.Body, UpdateInEditor(language, language.LineHeader + Crlf + language.Body), "Replace");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DynamicData(nameof(LanguageNames))]
    public void BothModes_HeaderEndingWithALineBreak_DoesNotAddABlankLine(string name)
    {
        HeaderLanguage language = Find(name);

        foreach (HeaderUpdateMode mode in new[] { HeaderUpdateMode.Insert, HeaderUpdateMode.Replace })
        {
            Configure(language, language.Header + Crlf, mode);

            Assert.AreEqual(language.Header + Crlf + language.Body, UpdateInEditor(language, language.Body), mode.ToString());
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DynamicData(nameof(LanguageNames))]
    public void BothModes_EmptyHeaderSetting_LeavesTheDocumentUntouched(string name)
    {
        HeaderLanguage language = Find(name);
        string text = language.LineHeader + Crlf + language.Body;

        foreach (HeaderUpdateMode mode in new[] { HeaderUpdateMode.Insert, HeaderUpdateMode.Replace })
        {
            foreach (string header in new[] { string.Empty, "   ", "\r\n\r\n" })
            {
                Configure(language, header, mode);

                Assert.AreEqual(text, UpdateInEditor(language, text), $"{mode}, header '{header.Replace("\r", "\\r").Replace("\n", "\\n")}'");
            }
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DynamicData(nameof(LanguageNames))]
    public void BothModes_UseOnlyTheHeaderSettingOfTheLanguageOfTheDocument(string name)
    {
        HeaderLanguage language = Find(name);
        foreach (HeaderLanguage other in HeaderLanguages)
        {
            Settings.Default[other.Setting] = "WRONG HEADER of " + other.Name;
        }

        Settings.Default[language.Setting] = language.Header;

        foreach (HeaderUpdateMode mode in new[] { HeaderUpdateMode.Insert, HeaderUpdateMode.Replace })
        {
            Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = (int)mode;

            Assert.AreEqual(language.Header + Crlf + language.Body, UpdateInEditor(language, language.Body), mode.ToString());
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DynamicData(nameof(LanguageNames))]
    public void BothModes_OnlyAnotherLanguageHasAHeaderSetting_LeaveTheDocumentUntouched(string name)
    {
        HeaderLanguage language = Find(name);
        foreach (HeaderLanguage other in HeaderLanguages.Where(candidate => candidate != language))
        {
            Settings.Default[other.Setting] = "HEADER of " + other.Name;
        }

        foreach (HeaderUpdateMode mode in new[] { HeaderUpdateMode.Insert, HeaderUpdateMode.Replace })
        {
            Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = (int)mode;

            Assert.AreEqual(language.Body, UpdateInEditor(language, language.Body), mode.ToString());
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void BothModes_LanguageWithoutAHeaderSetting_LeavesTheDocumentUntouched()
    {
        foreach (HeaderLanguage language in HeaderLanguages)
        {
            Settings.Default[language.Setting] = "// header";
        }

        HeaderLanguage plainText = new("Plain text", "Plain Text", ".txt", null, null, null, null, "some text\r\n");

        foreach (HeaderUpdateMode mode in new[] { HeaderUpdateMode.Insert, HeaderUpdateMode.Replace })
        {
            Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = (int)mode;

            Assert.AreEqual(plainText.Body, UpdateInEditor(plainText, plainText.Body), mode.ToString());
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void GetFileHeaderFromSettings_ReturnsTheHeaderOfEachLanguage()
    {
        foreach (HeaderLanguage language in HeaderLanguages)
        {
            Settings.Default[language.Setting] = "HEADER of " + language.Name;
        }

        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(Path.Combine(_tempDirectory, "Sample.txt"));

        foreach (HeaderLanguage language in HeaderLanguages)
        {
            Assert.AreEqual("HEADER of " + language.Name, FileHeaderHelper.GetFileHeaderFromSettings(CodeLanguageHelper.GetCodeLanguage(language.Language), settings), language.Name);
        }

        Assert.IsNull(FileHeaderHelper.GetFileHeaderFromSettings(CodeLanguage.Unknown, settings));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void GetFileHeaderPositionFromSettings_OnlyCSharpHonorsTheConfiguredPosition()
    {
        Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition = (int)HeaderPosition.AfterUsings;
        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(Path.Combine(_tempDirectory, "Sample.cs"));

        foreach (HeaderLanguage language in HeaderLanguages)
        {
            HeaderPosition expected = language.Name == "C#" ? HeaderPosition.AfterUsings : HeaderPosition.DocumentStart;

            Assert.AreEqual(expected, FileHeaderHelper.GetFileHeaderPositionFromSettings(CodeLanguageHelper.GetCodeLanguage(language.Language), settings), language.Name);
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CSharpInsert_AfterUsings_AddsTheHeaderBelowTheUsings()
    {
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// Copyright";
        Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition = (int)HeaderPosition.AfterUsings;
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = (int)HeaderUpdateMode.Insert;

        string once = UpdateInEditor(Find("C#"), UsingsBody);

        Assert.AreEqual("using System;\r\nusing System.Linq;\r\n\r\n// Copyright\r\n\r\nnamespace Demo\r\n{\r\n}\r\n", once);
        Assert.AreEqual(once, UpdateInEditor(Find("C#"), once), "A second run must not add the header again.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CSharpReplace_AfterUsings_ReplacesTheHeaderBelowTheUsings()
    {
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// Copyright";
        Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition = (int)HeaderPosition.AfterUsings;
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = (int)HeaderUpdateMode.Replace;
        string text = "using System;\r\nusing System.Linq;\r\n\r\n// Old\r\n\r\nnamespace Demo\r\n{\r\n}\r\n";

        string once = UpdateInEditor(Find("C#"), text);

        Assert.AreEqual("using System;\r\nusing System.Linq;\r\n\r\n// Copyright\r\n\r\nnamespace Demo\r\n{\r\n}\r\n", once);
        Assert.AreEqual(once, UpdateInEditor(Find("C#"), once), "A second run must keep the header.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CSharpReplace_AfterUsings_MovesTheHeaderOfTheDocumentStartBelowTheUsings()
    {
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// Copyright";
        Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition = (int)HeaderPosition.AfterUsings;
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = (int)HeaderUpdateMode.Replace;

        string text = UpdateInEditor(Find("C#"), "// Old\r\n" + UsingsBody);

        Assert.AreEqual("using System;\r\nusing System.Linq;\r\n\r\n// Copyright\r\n\r\nnamespace Demo\r\n{\r\n}\r\n", text);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CSharpReplace_DocumentStart_ReplacesTheHeaderAndLeavesTheUsingsAlone()
    {
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// Copyright";
        Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition = (int)HeaderPosition.DocumentStart;
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = (int)HeaderUpdateMode.Replace;

        string text = UpdateInEditor(Find("C#"), "// Old\r\n" + UsingsBody);

        Assert.AreEqual("// Copyright\r\n" + UsingsBody, text);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CSharpInsert_DocumentStart_PutsTheHeaderAboveTheUsings()
    {
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// Copyright";
        Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition = (int)HeaderPosition.DocumentStart;
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = (int)HeaderUpdateMode.Insert;

        Assert.AreEqual("// Copyright\r\n" + UsingsBody, UpdateInEditor(Find("C#"), UsingsBody));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void NonCSharpLanguage_IgnoresTheAfterUsingsPosition()
    {
        Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition = (int)HeaderPosition.AfterUsings;
        HeaderLanguage vb = Find("VB");
        Configure(vb, vb.Header, HeaderUpdateMode.Insert);
        string text = "Imports System\r\n\r\n" + vb.Body;

        Assert.AreEqual(vb.Header + Crlf + text, UpdateInEditor(vb, text));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void XmlDeclaration_StaysTheFirstLineOfTheDocument()
    {
        foreach (string name in new[] { "XML", "XAML" })
        {
            HeaderLanguage language = Find(name);
            const string Declaration = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n";
            string body = Declaration + language.Body;

            Configure(language, language.Header, HeaderUpdateMode.Insert);
            string inserted = UpdateInEditor(language, body);
            Assert.AreEqual(Declaration + language.Header + Crlf + language.Body, inserted, name + " insert");
            Assert.AreEqual(inserted, UpdateInEditor(language, inserted), name + " insert twice");

            Configure(language, language.Header, HeaderUpdateMode.Replace);
            Assert.AreEqual(Declaration + language.Header + Crlf + language.Body, UpdateInEditor(language, Declaration + language.LineHeader + Crlf + language.Body), name + " replace");
            Assert.AreEqual(inserted, UpdateInEditor(language, inserted), name + " replace twice");
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void PhpOpeningTag_StaysTheFirstLineOfTheDocument()
    {
        HeaderLanguage php = Find("PHP");
        const string OpeningTag = "<?php\r\n";
        string text = OpeningTag + php.Body;

        Configure(php, php.Header, HeaderUpdateMode.Insert);
        string inserted = UpdateInEditor(php, text);
        Assert.AreEqual(OpeningTag + php.Header + Crlf + php.Body, inserted, "insert");
        Assert.AreEqual(inserted, UpdateInEditor(php, inserted), "insert twice");

        Configure(php, php.Header, HeaderUpdateMode.Replace);
        Assert.AreEqual(OpeningTag + php.Header + Crlf + php.Body, UpdateInEditor(php, OpeningTag + php.LineHeader + Crlf + php.Body), "replace");
        Assert.AreEqual(inserted, UpdateInEditor(php, inserted), "replace twice");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("R", "#!/usr/bin/env Rscript\r\n")]
    [DataRow("PowerShell", "#!/usr/bin/env pwsh\r\n")]
    [DataRow("PHP", "#!/usr/bin/env php\r\n")]
    [DataRow("JavaScript", "#!/usr/bin/env node\r\n")]
    [DataRow("TypeScript", "#!/usr/bin/env ts-node\r\n")]
    [DataRow("F#", "#!/usr/bin/env dotnet-fsi\r\n")]
    public void Shebang_StaysTheFirstLineOfTheDocument(string name, string shebang)
    {
        HeaderLanguage language = Find(name);
        string text = shebang + language.Body;

        Configure(language, language.Header, HeaderUpdateMode.Insert);
        string inserted = UpdateInEditor(language, text);
        Assert.AreEqual(shebang + language.Header + Crlf + language.Body, inserted, "insert");
        Assert.AreEqual(inserted, UpdateInEditor(language, inserted), "insert twice");

        Configure(language, language.Header, HeaderUpdateMode.Replace);
        Assert.AreEqual(shebang + language.Header + Crlf + language.Body, UpdateInEditor(language, shebang + language.LineHeader + Crlf + language.Body), "replace");
        Assert.AreEqual(inserted, UpdateInEditor(language, inserted), "replace twice");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void EditorConfigTemplate_BeatsThePolicyAndTheUserHeaderOfCSharpDocuments()
    {
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// User header";
        File.WriteAllText(Path.Combine(_tempDirectory, ".codejanitor"), "{ \"cleanup\": { \"fileHeaderCSharp\": \"// Policy header\" } }");
        WriteEditorConfig(@"file_header_template = Copyright (c) Contoso\n\n{fileName} is licensed under MIT.");
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = (int)HeaderUpdateMode.Insert;
        HeaderLanguage csharp = Find("C#");

        Assert.AreEqual(
            "// Copyright (c) Contoso\r\n//\r\n// Sample.cs is licensed under MIT.\r\n" + csharp.Body,
            UpdateInEditor(csharp, csharp.Body));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void EditorConfigTemplate_ReplacesTheHeaderOfCSharpDocumentsAndIsStable()
    {
        WriteEditorConfig(@"file_header_template = Copyright (c) Contoso\n{fileName}");
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = (int)HeaderUpdateMode.Replace;
        HeaderLanguage csharp = Find("C#");

        string once = UpdateInEditor(csharp, "// Old 1\r\n// Old 2\r\n" + csharp.Body);

        Assert.AreEqual("// Copyright (c) Contoso\r\n// Sample.cs\r\n" + csharp.Body, once);
        Assert.AreEqual(once, UpdateInEditor(csharp, once));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void EditorConfigTemplate_Unset_RemovesTheHeaderConfiguredByTheUser()
    {
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// User header";
        WriteEditorConfig("file_header_template = unset");
        HeaderLanguage csharp = Find("C#");

        foreach (HeaderUpdateMode mode in new[] { HeaderUpdateMode.Insert, HeaderUpdateMode.Replace })
        {
            Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = (int)mode;

            Assert.AreEqual(csharp.Body, UpdateInEditor(csharp, csharp.Body), mode.ToString());
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void EditorConfigTemplate_DoesNotChangeTheHeaderOfOtherLanguages()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"), "root = true\r\n\r\n[*]\r\nfile_header_template = Template header\r\n");
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = (int)HeaderUpdateMode.Insert;

        foreach (HeaderLanguage language in HeaderLanguages.Where(candidate => candidate.Name != "C#"))
        {
            Settings.Default[language.Setting] = language.Header;

            Assert.AreEqual(language.Header + Crlf + language.Body, UpdateInEditor(language, language.Body), language.Name);
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Headless_InsertAtDocumentStart_AddsTheHeaderOnce()
    {
        ConfigureHeadless("// Copyright (c) Contoso", HeaderPosition.DocumentStart, HeaderUpdateMode.Insert);

        string once = Headless(HeadlessBody);

        AssertHeaderFollowedByTheCode("// Copyright (c) Contoso", once);
        Assert.AreEqual(once, Headless(once));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Headless_InsertAtDocumentStart_KeepsADifferentExistingHeaderBelowTheNewOne()
    {
        ConfigureHeadless("// Copyright (c) Contoso", HeaderPosition.DocumentStart, HeaderUpdateMode.Insert);

        string text = Headless("// Old header\r\n" + HeadlessBody);

        AssertHeaderFollowedByTheCode("// Copyright (c) Contoso\r\n// Old header", text);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("// Old header 1\r\n// Old header 2\r\n", DisplayName = "line comments")]
    [DataRow("/* Old\r\n * header */\r\n", DisplayName = "block comment")]
    [DataRow("\r\n\r\n// Old header\r\n", DisplayName = "after blank lines")]
    public void Headless_ReplaceAtDocumentStart_ReplacesTheExistingHeader(string existingHeader)
    {
        ConfigureHeadless("// Copyright (c) Contoso", HeaderPosition.DocumentStart, HeaderUpdateMode.Replace);

        string once = Headless(existingHeader + HeadlessBody);

        AssertHeaderFollowedByTheCode("// Copyright (c) Contoso", once);
        Assert.AreEqual(once, Headless(once));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Headless_ReplaceAtDocumentStart_DocumentWithoutHeader_AddsItOnce()
    {
        ConfigureHeadless("// Copyright (c) Contoso", HeaderPosition.DocumentStart, HeaderUpdateMode.Replace);

        string once = Headless(HeadlessBody);

        AssertHeaderFollowedByTheCode("// Copyright (c) Contoso", once);
        Assert.AreEqual(once, Headless(once));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Headless_MultiLineHeader_IsWrittenLineByLine()
    {
        ConfigureHeadless("// Copyright (c) Contoso\r\n// All rights reserved.", HeaderPosition.DocumentStart, HeaderUpdateMode.Replace);

        string once = Headless("// Old\r\n" + HeadlessBody);

        AssertHeaderFollowedByTheCode("// Copyright (c) Contoso\r\n// All rights reserved.", once);
        Assert.AreEqual(once, Headless(once));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Headless_InsertAfterUsings_AddsTheHeaderBelowTheUsingsOnce()
    {
        ConfigureHeadless("// Copyright (c) Contoso", HeaderPosition.AfterUsings, HeaderUpdateMode.Insert);

        string once = Headless(UsingsBody);

        Assert.Contains("using System.Linq;\r\n\r\n// Copyright (c) Contoso\r\n\r\nnamespace Demo", once);
        Assert.IsFalse(once.StartsWith("//", StringComparison.Ordinal), "The header is not at the document start.");
        Assert.AreEqual(once, Headless(once));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Headless_ReplaceAfterUsings_ReplacesTheHeaderBelowTheUsings()
    {
        ConfigureHeadless("// Copyright (c) Contoso", HeaderPosition.AfterUsings, HeaderUpdateMode.Replace);

        string once = Headless("using System;\r\nusing System.Linq;\r\n\r\n// Old header\r\n\r\nnamespace Demo\r\n{\r\n}\r\n");

        Assert.Contains("using System.Linq;\r\n\r\n// Copyright (c) Contoso\r\n\r\nnamespace Demo", once);
        Assert.DoesNotContain("Old header", once);
        Assert.AreEqual(once, Headless(once));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(HeaderUpdateMode.Insert)]
    [DataRow(HeaderUpdateMode.Replace)]
    public void Headless_EmptyHeaderSetting_LeavesTheHeaderAlone(HeaderUpdateMode mode)
    {
        string text = "// Existing header\r\n" + HeadlessBody;

        foreach (string header in new[] { string.Empty, "  ", "\r\n" })
        {
            ConfigureHeadless(header, HeaderPosition.DocumentStart, mode);

            AssertHeaderFollowedByTheCode("// Existing header", Headless(text));
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Headless_EditorConfigTemplate_BeatsTheUserHeader()
    {
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// User header";
        WriteEditorConfig(@"file_header_template = Copyright (c) Contoso\n{fileName}");
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = (int)HeaderUpdateMode.Replace;

        string once = Headless("// User header\r\n" + HeadlessBody);

        AssertHeaderFollowedByTheCode("// Copyright (c) Contoso\r\n// Sample.cs", once);
        Assert.AreEqual(once, Headless(once));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Headless_EditorConfigTemplateUnset_AddsNoHeader()
    {
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// User header";
        WriteEditorConfig("file_header_template = unset");

        Assert.IsFalse(Headless(HeadlessBody).Contains("User header"));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Headless_HeaderPolicyKeys_BeatTheUserSettings()
    {
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = "// User header";
        Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition = (int)HeaderPosition.DocumentStart;
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = (int)HeaderUpdateMode.Insert;
        File.WriteAllText(
            Path.Combine(_tempDirectory, ".codejanitor"),
            "{ \"cleanup\": { \"fileHeaderCSharp\": \"// Policy header\", \"fileHeaderPosition\": \"afterUsings\", \"fileHeaderUpdateMode\": \"replace\" } }");

        string once = Headless("using System;\r\n\r\n// Old header\r\n\r\nnamespace Demo;\r\n");

        Assert.Contains("using System;\r\n\r\n// Policy header\r\n\r\nnamespace Demo;", once);
        Assert.DoesNotContain("Old header", once);
        Assert.DoesNotContain("User header", once);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(HeaderUpdateMode.Insert)]
    [DataRow(HeaderUpdateMode.Replace)]
    public void Headless_Shebang_StaysTheFirstLineOfTheFile(HeaderUpdateMode mode)
    {
        ConfigureHeadless("// Copyright (c) Contoso", HeaderPosition.DocumentStart, mode);
        const string Shebang = "#!/usr/bin/env dotnet\r\n";

        string once = Headless(Shebang + (mode == HeaderUpdateMode.Replace ? "// Old header\r\n" : string.Empty) + HeadlessBody);

        Assert.IsTrue(once.StartsWith(Shebang, StringComparison.Ordinal), once);
        AssertHeaderFollowedByTheCode("// Copyright (c) Contoso", once.Substring(Shebang.Length).TrimStart('\r', '\n'));
        Assert.AreEqual(once, Headless(once));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(CodeLanguage.XML, "<?xml version=\"1.0\"?>\r\n<root />\r\n", "<?xml version=\"1.0\"?>\r\n")]
    [DataRow(CodeLanguage.XML, "<?xml version=\"1.0\"\r\n encoding=\"utf-8\"?>\r\n<root />", "<?xml version=\"1.0\"\r\n encoding=\"utf-8\"?>\r\n")]
    [DataRow(CodeLanguage.XML, "<?xml version=\"1.0\"\n?>\n<root />", "<?xml version=\"1.0\"\n?>\n")]
    [DataRow(CodeLanguage.XAML, "<?xml version=\"1.0\"?>\r\n<Page />", "<?xml version=\"1.0\"?>\r\n")]
    [DataRow(CodeLanguage.HTML, "<?xml version=\"1.0\"?>\r\n<html />", "<?xml version=\"1.0\"?>\r\n")]
    [DataRow(CodeLanguage.XML, "<root />\r\n", "")]
    [DataRow(CodeLanguage.XML, "<?xml never closed\r\n<root />", "")]
    [DataRow(CodeLanguage.XML, "#!/x\r\n<?xml version=\"1.0\"?>\r\n<root />", "#!/x\r\n<?xml version=\"1.0\"?>\r\n")]
    [DataRow(CodeLanguage.PHP, "<?php\r\n$a = 1;", "<?php\r\n")]
    [DataRow(CodeLanguage.PHP, "<?\r\n$a = 1;", "<?\r\n")]
    [DataRow(CodeLanguage.PHP, "<?php echo 1; ?>\r\n", "")]
    [DataRow(CodeLanguage.PHP, "#!/usr/bin/env php\r\n<?php\r\n$a = 1;", "#!/usr/bin/env php\r\n<?php\r\n")]
    [DataRow(CodeLanguage.R, "#!/usr/bin/env Rscript\r\nx <- 1", "#!/usr/bin/env Rscript\r\n")]
    [DataRow(CodeLanguage.R, "# comment\r\nx <- 1", "")]
    [DataRow(CodeLanguage.CSharp, "#!/usr/bin/env dotnet\r\nclass C { }", "#!/usr/bin/env dotnet\r\n")]
    [DataRow(CodeLanguage.VisualBasic, "' comment\r\nModule M", "")]
    [DataRow(CodeLanguage.JavaScript, "", "")]
    public void GetPrologLength_CoversTheLinesThatMustStayFirst(CodeLanguage language, string text, string expectedProlog)
    {
        Assert.AreEqual(expectedProlog, text.Substring(0, FileHeaderHelper.GetPrologLength(language, text)));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ClosedCSharpFile_WithAByteOrderMark_KeepsItWhenTheHeaderIsAdded()
    {
        ConfigureHeadless("// Copyright (c) Contoso", HeaderPosition.DocumentStart, HeaderUpdateMode.Insert);
        Settings.Default.Cleaning_RemoveByteOrderMark = false;

        byte[] bytes = CleanFile("Bom.cs", new UTF8Encoding(true), HeadlessBody);

        CollectionAssertStartsWith(bytes, new UTF8Encoding(true).GetPreamble());
        AssertHeaderFollowedByTheCode("// Copyright (c) Contoso", new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ClosedCSharpFile_WithAByteOrderMark_LosesItWhenRemovingByteOrderMarksIsEnabled()
    {
        ConfigureHeadless("// Copyright (c) Contoso", HeaderPosition.DocumentStart, HeaderUpdateMode.Insert);
        Settings.Default.Cleaning_RemoveByteOrderMark = true;

        byte[] bytes = CleanFile("Bom.cs", new UTF8Encoding(true), HeadlessBody);

        AssertHeaderFollowedByTheCode("// Copyright (c) Contoso", new UTF8Encoding(false).GetString(bytes));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ClosedCSharpFile_WithoutAByteOrderMark_DoesNotGetOne()
    {
        ConfigureHeadless("// Copyright © Contoso", HeaderPosition.DocumentStart, HeaderUpdateMode.Insert);
        Settings.Default.Cleaning_RemoveByteOrderMark = false;

        byte[] bytes = CleanFile("NoBom.cs", new UTF8Encoding(false), HeadlessBody);

        Assert.AreNotEqual((byte)0xEF, bytes[0]);
        AssertHeaderFollowedByTheCode("// Copyright © Contoso", new UTF8Encoding(false).GetString(bytes));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ClosedCSharpFile_EncodedAsUtf16_KeepsItsEncodingWhenTheHeaderIsAdded()
    {
        ConfigureHeadless("// Copyright (c) Contoso", HeaderPosition.DocumentStart, HeaderUpdateMode.Insert);
        Settings.Default.Cleaning_RemoveByteOrderMark = false;

        byte[] bytes = CleanFile("Utf16.cs", new UnicodeEncoding(false, true), HeadlessBody);

        CollectionAssertStartsWith(bytes, new UnicodeEncoding(false, true).GetPreamble());
        AssertHeaderFollowedByTheCode("// Copyright (c) Contoso", new UnicodeEncoding(false, true).GetString(bytes, 2, bytes.Length - 2));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ClosedCSharpFile_EncodedAsLatin1ByEditorConfig_KeepsItsEncodingWhenTheHeaderIsAdded()
    {
        WriteEditorConfig("charset = latin1");
        ConfigureHeadless("// Copyright © Contoso", HeaderPosition.DocumentStart, HeaderUpdateMode.Insert);
        Encoding latin1 = Encoding.GetEncoding(28591);

        byte[] bytes = CleanFile("Latin1.cs", latin1, "namespace Demo;\r\n\r\npublic class C { public string Cafe = \"café\"; }\r\n");

        AssertHeaderFollowedByTheCode("// Copyright © Contoso", latin1.GetString(bytes), "namespace Demo;\r\n\r\npublic class C { public string Cafe = \"café\"; }\r\n");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("\r\n")]
    [DataRow("\n")]
    public void ClosedCSharpFile_KeepsItsLineEndingsForTheHeader(string lineEnding)
    {
        ConfigureHeadless("// Copyright (c) Contoso\r\n// All rights reserved.", HeaderPosition.DocumentStart, HeaderUpdateMode.Insert);

        byte[] bytes = CleanFile("Endings.cs", new UTF8Encoding(false), HeadlessBody.Replace("\r\n", lineEnding));

        AssertHeaderFollowedByTheCode("// Copyright (c) Contoso\r\n// All rights reserved.", new UTF8Encoding(false).GetString(bytes), HeadlessBody, lineEnding);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ClosedCSharpFile_WithTheHeaderAlready_IsNotRewritten()
    {
        ConfigureHeadless("// Copyright (c) Contoso", HeaderPosition.DocumentStart, HeaderUpdateMode.Replace);
        string filePath = Path.Combine(_tempDirectory, "UpToDate.cs");
        File.WriteAllText(filePath, "// Copyright (c) Contoso\r\n\r\n" + HeadlessBody, new UTF8Encoding(false));
        DateTime lastWrite = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(filePath, lastWrite);

        CodeCleanupManager.HeadlessPreCleanupOutcome outcome = CodeCleanupManager.GetInstance(null).TryRunHeadlessPreCleanupForCSharpCore(filePath);

        Assert.AreEqual(CodeCleanupManager.HeadlessCleanupResult.NoChanges, outcome.Result);
        Assert.AreEqual(lastWrite, File.GetLastWriteTimeUtc(filePath));
    }

    private static HeaderLanguage Find(string name) => HeaderLanguages.Single(language => language.Name == name);

    /// <summary>
    /// Asserts that the text starts with the header and continues with exactly the code, ignoring the blank lines the
    /// cleanup puts between them.
    /// </summary>
    private static void AssertHeaderFollowedByTheCode(string header, string actual, string code = HeadlessBody, string lineEnding = Crlf)
    {
        string expectedHeader = header.Replace(Crlf, lineEnding) + lineEnding;

        Assert.IsTrue(actual.StartsWith(expectedHeader, StringComparison.Ordinal), actual.Replace("\r", "\\r").Replace("\n", "\\n"));
        Assert.AreEqual(code.Replace(Crlf, lineEnding), actual.Substring(expectedHeader.Length).TrimStart('\r', '\n'));
    }

    private static void CollectionAssertStartsWith(byte[] actual, byte[] expectedPrefix)
    {
        Assert.IsGreaterThanOrEqualTo(expectedPrefix.Length, actual.Length);
        Assert.AreSequenceEqual(expectedPrefix, actual.Take(expectedPrefix.Length).ToArray());
    }

    private static void Configure(HeaderLanguage language, string header, HeaderUpdateMode mode)
    {
        Settings.Default[language.Setting] = header;
        Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition = (int)HeaderPosition.DocumentStart;
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = (int)mode;
    }

    private static void ConfigureHeadless(string header, HeaderPosition position, HeaderUpdateMode mode)
    {
        Settings.Default.Cleaning_UpdateFileHeaderCSharp = header;
        Settings.Default.Cleaning_UpdateFileHeader_HeaderPosition = (int)position;
        Settings.Default.Cleaning_UpdateFileHeader_HeaderUpdateMode = (int)mode;
    }

    private string Headless(string source) => CodeCleanupManager.ApplyHeadlessCSharpTransformations(source, Path.Combine(_tempDirectory, "Sample.cs"));

    private byte[] CleanFile(string fileName, Encoding encoding, string text)
    {
        string filePath = Path.Combine(_tempDirectory, fileName);
        File.WriteAllBytes(filePath, encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray());

        CodeCleanupManager.HeadlessPreCleanupOutcome outcome = CodeCleanupManager.GetInstance(null).TryRunHeadlessPreCleanupForCSharpCore(filePath);

        Assert.AreEqual(CodeCleanupManager.HeadlessCleanupResult.Changed, outcome.Result);

        return File.ReadAllBytes(filePath);
    }

    private void WriteEditorConfig(params string[] options)
    {
        string[] lines = new[] { "root = true", string.Empty, "[*.cs]" }.Concat(options).Concat(new[] { string.Empty }).ToArray();

        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"), string.Join("\r\n", lines));
    }

    /// <summary>
    /// Runs the editor-backed file header update on an editor buffer holding the text and returns the buffer's text
    /// afterwards.
    /// </summary>
    private string UpdateInEditor(HeaderLanguage language, string text)
    {
        FakeEditorBuffer buffer = new FakeEditorBuffer(text);
        RunOnVisualStudioUIThread(() =>
        {
            FileHeaderLogic logic = (FileHeaderLogic)Activator.CreateInstance(
                typeof(FileHeaderLogic),
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                [null],
                null);

            logic.UpdateFileHeader(buffer.CreateTextDocument(language.Language), EffectiveCleanupSettings.For(Path.Combine(_tempDirectory, "Sample" + language.Extension)));
        });

        return buffer.Text;
    }

    /// <summary>
    /// Runs the test on an STA thread that the Visual Studio <see cref="ThreadHelper" /> treats as its UI thread, and
    /// restores the <see cref="ThreadHelper" /> state afterwards.
    /// </summary>
    private static void RunOnVisualStudioUIThread(Action test)
    {
        Exception failure = null;
        FieldInfo uiThreadDispatcherField = typeof(ThreadHelper).GetField("uiThreadDispatcher", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo joinableTaskContextField = typeof(ThreadHelper).GetField("_joinableTaskContextCache", BindingFlags.Static | BindingFlags.NonPublic);
        object previousUIThreadDispatcher = uiThreadDispatcherField.GetValue(null);
        object previousJoinableTaskContext = joinableTaskContextField.GetValue(null);
        Thread uiThread = new Thread(() =>
        {
            // Without Visual Studio, no thread is its UI thread: this thread becomes it for the test, with a joinable
            // task context whose main thread is this thread.
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
#pragma warning disable VSSDK005 // The test stands in for Visual Studio, which owns the ThreadHelper singleton.
            using JoinableTaskContext joinableTaskContext = new JoinableTaskContext();
#pragma warning restore VSSDK005
            try
            {
                typeof(ThreadHelper).GetMethod("SetUIThread", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                joinableTaskContextField.SetValue(null, joinableTaskContext);

                test();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                uiThreadDispatcherField.SetValue(null, previousUIThreadDispatcher);
                joinableTaskContextField.SetValue(null, previousJoinableTaskContext);
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        uiThread.IsBackground = true;
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        if (!uiThread.Join(TimeSpan.FromSeconds(60)))
        {
            Assert.Fail("The test did not complete.");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class HeaderLanguage
    {
        public HeaderLanguage(string name, string language, string extension, string setting, string header, string lineHeader, string blockHeader, string body)
        {
            Name = name;
            Language = language;
            Extension = extension;
            Setting = setting;
            Header = header;
            LineHeader = lineHeader;
            BlockHeader = blockHeader;
            Body = body;
        }

        /// <summary>Gets the display name of the language.</summary>
        public string Name { get; }

        /// <summary>Gets the language Visual Studio reports for a document of the language.</summary>
        public string Language { get; }

        public string Extension { get; }

        /// <summary>Gets the name of the setting holding the header of the language.</summary>
        public string Setting { get; }

        /// <summary>Gets a header in the comment syntax of the language.</summary>
        public string Header { get; }

        /// <summary>Gets a different two-line header (a single comment for languages with block comments only).</summary>
        public string LineHeader { get; }

        /// <summary>Gets a different multi-line header in the block comment syntax, or null for a language without one.</summary>
        public string BlockHeader { get; }

        /// <summary>Gets document text without a header.</summary>
        public string Body { get; }
    }

#pragma warning disable VSTHRD010 // The substitutes stand in for Visual Studio's COM objects, which the test calls from its own thread.
    /// <summary>
    /// An editor buffer behind the <see cref="TextDocument" /> and <see cref="EditPoint" /> members the file header
    /// update uses. Like Visual Studio's, its offsets count a line break as one character.
    /// </summary>
    private sealed class FakeEditorBuffer
    {
        public FakeEditorBuffer(string text) => Text = text;

        public string Text { get; private set; }

        private int LineCount => Text.Count(c => c == '\n') + 1;

        public TextDocument CreateTextDocument(string language)
        {
            TextPoint start = Substitute.For<TextPoint>();
            start.CreateEditPoint().Returns(_ => CreateEditPoint());
            TextPoint end = Substitute.For<TextPoint>();
            end.Line.Returns(_ => LineCount);
            TextDocument document = Substitute.For<TextDocument>();
            document.Language.Returns(language);
            document.StartPoint.Returns(start);
            document.EndPoint.Returns(end);

            return document;
        }

        private EditPoint CreateEditPoint()
        {
            int index = 0;
            EditPoint point = Substitute.For<EditPoint>();
            point.GetText(Arg.Any<object>()).Returns(call =>
            {
                int end = Advance(index, (int)call.ArgAt<object>(0));

                return Text.Substring(index, end - index);
            });
            point.GetLines(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
            {
                int from = LineStart(call.ArgAt<int>(0));
                int to = LineStart(call.ArgAt<int>(1));

                return Text.Substring(from, Math.Max(0, to - from));
            });
            point.When(p => p.MoveToLineAndOffset(Arg.Any<int>(), Arg.Any<int>()))
                .Do(call => index = Advance(LineStart(call.ArgAt<int>(0)), call.ArgAt<int>(1) - 1));
            point.When(p => p.Insert(Arg.Any<string>())).Do(call =>
            {
                string inserted = call.Arg<string>();
                Text = Text.Insert(index, inserted);
                index += inserted.Length;
            });
            point.When(p => p.ReplaceText(Arg.Any<object>(), Arg.Any<string>(), Arg.Any<int>())).Do(call =>
            {
                int end = Advance(index, (int)call.ArgAt<object>(0));
                Text = Text.Substring(0, index) + call.ArgAt<string>(1) + Text.Substring(end);
            });

            return point;
        }

        /// <summary>
        /// Gets the index of the first character of the line (one-based), or the end of the text when the buffer has
        /// fewer lines.
        /// </summary>
        private int LineStart(int line)
        {
            int index = 0;
            for (int current = 1; current < line; current++)
            {
                int lineBreak = Text.IndexOf('\n', index);
                if (lineBreak < 0)
                {
                    return Text.Length;
                }

                index = lineBreak + 1;
            }

            return index;
        }

        /// <summary>Moves the index by the number of characters, counting a CR LF pair as one character.</summary>
        private int Advance(int index, int characters)
        {
            for (int moved = 0; moved < characters && index < Text.Length; moved++)
            {
                index += Text[index] == '\r' && index + 1 < Text.Length && Text[index + 1] == '\n' ? 2 : 1;
            }

            return index;
        }
    }
}
#pragma warning restore VSTHRD010
