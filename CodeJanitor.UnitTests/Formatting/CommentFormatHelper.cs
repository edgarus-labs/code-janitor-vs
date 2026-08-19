using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Model.Comments;
using CodeJanitor.Model.Comments.Options;
using System;

namespace CodeJanitor.UnitTests.Formatting;

internal sealed class CommentFormatHelper
{
    public static string AssertEqualAfterFormat(
         string text,
         Action<FormatterOptions> options = null)
    {
        return AssertEqualAfterFormat(text, null, null, options);
    }

    public static string AssertEqualAfterFormat(
              string text,
        string expected,
              Action<FormatterOptions> options = null)
    {
        return AssertEqualAfterFormat(text, expected, null, options);
    }

    public static string AssertEqualAfterFormat(
        string text,
        string expected,
        string prefix,
        Action<FormatterOptions> options = null)
    {
        var result = CodeComment.Format(text, prefix, options);
        Assert.AreEqual(expected ?? text, result);

        return result;
    }
}