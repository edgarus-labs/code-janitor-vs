using CodeJanitor.Logic.Transformations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="IndentationGuard" />: the indentation of a line may change unless the line starts inside a
/// multi-line string literal, disabled text or a multi-line comment. Each source marks the start of the tested line
/// with <c>|</c>, which is removed before parsing.
/// </summary>
[TestClass]
public sealed class IndentationGuardTests
{
    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C\n{\n|    int x;\n}\n", DisplayName = "member declaration")]
    [DataRow("|using System;\n", DisplayName = "first line of the file")]
    [DataRow("class C\n{\n|    /* one\n       two */\n}\n", DisplayName = "first line of a multi-line comment")]
    [DataRow("class C\n{\n    string S = @\"a\n\";\n|    int x;\n}\n", DisplayName = "line after a verbatim string")]
    [DataRow("class C\n{\n    string S(int x) => $\"\"\"\n        {\n|            x\n        } text\n        \"\"\";\n}\n", DisplayName = "code in a multi-line interpolation hole")]
    [DataRow("class C\n{\n    string S(int x) =>\n|        $\"\"\"\n        {x}\n        \"\"\";\n}\n", DisplayName = "line starting with the interpolated string start")]
    [DataRow("#if NEVER\nclass Old { }\n#endif\n|class C { }\n", DisplayName = "line after disabled text")]
    [DataRow("class C { }\n#if NEVER\nclass Old { }\n|", DisplayName = "end of a file that ends in disabled text")]
    [DataRow("System.Console.WriteLine(\n|    \"top-level\");\n", DisplayName = "continuation line of a top-level statement")]
    public void CanChangeIndentation_OutsideStringsCommentsAndDisabledText(string markedSource)
    {
        Assert.IsTrue(CanChangeIndentation(markedSource));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C\n{\n    string S = @\"first\n|    second\";\n}\n", DisplayName = "verbatim string")]
    [DataRow("class C\n{\n    string S = \"\"\"\n|        raw\n        \"\"\";\n}\n", DisplayName = "raw string")]
    [DataRow("class C\n{\n    string S(int x) => $\"\"\"\n|        {x} raw\n        \"\"\";\n}\n", DisplayName = "interpolated raw string text")]
    [DataRow("class C\n{\n    string S(int x) => $@\"{x}\n|    }} namespace {{\";\n}\n", DisplayName = "interpolated verbatim string text after a hole")]
    [DataRow("class C\n{\n    string S(int x) => $\"\"\"\n        {x}\n|        \"\"\";\n}\n", DisplayName = "closing delimiter of an interpolated raw string")]
    [DataRow("class C\n{\n    /* one\n|       two */\n}\n", DisplayName = "multi-line comment")]
    [DataRow("class C\n{\n    /** <summary>\n|     * doc</summary> */\n    int x;\n}\n", DisplayName = "multi-line documentation comment")]
    [DataRow("#if NEVER\nclass Old\n{\n|    int x;\n}\n#endif\n", DisplayName = "disabled text")]
    [DataRow("#if NEVER\n|class Old { }\n#endif\n", DisplayName = "first line of disabled text")]
    public void CannotChangeIndentation_InsideStringsCommentsAndDisabledText(string markedSource)
    {
        Assert.IsFalse(CanChangeIndentation(markedSource));
    }

    private static bool CanChangeIndentation(string markedSource)
    {
        int lineStart = markedSource.IndexOf('|');
        SyntaxNode root = CSharpSyntaxTree.ParseText(markedSource.Remove(lineStart, 1)).GetRoot();

        return IndentationGuard.CanChangeIndentation(root, lineStart);
    }
}
