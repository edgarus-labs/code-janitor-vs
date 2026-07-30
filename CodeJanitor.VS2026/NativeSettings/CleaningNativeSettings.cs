using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace CodeJanitor.NativeSettings
{
    // Cleaning category and its General/FileTypes/VisualStudio/Insert/Remove/Update sub-pages,
    // migrated from CodeJanitorShared/Properties/Settings.settings (Cleaning_*).
#pragma warning disable VSEXTPREVIEW_SETTINGS // Settings APIs are preview.
    internal static class CleaningNativeSettings
    {
        [VisualStudioContribution]
        internal static SettingCategory CleaningCategory { get; } = new("codeJanitorCleaning", "Cleaning", NativeSettingCategories.RootCategory);

        #region General

        [VisualStudioContribution]
        internal static SettingCategory GeneralCategory { get; } = new("codeJanitorCleaningGeneral", "General", CleaningCategory);

        [VisualStudioContribution]
        internal static Setting.Boolean AutoCleanupOnFileSave { get; } = new(
            "codeJanitorCleaningAutoCleanupOnFileSave", "Auto cleanup on file save", GeneralCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean AutoSaveAndCloseIfOpenedByCleanup { get; } = new(
            "codeJanitorCleaningAutoSaveAndCloseIfOpenedByCleanup", "Auto save and close if opened by cleanup", GeneralCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Integer PerformPartialCleanupOnExternal { get; } = new(
            "codeJanitorCleaningPerformPartialCleanupOnExternal", "Perform partial cleanup on externally modified files", GeneralCategory, defaultValue: 0);

        #endregion General

        #region File Types

        [VisualStudioContribution]
        internal static SettingCategory FileTypesCategory { get; } = new("codeJanitorCleaningFileTypes", "File Types", CleaningCategory);

        [VisualStudioContribution]
        internal static Setting.Boolean ExcludeT4GeneratedCode { get; } = new(
            "codeJanitorCleaningExcludeT4GeneratedCode", "Exclude T4 generated code", FileTypesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.String ExclusionExpression { get; } = new(
            "codeJanitorCleaningExclusionExpression", "Exclusion expression", FileTypesCategory,
            defaultValue: @"\.Designer\.cs$||\.Designer\.vb$||\.resx$||\.min\.css$||\.min\.js$");

        [VisualStudioContribution]
        internal static Setting.String InclusionExpression { get; } = new(
            "codeJanitorCleaningInclusionExpression", "Inclusion expression", FileTypesCategory, defaultValue: string.Empty);

        [VisualStudioContribution]
        internal static Setting.Boolean IncludeCPlusPlus { get; } = new(
            "codeJanitorCleaningIncludeCPlusPlus", "Include C++", FileTypesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean IncludeCSharp { get; } = new(
            "codeJanitorCleaningIncludeCSharp", "Include C#", FileTypesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean IncludeCSS { get; } = new(
            "codeJanitorCleaningIncludeCSS", "Include CSS", FileTypesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean IncludeEverythingElse { get; } = new(
            "codeJanitorCleaningIncludeEverythingElse", "Include everything else", FileTypesCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean IncludeFSharp { get; } = new(
            "codeJanitorCleaningIncludeFSharp", "Include F#", FileTypesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean IncludeHTML { get; } = new(
            "codeJanitorCleaningIncludeHTML", "Include HTML", FileTypesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean IncludeJavaScript { get; } = new(
            "codeJanitorCleaningIncludeJavaScript", "Include JavaScript", FileTypesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean IncludeJSON { get; } = new(
            "codeJanitorCleaningIncludeJSON", "Include JSON", FileTypesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean IncludeLESS { get; } = new(
            "codeJanitorCleaningIncludeLESS", "Include LESS", FileTypesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean IncludePHP { get; } = new(
            "codeJanitorCleaningIncludePHP", "Include PHP", FileTypesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean IncludePowerShell { get; } = new(
            "codeJanitorCleaningIncludePowerShell", "Include PowerShell", FileTypesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean IncludeR { get; } = new(
            "codeJanitorCleaningIncludeR", "Include R", FileTypesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean IncludeSCSS { get; } = new(
            "codeJanitorCleaningIncludeSCSS", "Include SCSS", FileTypesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean IncludeTypeScript { get; } = new(
            "codeJanitorCleaningIncludeTypeScript", "Include TypeScript", FileTypesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean IncludeVB { get; } = new(
            "codeJanitorCleaningIncludeVB", "Include VB", FileTypesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean IncludeXAML { get; } = new(
            "codeJanitorCleaningIncludeXAML", "Include XAML", FileTypesCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean IncludeXML { get; } = new(
            "codeJanitorCleaningIncludeXML", "Include XML", FileTypesCategory, defaultValue: true);

        #endregion File Types

        #region Visual Studio

        [VisualStudioContribution]
        internal static SettingCategory VisualStudioCategory { get; } = new("codeJanitorCleaningVisualStudio", "Visual Studio", CleaningCategory);

        [VisualStudioContribution]
        internal static Setting.Boolean RunVisualStudioFormatDocumentCommand { get; } = new(
            "codeJanitorCleaningRunVisualStudioFormatDocumentCommand", "Run Visual Studio's Format Document command", VisualStudioCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean RunVisualStudioRemoveAndSortUsingStatements { get; } = new(
            "codeJanitorCleaningRunVisualStudioRemoveAndSortUsingStatements", "Run Visual Studio's Remove and Sort Using Statements command", VisualStudioCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean SkipRemoveAndSortUsingStatementsDuringAutoCleanupOnSave { get; } = new(
            "codeJanitorCleaningSkipRemoveAndSortUsingStatementsDuringAutoCleanupOnSave", "Skip during automatic cleanup on save", VisualStudioCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.String UsingStatementsToReinsertWhenRemovedExpression { get; } = new(
            "codeJanitorCleaningUsingStatementsToReinsertWhenRemovedExpression", "Using statements to reinsert when removed", VisualStudioCategory, defaultValue: string.Empty);

        #endregion Visual Studio

        #region Insert

        [VisualStudioContribution]
        internal static SettingCategory InsertCategory { get; } = new("codeJanitorCleaningInsert", "Insert", CleaningCategory);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingAfterClasses { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingAfterClasses", "Blank line padding after classes", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingAfterDelegates { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingAfterDelegates", "Blank line padding after delegates", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingAfterEndRegionTags { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingAfterEndRegionTags", "Blank line padding after #endregion tags", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingAfterEnumerations { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingAfterEnumerations", "Blank line padding after enumerations", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingAfterEvents { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingAfterEvents", "Blank line padding after events", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingAfterFieldsMultiLine { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingAfterFieldsMultiLine", "Blank line padding after multi-line fields", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingAfterFieldsSingleLine { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingAfterFieldsSingleLine", "Blank line padding after single line fields", InsertCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingAfterInterfaces { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingAfterInterfaces", "Blank line padding after interfaces", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingAfterMethods { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingAfterMethods", "Blank line padding after methods", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingAfterNamespaces { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingAfterNamespaces", "Blank line padding after namespaces", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingAfterPropertiesMultiLine { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingAfterPropertiesMultiLine", "Blank line padding after multi-line properties", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingAfterPropertiesSingleLine { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingAfterPropertiesSingleLine", "Blank line padding after single line properties", InsertCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingAfterRegionTags { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingAfterRegionTags", "Blank line padding after #region tags", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingAfterStructs { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingAfterStructs", "Blank line padding after structs", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingAfterUsingStatementBlocks { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingAfterUsingStatementBlocks", "Blank line padding after using statement blocks", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingBeforeCaseStatements { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingBeforeCaseStatements", "Blank line padding before case statements", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingBeforeClasses { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingBeforeClasses", "Blank line padding before classes", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingBeforeDelegates { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingBeforeDelegates", "Blank line padding before delegates", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingBeforeEndRegionTags { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingBeforeEndRegionTags", "Blank line padding before #endregion tags", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingBeforeEnumerations { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingBeforeEnumerations", "Blank line padding before enumerations", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingBeforeEvents { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingBeforeEvents", "Blank line padding before events", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingBeforeFieldsMultiLine { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingBeforeFieldsMultiLine", "Blank line padding before multi-line fields", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingBeforeFieldsSingleLine { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingBeforeFieldsSingleLine", "Blank line padding before single line fields", InsertCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingBeforeInterfaces { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingBeforeInterfaces", "Blank line padding before interfaces", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingBeforeMethods { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingBeforeMethods", "Blank line padding before methods", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingBeforeNamespaces { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingBeforeNamespaces", "Blank line padding before namespaces", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingBeforePropertiesMultiLine { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingBeforePropertiesMultiLine", "Blank line padding before multi-line properties", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingBeforePropertiesSingleLine { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingBeforePropertiesSingleLine", "Blank line padding before single line properties", InsertCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingBeforeRegionTags { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingBeforeRegionTags", "Blank line padding before #region tags", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingBeforeSingleLineComments { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingBeforeSingleLineComments", "Blank line padding before single line comments", InsertCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingBeforeStructs { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingBeforeStructs", "Blank line padding before structs", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingBeforeUsingStatementBlocks { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingBeforeUsingStatementBlocks", "Blank line padding before using statement blocks", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankLinePaddingBetweenPropertiesMultiLineAccessors { get; } = new(
            "codeJanitorCleaningInsertBlankLinePaddingBetweenPropertiesMultiLineAccessors", "Blank line padding between multi-line property accessors", InsertCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertBlankSpaceBeforeSelfClosingAngleBrackets { get; } = new(
            "codeJanitorCleaningInsertBlankSpaceBeforeSelfClosingAngleBrackets", "Blank space before self closing angle brackets", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertEndOfFileTrailingNewLine { get; } = new(
            "codeJanitorCleaningInsertEndOfFileTrailingNewLine", "End of file trailing new line", InsertCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertExplicitAccessModifiersOnClasses { get; } = new(
            "codeJanitorCleaningInsertExplicitAccessModifiersOnClasses", "Explicit access modifiers on classes", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertExplicitAccessModifiersOnDelegates { get; } = new(
            "codeJanitorCleaningInsertExplicitAccessModifiersOnDelegates", "Explicit access modifiers on delegates", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertExplicitAccessModifiersOnEnumerations { get; } = new(
            "codeJanitorCleaningInsertExplicitAccessModifiersOnEnumerations", "Explicit access modifiers on enumerations", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertExplicitAccessModifiersOnEvents { get; } = new(
            "codeJanitorCleaningInsertExplicitAccessModifiersOnEvents", "Explicit access modifiers on events", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertExplicitAccessModifiersOnFields { get; } = new(
            "codeJanitorCleaningInsertExplicitAccessModifiersOnFields", "Explicit access modifiers on fields", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertExplicitAccessModifiersOnInterfaces { get; } = new(
            "codeJanitorCleaningInsertExplicitAccessModifiersOnInterfaces", "Explicit access modifiers on interfaces", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertExplicitAccessModifiersOnMethods { get; } = new(
            "codeJanitorCleaningInsertExplicitAccessModifiersOnMethods", "Explicit access modifiers on methods", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertExplicitAccessModifiersOnProperties { get; } = new(
            "codeJanitorCleaningInsertExplicitAccessModifiersOnProperties", "Explicit access modifiers on properties", InsertCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean InsertExplicitAccessModifiersOnStructs { get; } = new(
            "codeJanitorCleaningInsertExplicitAccessModifiersOnStructs", "Explicit access modifiers on structs", InsertCategory, defaultValue: true);

        #endregion Insert

        #region Remove

        [VisualStudioContribution]
        internal static SettingCategory RemoveCategory { get; } = new("codeJanitorCleaningRemove", "Remove", CleaningCategory);

        [VisualStudioContribution]
        internal static Setting.Boolean RemoveBlankLinesAfterAttributes { get; } = new(
            "codeJanitorCleaningRemoveBlankLinesAfterAttributes", "Blank lines after attributes", RemoveCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean RemoveBlankLinesAfterOpeningBrace { get; } = new(
            "codeJanitorCleaningRemoveBlankLinesAfterOpeningBrace", "Blank lines after opening brace", RemoveCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean RemoveBlankLinesAtBottom { get; } = new(
            "codeJanitorCleaningRemoveBlankLinesAtBottom", "Blank lines at bottom", RemoveCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean RemoveBlankLinesAtTop { get; } = new(
            "codeJanitorCleaningRemoveBlankLinesAtTop", "Blank lines at top", RemoveCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean RemoveBlankLinesBeforeClosingBrace { get; } = new(
            "codeJanitorCleaningRemoveBlankLinesBeforeClosingBrace", "Blank lines before closing brace", RemoveCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean RemoveBlankLinesBeforeClosingTags { get; } = new(
            "codeJanitorCleaningRemoveBlankLinesBeforeClosingTags", "Blank lines before closing tags", RemoveCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean RemoveBlankLinesBetweenChainedStatements { get; } = new(
            "codeJanitorCleaningRemoveBlankLinesBetweenChainedStatements", "Blank lines between chained statements", RemoveCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean RemoveBlankSpacesBeforeClosingAngleBrackets { get; } = new(
            "codeJanitorCleaningRemoveBlankSpacesBeforeClosingAngleBrackets", "Blank spaces before closing angle brackets", RemoveCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean RemoveEndOfFileTrailingNewLine { get; } = new(
            "codeJanitorCleaningRemoveEndOfFileTrailingNewLine", "End of file trailing new line", RemoveCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean RemoveEndOfLineWhitespace { get; } = new(
            "codeJanitorCleaningRemoveEndOfLineWhitespace", "End of line whitespace", RemoveCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean RemoveMultipleConsecutiveBlankLines { get; } = new(
            "codeJanitorCleaningRemoveMultipleConsecutiveBlankLines", "Multiple consecutive blank lines", RemoveCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Integer RemoveRegions { get; } = new(
            "codeJanitorCleaningRemoveRegions", "Remove regions", RemoveCategory, defaultValue: 1);

        #endregion Remove

        #region Update

        [VisualStudioContribution]
        internal static SettingCategory UpdateCategory { get; } = new("codeJanitorCleaningUpdate", "Update", CleaningCategory);

        [VisualStudioContribution]
        internal static Setting.Boolean UpdateAccessorsToBothBeSingleLineOrMultiLine { get; } = new(
            "codeJanitorCleaningUpdateAccessorsToBothBeSingleLineOrMultiLine", "Accessors to both be single line or multi-line", UpdateCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean UpdateEndRegionDirectives { get; } = new(
            "codeJanitorCleaningUpdateEndRegionDirectives", "#endregion directives", UpdateCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean UpdateSingleLineMethods { get; } = new(
            "codeJanitorCleaningUpdateSingleLineMethods", "Single line methods", UpdateCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean ConvertToFileScopedNamespace { get; } = new(
            "codeJanitorCleaningConvertToFileScopedNamespace", "Convert to file scoped namespace", UpdateCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean ConvertToVarWhenApparent { get; } = new(
            "codeJanitorCleaningConvertToVarWhenApparent", "Convert to var when apparent", UpdateCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean MakeFieldsReadonlyWhenSafe { get; } = new(
            "codeJanitorCleaningMakeFieldsReadonlyWhenSafe", "Make fields readonly when safe", UpdateCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean SealClassesWhenSafe { get; } = new(
            "codeJanitorCleaningSealClassesWhenSafe", "Seal classes when safe", UpdateCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Integer UpdateFileHeaderHeaderPosition { get; } = new(
            "codeJanitorCleaningUpdateFileHeaderHeaderPosition", "File header position", UpdateCategory, defaultValue: 0);

        [VisualStudioContribution]
        internal static Setting.Integer UpdateFileHeaderHeaderUpdateMode { get; } = new(
            "codeJanitorCleaningUpdateFileHeaderHeaderUpdateMode", "File header update mode", UpdateCategory, defaultValue: 0);

        [VisualStudioContribution]
        internal static Setting.String UpdateFileHeaderCPlusPlus { get; } = new(
            "codeJanitorCleaningUpdateFileHeaderCPlusPlus", "File header for C++", UpdateCategory, defaultValue: string.Empty);

        [VisualStudioContribution]
        internal static Setting.String UpdateFileHeaderCSharp { get; } = new(
            "codeJanitorCleaningUpdateFileHeaderCSharp", "File header for C#", UpdateCategory, defaultValue: string.Empty);

        [VisualStudioContribution]
        internal static Setting.String UpdateFileHeaderCSS { get; } = new(
            "codeJanitorCleaningUpdateFileHeaderCSS", "File header for CSS", UpdateCategory, defaultValue: string.Empty);

        [VisualStudioContribution]
        internal static Setting.String UpdateFileHeaderFSharp { get; } = new(
            "codeJanitorCleaningUpdateFileHeaderFSharp", "File header for F#", UpdateCategory, defaultValue: string.Empty);

        [VisualStudioContribution]
        internal static Setting.String UpdateFileHeaderHTML { get; } = new(
            "codeJanitorCleaningUpdateFileHeaderHTML", "File header for HTML", UpdateCategory, defaultValue: string.Empty);

        [VisualStudioContribution]
        internal static Setting.String UpdateFileHeaderJavaScript { get; } = new(
            "codeJanitorCleaningUpdateFileHeaderJavaScript", "File header for JavaScript", UpdateCategory, defaultValue: string.Empty);

        [VisualStudioContribution]
        internal static Setting.String UpdateFileHeaderJSON { get; } = new(
            "codeJanitorCleaningUpdateFileHeaderJSON", "File header for JSON", UpdateCategory, defaultValue: string.Empty);

        [VisualStudioContribution]
        internal static Setting.String UpdateFileHeaderLESS { get; } = new(
            "codeJanitorCleaningUpdateFileHeaderLESS", "File header for LESS", UpdateCategory, defaultValue: string.Empty);

        [VisualStudioContribution]
        internal static Setting.String UpdateFileHeaderPHP { get; } = new(
            "codeJanitorCleaningUpdateFileHeaderPHP", "File header for PHP", UpdateCategory, defaultValue: string.Empty);

        [VisualStudioContribution]
        internal static Setting.String UpdateFileHeaderPowerShell { get; } = new(
            "codeJanitorCleaningUpdateFileHeaderPowerShell", "File header for PowerShell", UpdateCategory, defaultValue: string.Empty);

        [VisualStudioContribution]
        internal static Setting.String UpdateFileHeaderR { get; } = new(
            "codeJanitorCleaningUpdateFileHeaderR", "File header for R", UpdateCategory, defaultValue: string.Empty);

        [VisualStudioContribution]
        internal static Setting.String UpdateFileHeaderSCSS { get; } = new(
            "codeJanitorCleaningUpdateFileHeaderSCSS", "File header for SCSS", UpdateCategory, defaultValue: string.Empty);

        [VisualStudioContribution]
        internal static Setting.String UpdateFileHeaderTypeScript { get; } = new(
            "codeJanitorCleaningUpdateFileHeaderTypeScript", "File header for TypeScript", UpdateCategory, defaultValue: string.Empty);

        [VisualStudioContribution]
        internal static Setting.String UpdateFileHeaderVB { get; } = new(
            "codeJanitorCleaningUpdateFileHeaderVB", "File header for VB", UpdateCategory, defaultValue: string.Empty);

        [VisualStudioContribution]
        internal static Setting.String UpdateFileHeaderXAML { get; } = new(
            "codeJanitorCleaningUpdateFileHeaderXAML", "File header for XAML", UpdateCategory, defaultValue: string.Empty);

        [VisualStudioContribution]
        internal static Setting.String UpdateFileHeaderXML { get; } = new(
            "codeJanitorCleaningUpdateFileHeaderXML", "File header for XML", UpdateCategory, defaultValue: string.Empty);

        #endregion Update
    }
#pragma warning restore VSEXTPREVIEW_SETTINGS
}
