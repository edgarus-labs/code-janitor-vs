using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Model.Comments;
using CodeJanitor.Model.Comments.Options;
using System;

namespace CodeJanitor.UnitTests.Formatting;

internal sealed class CommentFormatHelper
{
    /// <summary>
    /// This convenience overload delegates to the four-parameter AssertEqualAfterFormat overload, passing null for the two missing arguments and forwarding the optional FormatterOptions, so its behavior and any side effects are entirely determined by that underlying method.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="options">The options.</param>
    /// <returns>A string value produced by this method.</returns>

    public static string AssertEqualAfterFormat(
             string text,
             Action<FormatterOptions> options = null)
    {
        return AssertEqualAfterFormat(text, null, null, options);
    }

    /// <summary>
    /// This convenience overload delegates to the four-argument Assert.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected.</param>
    /// <param name="options">The options.</param>
    /// <returns>A string value produced by this method.</returns>

    public static string AssertEqualAfterFormat(
                  string text,
            string expected,
                  Action<FormatterOptions> options = null)
    {
        return AssertEqualAfterFormat(text, expected, null, options);
    }

    /// <summary>
    /// Formats the given text via CodeComment.Format, applies optional FormatterOptions from default settings via the options callback, asserts the result equals expected (or the original text if expected is null), and returns the formatted result.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected.</param>
    /// <param name="prefix">The prefix.</param>
    /// <param name="options">The options.</param>
    /// <returns>A string value produced by this method.</returns>

    public static string AssertEqualAfterFormat(
            string text,
            string expected,
            string prefix,
            Action<FormatterOptions> options = null)
    {
        var result = CodeComment.Format(text, prefix, options);
        var fOptions = FormatterOptions.FromSettings(Properties.Settings.Default);
        options?.Invoke(fOptions);
        Assert.AreEqual(expected ?? text, result);

        return result;
    }
}
