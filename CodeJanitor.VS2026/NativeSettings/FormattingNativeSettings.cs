using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace CodeJanitor.NativeSettings
{
    // Formatting category, migrated from CodeJanitorShared/Properties/Settings.settings (Formatting_*).
    // Formatting_IgnoreLinesStartingWith is intentionally NOT migrated - it's a StringCollection
    // setting with no direct native Setting equivalent.
#pragma warning disable VSEXTPREVIEW_SETTINGS // Settings APIs are preview.
    internal static class FormattingNativeSettings
    {
        [VisualStudioContribution]
        internal static SettingCategory FormattingCategory { get; } = new("codeJanitorFormatting", "Formatting", NativeSettingCategories.RootCategory);

        [VisualStudioContribution]
        internal static Setting.Boolean CommentRunDuringCleanup { get; } = new(
            "codeJanitorFormattingCommentRunDuringCleanup", "Run comment formatting during cleanup", FormattingCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean CommentSkipWrapOnLastWord { get; } = new(
            "codeJanitorFormattingCommentSkipWrapOnLastWord", "Skip wrap on last word", FormattingCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Integer CommentWrapColumn { get; } = new(
            "codeJanitorFormattingCommentWrapColumn", "Comment wrap column", FormattingCategory, defaultValue: 100);

        [VisualStudioContribution]
        internal static Setting.Boolean CommentXmlAlignParamTags { get; } = new(
            "codeJanitorFormattingCommentXmlAlignParamTags", "Align param tags", FormattingCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean CommentXmlKeepTagsTogether { get; } = new(
            "codeJanitorFormattingCommentXmlKeepTagsTogether", "Keep tags together", FormattingCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean CommentXmlSpaceSingleTags { get; } = new(
            "codeJanitorFormattingCommentXmlSpaceSingleTags", "Space single tags", FormattingCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean CommentXmlSpaceTags { get; } = new(
            "codeJanitorFormattingCommentXmlSpaceTags", "Space tags", FormattingCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean CommentXmlSplitAllTags { get; } = new(
            "codeJanitorFormattingCommentXmlSplitAllTags", "Split all tags", FormattingCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean CommentXmlSplitSummaryTagToMultipleLines { get; } = new(
            "codeJanitorFormattingCommentXmlSplitSummaryTagToMultipleLines", "Split summary tag to multiple lines", FormattingCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean CommentXmlTagsToLowerCase { get; } = new(
            "codeJanitorFormattingCommentXmlTagsToLowerCase", "Tags to lower case", FormattingCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Integer CommentXmlValueIndent { get; } = new(
            "codeJanitorFormattingCommentXmlValueIndent", "Value indent", FormattingCategory, defaultValue: 0);
    }
#pragma warning restore VSEXTPREVIEW_SETTINGS
}
