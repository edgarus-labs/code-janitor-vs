using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
[TestCategory("Cleaning UnitTests")]
public sealed class NonCSharpCleanupTests
{
    private const string Crlf = "\r\n";

    public enum Routine
    {
        CFamily,
        Markup,
        Generic,
        VisualBasic,
    }

    private sealed class Probe
    {
        public Probe(string step, string text, string cleaned, Routine[] runBy)
        {
            Step = step;
            Text = text;
            Cleaned = cleaned;
            RunBy = runBy;
        }

        public string Step { get; }

        public string Text { get; }

        public string Cleaned { get; }

        public Routine[] RunBy { get; }
    }

    private static readonly Routine[] All = { Routine.CFamily, Routine.Markup, Routine.Generic, Routine.VisualBasic };

    private static readonly Probe[] Probes =
    {
        new Probe("end of line whitespace", "a  \t" + Crlf + "b" + Crlf, "a" + Crlf + "b" + Crlf, All),
        new Probe("multiple consecutive blank lines", "a" + Crlf + Crlf + Crlf + Crlf + "b" + Crlf, "a" + Crlf + Crlf + "b" + Crlf, All),
        new Probe("final newline", "a", "a" + Crlf, All),
        new Probe("blank lines after opening brace", "{" + Crlf + Crlf + "x;" + Crlf + "}" + Crlf, "{" + Crlf + "x;" + Crlf + "}" + Crlf, new[] { Routine.CFamily }),
        new Probe("blank lines before closing brace", "{" + Crlf + "x;" + Crlf + Crlf + "}" + Crlf, "{" + Crlf + "x;" + Crlf + "}" + Crlf, new[] { Routine.CFamily }),
        new Probe("blank line padding before single-line comments", "x;" + Crlf + "// c" + Crlf, "x;" + Crlf + Crlf + "// c" + Crlf, new[] { Routine.CFamily, Routine.VisualBasic }),
        new Probe("blank lines before closing tags", "<a>" + Crlf + "x" + Crlf + Crlf + "</a>" + Crlf, "<a>" + Crlf + "x" + Crlf + "</a>" + Crlf, new[] { Routine.Markup }),
        new Probe("blank spaces before closing angle brackets", "<a" + Crlf + "  b=\"1\"" + Crlf + "  >" + Crlf, "<a" + Crlf + "  b=\"1\">" + Crlf, new[] { Routine.Markup }),
        new Probe("blank space before self-closing angle brackets", "<b/>" + Crlf, "<b />" + Crlf, new[] { Routine.Markup }),
        new Probe("blank lines after attributes", "[A]" + Crlf + Crlf + "x;" + Crlf, "[A]" + Crlf + "x;" + Crlf, new[] { Routine.VisualBasic }),
        new Probe("blank lines after C# documentation comments", "/// d" + Crlf + Crlf + "x;" + Crlf, "/// d" + Crlf + "x;" + Crlf, Array.Empty<Routine>()),
        new Probe("blank lines after Visual Basic documentation comments", "''' d" + Crlf + Crlf + "x" + Crlf, "''' d" + Crlf + "x" + Crlf, new[] { Routine.VisualBasic }),
        new Probe("blank lines between chained statements", "x" + Crlf + Crlf + "else x" + Crlf, "x" + Crlf + "else x" + Crlf, new[] { Routine.VisualBasic }),
    };

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
    [DataRow("C/C++", Routine.CFamily)]
    [DataRow("CSS", Routine.CFamily)]
    [DataRow("LESS", Routine.CFamily)]
    [DataRow("SCSS", Routine.CFamily)]
    [DataRow("JavaScript", Routine.CFamily)]
    [DataRow("TypeScript", Routine.CFamily)]
    [DataRow("JSON", Routine.CFamily)]
    [DataRow("PHP", Routine.CFamily)]
    [DataRow("PowerShell", Routine.CFamily)]
    [DataRow("R", Routine.CFamily)]
    [DataRow("HTML", Routine.Markup)]
    [DataRow("XAML", Routine.Markup)]
    [DataRow("XML", Routine.Markup)]
    [DataRow("F#", Routine.Generic)]
    [DataRow("Plain Text", Routine.Generic)]
    [DataRow("Basic", Routine.VisualBasic)]
    public void Cleanup_RunsTheStepsOfTheLanguageRoutineOnly(string language, Routine routine)
    {
        EnableAllWhitespaceSteps();

        RunWithManager(manager =>
        {
            foreach (Probe probe in Probes)
            {
                string expected = probe.RunBy.Contains(routine) ? probe.Cleaned : probe.Text;

                Assert.AreEqual(expected, Cleanup(manager, language, probe.Text, out _), $"{language}: {probe.Step}");
            }
        });
    }

    [TestMethod]
    [DataRow("C/C++")]
    [DataRow("CSS")]
    [DataRow("LESS")]
    [DataRow("SCSS")]
    [DataRow("JavaScript")]
    [DataRow("TypeScript")]
    [DataRow("JSON")]
    [DataRow("PHP")]
    [DataRow("PowerShell")]
    [DataRow("R")]
    [DataRow("HTML")]
    [DataRow("XAML")]
    [DataRow("XML")]
    [DataRow("F#")]
    [DataRow("Plain Text")]
    [DataRow("Basic")]
    public void Cleanup_RemovesBlankLinesAtTopAndBottomOfTheDocument(string language)
    {
        EnableAllWhitespaceSteps();

        RunWithManager(manager =>
        {
            string text = Crlf + "a" + Crlf + Crlf;

            Cleanup(manager, language, text, out List<string> deleteWhitespaceCalls);

            CollectionAssert.AreEqual(
                new[] { "0:" + vsWhitespaceOptions.vsWhitespaceOptionsVertical, text.Length + ":" + vsWhitespaceOptions.vsWhitespaceOptionsVertical },
                deleteWhitespaceCalls);
        });
    }

    [TestMethod]
    [DataRow("C/C++")]
    [DataRow("CSS")]
    [DataRow("JavaScript")]
    [DataRow("JSON")]
    [DataRow("PHP")]
    [DataRow("PowerShell")]
    [DataRow("R")]
    [DataRow("HTML")]
    [DataRow("XAML")]
    [DataRow("XML")]
    [DataRow("F#")]
    [DataRow("Plain Text")]
    [DataRow("Basic")]
    public void Cleanup_LeavesTheDocumentUnchanged_WhenEveryCleaningSettingIsOff(string language)
    {
        foreach (SettingsProperty property in Settings.Default.Properties)
        {
            if (property.PropertyType == typeof(bool) && property.Name.StartsWith("Cleaning_", StringComparison.Ordinal))
            {
                Settings.Default[property.Name] = false;
            }
        }

        RunWithManager(manager =>
        {
            string text = Crlf + "{" + Crlf + Crlf + "  a;  " + Crlf + Crlf + Crlf + Crlf + "}" + Crlf + "<b/>" + Crlf + "  >" + Crlf + Crlf + "// c" + Crlf + Crlf;

            Assert.AreEqual(text, Cleanup(manager, language, text, out List<string> deleteWhitespaceCalls));
            Assert.IsEmpty(deleteWhitespaceCalls);
        });
    }

    [TestMethod]
    public void Cleanup_AppliesEveryStepOfTheCFamilyRoutine_ToOneDocument()
    {
        EnableAllWhitespaceSteps();

        RunWithManager(manager =>
        {
            string text = "{" + Crlf + Crlf + "    int x;   " + Crlf + Crlf + Crlf + Crlf + Crlf + "    // c" + Crlf + "}" + Crlf + Crlf + Crlf + Crlf + "int y;";

            string cleaned = Cleanup(manager, "JavaScript", text, out _);

            Assert.AreEqual("{" + Crlf + "    int x;" + Crlf + Crlf + "    // c" + Crlf + "}" + Crlf + Crlf + "int y;" + Crlf, cleaned);
        });
    }

    [TestMethod]
    public void Cleanup_AppliesEveryStepOfTheMarkupRoutine_ToOneDocument()
    {
        EnableAllWhitespaceSteps();

        RunWithManager(manager =>
        {
            string text = "<root>  " + Crlf + "  <a" + Crlf + "    b=\"1\"" + Crlf + "    >" + Crlf + Crlf + Crlf + Crlf + "    <c/>" + Crlf + Crlf + "  </a>" + Crlf + "</root>";

            string cleaned = Cleanup(manager, "XML", text, out _);

            Assert.AreEqual("<root>" + Crlf + "  <a" + Crlf + "    b=\"1\">" + Crlf + Crlf + "    <c />" + Crlf + "  </a>" + Crlf + "</root>" + Crlf, cleaned);
        });
    }

    [TestMethod]
    public void Cleanup_RemovesTheFinalNewline_OfEveryRoutine_WhenEditorConfigInsertFinalNewlineIsFalse()
    {
        EnableAllWhitespaceSteps();
        File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"), "root = true" + Crlf + Crlf + "[*]" + Crlf + "insert_final_newline = false" + Crlf);

        RunWithManager(manager =>
        {
            foreach (string language in new[] { "JavaScript", "XML", "F#" })
            {
                Assert.AreEqual("a", Cleanup(manager, language, "a" + Crlf + Crlf, out _), language);
            }
        });
    }

    [TestMethod]
    public void Cleanup_DisablingASingleStepOnlyTurnsOffThatStep()
    {
        Settings.Default.Cleaning_RemoveBlankSpacesBeforeClosingAngleBrackets = false;
        EnableAllWhitespaceSteps(keep: nameof(Settings.Cleaning_RemoveBlankSpacesBeforeClosingAngleBrackets));

        RunWithManager(manager =>
        {
            string text = "<a" + Crlf + "  b=\"1\"" + Crlf + "  >" + Crlf + Crlf + Crlf + Crlf + "</a>" + Crlf;

            Assert.AreEqual("<a" + Crlf + "  b=\"1\"" + Crlf + "  >" + Crlf + "</a>" + Crlf, Cleanup(manager, "HTML", text, out _));
        });
    }

    private static void EnableAllWhitespaceSteps(string keep = null)
    {
        foreach (string name in new[]
        {
            nameof(Settings.Cleaning_RemoveEndOfLineWhitespace),
            nameof(Settings.Cleaning_RemoveBlankLinesAtTop),
            nameof(Settings.Cleaning_RemoveBlankLinesAtBottom),
            nameof(Settings.Cleaning_RemoveBlankLinesAfterAttributes),
            nameof(Settings.Cleaning_RemoveBlankLinesAfterOpeningBrace),
            nameof(Settings.Cleaning_RemoveBlankLinesBeforeClosingBrace),
            nameof(Settings.Cleaning_RemoveBlankLinesBetweenChainedStatements),
            nameof(Settings.Cleaning_RemoveBlankLinesBeforeClosingTags),
            nameof(Settings.Cleaning_RemoveBlankSpacesBeforeClosingAngleBrackets),
            nameof(Settings.Cleaning_RemoveMultipleConsecutiveBlankLines),
            nameof(Settings.Cleaning_InsertBlankSpaceBeforeSelfClosingAngleBrackets),
            nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeSingleLineComments),
        })
        {
            if (name != keep)
            {
                Settings.Default[name] = true;
            }
        }

        Settings.Default.Cleaning_RemoveByteOrderMark = false;
        Settings.Default.Cleaning_RunVisualStudioFormatDocumentCommand = false;
        Settings.Default.Cleaning_RunVisualStudioRemoveAndSortUsingStatements = false;
        Settings.Default.Cleaning_FormatRazorComponents = false;
        Settings.Default.ThirdParty_UseJetBrainsReSharperCleanup = false;
        Settings.Default.ThirdParty_UseTelerikJustCodeCleanup = false;
        Settings.Default.ThirdParty_UseXAMLStylerCleanup = false;
    }

    private string Cleanup(CodeCleanupManager manager, string language, string text, out List<string> deleteWhitespaceCalls)
    {
        string filePath = Path.Combine(_tempDirectory, "Sample.txt");
        using FakeTextEditor editor = new FakeTextEditor(filePath, language, text);
        EffectiveCleanupSettings settings = EffectiveCleanupSettings.For(filePath);

        MethodInfo find = typeof(CodeCleanupManager).GetMethod("FindCodeCleanupMethod", BindingFlags.Instance | BindingFlags.NonPublic);
        Action<Document> cleanup = (Action<Document>)find.Invoke(manager, new object[] { editor.Document, settings, false });
        Assert.IsNotNull(cleanup, $"{language} has a cleanup routine.");

        cleanup(editor.Document);

        deleteWhitespaceCalls = editor.DeleteWhitespaceCalls;

        return editor.Text;
    }

    private static void RunWithManager(Action<CodeCleanupManager> test)
    {
        WhitespaceStepCleanupTests.RunOnVisualStudioUIThread(() =>
        {
            CodeJanitorPackage package = (CodeJanitorPackage)FormatterServices.GetUninitializedObject(typeof(CodeJanitorPackage));
            typeof(CodeJanitorPackage).GetField("_ide", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(package, Substitute.For<DTE2>());

            FieldInfo[] singletonFields = typeof(CodeCleanupManager).Assembly.GetTypes()
                .Where(type => type.GetMethod("GetInstance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(CodeJanitorPackage) }, null) is not null)
                .Select(type => type.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic))
                .Where(field => field is not null)
                .ToArray();
            object[] previousInstances = Array.ConvertAll(singletonFields, field => field.GetValue(null));
            foreach (FieldInfo field in singletonFields)
            {
                field.SetValue(null, null);
            }

            try
            {
                test(CodeCleanupManager.GetInstance(package));
            }
            finally
            {
                for (int i = 0; i < singletonFields.Length; i++)
                {
                    singletonFields[i].SetValue(null, previousInstances[i]);
                }
            }
        });
    }
}
