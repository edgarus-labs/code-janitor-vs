using System.Text.RegularExpressions;

namespace CodeJanitor.Model.Comments.Options;

/// <summary>
/// Comment specific options for the formatter.
/// </summary>

internal sealed class CommentOptions
{
    public string Prefix { get; internal set; }

    public Regex Regex { get; internal set; }
}