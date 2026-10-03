using CodeJanitor.Logic.Transformations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Transformations;

/// <summary>
/// Unit tests for <see cref="ReturnThrowBlankLinePaddingConverter" />.
/// Pure transformation tests (no Visual Studio / EnvDTE required).
/// </summary>
[TestClass]
public sealed class ReturnThrowBlankLinePaddingConverterTests
{
    private ISourceTransformation _converter;

    [TestInitialize]
    public void TestInitialize() => _converter = new ReturnThrowBlankLinePaddingConverter();

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void InsertsBlankLineBeforeReturn_WhenPrecededByOtherStatements()
    {
        string input =
            "class C\r\n{\r\n    int M()\r\n    {\r\n        int x = 1;\r\n        return x;\r\n    }\r\n}\r\n";
        string expected =
            "class C\r\n{\r\n    int M()\r\n    {\r\n        int x = 1;\r\n\r\n        return x;\r\n    }\r\n}\r\n";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void InsertsBlankLineBeforeThrow_WhenPrecededByOtherStatements()
    {
        string input =
            "class C\r\n{\r\n    void M()\r\n    {\r\n        int x = 1;\r\n        throw new System.Exception();\r\n    }\r\n}\r\n";
        string expected =
            "class C\r\n{\r\n    void M()\r\n    {\r\n        int x = 1;\r\n\r\n        throw new System.Exception();\r\n    }\r\n}\r\n";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void DoesNotInsertBlankLine_WhenReturnIsTheOnlyStatement()
    {
        string input = "class C\r\n{\r\n    int M()\r\n    {\r\n        return 1;\r\n    }\r\n}\r\n";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void DoesNotInsertBlankLine_WhenReturnIsFirstStatementFollowedByUnreachableCode()
    {
        // Unusual (and would normally trigger CS0162), but the return is still the first
        // statement in its block, so there is nothing preceding it to separate it from.
        string input = "class C\r\n{\r\n    int M()\r\n    {\r\n        return 1;\r\n#pragma warning disable CS0162\r\n        int x = 2;\r\n#pragma warning restore CS0162\r\n    }\r\n}\r\n";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void IsIdempotent_WhenBlankLineAlreadyPresent()
    {
        string input =
            "class C\r\n{\r\n    int M()\r\n    {\r\n        int x = 1;\r\n\r\n        return x;\r\n    }\r\n}\r\n";

        Assert.AreEqual(input, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void InsertsBlankLineBeforeReturn_InsideIfBlock()
    {
        string input =
            "class C\r\n{\r\n    int M(bool b)\r\n    {\r\n        if (b)\r\n        {\r\n            int y = 1;\r\n            return y;\r\n        }\r\n        return 0;\r\n    }\r\n}\r\n";
        string expected =
            "class C\r\n{\r\n    int M(bool b)\r\n    {\r\n        if (b)\r\n        {\r\n            int y = 1;\r\n\r\n            return y;\r\n        }\r\n\r\n        return 0;\r\n    }\r\n}\r\n";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void HandlesMultipleCandidatesInTheSameFile()
    {
        string input =
            "class C\r\n{\r\n    int M1()\r\n    {\r\n        int x = 1;\r\n        return x;\r\n    }\r\n\r\n    int M2()\r\n    {\r\n        int y = 2;\r\n        return y;\r\n    }\r\n}\r\n";
        string expected =
            "class C\r\n{\r\n    int M1()\r\n    {\r\n        int x = 1;\r\n\r\n        return x;\r\n    }\r\n\r\n    int M2()\r\n    {\r\n        int y = 2;\r\n\r\n        return y;\r\n    }\r\n}\r\n";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void EmptySource_ReturnsUnchanged() => Assert.AreEqual(string.Empty, _converter.Apply(string.Empty));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NullSource_ReturnsNull() => Assert.IsNull(_converter.Apply(null));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void NameAndSingleLineIfWithoutBlock_HandledCorrectly()
    {
        Assert.AreEqual("Blank Line Before Return/Throw", _converter.Name);

        string input = "class C { void M(bool b) { if (b) return; } }";
        Assert.AreEqual(input, _converter.Apply(input));

        string unixInput = "class C\n{\n    int M()\n    {\n        int x = 1;\n        return x;\n    }\n}\n";
        string expectedUnix = "class C\n{\n    int M()\n    {\n        int x = 1;\n\n        return x;\n    }\n}\n";
        Assert.AreEqual(expectedUnix, _converter.Apply(unixInput));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    public void TopLevelStatementsFile_PadsReturnsInLocalFunctionsButNotTheTopLevelReturn()
    {
        string input =
            "using System;\r\n\r\nConsole.WriteLine(\"hi\");\r\nreturn Compute();\r\n\r\n" +
            "static int Compute()\r\n{\r\n    var x = 1;\r\n    return x;\r\n}\r\n";
        string expected =
            "using System;\r\n\r\nConsole.WriteLine(\"hi\");\r\nreturn Compute();\r\n\r\n" +
            "static int Compute()\r\n{\r\n    var x = 1;\r\n\r\n    return x;\r\n}\r\n";

        Assert.AreEqual(expected, _converter.Apply(input));
    }

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("class C\r\n{\r\n    int M(int x)\r\n    {\r\n        switch (x)\r\n        {\r\n            case 1:\r\n                Log();\r\n                return 1;\r\n        }\r\n        return 0;\r\n    }\r\n}\r\n",
        "class C\r\n{\r\n    int M(int x)\r\n    {\r\n        switch (x)\r\n        {\r\n            case 1:\r\n                Log();\r\n                return 1;\r\n        }\r\n\r\n        return 0;\r\n    }\r\n}\r\n",
        DisplayName = "switch section statements are not a block")]
    [DataRow("class C\r\n{\r\n    string M(string s)\r\n    {\r\n        Log();\r\n        var t = s ?? throw new System.Exception();\r\n    }\r\n}\r\n",
        "class C\r\n{\r\n    string M(string s)\r\n    {\r\n        Log();\r\n        var t = s ?? throw new System.Exception();\r\n    }\r\n}\r\n",
        DisplayName = "throw expressions are not statements")]
    [DataRow("class C\r\n{\r\n    System.Collections.Generic.IEnumerable<int> M()\r\n    {\r\n        Log();\r\n        yield return 1;\r\n    }\r\n}\r\n",
        "class C\r\n{\r\n    System.Collections.Generic.IEnumerable<int> M()\r\n    {\r\n        Log();\r\n        yield return 1;\r\n    }\r\n}\r\n",
        DisplayName = "yield return is not padded")]
    [DataRow("class C\r\n{\r\n    void M()\r\n    {\r\n        Run(() =>\r\n        {\r\n            Log();\r\n            return;\r\n        });\r\n    }\r\n}\r\n",
        "class C\r\n{\r\n    void M()\r\n    {\r\n        Run(() =>\r\n        {\r\n            Log();\r\n\r\n            return;\r\n        });\r\n    }\r\n}\r\n",
        DisplayName = "lambda block body")]
    [DataRow("class C\r\n{\r\n    async System.Threading.Tasks.Task<int> M()\r\n    {\r\n        await Log();\r\n        throw new System.Exception();\r\n    }\r\n}\r\n",
        "class C\r\n{\r\n    async System.Threading.Tasks.Task<int> M()\r\n    {\r\n        await Log();\r\n\r\n        throw new System.Exception();\r\n    }\r\n}\r\n",
        DisplayName = "async method")]
    [DataRow("class C\r\n{\r\n    string M()\r\n    {\r\n        var x = @\"a\r\nreturn b;\";\r\n        return x;\r\n    }\r\n}\r\n",
        "class C\r\n{\r\n    string M()\r\n    {\r\n        var x = @\"a\r\nreturn b;\";\r\n\r\n        return x;\r\n    }\r\n}\r\n",
        DisplayName = "multi-line verbatim string before the return")]
    [DataRow("class C\r\n{\r\n    string M()\r\n    {\r\n        var x = \"\"\"\r\n            Log();\r\n            return y;\r\n            \"\"\";\r\n        return x;\r\n    }\r\n}\r\n",
        "class C\r\n{\r\n    string M()\r\n    {\r\n        var x = \"\"\"\r\n            Log();\r\n            return y;\r\n            \"\"\";\r\n\r\n        return x;\r\n    }\r\n}\r\n",
        DisplayName = "raw string containing a return is not touched")]
    [DataRow("class C\r\n{\r\n    int M()\r\n    {\r\n        Log();\r\n#if NEVER_DEFINED\r\n        Log();\r\n        return 1;\r\n#endif\r\n        return 2;\r\n    }\r\n}\r\n",
        "class C\r\n{\r\n    int M()\r\n    {\r\n        Log();\r\n#if NEVER_DEFINED\r\n        Log();\r\n        return 1;\r\n#endif\r\n        return 2;\r\n    }\r\n}\r\n",
        DisplayName = "return directly below #endif gets no blank line after the directive")]
    [DataRow("class C\r\n{\r\n    int M()\r\n    {\r\n        Log();\r\n#if NEVER_DEFINED\r\n        Log();\r\n#else\r\n        return 2;\r\n#endif\r\n    }\r\n}\r\n",
        "class C\r\n{\r\n    int M()\r\n    {\r\n        Log();\r\n#if NEVER_DEFINED\r\n        Log();\r\n#else\r\n        return 2;\r\n#endif\r\n    }\r\n}\r\n",
        DisplayName = "return directly below #else gets no blank line after the directive")]
    [DataRow("class C\r\n{\r\n    int M()\r\n    {\r\n        Log();\r\n#region Exit\r\n        return 2;\r\n#endregion\r\n    }\r\n}\r\n",
        "class C\r\n{\r\n    int M()\r\n    {\r\n        Log();\r\n#region Exit\r\n        return 2;\r\n#endregion\r\n    }\r\n}\r\n",
        DisplayName = "return directly below #region gets no blank line after the directive")]
    [DataRow("class C\r\n{\r\n    string M()\r\n    {\r\n        var x = @\"a\r\n#b\";\r\n        return x;\r\n    }\r\n}\r\n",
        "class C\r\n{\r\n    string M()\r\n    {\r\n        var x = @\"a\r\n#b\";\r\n\r\n        return x;\r\n    }\r\n}\r\n",
        DisplayName = "verbatim string line that looks like a directive is not a directive")]
    [DataRow("class C\r\n{\r\n\tint M()\r\n\t{\r\n\t\tLog();\r\n\t\treturn 1;\r\n\t}\r\n}",
        "class C\r\n{\r\n\tint M()\r\n\t{\r\n\t\tLog();\r\n\r\n\t\treturn 1;\r\n\t}\r\n}",
        DisplayName = "tabs and no final newline")]
    [DataRow("class C\r\n{\r\n    int M()\r\n    {\r\n        var x = 1;\r\n        // explain\r\n        return x;\r\n    }\r\n}\r\n",
        "class C\r\n{\r\n    int M()\r\n    {\r\n        var x = 1;\r\n\r\n        // explain\r\n        return x;\r\n    }\r\n}\r\n",
        DisplayName = "comment above the return stays attached to it")]
    [DataRow("class C\r\n{\n    int M()\r\n    {\r\n        var x = 1;\r\n        return x;\r\n    }\r\n}\r\n",
        "class C\r\n{\n    int M()\r\n    {\r\n        var x = 1;\r\n\r\n        return x;\r\n    }\r\n}\r\n",
        DisplayName = "mixed line endings")]
    [DataRow("class C\r{\r    int M()\r    {\r        Log();\r        return 1;\r    }\r}\r",
        "class C\r{\r    int M()\r    {\r        Log();\r\r        return 1;\r    }\r}\r",
        DisplayName = "carriage-return-only line breaks")]
    public void RealisticInputs_ArePaddedOnlyBeforeBlockLevelReturnAndThrow(string input, string expected) => Assert.AreEqual(expected, _converter.Apply(input));

    [TestMethod]
    [TestCategory("Transformations UnitTests")]
    [DataRow("   \r\n\t\r\n", DisplayName = "whitespace only")]
    [DataRow("class C { void M( { Log(); return; }", DisplayName = "syntax errors on one line")]
    [DataRow("class C\r\n{\r\n    void M() { Foo(); return; }\r\n}\r\n", DisplayName = "return on the same line as the previous statement")]
    public void InputsWithoutSeparateReturnLines_AreUnchanged(string input) => Assert.AreEqual(input, _converter.Apply(input));
}
