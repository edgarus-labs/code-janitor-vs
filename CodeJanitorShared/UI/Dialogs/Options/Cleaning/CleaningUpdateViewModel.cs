using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Helpers;
using CodeJanitor.Properties;
using CodeJanitor.UI.Enumerations;
using System;

namespace CodeJanitor.UI.Dialogs.Options.Cleaning;

/// <summary>
/// The view model for cleaning update options.
/// </summary>

public class CleaningUpdateViewModel : OptionsPageViewModel
{
    private static readonly TimeSpan SuccessfulConnectionCacheDuration = TimeSpan.FromMinutes(30);
    private static string _lastSuccessfulConnectionFingerprint;
    private static DateTime _lastSuccessfulConnectionUtc;

    /// <summary>
    /// Initializes a new instance of the <see cref="CleaningUpdateViewModel" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <param name="activeSettings">The active settings.</param>

    public CleaningUpdateViewModel(CodeJanitorPackage package, Settings activeSettings)
        : base(package, activeSettings)
    {
        Mappings = new SettingsToOptionsList(ActiveSettings, this)
        {
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_UpdateAccessorsToBothBeSingleLineOrMultiLine, x => UpdateAccessorsToBothBeSingleLineOrMultiLine),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_UpdateEndRegionDirectives, x => UpdateEndRegionDirectives),
            new SettingToOptionMapping<int, HeaderPosition>(x => ActiveSettings.Cleaning_UpdateFileHeader_HeaderPosition, x => HeaderPosition),
            new SettingToOptionMapping<int, HeaderUpdateMode>(x => ActiveSettings.Cleaning_UpdateFileHeader_HeaderUpdateMode, x => HeaderUpdateMode),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderCPlusPlus, x => UpdateFileHeaderCPlusPlus),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderCSharp, x => UpdateFileHeaderCSharp),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderCSS, x => UpdateFileHeaderCSS),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderFSharp, x => UpdateFileHeaderFSharp),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderHTML, x => UpdateFileHeaderHTML),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderJavaScript, x => UpdateFileHeaderJavaScript),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderJSON, x => UpdateFileHeaderJSON),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderLESS, x => UpdateFileHeaderLESS),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderPHP, x => UpdateFileHeaderPHP),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderPowerShell, x => UpdateFileHeaderPowerShell),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderR, x => UpdateFileHeaderR),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderSCSS, x => UpdateFileHeaderSCSS),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderTypeScript, x => UpdateFileHeaderTypeScript),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderVB, x => UpdateFileHeaderVisualBasic),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderXAML, x => UpdateFileHeaderXAML),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderXML, x => UpdateFileHeaderXML),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_UpdateSingleLineMethods, x => UpdateSingleLineMethods),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_ConvertToFileScopedNamespace, x => ConvertToFileScopedNamespace),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_MoveTopLevelTypesToSeparateFiles, x => MoveTopLevelTypesToSeparateFiles),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_ConvertToVarWhenApparent, x => ConvertToVarWhenApparent),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_ConvertToCollectionExpressions, x => ConvertToCollectionExpressions),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_ReuseJsonSerializerOptionsForCA1869, x => ReuseJsonSerializerOptionsForCA1869),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_SimplifySingleStatementLambdas, x => SimplifySingleStatementLambdas),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_MakeFieldsReadonlyWhenSafe, x => MakeFieldsReadonlyWhenSafe),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_SealClassesWhenSafe, x => SealClassesWhenSafe),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_ConvertToPatternMatchingNullChecks, x => ConvertToPatternMatchingNullChecks),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_ConvertStringFormatToInterpolation, x => ConvertStringFormatToInterpolation),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_ConvertToStringNameOf, x => ConvertToStringNameOf),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_InlineOutVariableDeclarations, x => InlineOutVariableDeclarations),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_InsertBlankLineBeforeReturnAndThrowStatements, x => InsertBlankLineBeforeReturnAndThrowStatements),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_FormatRazorComponents, x => FormatRazorComponents),
        };
    }

    /// <summary>
    /// Gets the header.
    /// </summary>
    public override string Header => Resources.CleaningUpdateViewModel_Update;

    /// <summary>
    /// Gets or sets the position of the file header.
    /// </summary>

    public HeaderPosition HeaderPosition
    {
        get { return GetPropertyValue<HeaderPosition>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the position of the file header.
    /// </summary>

    public HeaderUpdateMode HeaderUpdateMode
    {
        get { return GetPropertyValue<HeaderUpdateMode>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if accessors should be updated to both be single line
    /// or multi line.
    /// </summary>

    public bool UpdateAccessorsToBothBeSingleLineOrMultiLine
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if end region directives should be updated.
    /// </summary>

    public bool UpdateEndRegionDirectives
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the file header that should be at the top of C++ files.
    /// </summary>

    public string UpdateFileHeaderCPlusPlus
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the file header that should be at the top of C# files.
    /// </summary>

    public string UpdateFileHeaderCSharp
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the file header that should be at the top of CSS files.
    /// </summary>

    public string UpdateFileHeaderCSS
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the file header that should be at the top of F# files.
    /// </summary>

    public string UpdateFileHeaderFSharp
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the file header that should be at the top of HTML files.
    /// </summary>

    public string UpdateFileHeaderHTML
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the file header that should be at the top of JavaScript files.
    /// </summary>

    public string UpdateFileHeaderJavaScript
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the file header that should be at the top of JSON files.
    /// </summary>

    public string UpdateFileHeaderJSON
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the file header that should be at the top of LESS files.
    /// </summary>

    public string UpdateFileHeaderLESS
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the file header that should be at the top of PHP files.
    /// </summary>

    public string UpdateFileHeaderPHP
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the file header that should be at the top of PowerShell files.
    /// </summary>

    public string UpdateFileHeaderPowerShell
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the file header that should be at the top of R files.
    /// </summary>

    public string UpdateFileHeaderR
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the file header that should be at the top of SCSS files.
    /// </summary>

    public string UpdateFileHeaderSCSS
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the file header that should be at the top of TypeScript files.
    /// </summary>

    public string UpdateFileHeaderTypeScript
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the file header that should be at the top of VB files.
    /// </summary>

    public string UpdateFileHeaderVisualBasic
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the file header that should be at the top of XAML files.
    /// </summary>

    public string UpdateFileHeaderXAML
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the file header that should be at the top of XML files.
    /// </summary>

    public string UpdateFileHeaderXML
    {
        get { return GetPropertyValue<string>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if single line methods should be updated.
    /// </summary>

    public bool UpdateSingleLineMethods
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if block-scoped namespaces should be converted to file-scoped.
    /// </summary>

    public bool ConvertToFileScopedNamespace
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if extra top-level C# types should be moved into
    /// their own files during cleanup.
    /// </summary>

    public bool MoveTopLevelTypesToSeparateFiles
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if local variable declarations should be converted to
    /// <c>var</c> when the type is apparent from the right-hand side.
    /// </summary>

    public bool ConvertToVarWhenApparent
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if <c>List&lt;T&gt;</c> and array initializations
    /// should be converted to the C# 12 collection expression syntax.
    /// </summary>

    public bool ConvertToCollectionExpressions
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if direct <c>new JsonSerializerOptions()</c>
    /// allocations in <c>JsonSerializer.*</c> calls should be replaced with <c>null</c>
    /// (conservative CA1869-focused optimization).
    /// </summary>

    public bool ReuseJsonSerializerOptionsForCA1869
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if single-statement lambda block bodies should be
    /// simplified to expression bodies (including removing unnecessary <c>return</c>).
    /// </summary>

    public bool SimplifySingleStatementLambdas
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if fields provably never written outside their
    /// constructor should have the <c>readonly</c> modifier added.
    /// </summary>

    public bool MakeFieldsReadonlyWhenSafe
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if classes provably not derived from within the same
    /// file should have the <c>sealed</c> modifier added. This is a single-file heuristic and
    /// changes the API surface, so it defaults to disabled.
    /// </summary>

    public bool SealClassesWhenSafe
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if traditional null checks (== null, != null) should be converted to pattern matching (is null, is not null).
    /// </summary>
    public bool ConvertToPatternMatchingNullChecks
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if string.Format calls should be converted to modern string interpolation ($"...").
    /// </summary>
    public bool ConvertStringFormatToInterpolation
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if string literals matching parameter names in argument exceptions should be converted to nameof(...).
    /// </summary>
    public bool ConvertToStringNameOf
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if separate uninitialized out variable declarations should be inlined into out var expressions.
    /// </summary>
    public bool InlineOutVariableDeclarations
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if a blank line should be inserted before
    /// <c>return</c> and <c>throw</c> statements that are preceded by other statements within
    /// the same block, to visually separate the exit/failure path from the preceding logic.
    /// </summary>

    public bool InsertBlankLineBeforeReturnAndThrowStatements
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }

    /// <summary>
    /// Gets or sets the flag indicating if safe Razor component formatting should run for
    /// <c>.razor</c> files during cleanup.
    /// </summary>

    public bool FormatRazorComponents
    {
        get { return GetPropertyValue<bool>(); }
        set { SetPropertyValue(value); }
    }
}
