using CodeJanitor.Helpers;
using CodeJanitor.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// Resolves the cleanup settings that apply to one file for both the editor and the closed-file cleanup:
/// a setting defined by .editorconfig wins, otherwise the repository policy (.codejanitor) wins, otherwise the
/// user's Visual Studio setting applies. A plain .editorconfig option (such as <c>indent_style</c>) whose severity
/// suffix is <c>:none</c> or <c>:silent</c> is ignored, as are options with unrecognized values or severities, so
/// the next source decides; <c>suggestion</c> or higher, or no suffix, enforces the value. A Roslyn rule is enforced
/// as Roslyn reports it (see <see cref="TryReadRule" />), which also resolves the Code Janitor code-style rules
/// (<see cref="CodeStyleRules" />).
/// </summary>
internal sealed class EffectiveCleanupSettings
{
    private const string FileHeaderSetting = "Cleaning_UpdateFileHeaderCSharp";
    private const string MakeFieldsReadonlySetting = "Cleaning_MakeFieldsReadonlyWhenSafe";
    private const string ExplicitAccessModifiersSetting = "Cleaning_InsertExplicitAccessModifiers";
    private const int DefaultTabSize = 4;

    /// <summary>
    /// The analyzer categories of the non-IDE diagnostics the cleanup steps follow; every IDE diagnostic is "Style".
    /// </summary>
    private static readonly Dictionary<string, string> AnalyzerCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["CA1507"] = "Maintainability",
        ["CA1852"] = "Performance",
        ["CA1869"] = "Performance",
    };

    /// <summary>
    /// The diagnostics the cleanup steps follow that are disabled by default. As in Roslyn, category and global
    /// severities do not apply to them; only <c>dotnet_diagnostic.&lt;id&gt;.severity</c> enables them.
    /// </summary>
    private static readonly HashSet<string> DisabledByDefaultDiagnostics = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CA1852",
    };

    private static readonly string[] NoDiagnosticIds = Array.Empty<string>();

    /// <summary>
    /// The cleanup steps applied through a Roslyn analyzer and code fix in the diagnostic cleanup, each the way the
    /// Microsoft Learn page of its rule documents it.
    /// </summary>
    private static readonly RoslynStep[] RoslynSteps =
    {
        new RoslynStep("Cleaning_ConvertToVarWhenApparent", "csharp_style_var_when_type_is_apparent", "true", new[] { "IDE0007" }, NoDiagnosticIds, "csharp_style_var_for_built_in_types", "csharp_style_var_elsewhere"),
        new RoslynStep("Cleaning_InlineOutVariableDeclarations", "csharp_style_inlined_variable_declaration", "true", new[] { "IDE0018" }, NoDiagnosticIds),
        new RoslynStep("Cleaning_ConvertToCollectionExpressions", "dotnet_style_prefer_collection_expression", "true", new[] { "IDE0300", "IDE0301", "IDE0302", "IDE0303", "IDE0304", "IDE0305", "IDE0306" }, NoDiagnosticIds),
        new RoslynStep(MakeFieldsReadonlySetting, "dotnet_style_readonly_field", "true", new[] { "IDE0044" }, NoDiagnosticIds),
        new RoslynStep(ExplicitAccessModifiersSetting, "dotnet_style_require_accessibility_modifiers", "for_non_interface_members", new[] { "IDE0040" }, NoDiagnosticIds),
        new RoslynStep("Cleaning_SimplifySingleStatementLambdas", "csharp_style_expression_bodied_lambdas", "true", new[] { "IDE0053" }, NoDiagnosticIds),
        new RoslynStep("Cleaning_ConvertToFileScopedNamespace", "csharp_style_namespace_declarations", "file_scoped", new[] { "IDE0161" }, new[] { "IDE0160" }),
        new RoslynStep("Cleaning_MoveUsingsOutsideNamespace", "csharp_using_directive_placement", "outside_namespace", new[] { "IDE0065" }, new[] { "IDE0065" }),
        new RoslynStep("Cleaning_RemoveMultipleConsecutiveBlankLines", "dotnet_style_allow_multiple_blank_lines_experimental", "false", new[] { "IDE2000" }, NoDiagnosticIds),
    };

    private readonly IReadOnlyDictionary<string, string> _editorConfigOptions;
    private readonly Dictionary<string, object> _editorConfigValues = new Dictionary<string, object>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _editorConfigKeys = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _codeStyleValues = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _codeStyleEditorConfigKeys = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly RepositoryCleanupOverrides _repositoryOverrides;

    /// <summary>
    /// Initializes a new instance of the <see cref="EffectiveCleanupSettings" /> class.
    /// </summary>
    /// <param name="filePath">The source file path, used for the <c>{fileName}</c> header placeholder.</param>
    /// <param name="editorConfigOptions">The .editorconfig options that apply to the file.</param>
    /// <param name="repositoryOverrides">The repository policy that applies to the file.</param>
    private EffectiveCleanupSettings(
        string filePath,
        IReadOnlyDictionary<string, string> editorConfigOptions,
        RepositoryCleanupOverrides repositoryOverrides)
    {
        _editorConfigOptions = editorConfigOptions;
        _repositoryOverrides = repositoryOverrides;

        ApplyEditorConfigBooleans();
        ApplyEditorConfigRules();
        ApplyEditorConfigFileHeader(filePath);
        ResolveCodeStyleRules();
        AnalyzerConfigOverrides = BuildAnalyzerConfigOverrides();

        OrganizeUsings = ResolveOrganizeUsings();

        Indentation = TryReadOption("indent_style", ParseIndentStyle, out var indentStyle)
            ? indentStyle ?? IndentationPreference.Unchanged
            : IndentationPreference.Unchanged;
        TabSize = ReadPositiveInteger("tab_width") ?? ReadPositiveInteger("indent_size") ?? DefaultTabSize;

        // The closed-file cleanup always ensured a final newline; only an enforced .editorconfig "false" reverses it.
        InsertFinalNewline = !(TryReadOption("insert_final_newline", ParseBoolean, out var insertFinalNewline) && insertFinalNewline == false);
    }

    /// <summary>
    /// Gets the Visual Studio settings decided by .editorconfig for this file, each mapped to the .editorconfig option
    /// name that decides it. A setting absent from the map follows the repository policy or the user setting.
    /// </summary>
    internal IReadOnlyDictionary<string, string> EditorConfigKeys => _editorConfigKeys;

    /// <summary>
    /// Gets the Code Janitor code-style rules (<see cref="CodeStyleRules" />) that apply to this file because
    /// .editorconfig does not enforce them, keyed by .editorconfig option name, with the value from the repository
    /// policy or the user setting.
    /// </summary>
    internal IReadOnlyDictionary<string, string> CodeStyleValues => _codeStyleValues;

    /// <summary>
    /// Gets the Code Janitor code-style rules that .editorconfig enforces for this file, keyed by .editorconfig option
    /// name, each mapped to the .editorconfig option that enforces it.
    /// </summary>
    internal IReadOnlyDictionary<string, string> CodeStyleEditorConfigKeys => _codeStyleEditorConfigKeys;

    /// <summary>
    /// Gets the analyzer configuration entries the diagnostic cleanup applies on top of .editorconfig so that Roslyn
    /// reports, and fixes, exactly the rules of <see cref="CodeStyleValues" /> and the enabled cleanup steps applied
    /// through Roslyn rules. A Code Style rule gets its value with <c>suggestion</c> severity and the severity of its
    /// diagnostics raised to <c>suggestion</c>; every other rule reported through one of those diagnostics, unless
    /// .editorconfig enforces it, is silenced with <c>none</c>. A Roslyn step .editorconfig does not decide gets the
    /// value the step applies, with its diagnostics raised to <c>suggestion</c>; a step .editorconfig decides by its
    /// option keeps that value, repeated lower-cased with <c>suggestion</c> severity, and the diagnostics of the decided
    /// direction that no severity configures are raised to <c>suggestion</c>.
    /// </summary>
    internal IReadOnlyDictionary<string, string> AnalyzerConfigOverrides { get; }

    /// <summary>
    /// Gets a value indicating whether region directives are removed: always, unless the repository policy opts
    /// out with <c>removeRegions: false</c>.
    /// </summary>
    internal bool RemovesRegions => _repositoryOverrides.RemovesRegions;

    /// <summary>
    /// Gets a value indicating whether using directives are organized (System directives first, no group
    /// separation). .editorconfig decides when it enforces <c>dotnet_sort_system_directives_first = true</c> or
    /// contradicts it; otherwise the repository <c>organizeUsings</c> policy decides.
    /// </summary>
    internal bool OrganizeUsings { get; }

    /// <summary>
    /// Determines whether the Roslyn equivalent of the Visual Studio "Remove and Sort Usings" command runs: it is on and
    /// not skipped because the cleanup runs on save.
    /// </summary>
    /// <param name="isAutoSaveContext">True when the cleanup runs on save.</param>
    /// <returns>True when Remove and Sort Usings runs.</returns>
    internal bool RunsRemoveAndSortUsings(bool isAutoSaveContext) =>
        GetBoolean(nameof(Settings.Cleaning_RunVisualStudioRemoveAndSortUsingStatements))
        && !(isAutoSaveContext && GetBoolean(nameof(Settings.Cleaning_SkipRemoveAndSortUsingStatementsDuringAutoCleanupOnSave)));

    /// <summary>
    /// Gets how the diagnostic cleanup sorts the using directives of a document a fix changed: as the step that sorted
    /// them before the fixes, which is Remove and Sort Usings when it is on, otherwise, for a closed file, the
    /// <see cref="OrganizeUsings" /> organizer of the headless cleanup. An open file is not sorted by that organizer.
    /// </summary>
    /// <param name="isAutoSaveContext">True when the cleanup runs on save.</param>
    /// <param name="isClosedFile">True for a closed file, whose text the headless cleanup produced.</param>
    /// <returns>The sorting.</returns>
    internal Diagnostics.UsingDirectiveSorting GetUsingDirectiveSortingAfterFixes(bool isAutoSaveContext, bool isClosedFile) =>
        GetBoolean(nameof(Settings.Cleaning_RunVisualStudioRemoveAndSortUsingStatements))
            ? (RunsRemoveAndSortUsings(isAutoSaveContext) ? Diagnostics.UsingDirectiveSorting.RemoveAndSortUsings : Diagnostics.UsingDirectiveSorting.None)
            : (isClosedFile && OrganizeUsings ? Diagnostics.UsingDirectiveSorting.OrganizeUsings : Diagnostics.UsingDirectiveSorting.None);

    /// <summary>
    /// Gets the indentation style to enforce. Only .editorconfig (<c>indent_style</c>) defines it.
    /// </summary>
    internal IndentationPreference Indentation { get; }

    /// <summary>
    /// Gets the tab size used for indentation conversions: <c>tab_width</c>, otherwise a numeric
    /// <c>indent_size</c>, otherwise 4.
    /// </summary>
    internal int TabSize { get; }

    /// <summary>
    /// Gets a value indicating whether the closed-file cleanup ensures a final newline (true) or removes trailing
    /// line breaks (false). Only an enforced .editorconfig <c>insert_final_newline = false</c> yields false.
    /// </summary>
    internal bool InsertFinalNewline { get; }

    /// <summary>
    /// Resolves the cleanup settings that apply to the specified file. Missing, unreadable or malformed
    /// configuration files are ignored; a blank path yields the user's Visual Studio settings only.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    /// <returns>The effective settings for the file.</returns>
    internal static EffectiveCleanupSettings For(string filePath) => new EffectiveCleanupSettings(
            filePath,
            EditorConfigHelper.LoadOptions(filePath),
            RepositoryCleanupSettings.LoadForFile(filePath));

    /// <summary>
    /// Gets the effective value of a boolean Visual Studio setting.
    /// </summary>
    /// <param name="settingName">The Visual Studio setting property name, for example <c>Cleaning_RemoveEndOfLineWhitespace</c>.</param>
    /// <returns>The effective value.</returns>
    internal bool GetBoolean(string settingName) => _editorConfigValues.TryGetValue(settingName, out var value) && value is bool boolean
            ? boolean
            : _repositoryOverrides.TryGetBoolean(settingName, (bool)Settings.Default[settingName]);

    /// <summary>
    /// Gets the effective value of a string Visual Studio setting.
    /// </summary>
    /// <param name="settingName">The Visual Studio setting property name, for example <c>Cleaning_UpdateFileHeaderCSharp</c>.</param>
    /// <returns>The effective value.</returns>
    internal string GetString(string settingName) => _editorConfigValues.TryGetValue(settingName, out var value) && value is string text
            ? text
            : _repositoryOverrides.TryGetString(settingName, (string)Settings.Default[settingName]);

    /// <summary>
    /// Gets the effective value of an integer Visual Studio setting.
    /// </summary>
    /// <param name="settingName">The Visual Studio setting property name.</param>
    /// <returns>The effective value.</returns>
    internal int GetInt32(string settingName) => _repositoryOverrides.TryGetInt32(settingName, (int)Settings.Default[settingName]);

    /// <summary>
    /// Records the boolean settings defined by plain .editorconfig options, which have no Roslyn rule counterpart.
    /// </summary>
    private void ApplyEditorConfigBooleans()
    {
        if (TryReadOption("trim_trailing_whitespace", ParseBoolean, out var trimTrailingWhitespace))
        {
            SetEditorConfigValue("Cleaning_RemoveEndOfLineWhitespace", "trim_trailing_whitespace", trimTrailingWhitespace == true);
        }

        if (TryReadOption("insert_final_newline", ParseBoolean, out var insertFinalNewline))
        {
            SetEditorConfigValue("Cleaning_InsertEndOfFileTrailingNewLine", "insert_final_newline", insertFinalNewline == true);
            SetEditorConfigValue("Cleaning_RemoveEndOfFileTrailingNewLine", "insert_final_newline", insertFinalNewline == false);
        }
    }

    /// <summary>
    /// Records a setting value defined by .editorconfig together with the option name that defines it.
    /// </summary>
    /// <param name="settingName">The Visual Studio setting property name.</param>
    /// <param name="key">The .editorconfig option name.</param>
    /// <param name="value">The setting value.</param>
    private void SetEditorConfigValue(string settingName, string key, object value)
    {
        _editorConfigValues[settingName] = value;
        _editorConfigKeys[settingName] = key;
    }

    /// <summary>
    /// Records the boolean settings whose cleanup step has a Roslyn rule counterpart, when .editorconfig enforces that
    /// rule (see <see cref="TryReadRule" />). A rule enforced only through a diagnostic, category or global severity
    /// uses Roslyn's default option value; a rule configured by severity only turns its step on.
    /// </summary>
    private void ApplyEditorConfigRules()
    {
        ApplyRule("Cleaning_ConvertToVarWhenApparent", "csharp_style_var_when_type_is_apparent", new[] { "IDE0007", "IDE0008" }, ParseBoolean, defaultValue: false);
        ApplyRule("Cleaning_InlineOutVariableDeclarations", "csharp_style_inlined_variable_declaration", new[] { "IDE0018" }, ParseBoolean, defaultValue: true);
        ApplyRule("Cleaning_ConvertToCollectionExpressions", "dotnet_style_prefer_collection_expression", new[] { "IDE0300", "IDE0301", "IDE0302", "IDE0303", "IDE0304", "IDE0305", "IDE0306" }, ParseCollectionExpressionPreference, defaultValue: true);
        ApplyRule("Cleaning_MakeFieldsReadonlyWhenSafe", "dotnet_style_readonly_field", new[] { "IDE0044" }, ParseBoolean, defaultValue: true);
        ApplyRule(ExplicitAccessModifiersSetting, "dotnet_style_require_accessibility_modifiers", new[] { "IDE0040" }, ParseAccessibilityModifiersPreference, defaultValue: true);
        ApplyRule("Cleaning_ConvertToFileScopedNamespace", "csharp_style_namespace_declarations", new[] { "IDE0160", "IDE0161" }, ParseNamespaceDeclarations, defaultValue: false);
        ApplyRule("Cleaning_MoveUsingsOutsideNamespace", "csharp_using_directive_placement", new[] { "IDE0065" }, ParseUsingDirectivePlacement, defaultValue: true);
        ApplyRule("Cleaning_SimplifySingleStatementLambdas", "csharp_style_expression_bodied_lambdas", new[] { "IDE0053" }, ParseExpressionBodyPreference, defaultValue: true);
        ApplyNullCheckRules();
        ApplyRule("Cleaning_SealClassesWhenSafe", null, new[] { "CA1852" }, null, defaultValue: true);
        ApplyRule("Cleaning_ConvertToStringNameOf", null, new[] { "CA1507" }, null, defaultValue: true);
        ApplyRule("Cleaning_ReuseJsonSerializerOptionsForCA1869", null, new[] { "CA1869" }, null, defaultValue: true);
        ApplyRule("Cleaning_RemoveMultipleConsecutiveBlankLines", "dotnet_style_allow_multiple_blank_lines_experimental", new[] { "IDE2000" }, ParseInvertedBoolean, defaultValue: false);
        ApplyRule("Cleaning_RemoveBlankLinesAfterOpeningBrace", "csharp_style_allow_blank_lines_between_consecutive_braces_experimental", new[] { "IDE2002" }, ParseInvertedBoolean, defaultValue: false);
        ApplyRule("Cleaning_RemoveBlankLinesBeforeClosingBrace", "csharp_style_allow_blank_lines_between_consecutive_braces_experimental", new[] { "IDE2002" }, ParseInvertedBoolean, defaultValue: false);
        ApplyRule("Cleaning_RunVisualStudioRemoveAndSortUsingStatements", null, new[] { "IDE0005" }, null, defaultValue: true);
        ApplyRule("Cleaning_RunVisualStudioFormatDocumentCommand", null, new[] { "IDE0055" }, null, defaultValue: true);
    }

    /// <summary>
    /// Records a boolean setting decided by a Roslyn rule when .editorconfig enforces the rule.
    /// </summary>
    /// <param name="settingName">The Visual Studio setting property name.</param>
    /// <param name="key">The .editorconfig option name, or null for a rule configured by severity only.</param>
    /// <param name="diagnosticIds">The IDs of the rule's diagnostics.</param>
    /// <param name="parseValue">Maps the option value to the setting value; null when <paramref name="key" /> is null.</param>
    /// <param name="defaultValue">The setting value when the rule is enforced with Roslyn's default option value.</param>
    private void ApplyRule(string settingName, string key, string[] diagnosticIds, Func<string, bool?> parseValue, bool defaultValue)
    {
        if (TryReadRule(key, diagnosticIds, value => parseValue(value).HasValue, out var ruleValue, out var decidingKey))
        {
            SetEditorConfigValue(settingName, decidingKey, ruleValue is null ? defaultValue : parseValue(ruleValue).Value);
        }
    }

    /// <summary>
    /// Records <c>Cleaning_ConvertToPatternMatchingNullChecks</c>, decided by two Roslyn rules: when .editorconfig
    /// enforces either, null checks are converted only if every enforced rule prefers null checks (both default to
    /// true). The note names the first enforced rule, or the first one that turns the conversion off.
    /// </summary>
    private void ApplyNullCheckRules()
    {
        var rules = new[]
        {
            (Key: "csharp_style_prefer_null_check_over_type_check", DiagnosticId: "IDE0150"),
            (Key: "dotnet_style_prefer_is_null_check_over_reference_equality_method", DiagnosticId: "IDE0041"),
        };
        bool? convert = null;
        string decidingKey = null;

        foreach (var rule in rules)
        {
            if (!TryReadRule(rule.Key, new[] { rule.DiagnosticId }, value => ParseBoolean(value).HasValue, out var value, out var ruleKey))
            {
                continue;
            }

            var prefersNullCheck = value is null || ParseBoolean(value) == true;
            if (convert is null || (convert == true && !prefersNullCheck))
            {
                decidingKey = ruleKey;
            }

            convert = (convert ?? true) && prefersNullCheck;
        }

        if (convert.HasValue)
        {
            SetEditorConfigValue("Cleaning_ConvertToPatternMatchingNullChecks", decidingKey, convert.Value);
        }
    }

    /// <summary>
    /// Resolves every Code Janitor code-style rule: .editorconfig wins when it enforces the rule (see
    /// <see cref="TryReadRule" />); otherwise the repository policy decides (a null value disables the rule), and
    /// otherwise the user setting.
    /// </summary>
    private void ResolveCodeStyleRules()
    {
        var userValues = CodeStyleRules.ParseSetting((string)Settings.Default[nameof(Settings.Cleaning_CodeStyleRules)]);

        foreach (var rule in CodeStyleRules.All)
        {
            if (TryReadRule(rule.Key, rule.DiagnosticIds, rule.IsValidValue, out _, out var decidingKey))
            {
                _codeStyleEditorConfigKeys[rule.Key] = decidingKey;
            }
            else if (_repositoryOverrides.CodeStyle.TryGetValue(rule.Key, out var policyValue))
            {
                if (policyValue is not null)
                {
                    _codeStyleValues[rule.Key] = policyValue;
                }
            }
            else if (userValues.TryGetValue(rule.Key, out var userValue))
            {
                _codeStyleValues[rule.Key] = userValue;
            }
        }
    }

    /// <summary>
    /// Builds <see cref="AnalyzerConfigOverrides" />.
    /// </summary>
    /// <returns>The analyzer configuration entries.</returns>
    private IReadOnlyDictionary<string, string> BuildAnalyzerConfigOverrides()
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        var raisedDiagnosticIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in CodeStyleRules.All.Where(rule => _codeStyleValues.ContainsKey(rule.Key)))
        {
            overrides[rule.Key] = _codeStyleValues[rule.Key] + ":suggestion";
            foreach (var diagnosticId in rule.DiagnosticIds)
            {
                overrides["dotnet_diagnostic." + diagnosticId + ".severity"] = "suggestion";
                raisedDiagnosticIds.Add(diagnosticId);
            }
        }

        // A raised severity would also surface the diagnostics other rules report under the same ID (e.g. IDE0009 for
        // every 'this.' qualification option), so the rules Code Janitor does not apply and .editorconfig does not
        // enforce are switched off.
        foreach (var rule in CodeStyleRules.All)
        {
            if (_codeStyleValues.ContainsKey(rule.Key)
                || _codeStyleEditorConfigKeys.ContainsKey(rule.Key)
                || !rule.DiagnosticIds.Any(raisedDiagnosticIds.Contains))
            {
                continue;
            }

            var value = TryReadRawOption(rule.Key, out var configuredValue, out _) && rule.IsValidValue(configuredValue)
                ? rule.Normalize(configuredValue)
                : rule.DefaultValue;
            overrides[rule.Key] = value + ":none";
        }

        // A Roslyn step that .editorconfig decides by its option keeps the .editorconfig value. Roslyn reports a
        // diagnostic that no dotnet_diagnostic, category or global severity configures with the option's own severity,
        // which may be silent (some analyzers then skip the rule) or no different from a run without .editorconfig, so
        // the option is repeated, lower-cased as Roslyn parses it, with suggestion severity, and those diagnostics of
        // the decided direction are raised to suggestion. A step .editorconfig decides only through severities is left
        // to them. A step .editorconfig does not decide gets the value the step applies. The other options reported
        // through the same diagnostic are switched off when .editorconfig sets them to true without enforcing them, so
        // the raised severity applies to the step only.
        foreach (var step in RoslynSteps)
        {
            var enabled = GetBoolean(step.SettingName);
            if (_editorConfigKeys.TryGetValue(step.SettingName, out var decidingKey))
            {
                var unconfiguredIds = (enabled ? step.DiagnosticIds : step.ReverseDiagnosticIds)
                    .Where(diagnosticId => !TryReadDiagnosticSeverity(diagnosticId, out _, out _))
                    .ToList();
                if (decidingKey != step.Key || unconfiguredIds.Count == 0 || !TryReadRawOption(step.Key, out var decidedValue, out _))
                {
                    continue;
                }

                overrides[step.Key] = decidedValue.ToLowerInvariant() + ":suggestion";
                RaiseSeverities(unconfiguredIds);
            }
            else if (enabled)
            {
                overrides[step.Key] = step.Value + ":suggestion";
                RaiseSeverities(step.DiagnosticIds);
            }
            else
            {
                continue;
            }

            if (enabled)
            {
                foreach (var key in step.OtherOptions)
                {
                    if (TryReadRawOption(key, out var configuredValue, out _)
                        && ParseBoolean(configuredValue) == true
                        && !TryReadRule(key, step.DiagnosticIds, value => ParseBoolean(value).HasValue, out _, out _))
                    {
                        overrides[key] = "true:none";
                    }
                }
            }
        }

        void RaiseSeverities(IEnumerable<string> diagnosticIds)
        {
            foreach (var diagnosticId in diagnosticIds)
            {
                overrides["dotnet_diagnostic." + diagnosticId + ".severity"] = "suggestion";
            }
        }

        return overrides;
    }

    /// <summary>
    /// Records the C# file header defined by <c>file_header_template</c>: each template line becomes a
    /// <c>//</c> comment line, <c>\n</c> escapes separate lines and <c>{fileName}</c> is replaced by the file name.
    /// <c>unset</c> or an empty template defines an empty header. The option takes no severity suffix.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    private void ApplyEditorConfigFileHeader(string filePath)
    {
        if (!_editorConfigOptions.TryGetValue("file_header_template", out var template))
        {
            return;
        }

        template = template?.Trim() ?? string.Empty;
        if (template.Length == 0 || string.Equals(template, "unset", StringComparison.OrdinalIgnoreCase))
        {
            SetEditorConfigValue(FileHeaderSetting, "file_header_template", string.Empty);

            return;
        }

        var lines = template
            .Replace("{fileName}", Path.GetFileName(filePath))
            .Split(new[] { "\\n" }, StringSplitOptions.None)
            .Select(line => line.Length == 0 ? "//" : "// " + line);

        SetEditorConfigValue(FileHeaderSetting, "file_header_template", string.Join(Environment.NewLine, lines));
    }

    /// <summary>
    /// Resolves whether using directives are organized. .editorconfig turns it off when it contradicts the organizer
    /// (System directives not first, or groups separated) and on when System directives first is enforced;
    /// otherwise the repository policy decides.
    /// </summary>
    /// <returns>True when using directives are organized.</returns>
    private bool ResolveOrganizeUsings()
    {
        var sortDefined = TryReadOption("dotnet_sort_system_directives_first", ParseBoolean, out var sortSystemFirst);
        var separateDefined = TryReadOption("dotnet_separate_import_directive_groups", ParseBoolean, out var separateGroups);

        if ((sortDefined && sortSystemFirst != true) || (separateDefined && separateGroups != false))
        {
            return false;
        }

        return sortSystemFirst == true || _repositoryOverrides.OrganizeUsings == true;
    }

    /// <summary>
    /// Reads a positive integer .editorconfig option such as <c>tab_width</c>.
    /// </summary>
    /// <param name="key">The .editorconfig option name.</param>
    /// <returns>The value, or null when the option is undefined, not a positive integer or ignored.</returns>
    private int? ReadPositiveInteger(string key) => TryReadOption(key, ParsePositiveInteger, out var value) ? value : null;

    /// <summary>
    /// Reads a plain .editorconfig option (one without a Roslyn rule) in the <c>value[:severity]</c> form.
    /// </summary>
    /// <typeparam name="T">The parsed value type.</typeparam>
    /// <param name="key">The .editorconfig option name.</param>
    /// <param name="parseValue">Maps the option value, or returns null when the value is unrecognized.</param>
    /// <param name="value">The enforced value, or null when the option is ignored.</param>
    /// <returns>
    /// True when the option is defined with a recognized value and no severity or an enforcing one; otherwise false,
    /// so the option is ignored.
    /// </returns>
    private bool TryReadOption<T>(string key, Func<string, T?> parseValue, out T? value)
        where T : struct
    {
        value = null;
        if (!TryReadRawOption(key, out var rawValue, out var severity))
        {
            return false;
        }

        var parsed = parseValue(rawValue);
        if (parsed is null || !IsEnforcing(severity))
        {
            return false;
        }

        value = parsed;

        return true;
    }

    /// <summary>
    /// Splits an .editorconfig option in the <c>value[:severity]</c> form.
    /// </summary>
    /// <param name="key">The .editorconfig option name.</param>
    /// <param name="value">The trimmed value.</param>
    /// <param name="severity">The trimmed, lower-cased severity suffix, or null without one.</param>
    /// <returns>True when the option is defined with no severity or a recognized one.</returns>
    private bool TryReadRawOption(string key, out string value, out string severity)
    {
        value = null;
        severity = null;
        if (!_editorConfigOptions.TryGetValue(key, out var rawValue) || rawValue is null)
        {
            return false;
        }

        var separatorIndex = rawValue.IndexOf(':');
        if (separatorIndex >= 0)
        {
            var suffix = rawValue.Substring(separatorIndex + 1).Trim().ToLowerInvariant();
            if (separatorIndex != rawValue.LastIndexOf(':') || !TryParseSeverity(suffix, out _))
            {
                return false;
            }

            severity = suffix;
            rawValue = rawValue.Substring(0, separatorIndex);
        }

        value = rawValue.Trim();

        return true;
    }

    /// <summary>
    /// Reads a Roslyn rule configured by an option and the severities of its diagnostics, resolved as Roslyn reports
    /// it. An option with the <c>:none</c> suffix stops the rule whatever else is configured. Otherwise the effective
    /// severity of each diagnostic is the first one defined of <c>dotnet_diagnostic.&lt;id&gt;.severity</c>,
    /// <c>dotnet_analyzer_diagnostic.category-&lt;category&gt;.severity</c>, <c>dotnet_analyzer_diagnostic.severity</c>
    /// and the option's severity suffix; an option without a suffix enforces the diagnostics none of those configure.
    /// The rule is enforced when one of its diagnostics is <c>suggestion</c> or higher, and not enforced when none is
    /// (all <c>none</c> or <c>silent</c>, or nothing configured), so the next source decides.
    /// </summary>
    /// <param name="key">The .editorconfig option name, or null for a rule configured by severity only.</param>
    /// <param name="diagnosticIds">The IDs of the rule's diagnostics.</param>
    /// <param name="isValidValue">Validates the option value; an invalid option is treated as undefined.</param>
    /// <param name="value">The enforced option value, or null when the rule is enforced with Roslyn's default value.</param>
    /// <param name="decidingKey">
    /// The option name when the option is defined, otherwise the severity entry that enforces the rule.
    /// </param>
    /// <returns>True when .editorconfig enforces the rule.</returns>
    private bool TryReadRule(string key, IEnumerable<string> diagnosticIds, Func<string, bool> isValidValue, out string value, out string decidingKey)
    {
        value = null;
        decidingKey = null;

        string optionSeverity = null;
        var optionDefined = key is not null && TryReadRawOption(key, out value, out optionSeverity) && isValidValue(value);
        if (!optionDefined)
        {
            value = null;
        }
        else if (optionSeverity == "none")
        {
            // Roslyn does not run the rule's analysis at all, whatever severity its diagnostics get.
            value = null;

            return false;
        }

        string enforcingKey = null;
        foreach (var diagnosticId in diagnosticIds)
        {
            if (TryReadDiagnosticSeverity(diagnosticId, out var severityKey, out var enforced))
            {
                enforcingKey ??= enforced ? severityKey : null;
            }
            else if (optionDefined && IsEnforcing(optionSeverity))
            {
                enforcingKey ??= key;
            }
        }

        if (enforcingKey is null)
        {
            value = null;

            return false;
        }

        decidingKey = optionDefined ? key : enforcingKey;

        return true;
    }

    /// <summary>
    /// Reads the severity .editorconfig configures for a diagnostic, other than through an option suffix: the first
    /// recognized one of <c>dotnet_diagnostic.&lt;id&gt;.severity</c>,
    /// <c>dotnet_analyzer_diagnostic.category-&lt;category&gt;.severity</c> and <c>dotnet_analyzer_diagnostic.severity</c>.
    /// Keys are compared ignoring case, as in Roslyn.
    /// </summary>
    /// <param name="diagnosticId">The diagnostic ID.</param>
    /// <param name="severityKey">The lower-cased entry that configures the severity.</param>
    /// <param name="enforced">True when the severity is <c>suggestion</c> or higher.</param>
    /// <returns>True when one of the entries configures the severity.</returns>
    private bool TryReadDiagnosticSeverity(string diagnosticId, out string severityKey, out bool enforced)
    {
        var bulkApplies = !DisabledByDefaultDiagnostics.Contains(diagnosticId);
        var category = GetDiagnosticCategory(diagnosticId);
        var candidates = new[]
        {
            EditorConfigHelper.DiagnosticSeverityKey(diagnosticId),
            !bulkApplies || category is null ? null : "dotnet_analyzer_diagnostic.category-" + category.ToLowerInvariant() + ".severity",
            bulkApplies ? "dotnet_analyzer_diagnostic.severity" : null,
        };

        foreach (var candidate in candidates)
        {
            if (candidate is not null
                && _editorConfigOptions.TryGetValue(candidate, out var severity)
                && severity is not null
                && TryParseSeverity(severity.Trim(), out enforced))
            {
                severityKey = candidate;

                return true;
            }
        }

        severityKey = null;
        enforced = false;

        return false;
    }

    /// <summary>
    /// Gets the analyzer category of a diagnostic handled by the cleanup, which
    /// <c>dotnet_analyzer_diagnostic.category-&lt;category&gt;.severity</c> configures.
    /// </summary>
    /// <param name="diagnosticId">The diagnostic ID.</param>
    /// <returns>The category, or null when unknown.</returns>
    private static string GetDiagnosticCategory(string diagnosticId) => diagnosticId.StartsWith("IDE", StringComparison.OrdinalIgnoreCase)
            ? "Style"
            : AnalyzerCategories.TryGetValue(diagnosticId, out var category) ? category : null;

    /// <summary>
    /// Tells whether an option severity suffix enforces the option.
    /// </summary>
    /// <param name="severity">The recognized severity suffix, or null without one.</param>
    /// <returns>True without a suffix or for <c>suggestion</c> or higher.</returns>
    private static bool IsEnforcing(string severity) => severity is null || (TryParseSeverity(severity, out var enforced) && enforced);

    /// <summary>
    /// Parses an .editorconfig option severity suffix.
    /// </summary>
    /// <param name="severity">The severity text.</param>
    /// <param name="enforced">
    /// False for <c>none</c>, <c>silent</c> and <c>refactoring</c> (Visual Studio does not act on them, so the option is
    /// ignored); true for <c>suggestion</c>, <c>warning</c> and <c>error</c>.
    /// </param>
    /// <returns>True when the severity is recognized.</returns>
    private static bool TryParseSeverity(string severity, out bool enforced)
    {
        enforced = true;
        switch (severity.ToLowerInvariant())
        {
            case "none":
            case "silent":
            case "refactoring":
                enforced = false;
                return true;

            case "suggestion":
            case "warning":
            case "error":
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Parses <c>true</c> or <c>false</c>.
    /// </summary>
    /// <param name="value">The option value.</param>
    /// <returns>The parsed value, or null when unrecognized.</returns>
    private static bool? ParseBoolean(string value) => bool.TryParse(value, out var parsed) ? parsed : null;

    /// <summary>
    /// Parses <c>true</c> or <c>false</c> and inverts it, for options that allow what the cleanup step removes.
    /// </summary>
    /// <param name="value">The option value.</param>
    /// <returns>The inverted value, or null when unrecognized.</returns>
    private static bool? ParseInvertedBoolean(string value) => bool.TryParse(value, out var parsed) ? !parsed : null;

    /// <summary>
    /// Parses an expression body preference such as <c>csharp_style_expression_bodied_lambdas</c>.
    /// </summary>
    /// <param name="value">The option value.</param>
    /// <returns>True when expression bodies are preferred (always or on a single line), false when not, or null when unrecognized.</returns>
    private static bool? ParseExpressionBodyPreference(string value) => string.Equals(value, "when_on_single_line", StringComparison.OrdinalIgnoreCase) ? true : ParseBoolean(value);

    /// <summary>
    /// Parses a positive integer.
    /// </summary>
    /// <param name="value">The option value.</param>
    /// <returns>The parsed value, or null when not a positive integer.</returns>
    private static int? ParsePositiveInteger(string value) => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;

    /// <summary>
    /// Parses <c>dotnet_style_prefer_collection_expression</c>.
    /// </summary>
    /// <param name="value">The option value.</param>
    /// <returns>True when collection expressions are preferred, false when not, or null when unrecognized.</returns>
    private static bool? ParseCollectionExpressionPreference(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "true":
            case "when_types_exactly_match":
            case "when_types_loosely_match":
                return true;

            case "false":
            case "never":
                return false;

            default:
                return null;
        }
    }

    /// <summary>
    /// Parses <c>dotnet_style_require_accessibility_modifiers</c>.
    /// </summary>
    /// <param name="value">The option value.</param>
    /// <returns>True when explicit modifiers are required, false when not, or null when unrecognized.</returns>
    private static bool? ParseAccessibilityModifiersPreference(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "always":
            case "for_non_interface_members":
                return true;

            case "never":
            case "omit_if_default":
                return false;

            default:
                return null;
        }
    }

    /// <summary>
    /// Parses <c>csharp_style_namespace_declarations</c>.
    /// </summary>
    /// <param name="value">The option value.</param>
    /// <returns>True for <c>file_scoped</c>, false for <c>block_scoped</c>, or null when unrecognized.</returns>
    private static bool? ParseNamespaceDeclarations(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "file_scoped":
                return true;

            case "block_scoped":
                return false;

            default:
                return null;
        }
    }

    /// <summary>
    /// Parses <c>csharp_using_directive_placement</c>.
    /// </summary>
    /// <param name="value">The option value.</param>
    /// <returns>True for <c>outside_namespace</c>, false for <c>inside_namespace</c>, or null when unrecognized.</returns>
    private static bool? ParseUsingDirectivePlacement(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "outside_namespace":
                return true;

            case "inside_namespace":
                return false;

            default:
                return null;
        }
    }

    /// <summary>
    /// Parses <c>indent_style</c>.
    /// </summary>
    /// <param name="value">The option value.</param>
    /// <returns>The indentation style, or null when unrecognized.</returns>
    private static IndentationPreference? ParseIndentStyle(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "space":
                return IndentationPreference.Spaces;

            case "tab":
                return IndentationPreference.Tabs;

            default:
                return null;
        }
    }

    /// <summary>
    /// A cleanup step applied through a Roslyn analyzer and code fix.
    /// </summary>
    private sealed class RoslynStep
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RoslynStep" /> class.
        /// </summary>
        /// <param name="settingName">The Visual Studio setting that turns the step on.</param>
        /// <param name="key">The .editorconfig option of the rule.</param>
        /// <param name="value">The option value the step applies.</param>
        /// <param name="diagnosticIds">The IDs of the diagnostics the step fixes.</param>
        /// <param name="reverseDiagnosticIds">
        /// The IDs of the diagnostics that apply the opposite value of the option when .editorconfig decides it.
        /// </param>
        /// <param name="otherOptions">The other boolean options reported through the same diagnostics.</param>
        internal RoslynStep(string settingName, string key, string value, string[] diagnosticIds, string[] reverseDiagnosticIds, params string[] otherOptions)
        {
            SettingName = settingName;
            Key = key;
            Value = value;
            DiagnosticIds = diagnosticIds;
            ReverseDiagnosticIds = reverseDiagnosticIds;
            OtherOptions = otherOptions;
        }

        /// <summary>
        /// Gets the Visual Studio setting that turns the step on.
        /// </summary>
        internal string SettingName { get; }

        /// <summary>
        /// Gets the .editorconfig option of the rule.
        /// </summary>
        internal string Key { get; }

        /// <summary>
        /// Gets the option value the step applies.
        /// </summary>
        internal string Value { get; }

        /// <summary>
        /// Gets the IDs of the diagnostics the step fixes.
        /// </summary>
        internal string[] DiagnosticIds { get; }

        /// <summary>
        /// Gets the IDs of the diagnostics that apply the opposite value of the option when .editorconfig decides it.
        /// </summary>
        internal string[] ReverseDiagnosticIds { get; }

        /// <summary>
        /// Gets the other boolean options reported through the same diagnostics.
        /// </summary>
        internal string[] OtherOptions { get; }
    }
}
