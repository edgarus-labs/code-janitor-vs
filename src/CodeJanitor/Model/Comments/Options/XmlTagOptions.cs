using CodeJanitor.Properties;

namespace CodeJanitor.Model.Comments.Options;

/// <summary>
/// presents a set of formatting options that control how XML tags are rendered, including aspects such as casing, indentation, content spacing, and element splitting.
/// </summary>
public sealed class XmlTagOptions : IXmlTagOptions
{
    public XmlTagOptions()
    {
    }

    public XmlTagOptions(FormatterOptionsXmlTag tag, IXmlTagOptions fallback)
    {
        Case = tag.Case != XmlTagCase.Default ? tag.Case : fallback.Case != XmlTagCase.Default ? fallback.Case : XmlTagCase.Keep;
        Indent = tag.Indent ?? fallback.Indent;
        KeepTogether = tag.KeepTogether ?? fallback.KeepTogether;
        Literal = tag.Literal ?? false;
        SpaceContent = tag.SpaceContent ?? fallback.SpaceContent;
        SpaceSelfClosing = tag.SpaceSelfClosing ?? fallback.SpaceSelfClosing;
        Split = tag.Split != XmlTagNewLine.Default ? tag.Split : fallback.Split != XmlTagNewLine.Default ? fallback.Split : XmlTagNewLine.Content;
    }

    /// <summary>
    /// Gets or sets the case.
    /// </summary>
    public XmlTagCase Case { get; set; }

    /// <summary>
    /// Gets or sets the indent.
    /// </summary>
    public int Indent { get; set; }

    /// <summary>
    /// Gets or sets the keep together.
    /// </summary>
    public bool KeepTogether { get; set; }

    /// <summary>
    /// Gets or sets the literal.
    /// </summary>
    public bool Literal { get; set; }

    /// <summary>
    /// Gets or sets the space content.
    /// </summary>
    public bool SpaceContent { get; set; }

    /// <summary>
    /// Gets or sets the space self closing.
    /// </summary>
    public bool SpaceSelfClosing { get; set; }

    /// <summary>
    /// Gets or sets the split.
    /// </summary>
    public XmlTagNewLine Split { get; set; }

    /// <summary>
    /// Converts a Settings object into a new XmlTagOptions instance by mapping formatting flags and indentation settings, with no side effects or exceptions.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>A XmlTagOptions value produced by this method.</returns>

    internal static XmlTagOptions FromSettings(Settings settings)
    {
        return new XmlTagOptions
        {
            Case = settings.Formatting_CommentXmlTagsToLowerCase ? XmlTagCase.LowerCase : XmlTagCase.Keep,
            Indent = settings.Formatting_CommentXmlValueIndent,
            KeepTogether = settings.Formatting_CommentXmlKeepTagsTogether,
            Literal = false,
            SpaceContent = settings.Formatting_CommentXmlSpaceTags,
            SpaceSelfClosing = settings.Formatting_CommentXmlSpaceSingleTags,
            Split = settings.Formatting_CommentXmlSplitAllTags ? XmlTagNewLine.Always : XmlTagNewLine.Default
        };
    }
}
