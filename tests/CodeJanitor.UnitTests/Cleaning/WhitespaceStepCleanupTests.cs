using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Threading;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Model.CodeItems;
using CodeJanitor.Properties;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Threading;
using NSubstitute;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
[TestCategory("Cleaning UnitTests")]
public sealed class WhitespaceStepCleanupTests
{
    private const string Crlf = "\r\n";

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
        TextDocumentHelper.TextDocumentSubstitutionOverride = null;

        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Closed-file C# pipeline
    // ---------------------------------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow("\r\n")]
    [DataRow("\n")]
    public void Headless_RemoveBlankLinesAtTop_RemovesLeadingBlankAndWhitespaceOnlyLines_WhenEnabled(string nl)
    {
        string input = nl + "  \t" + nl + "\t" + nl + "namespace Demo;" + nl + nl + "public class C { }" + nl;

        string output = RunHeadless(input, nameof(Settings.Cleaning_RemoveBlankLinesAtTop));

        Assert.AreEqual("namespace Demo;" + nl + nl + "public class C { }" + nl, output);
    }

    [TestMethod]
    [DataRow("\r\n")]
    [DataRow("\n")]
    public void Headless_RemoveBlankLinesAtTop_KeepsLeadingBlankLines_WhenDisabled(string nl)
    {
        string input = nl + "  \t" + nl + "namespace Demo;" + nl + nl + "public class C { }" + nl;

        Assert.AreEqual(input, RunHeadless(input));
    }

    [TestMethod]
    public void Headless_RemoveBlankLinesAtTop_KeepsLeadingCommentAndOnlyRemovesBlankLinesBeforeIt()
    {
        string input = Crlf + "// header" + Crlf + Crlf + "namespace Demo;" + Crlf;

        Assert.AreEqual("// header" + Crlf + Crlf + "namespace Demo;" + Crlf, RunHeadless(input, nameof(Settings.Cleaning_RemoveBlankLinesAtTop)));
    }

    [TestMethod]
    public void Headless_RemoveBlankLinesAtTop_EmptiesAFileOfOnlyBlankLines_WhenEnabled() => Assert.AreEqual(string.Empty, RunHeadless(Crlf + "  " + Crlf + Crlf, nameof(Settings.Cleaning_RemoveBlankLinesAtTop)));

    [TestMethod]
    public void Headless_RemoveBlankLinesAtTop_KeepsTheBlankContentOfAFileOfOnlyBlankLines_WhenDisabled() => Assert.AreEqual(Crlf + "  " + Crlf, RunHeadless(Crlf + "  " + Crlf + Crlf));

    [TestMethod]
    public void Headless_RemoveBlankLinesAtTop_LeavesAnEmptyFileEmpty_WhetherEnabledOrNot()
    {
        Assert.AreEqual(string.Empty, RunHeadless(string.Empty, nameof(Settings.Cleaning_RemoveBlankLinesAtTop)));
        Assert.AreEqual(string.Empty, RunHeadless(string.Empty));
    }

    [TestMethod]
    [DataRow("\r\n")]
    [DataRow("\n")]
    public void Headless_RemoveBlankLinesAtBottom_RemovesTrailingBlankAndWhitespaceOnlyLines_WhenEnabled(string nl)
    {
        string input = "namespace Demo;" + nl + "// x" + nl + nl + "   " + nl + "\t";

        Assert.AreEqual("namespace Demo;" + nl + "// x" + nl, RunHeadless(input, nameof(Settings.Cleaning_RemoveBlankLinesAtBottom)));
    }

    [TestMethod]
    [DataRow("\r\n")]
    [DataRow("\n")]
    public void Headless_RemoveBlankLinesAtBottom_KeepsTrailingWhitespaceOnlyLines_WhenDisabled(string nl)
    {
        string input = "namespace Demo;" + nl + nl + "   " + nl + "\t";

        Assert.AreEqual(input + nl, RunHeadless(input));
    }

    [TestMethod]
    public void Headless_RemoveBlankLinesAtBottom_EmptiesAFileOfOnlyBlankLines_WhenEnabled() => Assert.AreEqual(string.Empty, RunHeadless(Crlf + Crlf, nameof(Settings.Cleaning_RemoveBlankLinesAtBottom)));

    [TestMethod]
    public void Headless_RemoveBlankLinesAtBottom_KeepsASingleNewlineInAFileOfOnlyBlankLines_WhenDisabled() => Assert.AreEqual(Crlf, RunHeadless(Crlf + Crlf));

    [TestMethod]
    [DataRow("\r\n")]
    [DataRow("\n")]
    public void Headless_RemoveBlankLinesAfterAttributes_RemovesTheBlankLine_WhenEnabled(string nl)
    {
        string input = Source(nl,
            "namespace Demo;",
            "",
            "[Serializable]",
            "",
            "public class C",
            "{",
            "    [Obsolete(\"x\")] // note",
            "",
            "    public int P { get; set; }",
            "}");

        string expected = Source(nl,
            "namespace Demo;",
            "",
            "[Serializable]",
            "public class C",
            "{",
            "    [Obsolete(\"x\")] // note",
            "    public int P { get; set; }",
            "}");

        Assert.AreEqual(expected, RunHeadless(input, nameof(Settings.Cleaning_RemoveBlankLinesAfterAttributes)));
    }

    [TestMethod]
    [DataRow("\r\n")]
    [DataRow("\n")]
    public void Headless_RemoveBlankLinesAfterAttributes_KeepsTheBlankLine_WhenDisabled(string nl)
    {
        string input = Source(nl, "namespace Demo;", "", "[Serializable]", "", "public class C { }");

        Assert.AreEqual(input, RunHeadless(input));
    }

    [TestMethod]
    public void Headless_RemoveBlankLinesAfterAttributes_KeepsABlankLineBeforeACommentOfTheMember()
    {
        string input = Source(Crlf, "namespace Demo;", "", "[Serializable]", "", "// the class", "public class C { }");

        Assert.AreEqual(input, RunHeadless(input, nameof(Settings.Cleaning_RemoveBlankLinesAfterAttributes)));
    }

    [TestMethod]
    public void Headless_RemoveBlankLinesAfterAttributes_RemovesSeveralBlankLines_InOnePass()
    {
        string input = Source(Crlf, "namespace Demo;", "", "[Serializable]", "", "", "", "public class C { }");

        Assert.AreEqual(
            Source(Crlf, "namespace Demo;", "", "[Serializable]", "public class C { }"),
            RunHeadless(input, nameof(Settings.Cleaning_RemoveBlankLinesAfterAttributes)));
    }

    [TestMethod]
    [DataRow("\r\n")]
    [DataRow("\n")]
    public void Headless_RemoveBlankLinesAfterOpeningBrace_RemovesTheBlankLines_WhenEnabled(string nl)
    {
        string input = Source(nl,
            "namespace Demo;",
            "",
            "public class C",
            "{",
            "",
            "",
            "    public void M() { // open",
            "",
            "        var x = new[] { 1,",
            "",
            "            2 };",
            "    }",
            "}");

        string expected = Source(nl,
            "namespace Demo;",
            "",
            "public class C",
            "{",
            "    public void M() { // open",
            "        var x = new[] { 1,",
            "",
            "            2 };",
            "    }",
            "}");

        Assert.AreEqual(expected, RunHeadless(input, nameof(Settings.Cleaning_RemoveBlankLinesAfterOpeningBrace)));
    }

    [TestMethod]
    public void Headless_RemoveBlankLinesAfterOpeningBrace_KeepsTheBlankLines_WhenDisabled()
    {
        string input = Source(Crlf, "namespace Demo;", "", "public class C", "{", "", "    int _x;", "}");

        Assert.AreEqual(input, RunHeadless(input));
    }

    [TestMethod]
    public void Headless_RemoveBlankLinesAfterOpeningBrace_RemovesWhitespaceOnlyLines_OnlyTogetherWithEndOfLineWhitespace()
    {
        string input = Source(Crlf, "namespace Demo;", "", "public class C", "{", "    ", "    int _x;", "}");

        Assert.AreEqual(input, RunHeadless(input, nameof(Settings.Cleaning_RemoveBlankLinesAfterOpeningBrace)));
        Assert.AreEqual(
            Source(Crlf, "namespace Demo;", "", "public class C", "{", "    int _x;", "}"),
            RunHeadless(input, nameof(Settings.Cleaning_RemoveBlankLinesAfterOpeningBrace), nameof(Settings.Cleaning_RemoveEndOfLineWhitespace)));
    }

    [TestMethod]
    [DataRow("\r\n")]
    [DataRow("\n")]
    public void Headless_RemoveBlankLinesBeforeClosingBrace_RemovesTheBlankLines_WhenEnabled(string nl)
    {
        string input = Source(nl,
            "namespace Demo;",
            "",
            "public class C",
            "{",
            "    public void M()",
            "    {",
            "        var x = 1;",
            "",
            "    }",
            "",
            "",
            "}");

        string expected = Source(nl,
            "namespace Demo;",
            "",
            "public class C",
            "{",
            "    public void M()",
            "    {",
            "        var x = 1;",
            "    }",
            "}");

        Assert.AreEqual(expected, RunHeadless(input, nameof(Settings.Cleaning_RemoveBlankLinesBeforeClosingBrace)));
    }

    [TestMethod]
    public void Headless_RemoveBlankLinesBeforeClosingBrace_KeepsTheBlankLines_WhenDisabled()
    {
        string input = Source(Crlf, "namespace Demo;", "", "public class C", "{", "    int _x;", "", "}");

        Assert.AreEqual(input, RunHeadless(input));
    }

    [TestMethod]
    public void Headless_RemoveBlankLinesBeforeClosingBrace_RemovesWhitespaceOnlyLines_OnlyTogetherWithEndOfLineWhitespace()
    {
        string input = Source(Crlf, "namespace Demo;", "", "public class C", "{", "    int _x;", "    ", "}");

        Assert.AreEqual(input, RunHeadless(input, nameof(Settings.Cleaning_RemoveBlankLinesBeforeClosingBrace)));
        Assert.AreEqual(
            Source(Crlf, "namespace Demo;", "", "public class C", "{", "    int _x;", "}"),
            RunHeadless(input, nameof(Settings.Cleaning_RemoveBlankLinesBeforeClosingBrace), nameof(Settings.Cleaning_RemoveEndOfLineWhitespace)));
    }

    [TestMethod]
    [DataRow("\r\n")]
    [DataRow("\n")]
    public void Headless_RemoveBlankLinesBetweenChainedStatements_RemovesTheBlankLinesBeforeElseCatchAndFinally_WhenEnabled(string nl)
    {
        string input = Source(nl,
            "namespace Demo;",
            "",
            "public class C",
            "{",
            "    public void M(bool a)",
            "    {",
            "        if (a)",
            "        {",
            "        }",
            "",
            "        else",
            "        {",
            "        }",
            "",
            "        try",
            "        {",
            "        }",
            "",
            "        catch (System.Exception)",
            "        {",
            "        }",
            "",
            "",
            "        finally",
            "        {",
            "        }",
            "    }",
            "}");

        string expected = input.Replace(nl + nl + "        else", nl + "        else")
                               .Replace(nl + nl + "        catch", nl + "        catch")
                               .Replace(nl + nl + nl + "        finally", nl + "        finally");

        Assert.AreNotEqual(expected, input);
        Assert.AreEqual(expected, RunHeadless(input, nameof(Settings.Cleaning_RemoveBlankLinesBetweenChainedStatements)));
    }

    [TestMethod]
    public void Headless_RemoveBlankLinesBetweenChainedStatements_KeepsTheBlankLines_WhenDisabled()
    {
        string input = Source(Crlf, "namespace Demo;", "", "public class C", "{", "    public void M(bool a)", "    {", "        if (a)", "        {", "        }", "", "        else", "        {", "        }", "    }", "}");

        Assert.AreEqual(input, RunHeadless(input));
    }

    [TestMethod]
    public void Headless_RemoveBlankLinesBetweenChainedStatements_LeavesIdentifiersThatStartWithElse()
    {
        string input = Source(Crlf, "namespace Demo;", "", "public class C", "{", "    int _a;", "", "    int elsewhere;", "}");

        Assert.AreEqual(input, RunHeadless(input, nameof(Settings.Cleaning_RemoveBlankLinesBetweenChainedStatements)));
    }

    [TestMethod]
    [DataRow("\r\n")]
    [DataRow("\n")]
    public void Headless_RemoveEndOfLineWhitespace_RemovesTrailingSpacesAndTabs_WhenEnabled(string nl)
    {
        string input = Source(nl, "namespace Demo;  ", "", "public class C\t", "{", "    int _x; // note \t", "\t", "    int _y;", "}");

        string expected = Source(nl, "namespace Demo;", "", "public class C", "{", "    int _x; // note", "", "    int _y;", "}");

        Assert.AreEqual(expected, RunHeadless(input, nameof(Settings.Cleaning_RemoveEndOfLineWhitespace)));
    }

    [TestMethod]
    public void Headless_RemoveEndOfLineWhitespace_KeepsTrailingWhitespace_WhenDisabled()
    {
        string input = Source(Crlf, "namespace Demo;  ", "", "public class C\t", "{", "    int _x;   ", "}");

        Assert.AreEqual(input, RunHeadless(input));
    }

    [TestMethod]
    public void Headless_RemoveEndOfLineWhitespace_KeepsWhitespaceInsideMultiLineStringLiterals()
    {
        string input = "namespace Demo;" + Crlf + Crlf + "public class C" + Crlf + "{" + Crlf +
                       "    string _s = @\"first   " + Crlf + "second\t" + Crlf + "\";" + Crlf + "}" + Crlf;

        Assert.AreEqual(input, RunHeadless(input, nameof(Settings.Cleaning_RemoveEndOfLineWhitespace)));
    }

    [TestMethod]
    public void Headless_RemoveEndOfLineWhitespace_KeepsDisabledConditionalCode()
    {
        string input = Source(Crlf, "namespace Demo;", "", "public class C", "{", "#if NEVER", "    int _x;   ", "#endif", "    int _y;   ", "}");

        string expected = Source(Crlf, "namespace Demo;", "", "public class C", "{", "#if NEVER", "    int _x;   ", "#endif", "    int _y;", "}");

        Assert.AreEqual(expected, RunHeadless(input, nameof(Settings.Cleaning_RemoveEndOfLineWhitespace)));
    }

    [TestMethod]
    public void Headless_FinalNewline_IsInserted_WhenTheFileHasNone_AndTrailingBlankLinesAreCollapsed()
    {
        Assert.AreEqual("namespace Demo;" + Crlf + "// x" + Crlf, RunHeadless("namespace Demo;" + Crlf + "// x"));
        Assert.AreEqual("namespace Demo;" + Crlf + "// x" + Crlf, RunHeadless("namespace Demo;" + Crlf + "// x" + Crlf + Crlf + Crlf));
    }

    [TestMethod]
    public void Headless_FinalNewline_IsRemoved_WhenEditorConfigInsertFinalNewlineIsFalse()
    {
        WriteEditorConfig("insert_final_newline = false");

        Assert.AreEqual("namespace Demo;", RunHeadless("namespace Demo;" + Crlf + Crlf));
        Assert.AreEqual("namespace Demo;", RunHeadless("namespace Demo;"));
    }

    [TestMethod]
    public void Headless_BlankLineSteps_KeepTheContentOfMultiLineStringLiterals()
    {
        string input = "namespace Demo;" + Crlf + Crlf + "public class C" + Crlf + "{" + Crlf +
                       "    string _s = @\"{" + Crlf + Crlf + "x" + Crlf + Crlf + "}" + Crlf + Crlf + "[a]" + Crlf + Crlf + "x" + Crlf + Crlf + "else y\";" + Crlf + "}" + Crlf;

        string output = RunHeadless(
            input,
            nameof(Settings.Cleaning_RemoveBlankLinesAfterAttributes),
            nameof(Settings.Cleaning_RemoveBlankLinesAfterOpeningBrace),
            nameof(Settings.Cleaning_RemoveBlankLinesBeforeClosingBrace),
            nameof(Settings.Cleaning_RemoveBlankLinesBetweenChainedStatements),
            nameof(Settings.Cleaning_RemoveEndOfLineWhitespace));

        Assert.AreEqual(input, output);
    }

    [TestMethod]
    public void Headless_FinalNewline_FollowsTheFileLineEnding()
    {
        Assert.AreEqual("namespace Demo;\n// x\n", RunHeadless("namespace Demo;\n// x\n\n\n"));
        Assert.AreEqual("namespace Demo;" + Crlf + "// x" + Crlf, RunHeadless("namespace Demo;" + Crlf + "// x"));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Editor pipeline: whitespace removal steps
    // ---------------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void Editor_RemoveBlankLinesAtTop_DeletesTheVerticalWhitespaceAtTheStartOfTheDocument_WhenEnabled() => RunOnVisualStudioUIThread(() =>
                                                                                                                       {
                                                                                                                           using FakeTextEditor editor = NewEditor(Crlf + Crlf + "text" + Crlf + Crlf);

                                                                                                                           Logic.RemoveBlankLinesAtTop(editor.TextDocument, Effective());

                                                                                                                           Assert.AreSequenceEqual(new[] { "0:" + vsWhitespaceOptions.vsWhitespaceOptionsVertical }, editor.DeleteWhitespaceCalls);
                                                                                                                       });

    [TestMethod]
    public void Editor_RemoveBlankLinesAtTop_LeavesTheDocument_WhenDisabled() => RunOnVisualStudioUIThread(() =>
                                                                                      {
                                                                                          Settings.Default.Cleaning_RemoveBlankLinesAtTop = false;
                                                                                          using FakeTextEditor editor = NewEditor(Crlf + "text");

                                                                                          Logic.RemoveBlankLinesAtTop(editor.TextDocument, Effective());

                                                                                          Assert.IsEmpty(editor.DeleteWhitespaceCalls);
                                                                                      });

    [TestMethod]
    public void Editor_RemoveBlankLinesAtBottom_DeletesTheVerticalWhitespaceAtTheEndOfTheDocument_WhenEnabled() => RunOnVisualStudioUIThread(() =>
                                                                                                                        {
                                                                                                                            string text = "text" + Crlf + Crlf;
                                                                                                                            using FakeTextEditor editor = NewEditor(text);

                                                                                                                            Logic.RemoveBlankLinesAtBottom(editor.TextDocument, Effective());

                                                                                                                            Assert.AreSequenceEqual(new[] { text.Length + ":" + vsWhitespaceOptions.vsWhitespaceOptionsVertical }, editor.DeleteWhitespaceCalls);
                                                                                                                        });

    [TestMethod]
    public void Editor_RemoveBlankLinesAtBottom_LeavesTheDocument_WhenDisabled() => RunOnVisualStudioUIThread(() =>
                                                                                         {
                                                                                             Settings.Default.Cleaning_RemoveBlankLinesAtBottom = false;
                                                                                             using FakeTextEditor editor = NewEditor("text" + Crlf);

                                                                                             Logic.RemoveBlankLinesAtBottom(editor.TextDocument, Effective());

                                                                                             Assert.IsEmpty(editor.DeleteWhitespaceCalls);
                                                                                         });

    [TestMethod]
    public void Editor_RemoveBlankLinesAfterAttributes_RemovesTheBlankLineAfterTheAttribute_WhenEnabled() => RunOnVisualStudioUIThread(() =>
                                                                                                                  {
                                                                                                                      using FakeTextEditor editor = NewEditor(Source(Crlf, "[Serializable]", "", "public class C { }", "", "[Obsolete] // note", "", "int _p;", "[A]", "", "// comment", "int _q;"));

                                                                                                                      Logic.RemoveBlankLinesAfterAttributes(editor.TextDocument, Effective());

                                                                                                                      Assert.AreEqual(Source(Crlf, "[Serializable]", "public class C { }", "", "[Obsolete] // note", "int _p;", "[A]", "", "// comment", "int _q;"), editor.Text);
                                                                                                                  });

    [TestMethod]
    public void Editor_RemoveBlankLinesAfterAttributes_RemovesSeveralBlankLines_InOnePass() => RunOnVisualStudioUIThread(() =>
                                                                                                    {
                                                                                                        using FakeTextEditor editor = NewEditor(Source(Crlf, "[Serializable]", "", "", "", "public class C { }"));

                                                                                                        Logic.RemoveBlankLinesAfterAttributes(editor.TextDocument, Effective());

                                                                                                        Assert.AreEqual(Source(Crlf, "[Serializable]", "public class C { }"), editor.Text);
                                                                                                    });

    [TestMethod]
    public void Editor_RemoveBlankLinesAfterAttributes_LeavesTheDocument_WhenDisabled() => AssertUnchangedWhenDisabled(nameof(Settings.Cleaning_RemoveBlankLinesAfterAttributes), Source(Crlf, "[Serializable]", "", "public class C { }"), (logic, document, settings) => logic.RemoveBlankLinesAfterAttributes(document, settings));

    [TestMethod]
    public void Editor_RemoveBlankLinesAfterDocumentationComments_UsesTheDocumentationCommentPatternOfTheLanguage() => RunOnVisualStudioUIThread(() =>
                                                                                                                            {
                                                                                                                                using FakeTextEditor csharp = NewEditor(Source(Crlf, "/// <summary>A</summary>", "", "", "public class C { }", "//// not documentation", "", "int _x;"));

                                                                                                                                Logic.RemoveBlankLinesAfterDocumentationComments(csharp.TextDocument, RemoveWhitespaceLogic.BlankLinesAfterDocumentationCommentPattern);

                                                                                                                                Assert.AreEqual(Source(Crlf, "/// <summary>A</summary>", "public class C { }", "//// not documentation", "", "int _x;"), csharp.Text);

                                                                                                                                using FakeTextEditor basic = NewEditor(Source(Crlf, "''' <summary>A</summary>", "", "Public Class C", "End Class"));

                                                                                                                                Logic.RemoveBlankLinesAfterDocumentationComments(basic.TextDocument, RemoveWhitespaceLogic.BlankLinesAfterVisualBasicDocumentationCommentPattern);

                                                                                                                                Assert.AreEqual(Source(Crlf, "''' <summary>A</summary>", "Public Class C", "End Class"), basic.Text);
                                                                                                                            });

    [TestMethod]
    public void Editor_RemoveBlankLinesAfterOpeningBrace_RemovesTheBlankLines_WhenEnabled() => RunOnVisualStudioUIThread(() =>
                                                                                                    {
                                                                                                        using FakeTextEditor editor = NewEditor(Source(Crlf, "class C", "{", "", "", "    void M() { // open", "", "        var x = new[] { 1,", "", "            2 };", "    }", "}"));
                                                                                                        Logic.RemoveBlankLinesAfterOpeningBrace(editor.TextDocument, Effective());

                                                                                                        Assert.AreEqual(Source(Crlf, "class C", "{", "    void M() { // open", "        var x = new[] { 1,", "", "            2 };", "    }", "}"), editor.Text);
                                                                                                    });

    [TestMethod]
    public void Editor_RemoveBlankLinesAfterOpeningBrace_LeavesTheDocument_WhenDisabled() => AssertUnchangedWhenDisabled(nameof(Settings.Cleaning_RemoveBlankLinesAfterOpeningBrace), Source(Crlf, "class C", "{", "", "    int _x;", "}"), (logic, document, settings) => logic.RemoveBlankLinesAfterOpeningBrace(document, settings));

    [TestMethod]
    public void Editor_RemoveBlankLinesBeforeClosingBrace_RemovesTheBlankLines_WhenEnabled() => RunOnVisualStudioUIThread(() =>
                                                                                                     {
                                                                                                         using FakeTextEditor editor = NewEditor(Source(Crlf, "class C", "{", "    void M()", "    {", "        var x = 1;", "", "    }", "", "", "}"));

                                                                                                         Logic.RemoveBlankLinesBeforeClosingBrace(editor.TextDocument, Effective());

                                                                                                         Assert.AreEqual(Source(Crlf, "class C", "{", "    void M()", "    {", "        var x = 1;", "    }", "}"), editor.Text);
                                                                                                     });

    [TestMethod]
    public void Editor_RemoveBlankLinesBeforeClosingBrace_LeavesTheDocument_WhenDisabled() => AssertUnchangedWhenDisabled(nameof(Settings.Cleaning_RemoveBlankLinesBeforeClosingBrace), Source(Crlf, "class C", "{", "    int _x;", "", "}"), (logic, document, settings) => logic.RemoveBlankLinesBeforeClosingBrace(document, settings));

    [TestMethod]
    public void Editor_RemoveBlankLinesBetweenChainedStatements_RemovesTheBlankLinesBeforeElseCatchAndFinally_WhenEnabled() => RunOnVisualStudioUIThread(() =>
                                                                                                                                    {
                                                                                                                                        using FakeTextEditor editor = NewEditor(Source(Crlf, "if (a)", "{", "}", "", "else", "{", "}", "", "try", "{", "}", "", "", "catch (Exception)", "{", "}", "", "finally", "{", "}", "", "elsewhere();"));

                                                                                                                                        Logic.RemoveBlankLinesBetweenChainedStatements(editor.TextDocument, Effective());

                                                                                                                                        Assert.AreEqual(Source(Crlf, "if (a)", "{", "}", "else", "{", "}", "", "try", "{", "}", "catch (Exception)", "{", "}", "finally", "{", "}", "", "elsewhere();"), editor.Text);
                                                                                                                                    });

    [TestMethod]
    public void Editor_RemoveBlankLinesBetweenChainedStatements_LeavesTheDocument_WhenDisabled() => AssertUnchangedWhenDisabled(nameof(Settings.Cleaning_RemoveBlankLinesBetweenChainedStatements), Source(Crlf, "if (a)", "{", "}", "", "else", "{", "}"), (logic, document, settings) => logic.RemoveBlankLinesBetweenChainedStatements(document, settings));

    [TestMethod]
    public void Editor_RemoveEOLWhitespace_RemovesTrailingSpacesAndTabs_WhenEnabled() => RunOnVisualStudioUIThread(() =>
                                                                                              {
                                                                                                  using FakeTextEditor editor = NewEditor("class C  " + Crlf + "{\t" + Crlf + "    int _x; // note \t" + Crlf + "\t" + Crlf + "}   ");

                                                                                                  Logic.RemoveEOLWhitespace(editor.TextDocument, Effective());

                                                                                                  Assert.AreEqual("class C" + Crlf + "{" + Crlf + "    int _x; // note" + Crlf + "" + Crlf + "}   ", editor.Text, "Whitespace after the last line break has no line end to precede.");
                                                                                              });

    [TestMethod]
    public void Editor_RemoveEOLWhitespace_LeavesTheDocument_WhenDisabled() => AssertUnchangedWhenDisabled(nameof(Settings.Cleaning_RemoveEndOfLineWhitespace), "class C  " + Crlf + "{\t" + Crlf + "}", (logic, document, settings) => logic.RemoveEOLWhitespace(document, settings));

    [TestMethod]
    public void Editor_RemoveEOLWhitespace_IsDisabledByEditorConfigTrimTrailingWhitespaceFalse_OverTheUserSetting() => RunOnVisualStudioUIThread(() =>
                                                                                                                            {
                                                                                                                                WriteEditorConfig("trim_trailing_whitespace = false");
                                                                                                                                using FakeTextEditor editor = NewEditor("class C  " + Crlf + "{" + Crlf + "}" + Crlf);

                                                                                                                                Logic.RemoveEOLWhitespace(editor.TextDocument, Effective());

                                                                                                                                Assert.AreEqual("class C  " + Crlf + "{" + Crlf + "}" + Crlf, editor.Text);
                                                                                                                            });

    [TestMethod]
    public void Editor_RemoveMultipleConsecutiveBlankLines_KeepsASingleBlankLine_WhenEnabled() => RunOnVisualStudioUIThread(() =>
                                                                                                       {
                                                                                                           using FakeTextEditor editor = NewEditor(Source(Crlf, "a", "", "b", "", "", "", "", "c", "d"));

                                                                                                           Logic.RemoveMultipleConsecutiveBlankLines(editor.TextDocument, Effective());

                                                                                                           Assert.AreEqual(Source(Crlf, "a", "", "b", "", "c", "d"), editor.Text);
                                                                                                       });

    [TestMethod]
    public void Editor_RemoveMultipleConsecutiveBlankLines_LeavesTheDocument_WhenDisabled() => AssertUnchangedWhenDisabled(nameof(Settings.Cleaning_RemoveMultipleConsecutiveBlankLines), Source(Crlf, "a", "", "", "", "b"), (logic, document, settings) => logic.RemoveMultipleConsecutiveBlankLines(document, settings));

    [TestMethod]
    public void Editor_RemoveMultipleConsecutiveBlankLines_TreatsWhitespaceOnlyLinesAsContent() => RunOnVisualStudioUIThread(() =>
                                                                                                        {
                                                                                                            string text = Source(Crlf, "a", "  ", "", "", "b");
                                                                                                            using FakeTextEditor editor = NewEditor(text);

                                                                                                            Logic.RemoveMultipleConsecutiveBlankLines(editor.TextDocument, Effective());

                                                                                                            Assert.AreEqual(Source(Crlf, "a", "  ", "", "b"), editor.Text);
                                                                                                        });

    [TestMethod]
    public void Editor_RemoveBlankLinesBeforeClosingTag_RemovesTheBlankLines_WhenEnabled() => RunOnVisualStudioUIThread(() =>
                                                                                                   {
                                                                                                       using FakeTextEditor editor = NewEditor(Source(Crlf, "<root>", "  <a>", "", "    text", "", "", "  </a>", "", "</root>"));

                                                                                                       Logic.RemoveBlankLinesBeforeClosingTag(editor.TextDocument, Effective());

                                                                                                       Assert.AreEqual(Source(Crlf, "<root>", "  <a>", "", "    text", "  </a>", "</root>"), editor.Text);
                                                                                                   });

    [TestMethod]
    public void Editor_RemoveBlankLinesBeforeClosingTag_LeavesTheDocument_WhenDisabled() => AssertUnchangedWhenDisabled(nameof(Settings.Cleaning_RemoveBlankLinesBeforeClosingTags), Source(Crlf, "<root>", "  <a>", "", "  </a>", "", "</root>"), (logic, document, settings) => logic.RemoveBlankLinesBeforeClosingTag(document, settings));

    [TestMethod]
    public void Editor_RemoveBlankSpacesBeforeClosingAngleBracket_MovesTheBracketToTheEndOfTheAttributes_WhenEnabled() => RunOnVisualStudioUIThread(() =>
                                                                                                                               {
                                                                                                                                   using FakeTextEditor editor = NewEditor(Source(Crlf, "<a", "    b=\"1\"", "    >", "  text", "</a>"));

                                                                                                                                   Logic.RemoveBlankSpacesBeforeClosingAngleBracket(editor.TextDocument, Effective());

                                                                                                                                   Assert.AreEqual(Source(Crlf, "<a", "    b=\"1\">", "  text", "</a>"), editor.Text);
                                                                                                                               });

    [TestMethod]
    public void Editor_RemoveBlankSpacesBeforeClosingAngleBracket_LeavesTheDocument_WhenDisabled() => AssertUnchangedWhenDisabled(nameof(Settings.Cleaning_RemoveBlankSpacesBeforeClosingAngleBrackets), Source(Crlf, "<a", "    b=\"1\"", "    >", "<b x=\"1\"   />"), (logic, document, settings) => logic.RemoveBlankSpacesBeforeClosingAngleBracket(document, settings));

    [TestMethod]
    public void Editor_RemoveBlankSpacesBeforeClosingAngleBracket_CollapsesTheSpacesBeforeASelfClosingBracketToOne_WhenSelfClosingSpaceIsEnabled() => RunOnVisualStudioUIThread(() =>
                                                                                                                                                           {
                                                                                                                                                               Settings.Default.Cleaning_InsertBlankSpaceBeforeSelfClosingAngleBrackets = true;
                                                                                                                                                               using FakeTextEditor editor = NewEditor(Source(Crlf, "<b x=\"1\"   />", "<c x=\"1\" />", "<d x=\"1\"/>", "<e />"));

                                                                                                                                                               Logic.RemoveBlankSpacesBeforeClosingAngleBracket(editor.TextDocument, Effective());

                                                                                                                                                               Assert.AreEqual(Source(Crlf, "<b x=\"1\" />", "<c x=\"1\" />", "<d x=\"1\"/>", "<e />"), editor.Text);
                                                                                                                                                           });

    [TestMethod]
    public void Editor_RemoveBlankSpacesBeforeClosingAngleBracket_RemovesTheSpacesBeforeASelfClosingBracket_WhenSelfClosingSpaceIsDisabled() => RunOnVisualStudioUIThread(() =>
                                                                                                                                                     {
                                                                                                                                                         Settings.Default.Cleaning_InsertBlankSpaceBeforeSelfClosingAngleBrackets = false;
                                                                                                                                                         using FakeTextEditor editor = NewEditor(Source(Crlf, "<b x=\"1\"   />", "<c x=\"1\" />", "<d x=\"1\"/>", "<e />"));

                                                                                                                                                         Logic.RemoveBlankSpacesBeforeClosingAngleBracket(editor.TextDocument, Effective());

                                                                                                                                                         Assert.AreEqual(Source(Crlf, "<b x=\"1\"/>", "<c x=\"1\"/>", "<d x=\"1\"/>", "<e/>"), editor.Text);
                                                                                                                                                     });

    [TestMethod]
    public void Editor_RemovalSteps_KeepTheLfLineEndingsOfAnLfDocument() => RunOnVisualStudioUIThread(() =>
                                                                                 {
                                                                                     (string Name, string Text, string Expected, Action<TextDocument, EffectiveCleanupSettings> Step)[] cases =
                                                                                     {
                ("after documentation comments", "/// <summary>A</summary>\n\nclass C { }\n", "/// <summary>A</summary>\nclass C { }\n", (d, s) => Logic.RemoveBlankLinesAfterDocumentationComments(d, RemoveWhitespaceLogic.BlankLinesAfterDocumentationCommentPattern)),
                ("after attributes", "[A]\n\nclass C { }\n", "[A]\nclass C { }\n", (d, s) => Logic.RemoveBlankLinesAfterAttributes(d, s)),
                ("after opening brace", "{\n\nint x;\n}\n", "{\nint x;\n}\n", (d, s) => Logic.RemoveBlankLinesAfterOpeningBrace(d, s)),
                ("before closing brace", "{\nint x;\n\n}\n", "{\nint x;\n}\n", (d, s) => Logic.RemoveBlankLinesBeforeClosingBrace(d, s)),
                ("between chained statements", "if (a)\n{\n}\n\nelse\n{\n}\n", "if (a)\n{\n}\nelse\n{\n}\n", (d, s) => Logic.RemoveBlankLinesBetweenChainedStatements(d, s)),
                ("end of line whitespace", "int x; \nint y;\n", "int x;\nint y;\n", (d, s) => Logic.RemoveEOLWhitespace(d, s)),
                ("multiple blank lines", "a\n\n\n\nb\n", "a\n\nb\n", (d, s) => Logic.RemoveMultipleConsecutiveBlankLines(d, s)),
                ("before closing tags", "<a>\n\n</a>\n", "<a>\n</a>\n", (d, s) => Logic.RemoveBlankLinesBeforeClosingTag(d, s)),
                ("before closing angle brackets", "<a\n  b=\"1\"\n  >\n", "<a\n  b=\"1\">\n", (d, s) => Logic.RemoveBlankSpacesBeforeClosingAngleBracket(d, s)),
                                                                                     };

                                                                                     foreach ((string name, string text, string expected, Action<TextDocument, EffectiveCleanupSettings> step) in cases)
                                                                                     {
                                                                                         using FakeTextEditor editor = NewEditor(text);

                                                                                         step(editor.TextDocument, Effective());

                                                                                         Assert.AreEqual(expected, editor.Text, name);
                                                                                     }
                                                                                 });

    [TestMethod]
    public void Editor_InsertBlankSpaceBeforeSelfClosingAngleBracket_InsertsOneSpace_WhenEnabled() => RunOnVisualStudioUIThread(() =>
                                                                                                           {
                                                                                                               Settings.Default.Cleaning_InsertBlankSpaceBeforeSelfClosingAngleBrackets = true;
                                                                                                               using FakeTextEditor editor = NewEditor(Source(Crlf, "<a/>", "<b x=\"1\"/>", "<c x=\"1\" />", "<d x=\"1\"\t/>"));

                                                                                                               InsertLogic.InsertBlankSpaceBeforeSelfClosingAngleBracket(editor.TextDocument, Effective());

                                                                                                               Assert.AreEqual(Source(Crlf, "<a />", "<b x=\"1\" />", "<c x=\"1\" />", "<d x=\"1\"\t/>"), editor.Text);
                                                                                                           });

    [TestMethod]
    public void Editor_InsertBlankSpaceBeforeSelfClosingAngleBracket_LeavesTheDocument_WhenDisabled() => RunOnVisualStudioUIThread(() =>
                                                                                                              {
                                                                                                                  Settings.Default.Cleaning_InsertBlankSpaceBeforeSelfClosingAngleBrackets = false;
                                                                                                                  using FakeTextEditor editor = NewEditor(Source(Crlf, "<a/>", "<b x=\"1\"/>"));

                                                                                                                  InsertLogic.InsertBlankSpaceBeforeSelfClosingAngleBracket(editor.TextDocument, Effective());

                                                                                                                  Assert.AreEqual(Source(Crlf, "<a/>", "<b x=\"1\"/>"), editor.Text);
                                                                                                              });

    // ---------------------------------------------------------------------------------------------------------------
    // Editor pipeline: final newline
    // ---------------------------------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow("text", "text\r\n")]
    [DataRow("text\r\n", "text\r\n")]
    [DataRow("text\r\n\r\n", "text\r\n\r\n")]
    [DataRow("", "")]
    public void Editor_InsertEOFTrailingNewLine_EndsTheDocumentWithALineBreak(string text, string expected) => RunOnVisualStudioUIThread(() =>
                                                                                                                    {
                                                                                                                        using FakeTextEditor editor = NewEditor(text);

                                                                                                                        InsertLogic.InsertEOFTrailingNewLine(editor.TextDocument, Effective());

                                                                                                                        Assert.AreEqual(expected, editor.Text);
                                                                                                                    });

    [TestMethod]
    public void Editor_InsertEOFTrailingNewLine_LeavesTheDocument_WhenEditorConfigInsertFinalNewlineIsFalse() => RunOnVisualStudioUIThread(() =>
                                                                                                                      {
                                                                                                                          WriteEditorConfig("insert_final_newline = false");
                                                                                                                          using FakeTextEditor editor = NewEditor("text");

                                                                                                                          InsertLogic.InsertEOFTrailingNewLine(editor.TextDocument, Effective());

                                                                                                                          Assert.AreEqual("text", editor.Text);
                                                                                                                      });

    [TestMethod]
    [DataRow("text\r\n", "text")]
    [DataRow("text\r\n\r\n  \r\n", "text")]
    [DataRow("text", "text")]
    [DataRow("", "")]
    public void Editor_RemoveEOFTrailingNewLine_RemovesTheFinalLineBreaks_WhenEditorConfigInsertFinalNewlineIsFalse(string text, string expected) => RunOnVisualStudioUIThread(() =>
                                                                                                                                                          {
                                                                                                                                                              WriteEditorConfig("insert_final_newline = false");
                                                                                                                                                              using FakeTextEditor editor = NewEditor(text);

                                                                                                                                                              Logic.RemoveEOFTrailingNewLine(editor.TextDocument, Effective());

                                                                                                                                                              Assert.AreEqual(expected, editor.Text);
                                                                                                                                                          });

    [TestMethod]
    public void Editor_RemoveEOFTrailingNewLine_LeavesTheDocument_WhenTheFinalNewlineIsRequired() => RunOnVisualStudioUIThread(() =>
                                                                                                          {
                                                                                                              using FakeTextEditor editor = NewEditor("text\r\n\r\n");

                                                                                                              Logic.RemoveEOFTrailingNewLine(editor.TextDocument, Effective());

                                                                                                              Assert.AreEqual("text\r\n\r\n", editor.Text);
                                                                                                          });

    // ---------------------------------------------------------------------------------------------------------------
    // Padding between the accessors of a multi-line property
    // ---------------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void Editor_InsertPaddingBetweenMultiLinePropertyAccessors_SeparatesMultiLineAccessors_WhenEnabled() => RunOnVisualStudioUIThread(() =>
                                                                                                                        {
                                                                                                                            Settings.Default.Cleaning_InsertBlankLinePaddingBetweenPropertiesMultiLineAccessors = true;
                                                                                                                            using FakeTextEditor editor = NewEditor(MultiLineAccessorsSource);
                                                                                                                            CodeItemProperty property = Property(editor, getter: (5, 8), setter: (9, 12));

                                                                                                                            PaddingLogic.InsertPaddingBetweenMultiLinePropertyAccessors(new[] { property }, Effective());

                                                                                                                            Assert.AreEqual(
                                                                                                                                Source(Crlf, "public class C", "{", "    public int P", "    {", "        get", "        {", "            return _p;", "        }", "", "        set", "        {", "            _p = value;", "        }", "    }", "}"),
                                                                                                                                editor.Text);
                                                                                                                        });

    [TestMethod]
    public void Editor_InsertPaddingBetweenMultiLinePropertyAccessors_LeavesTheAccessors_WhenDisabled() => RunOnVisualStudioUIThread(() =>
                                                                                                                {
                                                                                                                    Settings.Default.Cleaning_InsertBlankLinePaddingBetweenPropertiesMultiLineAccessors = false;
                                                                                                                    using FakeTextEditor editor = NewEditor(MultiLineAccessorsSource);
                                                                                                                    CodeItemProperty property = Property(editor, getter: (5, 8), setter: (9, 12));

                                                                                                                    PaddingLogic.InsertPaddingBetweenMultiLinePropertyAccessors(new[] { property }, Effective());

                                                                                                                    Assert.AreEqual(MultiLineAccessorsSource, editor.Text);
                                                                                                                });

    [TestMethod]
    public void Editor_InsertPaddingBetweenMultiLinePropertyAccessors_LeavesSingleLineAccessorsAndAccessorlessPropertiesAlone() => RunOnVisualStudioUIThread(() =>
                                                                                                                                        {
                                                                                                                                            Settings.Default.Cleaning_InsertBlankLinePaddingBetweenPropertiesMultiLineAccessors = true;
                                                                                                                                            string singleLine = Source(Crlf, "public class C", "{", "    public int P", "    {", "        get { return _p; }", "        set { _p = value; }", "    }", "}");
                                                                                                                                            using FakeTextEditor editor = NewEditor(singleLine);
                                                                                                                                            CodeItemProperty singleLineProperty = Property(editor, getter: (5, 5), setter: (6, 6));
                                                                                                                                            CodeItemProperty getterOnly = Property(editor, getter: (5, 5), setter: null);

                                                                                                                                            PaddingLogic.InsertPaddingBetweenMultiLinePropertyAccessors(new[] { singleLineProperty, getterOnly }, Effective());

                                                                                                                                            Assert.AreEqual(singleLine, editor.Text);
                                                                                                                                        });

    [TestMethod]
    public void Editor_InsertPaddingBetweenMultiLinePropertyAccessors_SeparatesTheAccessors_WhenOnlyTheSetterIsMultiLine() => RunOnVisualStudioUIThread(() =>
                                                                                                                                   {
                                                                                                                                       Settings.Default.Cleaning_InsertBlankLinePaddingBetweenPropertiesMultiLineAccessors = true;
                                                                                                                                       string text = Source(Crlf, "public class C", "{", "    public int P", "    {", "        get { return _p; }", "        set", "        {", "            _p = value;", "        }", "    }", "}");
                                                                                                                                       using FakeTextEditor editor = NewEditor(text);
                                                                                                                                       CodeItemProperty property = Property(editor, getter: (5, 5), setter: (6, 9));

                                                                                                                                       PaddingLogic.InsertPaddingBetweenMultiLinePropertyAccessors(new[] { property }, Effective());

                                                                                                                                       Assert.AreEqual(
                                                                                                                                           Source(Crlf, "public class C", "{", "    public int P", "    {", "        get { return _p; }", "", "        set", "        {", "            _p = value;", "        }", "    }", "}"),
                                                                                                                                           editor.Text);
                                                                                                                                   });

    // ---------------------------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------------------------

    private static readonly string MultiLineAccessorsSource =
        Source(Crlf, "public class C", "{", "    public int P", "    {", "        get", "        {", "            return _p;", "        }", "        set", "        {", "            _p = value;", "        }", "    }", "}");

    private static RemoveWhitespaceLogic Logic => RemoveWhitespaceLogic.GetInstance(null);

    private static InsertWhitespaceLogic InsertLogic => InsertWhitespaceLogic.GetInstance(null);

    private static InsertBlankLinePaddingLogic PaddingLogic => InsertBlankLinePaddingLogic.GetInstance(null);

    private EffectiveCleanupSettings Effective() => EffectiveCleanupSettings.For(Path.Combine(_tempDirectory, "Sample.txt"));

    private FakeTextEditor NewEditor(string text) => new FakeTextEditor(Path.Combine(_tempDirectory, "Sample.txt"), "Plain Text", text);

    private static string Source(string newline, params string[] lines) => string.Join(newline, lines) + newline;

    private string RunHeadless(string source, params string[] enabledSettings)
    {
        foreach (SettingsProperty property in Settings.Default.Properties)
        {
            if (property.PropertyType == typeof(bool) &&
                (property.Name.StartsWith("Cleaning_", StringComparison.Ordinal) || property.Name == nameof(Settings.Formatting_CommentRunDuringCleanup)))
            {
                Settings.Default[property.Name] = false;
            }
        }

        foreach (string name in enabledSettings)
        {
            Settings.Default[name] = true;
        }

        return CodeCleanupManager.ApplyHeadlessCSharpTransformations(source, Path.Combine(_tempDirectory, "Sample.cs"));
    }

    private void WriteEditorConfig(params string[] options) => File.WriteAllText(Path.Combine(_tempDirectory, ".editorconfig"), "root = true" + Crlf + Crlf + "[*]" + Crlf + string.Join(Crlf, options) + Crlf);

    private void AssertUnchangedWhenDisabled(string settingName, string text, Action<RemoveWhitespaceLogic, TextDocument, EffectiveCleanupSettings> step) => RunOnVisualStudioUIThread(() =>
                                                                                                                                                                  {
                                                                                                                                                                      Settings.Default[settingName] = false;
                                                                                                                                                                      using FakeTextEditor editor = NewEditor(text);

                                                                                                                                                                      step(Logic, editor.TextDocument, Effective());

                                                                                                                                                                      Assert.AreEqual(text, editor.Text);
                                                                                                                                                                  });

#pragma warning disable VSTHRD010 // The substitutes stand in for Visual Studio's COM objects, which the test calls from its own thread.

    private static CodeItemProperty Property(FakeTextEditor editor, (int Start, int End) getter, (int Start, int End)? setter)
    {
        CodeProperty2 codeProperty = Substitute.For<CodeProperty2>();
        CodeFunction getterFunction = Accessor(editor, getter);
        CodeFunction setterFunction = setter is null ? null : Accessor(editor, setter.Value);
        codeProperty.Getter.Returns(getterFunction);
        codeProperty.Setter.Returns(setterFunction);

        return new CodeItemProperty { CodeProperty = codeProperty };
    }

    private static CodeFunction Accessor(FakeTextEditor editor, (int Start, int End) lines)
    {
        CodeFunction accessor = Substitute.For<CodeFunction>();
        TextPoint start = editor.TextPointAtLineStart(lines.Start);
        TextPoint end = editor.TextPointAtLineEnd(lines.End);
        accessor.StartPoint.Returns(start);
        accessor.EndPoint.Returns(end);

        return accessor;
    }

    /// <summary>
    /// Runs the test on an STA thread that the Visual Studio <see cref="ThreadHelper" /> treats as its UI thread, and
    /// restores the <see cref="ThreadHelper" /> state afterwards.
    /// </summary>
    internal static void RunOnVisualStudioUIThread(System.Action test)
    {
        Exception failure = null;
        FieldInfo uiThreadDispatcherField = typeof(ThreadHelper).GetField("uiThreadDispatcher", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo joinableTaskContextField = typeof(ThreadHelper).GetField("_joinableTaskContextCache", BindingFlags.Static | BindingFlags.NonPublic);
        object previousUIThreadDispatcher = uiThreadDispatcherField.GetValue(null);
        object previousJoinableTaskContext = joinableTaskContextField.GetValue(null);
        System.Threading.Thread uiThread = new System.Threading.Thread(() =>
        {
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

/// <summary>
/// A text document of Visual Studio backed by a string: the edit points move through it, and the find and replace of
/// the editor is replaced by a .NET regular expression replacement of the whole text, which has no editor to run in.
/// </summary>
internal sealed class FakeTextEditor : IDisposable
{
    private readonly Dictionary<object, int[]> _positions = new Dictionary<object, int[]>(new ReferenceComparer());

    public FakeTextEditor(string filePath, string language, string text)
    {
        Text = text;

        Document = Substitute.For<Document>();
        TextDocument = Substitute.For<TextDocument>();

        Document.FullName.Returns(filePath);
        Document.Name.Returns(Path.GetFileName(filePath));
        Document.Language.Returns(language);
        Document.ProjectItem.Returns((ProjectItem)null);
        Document.Object("TextDocument").Returns(TextDocument);

        TextDocument.Language.Returns(language);
        TextDocument.Parent.Returns(Document);
        TextDocument.StartPoint.Returns(_ => TextPointAt(0));
        TextDocument.EndPoint.Returns(_ => TextPointAt(Text.Length));

        TextDocumentHelper.TextDocumentSubstitutionOverride = (_, pattern, replacement) =>
            Text = Regex.Replace(Text, pattern, replacement, RegexOptions.Multiline);
    }

    public string Text { get; private set; }

    public Document Document { get; }

    public TextDocument TextDocument { get; }

    /// <summary>
    /// Gets the calls of <c>DeleteWhitespace</c>, each as the offset of the edit point and the direction.
    /// </summary>
    public List<string> DeleteWhitespaceCalls { get; } = [];

    public TextPoint TextPointAtLineStart(int line) => TextPointAt(LineStart(line));

    public TextPoint TextPointAtLineEnd(int line) => TextPointAt(LineEnd(line));

    public void Dispose() => TextDocumentHelper.TextDocumentSubstitutionOverride = null;

    private TextPoint TextPointAt(int offset)
    {
        TextPoint point = Substitute.For<TextPoint>();
        point.Line.Returns(LineOf(offset));
        point.LineCharOffset.Returns(offset - LineStart(LineOf(offset)) + 1);
        point.CreateEditPoint().Returns(_ => EditPointAt(offset));

        _positions[point] = new[] { offset };

        return point;
    }

    private EditPoint EditPointAt(int offset)
    {
        int[] position = { offset };
        EditPoint point = Substitute.For<EditPoint>();
        _positions[point] = position;

        point.Line.Returns(_ => LineOf(position[0]));
        point.AtEndOfDocument.Returns(_ => position[0] == Text.Length);
        point.AtStartOfLine.Returns(_ => position[0] == 0 || Text[position[0] - 1] == '\n');
        point.GetLines(Arg.Any<int>(), Arg.Any<int>()).Returns(ci => Text.Substring(LineStart(ci.ArgAt<int>(0)), LineEnd(ci.ArgAt<int>(1) - 1) - LineStart(ci.ArgAt<int>(0))));
        point.GetText(Arg.Any<object>()).Returns(ci => TextBetween(position[0], PositionOf(ci.Arg<object>())));

        point.When(p => p.LineDown(Arg.Any<int>())).Do(ci => position[0] = LineStart(LineOf(position[0]) + ci.ArgAt<int>(0)));
        point.When(p => p.LineUp(Arg.Any<int>())).Do(ci => position[0] = LineStart(LineOf(position[0]) - ci.ArgAt<int>(0)));
        point.When(p => p.StartOfLine()).Do(_ => position[0] = LineStart(LineOf(position[0])));
        point.When(p => p.EndOfLine()).Do(_ => position[0] = LineEnd(LineOf(position[0])));
        point.When(p => p.Insert(Arg.Any<string>())).Do(ci =>
        {
            string inserted = ci.Arg<string>();
            Text = Text.Insert(position[0], inserted);
            position[0] += inserted.Length;
        });
        point.When(p => p.ReplaceText(Arg.Any<object>(), Arg.Any<string>(), Arg.Any<int>())).Do(ci =>
        {
            int end = PositionOf(ci.Arg<object>());
            int start = Math.Min(position[0], end);
            Text = Text.Substring(0, start) + ci.ArgAt<string>(1) + Text.Substring(Math.Max(position[0], end));
        });
        point.When(p => p.DeleteWhitespace(Arg.Any<vsWhitespaceOptions>())).Do(ci => DeleteWhitespaceCalls.Add(position[0] + ":" + ci.Arg<vsWhitespaceOptions>()));

        return point;
    }

    private int PositionOf(object point) => _positions[point][0];

    private string TextBetween(int first, int second) => Text.Substring(Math.Min(first, second), Math.Abs(second - first));

    private int LineOf(int offset) => 1 + Text.Take(offset).Count(c => c == '\n');

    private int LineStart(int line)
    {
        int start = 0;
        for (int current = 1; current < line; current++)
        {
            int lineBreak = Text.IndexOf('\n', start);
            if (lineBreak < 0)
            {
                return Text.Length;
            }

            start = lineBreak + 1;
        }

        return start;
    }

    private int LineEnd(int line)
    {
        int start = LineStart(line);
        int lineBreak = Text.IndexOf('\n', start);
        int end = lineBreak < 0 ? Text.Length : lineBreak;

        return end > start && Text[end - 1] == '\r' ? end - 1 : end;
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

#pragma warning restore VSTHRD010
