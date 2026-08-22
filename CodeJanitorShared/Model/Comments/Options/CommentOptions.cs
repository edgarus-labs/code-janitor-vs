using System.Text.RegularExpressions;

namespace CodeJanitor.Model.Comments.Options;

/// <summary>
/// Comment specific options for the formatter.
/// </summary>

internal sealed class CommentOptions
{
    /// <summary>
    /// Gets or sets the prefix.
    /// </summary>
    public string Prefix { get; internal set; }

    /// <summary>
    /// Gets or sets the regex.
    /// </summary>
    public Regex Regex { get; internal set; }
}
