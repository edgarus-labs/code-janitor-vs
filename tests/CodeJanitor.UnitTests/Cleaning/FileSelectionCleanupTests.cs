using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Threading;
using CodeJanitor.Helpers;
using CodeJanitor.Integration.Commands;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using CodeJanitor.UI.Enumerations;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Threading;
using NSubstitute;
using Constants = EnvDTE.Constants;
using dbgDebugMode = EnvDTE.dbgDebugMode;
using Document = EnvDTE.Document;
using ProjectItem = EnvDTE.ProjectItem;
using ProjectItems = EnvDTE.ProjectItems;

namespace CodeJanitor.UnitTests.Cleaning;

/// <summary>
/// Behavioral tests for the decisions about whether a file is cleaned at all: the language flags, the file name
/// exclusion and inclusion expressions, generated code, files external to the solution, cleanup on save and the
/// files a git based cleanup selects.
/// </summary>
[TestClass]
public sealed class FileSelectionCleanupTests
{
    private static readonly string[] IncludeSettings =
    [
        nameof(Settings.Cleaning_IncludeCPlusPlus),
        nameof(Settings.Cleaning_IncludeCSharp),
        nameof(Settings.Cleaning_IncludeCSS),
        nameof(Settings.Cleaning_IncludeEverythingElse),
        nameof(Settings.Cleaning_IncludeFSharp),
        nameof(Settings.Cleaning_IncludeHTML),
        nameof(Settings.Cleaning_IncludeJavaScript),
        nameof(Settings.Cleaning_IncludeJSON),
        nameof(Settings.Cleaning_IncludeLESS),
        nameof(Settings.Cleaning_IncludePHP),
        nameof(Settings.Cleaning_IncludePowerShell),
        nameof(Settings.Cleaning_IncludeR),
        nameof(Settings.Cleaning_IncludeSCSS),
        nameof(Settings.Cleaning_IncludeTypeScript),
        nameof(Settings.Cleaning_IncludeVB),
        nameof(Settings.Cleaning_IncludeXAML),
        nameof(Settings.Cleaning_IncludeXML),
    ];

    private string _tempDirectory;

    [TestInitialize]
    public void TestInitialize()
    {
        Settings.Default.Reset();
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
    public void DefaultSettings_CleanEveryKnownLanguageAndNoOtherFile()
    {
        foreach (string setting in IncludeSettings.Where(name => name != nameof(Settings.Cleaning_IncludeEverythingElse)))
        {
            Assert.IsTrue((bool)Settings.Default[setting], setting);
        }

        Assert.IsFalse(Settings.Default.Cleaning_IncludeEverythingElse);
        Assert.IsFalse(Settings.Default.Cleaning_AutoCleanupOnFileSave);
        Assert.IsTrue(Settings.Default.Cleaning_AutoSaveAndCloseIfOpenedByCleanup);
        Assert.IsTrue(Settings.Default.Cleaning_ExcludeT4GeneratedCode);
        Assert.AreEqual((int)AskYesNo.Ask, Settings.Default.Cleaning_PerformPartialCleanupOnExternal);
        Assert.AreEqual(string.Empty, Settings.Default.Cleaning_InclusionExpression);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(CodeLanguage.CPlusPlus, nameof(Settings.Cleaning_IncludeCPlusPlus))]
    [DataRow(CodeLanguage.CSharp, nameof(Settings.Cleaning_IncludeCSharp))]
    [DataRow(CodeLanguage.CSS, nameof(Settings.Cleaning_IncludeCSS))]
    [DataRow(CodeLanguage.FSharp, nameof(Settings.Cleaning_IncludeFSharp))]
    [DataRow(CodeLanguage.HTML, nameof(Settings.Cleaning_IncludeHTML))]
    [DataRow(CodeLanguage.JavaScript, nameof(Settings.Cleaning_IncludeJavaScript))]
    [DataRow(CodeLanguage.JSON, nameof(Settings.Cleaning_IncludeJSON))]
    [DataRow(CodeLanguage.LESS, nameof(Settings.Cleaning_IncludeLESS))]
    [DataRow(CodeLanguage.PHP, nameof(Settings.Cleaning_IncludePHP))]
    [DataRow(CodeLanguage.PowerShell, nameof(Settings.Cleaning_IncludePowerShell))]
    [DataRow(CodeLanguage.R, nameof(Settings.Cleaning_IncludeR))]
    [DataRow(CodeLanguage.SCSS, nameof(Settings.Cleaning_IncludeSCSS))]
    [DataRow(CodeLanguage.TypeScript, nameof(Settings.Cleaning_IncludeTypeScript))]
    [DataRow(CodeLanguage.VisualBasic, nameof(Settings.Cleaning_IncludeVB))]
    [DataRow(CodeLanguage.XAML, nameof(Settings.Cleaning_IncludeXAML))]
    [DataRow(CodeLanguage.XML, nameof(Settings.Cleaning_IncludeXML))]
    [DataRow(CodeLanguage.Unknown, nameof(Settings.Cleaning_IncludeEverythingElse))]
    public void IsDocumentLanguageIncluded_OnlyTheFlagOfTheLanguageDecides(CodeLanguage language, string flag)
    {
        SetIncludeSettings(enabled: flag);
        Assert.IsTrue(CodeCleanupAvailabilityLogic.IsDocumentLanguageIncluded(".txt", language), "Only the flag of the language is on.");

        SetIncludeSettings(disabled: flag);
        Assert.IsFalse(CodeCleanupAvailabilityLogic.IsDocumentLanguageIncluded(".txt", language), "Only the flag of the language is off.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(".php")]
    [DataRow(".PHP")]
    public void IsDocumentLanguageIncluded_PhpFile_FollowsThePhpFlagWhateverLanguageVisualStudioReports(string extension)
    {
        SetIncludeSettings(enabled: nameof(Settings.Cleaning_IncludeHTML));
        Assert.IsFalse(CodeCleanupAvailabilityLogic.IsDocumentLanguageIncluded(extension, CodeLanguage.HTML), "PHP is off although the editor reports HTML.");

        SetIncludeSettings(enabled: nameof(Settings.Cleaning_IncludePHP));
        Assert.IsTrue(CodeCleanupAvailabilityLogic.IsDocumentLanguageIncluded(extension, CodeLanguage.HTML), "PHP is on although the editor reports HTML.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("Basic", CodeLanguage.VisualBasic)]
    [DataRow("CSharp", CodeLanguage.CSharp)]
    [DataRow("C/C++", CodeLanguage.CPlusPlus)]
    [DataRow("C/C++ (VisualGDB)", CodeLanguage.CPlusPlus)]
    [DataRow("CSS", CodeLanguage.CSS)]
    [DataRow("F#", CodeLanguage.FSharp)]
    [DataRow("HTML", CodeLanguage.HTML)]
    [DataRow("HTMLX", CodeLanguage.HTML)]
    [DataRow("Razor", CodeLanguage.HTML)]
    [DataRow("WebForms", CodeLanguage.HTML)]
    [DataRow("JavaScript", CodeLanguage.JavaScript)]
    [DataRow("JScript", CodeLanguage.JavaScript)]
    [DataRow("Node.js", CodeLanguage.JavaScript)]
    [DataRow("JSON", CodeLanguage.JSON)]
    [DataRow("LESS", CodeLanguage.LESS)]
    [DataRow("PHP", CodeLanguage.PHP)]
    [DataRow("PowerShell", CodeLanguage.PowerShell)]
    [DataRow("R", CodeLanguage.R)]
    [DataRow("SCSS", CodeLanguage.SCSS)]
    [DataRow("TypeScript", CodeLanguage.TypeScript)]
    [DataRow("XAML", CodeLanguage.XAML)]
    [DataRow("XML", CodeLanguage.XML)]
    [DataRow("Plain Text", CodeLanguage.Unknown)]
    [DataRow("", CodeLanguage.Unknown)]
    public void GetCodeLanguage_MapsTheLanguageVisualStudioReports(string language, CodeLanguage expected) => Assert.AreEqual(expected, CodeLanguageHelper.GetCodeLanguage(language));

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("{694DD9B6-B865-4C5B-AD85-86356E9C88DC}", nameof(Settings.Cleaning_IncludeCSharp))]
    [DataRow("{B2F072B0-ABC1-11D0-9D62-00C04FD9DFD9}", nameof(Settings.Cleaning_IncludeCPlusPlus))]
    [DataRow("{A764E898-518D-11D2-9A89-00C04F79EFC3}", nameof(Settings.Cleaning_IncludeCSS))]
    [DataRow("{BC6DD5A5-D4D6-4DAB-A00D-A51242DBAF1B}", nameof(Settings.Cleaning_IncludeFSharp))]
    [DataRow("{9BBFD173-9770-47DC-B191-651B7FF493CD}", nameof(Settings.Cleaning_IncludeHTML))]
    [DataRow("{58E975A0-F8FE-11D2-A6AE-00104BCC7269}", nameof(Settings.Cleaning_IncludeHTML))]
    [DataRow("{59E2F421-410A-4FC9-9803-1F4E79216BE8}", nameof(Settings.Cleaning_IncludeJavaScript))]
    [DataRow("{71D61D27-9011-4B17-9469-D20F798FB5C0}", nameof(Settings.Cleaning_IncludeJavaScript))]
    [DataRow("{18588C2A-9945-44AD-9894-B271BABC0582}", nameof(Settings.Cleaning_IncludeJSON))]
    [DataRow("{7B22909E-1B53-4CC7-8C2B-1F5C5039693A}", nameof(Settings.Cleaning_IncludeLESS))]
    [DataRow("{16B0638D-251A-4705-98D2-5251112C4139}", nameof(Settings.Cleaning_IncludePHP))]
    [DataRow("{1C4711F1-3766-4F84-9516-43FA4169CC36}", nameof(Settings.Cleaning_IncludePowerShell))]
    [DataRow("{29C0D8E0-C01C-412B-BEE8-7A7A253A31E6}", nameof(Settings.Cleaning_IncludeR))]
    [DataRow("{5FA499F6-2CEC-435B-BFCE-53BBE29F37F6}", nameof(Settings.Cleaning_IncludeSCSS))]
    [DataRow("{4A0DDDB5-7A95-4FBF-97CC-616D07737A77}", nameof(Settings.Cleaning_IncludeTypeScript))]
    [DataRow("{E34ACDC0-BAAE-11D0-88BF-00A0C9110049}", nameof(Settings.Cleaning_IncludeVB))]
    [DataRow("{CD53C9A1-6BC2-412B-BE36-CC715ED8DD41}", nameof(Settings.Cleaning_IncludeXAML))]
    [DataRow("{C9164055-039B-4669-832D-F257BD5554D4}", nameof(Settings.Cleaning_IncludeXAML))]
    [DataRow("{F6819A78-A205-47B5-BE1C-675B3C7F0B8E}", nameof(Settings.Cleaning_IncludeXML))]
    [DataRow("{00000000-0000-0000-0000-000000000000}", nameof(Settings.Cleaning_IncludeEverythingElse))]
    [DataRow(null, nameof(Settings.Cleaning_IncludeEverythingElse))]
    public void IsLanguageServiceIncluded_OnlyTheFlagOfTheLanguageServiceDecides(string languageService, string flag)
    {
        SetIncludeSettings(enabled: flag);
        Assert.IsTrue(CodeCleanupAvailabilityLogic.IsLanguageServiceIncluded(".txt", languageService), "Only the flag of the language is on.");
        Assert.IsTrue(CodeCleanupAvailabilityLogic.IsLanguageServiceIncluded(".txt", languageService?.ToLowerInvariant()), "The GUID is not case sensitive.");

        SetIncludeSettings(disabled: flag);
        Assert.IsFalse(CodeCleanupAvailabilityLogic.IsLanguageServiceIncluded(".txt", languageService), "Only the flag of the language is off.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(".js")]
    [DataRow(".JS")]
    public void IsLanguageServiceIncluded_JavaScriptFile_FollowsTheJavaScriptFlagWhateverLanguageServiceIsRegistered(string extension)
    {
        const string HtmlLanguageService = "{9BBFD173-9770-47DC-B191-651B7FF493CD}";

        SetIncludeSettings(enabled: nameof(Settings.Cleaning_IncludeHTML));
        Assert.IsFalse(CodeCleanupAvailabilityLogic.IsLanguageServiceIncluded(extension, HtmlLanguageService), "JavaScript is off although HTML is registered.");

        SetIncludeSettings(enabled: nameof(Settings.Cleaning_IncludeJavaScript));
        Assert.IsTrue(CodeCleanupAvailabilityLogic.IsLanguageServiceIncluded(extension, HtmlLanguageService), "JavaScript is on although HTML is registered.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ParseFilterExpression_SplitsOnDoublePipes_TrimsAndDropsEmptyEntries()
    {
        List<string> filters = CodeCleanupAvailabilityLogic.ParseFilterExpression(@"  \.a$ ||||  ||\.b$|\.c$||   ");

        Assert.AreSequenceEqual(new[] { @"\.a$", @"\.b$|\.c$" }, filters);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ParseFilterExpression_KeepsTheCaseOfTheRegularExpressions()
    {
        List<string> filters = CodeCleanupAvailabilityLogic.ParseFilterExpression(@"\S+\.Generated\.cs$||\D+\.cs$");

        Assert.AreSequenceEqual(new[] { @"\S+\.Generated\.cs$", @"\D+\.cs$" }, filters);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(@"C:\repo\Form1.Designer.cs")]
    [DataRow(@"C:\repo\Form1.Designer.vb")]
    [DataRow(@"C:\repo\Model.g.cs")]
    [DataRow(@"C:\repo\Strings.resx")]
    [DataRow(@"C:\repo\wwwroot\site.min.css")]
    [DataRow(@"C:\repo\wwwroot\app.min.js")]
    [DataRow(@"C:\REPO\FORM1.DESIGNER.CS")]
    public void DefaultExclusionExpression_ExcludesGeneratedAndMinifiedFiles(string filename) => Assert.IsTrue(CodeCleanupAvailabilityLogic.IsFileNameExcluded(filename, DefaultExclusions()));

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(@"C:\repo\Program.cs")]
    [DataRow(@"C:\repo\Designer.cs")]
    [DataRow(@"C:\repo\Form1.Designer.cs.bak")]
    [DataRow(@"C:\repo\Model.generated.cs")]
    [DataRow(@"C:\repo\Page.g.cshtml")]
    [DataRow(@"C:\repo\wwwroot\site.css")]
    [DataRow(@"C:\repo\wwwroot\app.js")]
    [DataRow(@"C:\repo\Resources.resx.cs")]
    public void DefaultExclusionExpression_DoesNotExcludeOtherFiles(string filename) => Assert.IsFalse(CodeCleanupAvailabilityLogic.IsFileNameExcluded(filename, DefaultExclusions()));

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void IsFileNameExcluded_MatchesAnywhereInThePathIgnoringCase()
    {
        string[] exclusions = [@"\\Generated\\", @"^c:\\tools\\"];

        Assert.IsTrue(CodeCleanupAvailabilityLogic.IsFileNameExcluded(@"D:\repo\GENERATED\A.cs", exclusions));
        Assert.IsTrue(CodeCleanupAvailabilityLogic.IsFileNameExcluded(@"C:\Tools\B.cs", exclusions));
        Assert.IsFalse(CodeCleanupAvailabilityLogic.IsFileNameExcluded(@"D:\repo\Tools\C.cs", exclusions));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void IsFileNameExcluded_ExpressionOfBothPathSeparators_MatchesBackslashAndForwardSlashPaths()
    {
        string[] exclusions = [@"[\\/]generated[\\/]"];

        Assert.IsTrue(CodeCleanupAvailabilityLogic.IsFileNameExcluded(@"D:\repo\generated\A.cs", exclusions));
        Assert.IsTrue(CodeCleanupAvailabilityLogic.IsFileNameExcluded("D:/repo/generated/A.cs", exclusions));
        Assert.IsFalse(CodeCleanupAvailabilityLogic.IsFileNameExcluded(@"D:\repo\generatedcode\A.cs", exclusions));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void IsFileNameExcluded_ExpressionWithUpperCaseEscapes_KeepsTheirMeaning()
    {
        List<string> exclusions = CodeCleanupAvailabilityLogic.ParseFilterExpression(@"\S+\.Generated\.cs$");

        Assert.IsTrue(CodeCleanupAvailabilityLogic.IsFileNameExcluded(@"C:\repo\Model.Generated.cs", exclusions), "\\S matches the characters of the name.");
        Assert.IsFalse(CodeCleanupAvailabilityLogic.IsFileNameExcluded(@"C:\repo\Model.Generated.cs", [@"\s+\.Generated\.cs$"]), "Control: \\s only matches white space.");
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(null)]
    [DataRow("")]
    public void IsFileNameExcluded_WithoutAFileName_ExcludesNothing(string filename) => Assert.IsFalse(CodeCleanupAvailabilityLogic.IsFileNameExcluded(filename, [".*"]));

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void IsFileNameExcluded_WithoutExpressions_ExcludesNothing()
    {
        Assert.IsFalse(CodeCleanupAvailabilityLogic.IsFileNameExcluded(@"C:\repo\A.cs", []));
        Assert.IsFalse(CodeCleanupAvailabilityLogic.IsFileNameExcluded(@"C:\repo\A.cs", null));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void IsFileNameExcluded_InvalidExpression_IsIgnoredAndTheOtherExpressionsStillApply()
    {
        string[] exclusions = ["([unclosed", @"\.skip\.cs$"];

        Assert.IsTrue(CodeCleanupAvailabilityLogic.IsFileNameExcluded(@"C:\repo\A.skip.cs", exclusions));
        Assert.IsFalse(CodeCleanupAvailabilityLogic.IsFileNameExcluded(@"C:\repo\A.cs", exclusions));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void IsFileNameIncluded_WithoutExpressions_IncludesEveryFile()
    {
        Assert.IsTrue(CodeCleanupAvailabilityLogic.IsFileNameIncluded(@"C:\repo\A.cs", []));
        Assert.IsTrue(CodeCleanupAvailabilityLogic.IsFileNameIncluded(@"C:\repo\A.cs", null));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void IsFileNameIncluded_WithExpressions_IncludesOnlyMatchingFilesIgnoringCase()
    {
        string[] inclusions = [@"\\src\\", @"\.Special\.cs$"];

        Assert.IsTrue(CodeCleanupAvailabilityLogic.IsFileNameIncluded(@"C:\repo\SRC\A.cs", inclusions));
        Assert.IsTrue(CodeCleanupAvailabilityLogic.IsFileNameIncluded(@"C:\other\B.special.CS", inclusions));
        Assert.IsFalse(CodeCleanupAvailabilityLogic.IsFileNameIncluded(@"C:\repo\tests\C.cs", inclusions));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(null)]
    [DataRow("")]
    public void IsFileNameIncluded_WithoutAFileName_IncludesNothing(string filename) => Assert.IsFalse(CodeCleanupAvailabilityLogic.IsFileNameIncluded(filename, []));

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void IsFileNameIncluded_InvalidExpression_IsIgnoredAndTheOtherExpressionsStillApply()
    {
        string[] inclusions = ["(unclosed", @"\\src\\"];

        Assert.IsTrue(CodeCleanupAvailabilityLogic.IsFileNameIncluded(@"C:\repo\src\A.cs", inclusions));
        Assert.IsFalse(CodeCleanupAvailabilityLogic.IsFileNameIncluded(@"C:\repo\tests\A.cs", inclusions));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(AskYesNo.Ask, true, true, false, true, DisplayName = "ask, prompts allowed, user accepts")]
    [DataRow(AskYesNo.Ask, true, false, true, true, DisplayName = "ask, prompts allowed, user declines")]
    [DataRow(AskYesNo.Ask, false, true, false, false, DisplayName = "ask, prompts not allowed: decision is deferred")]
    [DataRow(AskYesNo.Yes, true, false, false, false, DisplayName = "yes: cleaned without asking")]
    [DataRow(AskYesNo.Yes, false, false, false, false, DisplayName = "yes, prompts not allowed")]
    [DataRow(AskYesNo.No, true, true, true, false, DisplayName = "no: excluded without asking")]
    [DataRow(AskYesNo.No, false, true, true, false, DisplayName = "no, prompts not allowed")]
    public void IsExternalDocumentExcluded_FollowsThePreference(AskYesNo preference, bool allowUserPrompts, bool userAccepts, bool expectedExcluded, bool expectedPrompted)
    {
        bool prompted = false;

        bool excluded = CodeCleanupAvailabilityLogic.IsExternalDocumentExcluded(preference, allowUserPrompts, () =>
        {
            prompted = true;

            return userAccepts;
        });

        Assert.AreEqual(expectedExcluded, excluded);
        Assert.AreEqual(expectedPrompted, prompted);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(".tt", true, true)]
    [DataRow(".TT", true, true)]
    [DataRow(".tt", false, false)]
    [DataRow(".cs", true, false)]
    [DataRow("", true, false)]
    public void IsCodeGeneratorExcluded_OnlyT4TemplatesAreCodeGenerators(string parentExtension, bool excludeT4, bool expected)
    {
        Settings.Default.Cleaning_ExcludeT4GeneratedCode = excludeT4;

        Assert.AreEqual(expected, CodeCleanupAvailabilityLogic.IsCodeGeneratorExcluded(parentExtension));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CanCleanupDocument_RegularCSharpDocumentOfTheSolution_CanBeCleaned() => RunOnVisualStudioUIThread(() =>
                                                                                             {
                                                                                                 CodeCleanupAvailabilityLogic logic = CreateLogic();

                                                                                                 Assert.IsTrue(logic.CanCleanupDocument(CreateDocument(@"C:\repo\A.cs", "CSharp")));
                                                                                             });

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CanCleanup_NothingToClean_IsRefused() => RunOnVisualStudioUIThread(() =>
                                                              {
                                                                  CodeCleanupAvailabilityLogic logic = CreateLogic();

                                                                  Assert.IsFalse(logic.CanCleanupDocument(null));
                                                                  Assert.IsFalse(logic.CanCleanupProjectItem(null));
                                                              });

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(dbgDebugMode.dbgBreakMode)]
    [DataRow(dbgDebugMode.dbgRunMode)]
    public void CanCleanup_WhileDebugging_IsRefused(dbgDebugMode mode) => RunOnVisualStudioUIThread(() =>
                                                                               {
                                                                                   CodeCleanupAvailabilityLogic logic = CreateLogic(mode);

                                                                                   Assert.IsFalse(logic.IsCleanupEnvironmentAvailable());
                                                                                   Assert.IsFalse(logic.CanCleanupDocument(CreateDocument(@"C:\repo\A.cs", "CSharp")));
                                                                                   Assert.IsFalse(logic.CanCleanupProjectItem(CreateProjectItem(@"C:\repo\A.js")));
                                                                               });

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void IsCleanupEnvironmentAvailable_InDesignMode_IsTrue() => RunOnVisualStudioUIThread(() => Assert.IsTrue(CreateLogic().IsCleanupEnvironmentAvailable()));

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("CSharp", @"C:\repo\A.cs", nameof(Settings.Cleaning_IncludeCSharp))]
    [DataRow("Basic", @"C:\repo\A.vb", nameof(Settings.Cleaning_IncludeVB))]
    [DataRow("C/C++", @"C:\repo\a.cpp", nameof(Settings.Cleaning_IncludeCPlusPlus))]
    [DataRow("CSS", @"C:\repo\a.css", nameof(Settings.Cleaning_IncludeCSS))]
    [DataRow("F#", @"C:\repo\a.fs", nameof(Settings.Cleaning_IncludeFSharp))]
    [DataRow("HTML", @"C:\repo\a.html", nameof(Settings.Cleaning_IncludeHTML))]
    [DataRow("HTMLX", @"C:\repo\a.cshtml", nameof(Settings.Cleaning_IncludeHTML))]
    [DataRow("Razor", @"C:\repo\a.razor", nameof(Settings.Cleaning_IncludeHTML))]
    [DataRow("WebForms", @"C:\repo\a.aspx", nameof(Settings.Cleaning_IncludeHTML))]
    [DataRow("JavaScript", @"C:\repo\a.js", nameof(Settings.Cleaning_IncludeJavaScript))]
    [DataRow("Node.js", @"C:\repo\a.mjs", nameof(Settings.Cleaning_IncludeJavaScript))]
    [DataRow("JSON", @"C:\repo\a.json", nameof(Settings.Cleaning_IncludeJSON))]
    [DataRow("LESS", @"C:\repo\a.less", nameof(Settings.Cleaning_IncludeLESS))]
    [DataRow("PHP", @"C:\repo\a.php", nameof(Settings.Cleaning_IncludePHP))]
    [DataRow("HTML", @"C:\repo\a.php", nameof(Settings.Cleaning_IncludePHP))]
    [DataRow("PowerShell", @"C:\repo\a.ps1", nameof(Settings.Cleaning_IncludePowerShell))]
    [DataRow("R", @"C:\repo\a.r", nameof(Settings.Cleaning_IncludeR))]
    [DataRow("SCSS", @"C:\repo\a.scss", nameof(Settings.Cleaning_IncludeSCSS))]
    [DataRow("TypeScript", @"C:\repo\a.ts", nameof(Settings.Cleaning_IncludeTypeScript))]
    [DataRow("XAML", @"C:\repo\a.xaml", nameof(Settings.Cleaning_IncludeXAML))]
    [DataRow("XML", @"C:\repo\a.xml", nameof(Settings.Cleaning_IncludeXML))]
    [DataRow("Plain Text", @"C:\repo\a.txt", nameof(Settings.Cleaning_IncludeEverythingElse))]
    public void CanCleanupDocument_FollowsTheLanguageFlagOfTheDocument(string language, string filePath, string flag) => RunOnVisualStudioUIThread(() =>
                                                                                                                              {
                                                                                                                                  CodeCleanupAvailabilityLogic logic = CreateLogic();

                                                                                                                                  SetIncludeSettings(enabled: flag);
                                                                                                                                  Assert.IsTrue(logic.CanCleanupDocument(CreateDocument(filePath, language)), "Only the flag of the language is on.");

                                                                                                                                  SetIncludeSettings(disabled: flag);
                                                                                                                                  Assert.IsFalse(logic.CanCleanupDocument(CreateDocument(filePath, language)), "Only the flag of the language is off.");
                                                                                                                              });

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CanCleanupProjectItem_JavaScriptFile_FollowsTheJavaScriptFlag() => RunOnVisualStudioUIThread(() =>
                                                                                        {
                                                                                            CodeCleanupAvailabilityLogic logic = CreateLogic();

                                                                                            Assert.IsTrue(logic.CanCleanupProjectItem(CreateProjectItem(@"C:\repo\a.js")));

                                                                                            Settings.Default.Cleaning_IncludeJavaScript = false;
                                                                                            Assert.IsFalse(logic.CanCleanupProjectItem(CreateProjectItem(@"C:\repo\a.js")));
                                                                                        });

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CanCleanupProjectItem_FileOfAnUnknownLanguage_FollowsTheEverythingElseFlag() => RunOnVisualStudioUIThread(() =>
                                                                                                     {
                                                                                                         CodeCleanupAvailabilityLogic logic = CreateLogic();

                                                                                                         Assert.IsFalse(logic.CanCleanupProjectItem(CreateProjectItem(@"C:\repo\a.unknownextension")));

                                                                                                         Settings.Default.Cleaning_IncludeEverythingElse = true;
                                                                                                         Assert.IsTrue(logic.CanCleanupProjectItem(CreateProjectItem(@"C:\repo\a.unknownextension")));
                                                                                                     });

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CanCleanupProjectItem_ItemThatIsNotAPhysicalFile_IsRefused() => RunOnVisualStudioUIThread(() =>
                                                                                     Assert.IsFalse(CreateLogic().CanCleanupProjectItem(CreateProjectItem(@"C:\repo\a.js", physicalFile: false))));

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CanCleanup_FileMatchingTheExclusionExpression_IsRefused() => RunOnVisualStudioUIThread(() =>
                                                                                  {
                                                                                      CodeCleanupAvailabilityLogic logic = CreateLogic();

                                                                                      Assert.IsFalse(logic.CanCleanupDocument(CreateDocument(@"C:\repo\Form1.Designer.cs", "CSharp")));
                                                                                      Assert.IsFalse(logic.CanCleanupProjectItem(CreateProjectItem(@"C:\repo\app.min.js")));
                                                                                      Assert.IsTrue(logic.CanCleanupDocument(CreateDocument(@"C:\repo\Form1.cs", "CSharp")));
                                                                                      Assert.IsTrue(logic.CanCleanupProjectItem(CreateProjectItem(@"C:\repo\app.js")));
                                                                                  });

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CanCleanup_ChangedExclusionExpression_AppliesToTheNextDecision() => RunOnVisualStudioUIThread(() =>
                                                                                         {
                                                                                             CodeCleanupAvailabilityLogic logic = CreateLogic();
                                                                                             Assert.IsTrue(logic.CanCleanupDocument(CreateDocument(@"C:\repo\Legacy\A.cs", "CSharp")));
                                                                                             Assert.IsTrue(logic.CanCleanupProjectItem(CreateProjectItem(@"C:\repo\Legacy\a.js")));

                                                                                             Settings.Default.Cleaning_ExclusionExpression = @"\\Legacy\\";

                                                                                             Assert.IsFalse(logic.CanCleanupDocument(CreateDocument(@"C:\repo\Legacy\A.cs", "CSharp")));
                                                                                             Assert.IsFalse(logic.CanCleanupProjectItem(CreateProjectItem(@"C:\repo\Legacy\a.js")));
                                                                                             Assert.IsTrue(logic.CanCleanupDocument(CreateDocument(@"C:\repo\Form1.Designer.cs", "CSharp")), "The default expressions were replaced.");
                                                                                         });

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CanCleanup_InvalidExclusionExpression_DoesNotStopTheCleanup() => RunOnVisualStudioUIThread(() =>
                                                                                      {
                                                                                          CodeCleanupAvailabilityLogic logic = CreateLogic();
                                                                                          Settings.Default.Cleaning_ExclusionExpression = @"([unclosed||\.skip\.cs$";

                                                                                          Assert.IsTrue(logic.CanCleanupDocument(CreateDocument(@"C:\repo\A.cs", "CSharp")));
                                                                                          Assert.IsFalse(logic.CanCleanupDocument(CreateDocument(@"C:\repo\A.skip.cs", "CSharp")));
                                                                                          Assert.IsTrue(logic.CanCleanupProjectItem(CreateProjectItem(@"C:\repo\a.js")));
                                                                                      });

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CanCleanup_WithAnInclusionExpression_OnlyMatchingFilesAreCleaned() => RunOnVisualStudioUIThread(() =>
                                                                                           {
                                                                                               CodeCleanupAvailabilityLogic logic = CreateLogic();
                                                                                               Settings.Default.Cleaning_InclusionExpression = @"\\src\\||\\lib\\";

                                                                                               Assert.IsTrue(logic.CanCleanupDocument(CreateDocument(@"C:\repo\SRC\A.cs", "CSharp")));
                                                                                               Assert.IsTrue(logic.CanCleanupDocument(CreateDocument(@"C:\repo\lib\A.cs", "CSharp")));
                                                                                               Assert.IsFalse(logic.CanCleanupDocument(CreateDocument(@"C:\repo\tests\A.cs", "CSharp")));
                                                                                               Assert.IsTrue(logic.CanCleanupProjectItem(CreateProjectItem(@"C:\repo\SRC\a.js")));
                                                                                               Assert.IsFalse(logic.CanCleanupProjectItem(CreateProjectItem(@"C:\repo\tests\a.js")));
                                                                                           });

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CanCleanup_FileMatchingBothExpressions_IsExcluded() => RunOnVisualStudioUIThread(() =>
                                                                            {
                                                                                CodeCleanupAvailabilityLogic logic = CreateLogic();
                                                                                Settings.Default.Cleaning_InclusionExpression = @"\\src\\";
                                                                                Settings.Default.Cleaning_ExclusionExpression = @"\.Designer\.cs$||\.min\.js$";

                                                                                Assert.IsFalse(logic.CanCleanupDocument(CreateDocument(@"C:\repo\src\Form1.Designer.cs", "CSharp")));
                                                                                Assert.IsFalse(logic.CanCleanupProjectItem(CreateProjectItem(@"C:\repo\src\app.min.js")));
                                                                                Assert.IsTrue(logic.CanCleanupDocument(CreateDocument(@"C:\repo\src\Form1.cs", "CSharp")));
                                                                            });

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(AskYesNo.Yes, true)]
    [DataRow(AskYesNo.No, false)]
    [DataRow(AskYesNo.Ask, true)]
    public void CanCleanupDocument_DocumentExternalToTheSolution_FollowsThePartialCleanupPreference(AskYesNo preference, bool expected) => RunOnVisualStudioUIThread(() =>
                                                                                                                                                {
                                                                                                                                                    CodeCleanupAvailabilityLogic logic = CreateLogic();
                                                                                                                                                    Settings.Default.Cleaning_PerformPartialCleanupOnExternal = (int)preference;
                                                                                                                                                    Document external = CreateDocument(@"C:\elsewhere\A.cs", "CSharp", openedOutsideAnyProject: true);

                                                                                                                                                    Assert.AreEqual(expected, logic.CanCleanupDocument(external, allowUserPrompts: false));
                                                                                                                                                });

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("CSharp", false, true, DisplayName = "C# document of the solution")]
    [DataRow("CSharp", true, false, DisplayName = "C# document outside the solution: the Roslyn workspace does not contain it")]
    [DataRow("Basic", false, false, DisplayName = "document that is not C#")]
    public void RunsDiagnosticCleanup_NeedsACSharpDocumentOfTheSolution(string language, bool outsideAnyProject, bool expected) => RunOnVisualStudioUIThread(() =>
                                                                                                                                        {
                                                                                                                                            Document document = CreateDocument(@"C:\repo\A.cs", language, openedOutsideAnyProject: outsideAnyProject);

                                                                                                                                            Assert.AreEqual(expected, CodeCleanupManager.RunsDiagnosticCleanup(document));
                                                                                                                                        });

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CanCleanupDocument_ItemMissingFromItsCollection_IsExternalToTheSolution() => RunOnVisualStudioUIThread(() =>
                                                                                                  {
                                                                                                      CodeCleanupAvailabilityLogic logic = CreateLogic();
                                                                                                      Settings.Default.Cleaning_PerformPartialCleanupOnExternal = (int)AskYesNo.No;

#pragma warning disable VSTHRD010 // The substitutes stand in for Visual Studio's COM objects, which the test calls from its own thread.
                                                                                                      ProjectItem notInItsCollection = CreateProjectItem(@"C:\repo\A.cs");
                                                                                                      SetItems(notInItsCollection.Collection, CreateProjectItem(@"C:\repo\Other.cs"));
#pragma warning restore VSTHRD010

                                                                                                      Assert.IsFalse(logic.CanCleanupDocument(CreateDocument(@"C:\repo\A.cs", "CSharp", notInItsCollection)), "The item is not part of its collection.");
                                                                                                      Assert.IsTrue(logic.CanCleanupDocument(CreateDocument(@"C:\repo\A.cs", "CSharp", CreateProjectItem(@"C:\repo\A.cs"))), "The item is part of the solution.");
                                                                                                  });

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void CanCleanup_FileGeneratedByAT4Template_FollowsTheExcludeT4Setting(bool excludeT4, bool expected) => RunOnVisualStudioUIThread(() =>
                                                                                                                        {
                                                                                                                            CodeCleanupAvailabilityLogic logic = CreateLogic();
                                                                                                                            Settings.Default.Cleaning_ExcludeT4GeneratedCode = excludeT4;
                                                                                                                            Settings.Default.Cleaning_IncludeEverythingElse = true;

                                                                                                                            ProjectItem template = CreateProjectItem(@"C:\repo\Model.tt");

                                                                                                                            Assert.AreEqual(expected, logic.CanCleanupDocument(CreateDocument(@"C:\repo\Model.cs", "CSharp", CreateProjectItem(@"C:\repo\Model.cs", parent: template))));
                                                                                                                            Assert.AreEqual(expected, logic.CanCleanupProjectItem(CreateProjectItem(@"C:\repo\Model.js", parent: template)));
                                                                                                                        });

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CanCleanup_FileNestedUnderAnotherFile_IsNotConsideredGeneratedCode() => RunOnVisualStudioUIThread(() =>
                                                                                             {
                                                                                                 CodeCleanupAvailabilityLogic logic = CreateLogic();
                                                                                                 ProjectItem parent = CreateProjectItem(@"C:\repo\Form1.cs");

                                                                                                 Assert.IsTrue(logic.CanCleanupDocument(CreateDocument(@"C:\repo\Form1.Layout.cs", "CSharp", CreateProjectItem(@"C:\repo\Form1.Layout.cs", parent: parent))));
                                                                                                 Assert.IsTrue(logic.CanCleanupProjectItem(CreateProjectItem(@"C:\repo\Form1.Layout.js", parent: parent)));
                                                                                             });

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ShouldCleanupOnSave_WithCleanupOnSaveDisabled_NeverTouchesTheDocument() => RunOnVisualStudioUIThread(() =>
                                                                                                {
                                                                                                    CleanupActiveCodeCommand command = CreateCleanupActiveCodeCommand();
                                                                                                    Document document = CreateDocument(@"C:\repo\A.cs", "CSharp");
                                                                                                    document.ClearReceivedCalls();
                                                                                                    Settings.Default.Cleaning_AutoCleanupOnFileSave = false;

                                                                                                    Assert.IsFalse(command.ShouldCleanupOnSave(document));
                                                                                                    Assert.IsEmpty(document.ReceivedCalls());
                                                                                                });

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ShouldCleanupOnSave_WithCleanupOnSaveEnabled_CleansDocumentsThatCanBeCleaned() => RunOnVisualStudioUIThread(() =>
                                                                                                       {
                                                                                                           CleanupActiveCodeCommand command = CreateCleanupActiveCodeCommand();
                                                                                                           Settings.Default.Cleaning_AutoCleanupOnFileSave = true;

                                                                                                           Assert.IsTrue(command.ShouldCleanupOnSave(CreateDocument(@"C:\repo\A.cs", "CSharp")));
                                                                                                           Assert.IsFalse(command.ShouldCleanupOnSave(CreateDocument(@"C:\repo\Form1.Designer.cs", "CSharp")), "Excluded file name.");

                                                                                                           Settings.Default.Cleaning_IncludeCSharp = false;
                                                                                                           Assert.IsFalse(command.ShouldCleanupOnSave(CreateDocument(@"C:\repo\A.cs", "CSharp")), "Language not enabled.");
                                                                                                       });

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow("// <auto-generated>\r\n//     This code was generated by a tool.\r\n// </auto-generated>\r\nvar a = 1;\r\n", DisplayName = "generated header")]
    [DataRow("//  <AUTO-GENERATED />\r\nvar a = 1;\r\n", DisplayName = "other case")]
    [DataRow("var a = 1;\r\n\r\n// <auto-generated>\r\nvar b = 2;\r\n", DisplayName = "marker below the first lines")]
    public void CanCleanupProjectItem_FileWithAnAutoGeneratedHeader_IsRefused(string content)
    {
        string filePath = Path.Combine(_tempDirectory, "Reference.js");
        File.WriteAllText(filePath, content);

        RunOnVisualStudioUIThread(() => Assert.IsFalse(CreateLogic().CanCleanupProjectItem(CreateProjectItem(filePath))));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CanCleanupProjectItem_FileWithoutAnAutoGeneratedHeaderOrThatCannotBeRead_CanBeCleaned()
    {
        string filePath = Path.Combine(_tempDirectory, "Plain.js");
        File.WriteAllText(filePath, "// A hand written file.\r\nvar a = 1;\r\n");

        RunOnVisualStudioUIThread(() =>
        {
            CodeCleanupAvailabilityLogic logic = CreateLogic();

            Assert.IsTrue(logic.CanCleanupProjectItem(CreateProjectItem(filePath)), "Hand written file.");
            Assert.IsTrue(logic.CanCleanupProjectItem(CreateProjectItem(Path.Combine(_tempDirectory, "Missing.js"))), "File that does not exist.");
        });
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(true, false, true, DisplayName = "enabled, opened by the cleanup")]
    [DataRow(true, true, false, DisplayName = "enabled, already open")]
    [DataRow(false, false, false, DisplayName = "disabled, opened by the cleanup")]
    [DataRow(false, true, false, DisplayName = "disabled, already open")]
    public void ShouldCloseDocumentAfterCleanup_ClosesOnlyDocumentsOpenedByTheCleanup_WhenEnabled(bool enabled, bool wasOpen, bool expected)
    {
        Settings.Default.Cleaning_AutoSaveAndCloseIfOpenedByCleanup = enabled;

        Assert.AreEqual(expected, CodeCleanupManager.ShouldCloseDocumentAfterCleanup(wasOpen));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void IsChangedFile_MatchesTheFilesOfTheItemIgnoringCase()
    {
        HashSet<string> changedFiles = new HashSet<string>([@"C:\repo\src\A.cs"], StringComparer.OrdinalIgnoreCase);

        Assert.IsTrue(CleanupChangedFilesCommand.IsChangedFile(CreateProjectItem(@"C:\REPO\SRC\a.cs"), changedFiles));
        Assert.IsFalse(CleanupChangedFilesCommand.IsChangedFile(CreateProjectItem(@"C:\repo\src\B.cs"), changedFiles));
    }

#pragma warning disable VSTHRD010 // The substitutes stand in for Visual Studio's COM objects, which the test calls from its own thread.

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void IsChangedFile_ItemOfSeveralFiles_IsChangedWhenAnyFileChanged()
    {
        HashSet<string> changedFiles = new HashSet<string>([@"C:\repo\Page.xaml.cs"], StringComparer.OrdinalIgnoreCase);
        ProjectItem item = Substitute.For<ProjectItem>();
        item.FileCount.Returns((short)2);
        item.FileNames[1].Returns(@"C:\repo\Page.xaml");
        item.FileNames[2].Returns(@"C:\repo\Page.xaml.cs");

        Assert.IsTrue(CleanupChangedFilesCommand.IsChangedFile(item, changedFiles));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void IsChangedFile_ItemWithoutFileNames_IsNotChanged()
    {
        HashSet<string> changedFiles = new HashSet<string>([string.Empty, @"C:\repo\A.cs"], StringComparer.OrdinalIgnoreCase);
        ProjectItem item = Substitute.For<ProjectItem>();
        item.FileCount.Returns((short)1);
        item.FileNames[1].Returns(string.Empty);

        Assert.IsFalse(CleanupChangedFilesCommand.IsChangedFile(item, changedFiles));

        item.FileCount.Returns((short)0);

        Assert.IsFalse(CleanupChangedFilesCommand.IsChangedFile(item, changedFiles));
    }

    private static List<string> DefaultExclusions() => CodeCleanupAvailabilityLogic.ParseFilterExpression(Settings.Default.Cleaning_ExclusionExpression);

    private static void SetIncludeSettings(string enabled = null, string disabled = null)
    {
        foreach (string setting in IncludeSettings)
        {
            Settings.Default[setting] = enabled is not null ? setting == enabled : setting != disabled;
        }
    }

    private static CodeCleanupAvailabilityLogic CreateLogic(dbgDebugMode mode = dbgDebugMode.dbgDesignMode)
    {
        DTE2 ide = Substitute.For<DTE2>();
        ide.Debugger.CurrentMode.Returns(mode);

        return (CodeCleanupAvailabilityLogic)Activator.CreateInstance(
            typeof(CodeCleanupAvailabilityLogic),
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [CreatePackage(ide)],
            null);
    }

    /// <summary>
    /// Creates a command that does not run Visual Studio's command infrastructure: only the cleanup availability is
    /// wired.
    /// </summary>
    private static CleanupActiveCodeCommand CreateCleanupActiveCodeCommand()
    {
        CleanupActiveCodeCommand command = (CleanupActiveCodeCommand)FormatterServices.GetUninitializedObject(typeof(CleanupActiveCodeCommand));
        typeof(CleanupActiveCodeCommand)
            .GetField("<CodeCleanupAvailabilityLogic>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(command, CreateLogic());

        return command;
    }

    private static CodeJanitorPackage CreatePackage(DTE2 ide)
    {
        CodeJanitorPackage package = (CodeJanitorPackage)FormatterServices.GetUninitializedObject(typeof(CodeJanitorPackage));
        typeof(CodeJanitorPackage).GetField("_ide", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(package, ide);

        return package;
    }

    private static Document CreateDocument(string filePath, string language, ProjectItem projectItem = null, bool openedOutsideAnyProject = false)
    {
        Document document = Substitute.For<Document>();
        document.FullName.Returns(filePath);
        document.Name.Returns(Path.GetFileName(filePath));
        document.Language.Returns(language);
        ProjectItem documentItem = openedOutsideAnyProject ? null : projectItem ?? CreateProjectItem(filePath);
        document.ProjectItem.Returns(documentItem);

        return document;
    }

    private static ProjectItem CreateProjectItem(string filePath, ProjectItem parent = null, bool physicalFile = true)
    {
        ProjectItem item = Substitute.For<ProjectItem>();
        item.Name.Returns(Path.GetFileName(filePath));
        item.FileCount.Returns((short)1);
        item.FileNames[1].Returns(filePath);
        item.Kind.Returns(physicalFile ? Constants.vsProjectItemKindPhysicalFile : Constants.vsProjectItemKindVirtualFolder);
        item.Object.Returns(new object());

        ProjectItems collection = Substitute.For<ProjectItems>();
        collection.Parent.Returns(parent);
        SetItems(collection, item);
        item.Collection.Returns(collection);

        return item;
    }

#pragma warning restore VSTHRD010

    /// <summary>
    /// Makes the collection enumerate the items, as Visual Studio's <c>ProjectItems</c> do through the non-generic
    /// <see cref="System.Collections.IEnumerable" />.
    /// </summary>
    private static void SetItems(ProjectItems collection, params ProjectItem[] items)
    {
        System.Collections.IEnumerable enumerable = collection;
        enumerable.GetEnumerator().Returns(_ => items.GetEnumerator());
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
}
