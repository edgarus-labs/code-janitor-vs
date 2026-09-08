using CodeJanitor.Properties;
using System;

namespace CodeJanitor.Model.Comments.Options;

/// <summary>
/// Document wide options for the comment formatter.
/// </summary>

public sealed class FormatterOptions
{
    /// <summary>
    /// The list of comment prefix tokens to ignore while formatting the comment.
    /// </summary>
    public string[] IgnoreTokens { get; set; }

    /// <summary>
    /// Do not wrap to a newline for only a single word.
    /// </summary>
    public bool SkipWrapOnLastWord { get; set; }

    /// <summary>
    /// Gets or sets the tab size.
    /// </summary>
    public int TabSize { get; set; } = 4;

    /// <summary>
    /// Gets or sets the wrap column.
    /// </summary>
    public int WrapColumn { get; set; }

    /// <summary>
    /// Gets or sets the xml.
    /// </summary>
    public FormatterOptionsXml Xml { get; set; }

    /// <summary>
    /// Creates a FormatterOptions instance from the given settings by copying comment wrap column and skip-wrap flags, building nested XML options via FormatterOptionsXml.FromSettings, with no side effects or exceptions.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>A FormatterOptions value produced by this method.</returns>

    internal static FormatterOptions FromSettings(Settings settings)
    {
        return new FormatterOptions
        {
            WrapColumn = settings.Formatting_CommentWrapColumn,
            SkipWrapOnLastWord = settings.Formatting_CommentSkipWrapOnLastWord,
            Xml = FormatterOptionsXml.FromSettings(settings)
        };
    }

    /// <summary>
    /// Invokes the provided action on the current instance if non-null, mutating it, then returns the same instance for fluent chaining, with no-op behavior when the action is null and no thrown exceptions.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <returns>A FormatterOptions value produced by this method.</returns>

    internal FormatterOptions Set(Action<FormatterOptions> action)
    {
        action?.Invoke(this);

        return this;
    }
}
