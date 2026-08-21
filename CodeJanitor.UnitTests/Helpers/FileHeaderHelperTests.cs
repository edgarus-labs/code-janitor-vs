using CodeJanitor.Helpers;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Xunit;

namespace CodeJanitor.UnitTests.Helpers;

// remark: EnvDTE only counts 1 character per newline

public class FileHeaderHelperTests
{
    [Theory]
    [InlineData(CodeLanguage.CSharp, "// some CSharp test header\r\n")]
    [InlineData(CodeLanguage.CSharp, "/* some C# \r\n* test\r\n header\r\n *\r\n *\r\n* !!! */\r\n")]
    [InlineData(CodeLanguage.JavaScript, "// some JavaScript test header\r\n")]
    [InlineData(CodeLanguage.JavaScript, "/* some JS \r\n* test \r\n header !!! */\r\n")]
    [InlineData(CodeLanguage.LESS, "// some LESS test header\r\n")]
    [InlineData(CodeLanguage.LESS, "/* some LESS \r\n* test \r\n header !!! */\r\n")]
    [InlineData(CodeLanguage.SCSS, "// some SCSS test header\r\n")]
    [InlineData(CodeLanguage.SCSS, "/* some SCSS\r\n * test\r\n header !!! */\r\n")]
    [InlineData(CodeLanguage.TypeScript, "// some TypeScript test header\r\n")]
    [InlineData(CodeLanguage.TypeScript, "/* some TypeScript\r\n* test\r\n header !!! */\r\n")]
    [InlineData(CodeLanguage.HTML, "<!-- some HTML test header -->\r\n")]
    [InlineData(CodeLanguage.HTML, "<!-- some HTML \r\n test\r\n header ! -->")]
    [InlineData(CodeLanguage.XAML, "<!-- some XAML test header -->\r\n")]
    [InlineData(CodeLanguage.XAML, "<!-- some XAML \r\n test\r\n header ! -->")]
    [InlineData(CodeLanguage.XML, "<!-- some XML test header -->\r\n")]
    [InlineData(CodeLanguage.XML, "<!-- some XML \r\n test\r\n header ! -->")]
    [InlineData(CodeLanguage.CSS, "/* some CSS test header */\r\n")]
    [InlineData(CodeLanguage.CSS, "/* some CSS \r\ntest \r\nheader */\r\n")]
    [InlineData(CodeLanguage.CPlusPlus, "// some CPlusPlus test header\r\n")]
    [InlineData(CodeLanguage.CPlusPlus, "/* some C++ \r\ntest\r\n header */\r\n")]
    [InlineData(CodeLanguage.PHP, "// some PHP test header\r\n")]
    [InlineData(CodeLanguage.PHP, "# some PHP test header\r\n")]
    [InlineData(CodeLanguage.PHP, "/* some PHP \r\n test\r\nheader */")]
    [InlineData(CodeLanguage.PowerShell, "# some PowerShell test header\r\n")]
    [InlineData(CodeLanguage.PowerShell, "<# some PowerShell\r\ntest\r\n  header ! #>")]
    [InlineData(CodeLanguage.R, "# some R test header\r\n")]
    [InlineData(CodeLanguage.FSharp, "// some F# test header\r\n")]
    [InlineData(CodeLanguage.FSharp, "(* some F#\r\n test \r\nheader\r\n*)")]
    [InlineData(CodeLanguage.VisualBasic, "' some PowerShell test header\r\n")]
    public void GetHeaderLengthLanguage(CodeLanguage language, string text)
    {
        var headerLength = FileHeaderHelper.GetHeaderLength(language, text);

        Assert.True(headerLength > 0, $"Expecting value > 0, found {headerLength}");
    }

    [Theory]
    [InlineData(CodeLanguage.CSharp, "# some CSharp test header\r\n")]
    [InlineData(CodeLanguage.JavaScript, "<!-- some JavaScript test header -->\r\n")]
    [InlineData(CodeLanguage.LESS, "' some LESS test header\r\n")]
    [InlineData(CodeLanguage.SCSS, "# some SCSS test header\r\n")]
    [InlineData(CodeLanguage.TypeScript, "<!-- some TypeScript test header -->\r\n")]
    [InlineData(CodeLanguage.HTML, "// some HTML test header\r\n")]
    [InlineData(CodeLanguage.XAML, "/* some XAML test header */\r\n")]
    [InlineData(CodeLanguage.XML, "' some XML test header\r\n")]
    [InlineData(CodeLanguage.CSS, "// some CSS test header\r\n")]
    [InlineData(CodeLanguage.CPlusPlus, "# some CPlusPlus test header\r\n")]
    [InlineData(CodeLanguage.PHP, "' some PHP test header\r\n")]
    [InlineData(CodeLanguage.PowerShell, "/* some PowerShell\r\ntest\r\n  header ! */")]
    [InlineData(CodeLanguage.R, "// some R test header\r\n")]
    [InlineData(CodeLanguage.FSharp, "/* some F#\r\n test \r\nheader\r\n*/")]
    [InlineData(CodeLanguage.VisualBasic, "// some PowerShell test header\r\n")]
    public void GetHeaderLengthLanguageWithWrongTags(CodeLanguage language, string text)
    {
        var headerLength = FileHeaderHelper.GetHeaderLength(language, text);

        Assert.Equal(0, headerLength);
    }

    [Theory]
    [InlineData("/* */", "/*", "*/")]
    [InlineData("/* \r\n   Copyright © 2021 \r\n */", "/*", "*/")]
    [InlineData("<!-- -->", "<!--", "-->")]
    [InlineData("<!-- =========== \r\n \r\n  Copyright © 2021 \r\n \r\n =========== -->", "<!--", "-->")]
    [InlineData("<# #>", "<#", "#>")]
    [InlineData("<# //////////////// \r\n   Copyright © 2021 \r\n //////////////// #>", "<#", "#>")]
    [InlineData("(* *)", "(*", "*)")]
    [InlineData("(* ~~~~ \r\n ~~ \r\n  Copyright © 2021 \r\n ~~ \r\n ~~~~ *)", "(*", "*)")]
    public void GetHeaderLengthMultiLine(string text, string tagStart, string tagEnd)
    {
        var headerLength = FileHeaderHelper.GetHeaderLength(text, tagStart, tagEnd, false);
        var expectedLength = text.Length - Regex.Matches(text, Environment.NewLine).Count + 1;

        Assert.Equal(expectedLength, headerLength);
    }

    [Theory]
    [InlineData("using System;\r\n/* */", "/*", "*/", 6)]
    [InlineData("using EnvDTE;\r\nusing System;\r\nusing CodeJanitor.Helpers;\r\n/* \r\n   Copyright © 2021 \r\n */", "/*", "*/", 29)]
    public void GetHeaderLengthMultiLineSkipUsings(string text, string tagStart, string tagEnd, int expectedLength)
    {
        var headerLength = FileHeaderHelper.GetHeaderLength(text, tagStart, tagEnd, true);

        Assert.Equal(expectedLength, headerLength);
    }

    [Theory]
    [InlineData("\r\n/* */", "/*", "*/")]
    [InlineData("\r\n\r\n\r\n/*    Copyright © 2021  */", "/*", "*/")]
    [InlineData("\r\n\r\n<!-- -->", "<!--", "-->")]
    public void GetHeaderLengthMultiLineWithEmptyLines(string text, string tagStart, string tagEnd)
    {
        var headerLength = FileHeaderHelper.GetHeaderLength(text, tagStart, tagEnd, false);
        var expectedLength = text.Length - Regex.Matches(text, Environment.NewLine).Count + 1;

        Assert.Equal(expectedLength, headerLength);
    }

    [Theory]
    [InlineData("using EnvDTE;\r\nusing System;\r\nusing CodeJanitor.Helpers;\r\n\r\n\r\n/* \r\n   Copyright © 2021 \r\n */", "/*", "*/", 29)]
    [InlineData("using System;\r\n\r\n<!-- -->", "<!--", "-->", 9)]
    public void GetHeaderLengthMultiLineWithEmptyLinesSkipUsings(string text, string tagStart, string tagEnd, int expectedLength)
    {
        var headerLength = FileHeaderHelper.GetHeaderLength(text, tagStart, tagEnd, true);

        Assert.Equal(expectedLength, headerLength);
    }

    [Theory]
    [InlineData("", "/*", "*/")]
    [InlineData("some text", "/*", "*/")]
    [InlineData("/*     \r\n \r\n      ", "/*", "*/")]
    [InlineData("/* header */", "/*", "*>")]
    [InlineData("*/           ", "/*", "*/")]
    public void GetHeaderLengthMultiLineZero(string text, string tagStart, string tagEnd)
    {
        var headerLength = FileHeaderHelper.GetHeaderLength(text, tagStart, tagEnd, false);

        Assert.Equal(0, headerLength);
    }

    [Theory]
    [InlineData("using System;", "/*", "*/")]
    [InlineData("using System;\r\n/*     \r\n \r\n      ", "/*", "*/")]
    [InlineData("using System;\r\n/* header */", "/*", "*>")]
    [InlineData("using System;\r\n*/           ", "/*", "*/")]
    public void GetHeaderLengthMultiLineZeroSkipUsings(string text, string tagStart, string tagEnd)
    {
        var headerLength = FileHeaderHelper.GetHeaderLength(text, tagStart, tagEnd, true);

        Assert.Equal(0, headerLength);
    }

    [Theory]
    [InlineData("//", "// header fdsfndksjnfe\r\n")]
    [InlineData("#", "# header eropf opekfropze jaqozerijfg uihgsdfidnffdsfg\r\n")]
    [InlineData("'", "'header  sdvq qsudg bqudgybuydzeau _(àç_ç*ù$^$*ùàyguozadgzao\r\n")]
    [InlineData("//", "// ====================\r\n// Copyright\r\n// ====================\r\n")]
    [InlineData("#", "# ==============\r\n#   Copyright !\r\n#    ~~\r\n# ==============\r\n")]
    [InlineData("'", "' ==============\r\n' some text\r\n'   Copyright !\r\n' ==============\r\n")]
    public void GetHeaderLengthMultiSingleLine(string tag, string text)
    {
        var headerLength = FileHeaderHelper.GetHeaderLength(text, tag, false);
        var expectedLength = text.Length - Regex.Matches(text, Environment.NewLine).Count;

        Assert.Equal(expectedLength, headerLength);
    }

    [Theory]
    [InlineData("//", "using System;\r\n// header\r\nnamespace ", 11)]
    [InlineData("//", "using System;\r\n// header I\r\n// header II\r\nnamespace ", 26)]
    public void GetHeaderLengthMultiSingleLineSkipUsings(string tag, string text, int expectedLength)
    {
        var headerLength = FileHeaderHelper.GetHeaderLength(text, tag, true);

        Assert.Equal(expectedLength, headerLength);
    }

    [Theory]
    [InlineData("//", "//  header \r\nnamespace\r\npublic class Test\r\n", 12)]
    [InlineData("//", "//  header \r\n// more header \r\n some code\r\n", 28)]
    [InlineData("#", "# some header\r\nnamespace\r\npublic class Test\r\n", 14)]
    [InlineData("'", "' some header\r\nnamespace\r\npublic class Test\r\n", 14)]
    public void GetHeaderLengthMultiSingleLineWithCode(string tag, string text, int expectedLength)
    {
        var headerLength = FileHeaderHelper.GetHeaderLength(text, tag, false);

        Assert.Equal(expectedLength, headerLength);
    }

    [Theory]
    [InlineData("//", "using System;\r\n\r\n//  header \r\nnamespace System.Windows;\r\npublic class Test\r\n", 13)]
    [InlineData("//", "using EnvDTE;\r\nusing System;\r\nusing CodeJanitor.Helpers;\r\n\r\n\r\n//  header \r\n// more header \r\nnamespace CodeJanitor;\r\n", 29)]
    [InlineData("//", "using System;\r\n\r\n//  header \r\n[assembly: AssemblyTitle(\"CodeJanitor.UnitTests\")]\r\nnamespace ", 13)]
    public void GetHeaderLengthMultiSingleLineWithCodeSkipUsings(string tag, string text, int expectedLength)
    {
        var headerLength = FileHeaderHelper.GetHeaderLength(text, tag, true);

        Assert.Equal(expectedLength, headerLength);
    }

    [Theory]
    [InlineData("//", "\r\n//  header \r\n// header\r\nnamespace\r\n//not header\r\n public class Test\r\n", 23)]
    [InlineData("//", "\r\n\r\n\r\n//  header \r\n// more header \r\n some code\r\n", 31)]
    [InlineData("#", "\r\n# some header\r\nnamespace\r\npublic class Test\r\n", 15)]
    [InlineData("'", "\r\n' some header\r\nnamespace\r\npublic class Test\r\n", 15)]
    public void GetHeaderLengthMultiSingleLineWithEmptyLines(string tag, string text, int expectedLength)
    {
        var headerLength = FileHeaderHelper.GetHeaderLength(text, tag, false);

        Assert.Equal(expectedLength, headerLength);
    }

    [Theory]
    [InlineData("//", "using System;\r\n\r\n\r\n//  header \r\n// header\r\nnamespace \r\n//not header\r\n public class Test\r\n", 23)]
    [InlineData("//", "using EnvDTE;\r\nusing System;\r\nusing CodeJanitor.Helpers;\r\n//  header \r\n// more header \r\n namespace System.Text;\r\n{\r\n", 29)]
    public void GetHeaderLengthMultiSingleLineWithEmptyLinesSkipUsings(string tag, string text, int expectedLength)
    {
        var headerLength = FileHeaderHelper.GetHeaderLength(text, tag, true);

        Assert.Equal(expectedLength, headerLength);
    }

    [Theory]
    [InlineData("using ", "", "", 0)]
    [InlineData(" using", "using System;\r\nnamespace CodeJanitor", "namespace ", 0)]
    [InlineData("using ", " using System;\r\nnamespace CodeJanitor", "namespace ", 1)]
    [InlineData("using ", "using System;\r\nusing System.Collections;\r\n\r\nnamespace CodeJanitor", "namespace ", 2)]
    [InlineData("using ", "using System;\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\nusing System.Collections;\r\n\r\nnamespace CodeJanitor", "namespace ", 10)]
    public void GetNbLinesToSkip(string patternToFind, string text, string limit, int expectedNbLines)
    {
        var nbLines = FileHeaderHelper.GetNbLinesToSkip(patternToFind, text, new List<string>() { limit });

        Assert.Equal(expectedNbLines, nbLines);
    }
}
