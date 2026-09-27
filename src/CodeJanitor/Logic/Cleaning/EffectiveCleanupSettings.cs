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
/// user's Visual Studio setting applies. An .editorconfig option whose severity suffix is <c>:none</c> or
/// <c>:silent</c> is ignored, as are options with unrecognized values or severities, so the next source decides;
/// <c>suggestion</c> or higher, or no suffix, enforces the value. The same order resolves the Code Janitor
/// code-style rules (<see cref="CodeStyleRules" />).
/// </summary>
internal sealed class EffectiveCleanupSettings
{
    private const string ConvertToFileScopedNamespaceSetting = "Cleaning_ConvertToFileScopedNamespace";
    private const string MoveUsingsOutsideNamespaceSetting = "Cleaning_MoveUsingsOutsideNamespace";
    private const string FileHeaderSetting = "Cleaning_UpdateFileHeaderCSharp";
    private const int DefaultTabSize = 4;
    private const int DefaultIndentSize = 4;

    /// <summary>
    /// The settings controlled by <c>dotnet_style_require_accessibility_modifiers</c>.
    /// </summary>
    private static readonly string[] ExplicitAccessModifierSettings =
    {
        "Cleaning_InsertExplicitAccessModifiersOnClasses",
        "Cleaning_InsertExplicitAccessModifiersOnDelegates",
        "Cleaning_InsertExplicitAccessModifiersOnEnumerations",
        "Cleaning_InsertExplicitAccessModifiersOnEvents",
        "Cleaning_InsertExplicitAccessModifiersOnFields",
        "Cleaning_InsertExplicitAccessModifiersOnInterfaces",
        "Cleaning_InsertExplicitAccessModifiersOnMethods",
        "Cleaning_InsertExplicitAccessModifiersOnProperties",
        "Cleaning_InsertExplicitAccessModifiersOnStructs",
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

        NamespaceDeclarations = ResolveNamespaceDeclarations();
        UsingDirectivePlacement = ResolveUsingDirectivePlacement();
        OrganizeUsings = ResolveOrganizeUsings();

        Indentation = TryReadOption("indent_style", ParseIndentStyle, out var indentStyle)
            ? indentStyle ?? IndentationPreference.Unchanged
            : IndentationPreference.Unchanged;
        TabSize = ReadPositiveInteger("tab_width") ?? ReadPositiveInteger("indent_size") ?? DefaultTabSize;
        IndentSize = ReadPositiveInteger("indent_size")
            ?? (TryReadOption("indent_size", ParseTabKeyword, out _) ? ReadPositiveInteger("tab_width") : null)
            ?? DefaultIndentSize;

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
    /// reports, and fixes, exactly the rules of <see cref="CodeStyleValues" />: each rule with its value and
    /// <c>suggestion</c> severity, the severity of its diagnostics raised to <c>suggestion</c>, and every other rule
    /// reported through one of those diagnostics, unless .editorconfig enforces it, silenced with <c>none</c>.
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
    /// Gets the namespace declaration style to enforce, from <c>csharp_style_namespace_declarations</c>, the
    /// repository <c>convertToFileScopedNamespace</c> policy or the user setting, in that order.
    /// </summary>
    internal NamespaceDeclarationPreference NamespaceDeclarations { get; }

    /// <summary>
    /// Gets the using directive placement to enforce, from <c>csharp_using_directive_placement</c>, the repository
    /// <c>moveUsingsOutsideNamespace</c> policy or the user setting, in that order.
    /// </summary>
    internal UsingDirectivePlacementPreference UsingDirectivePlacement { get; }

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
    /// Gets the number of columns of one indentation level: a numeric <c>indent_size</c>, <c>tab_width</c> when
    /// <c>indent_size = tab</c>, otherwise 4.
    /// </summary>
    internal int IndentSize { get; }

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
    internal static EffectiveCleanupSettings For(string filePath)
    {
        return new EffectiveCleanupSettings(
            filePath,
            EditorConfigHelper.LoadOptions(filePath),
            RepositoryCleanupSettings.LoadForFile(filePath));
    }

    /// <summary>
    /// Gets the effective value of a boolean Visual Studio setting.
    /// </summary>
    /// <param name="settingName">The Visual Studio setting property name, for example <c>Cleaning_RemoveEndOfLineWhitespace</c>.</param>
    /// <returns>The effective value.</returns>
    internal bool GetBoolean(string settingName)
    {
        return _editorConfigValues.TryGetValue(settingName, out var value) && value is bool boolean
            ? boolean
            : _repositoryOverrides.TryGetBoolean(settingName, (bool)Settings.Default[settingName]);
    }

    /// <summary>
    /// Gets the effective value of a string Visual Studio setting.
    /// </summary>
    /// <param name="settingName">The Visual Studio setting property name, for example <c>Cleaning_UpdateFileHeaderCSharp</c>.</param>
    /// <returns>The effective value.</returns>
    internal string GetString(string settingName)
    {
        return _editorConfigValues.TryGetValue(settingName, out var value) && value is string text
            ? text
            : _repositoryOverrides.TryGetString(settingName, (string)Settings.Default[settingName]);
    }

    /// <summary>
    /// Gets the effective value of an integer Visual Studio setting.
    /// </summary>
    /// <param name="settingName">The Visual Studio setting property name.</param>
    /// <returns>The effective value.</returns>
    internal int GetInt32(string settingName)
    {
        return _repositoryOverrides.TryGetInt32(settingName, (int)Settings.Default[settingName]);
    }

    /// <summary>
    /// Records the boolean settings defined by .editorconfig options.
    /// </summary>
    private void ApplyEditorConfigBooleans()
    {
        ApplyBoolean("trim_trailing_whitespace", ParseBoolean, "Cleaning_RemoveEndOfLineWhitespace");
        ApplyBoolean("csharp_style_var_when_type_is_apparent", ParseBoolean, "Cleaning_ConvertToVarWhenApparent");
        ApplyBoolean("csharp_style_inlined_variable_declaration", ParseBoolean, "Cleaning_InlineOutVariableDeclarations");
        ApplyBoolean("dotnet_style_prefer_collection_expression", ParseCollectionExpressionPreference, "Cleaning_ConvertToCollectionExpressions");
        ApplyBoolean("dotnet_style_readonly_field", ParseBoolean, "Cleaning_MakeFieldsReadonlyWhenSafe");
        ApplyBoolean("dotnet_style_require_accessibility_modifiers", ParseAccessibilityModifiersPreference, ExplicitAccessModifierSettings);

        if (TryReadOption("insert_final_newline", ParseBoolean, out var insertFinalNewline))
        {
            SetEditorConfigValue("Cleaning_InsertEndOfFileTrailingNewLine", "insert_final_newline", insertFinalNewline == true);
            SetEditorConfigValue("Cleaning_RemoveEndOfFileTrailingNewLine", "insert_final_newline", insertFinalNewline == false);
        }
    }

    /// <summary>
    /// Records the boolean settings controlled by one .editorconfig option when the option is defined.
    /// </summary>
    /// <param name="key">The .editorconfig option name.</param>
    /// <param name="parseValue">Maps the option value to a setting value, or null when the value is unrecognized.</param>
    /// <param name="settingNames">The controlled Visual Studio setting property names.</param>
    private void ApplyBoolean(string key, Func<string, bool?> parseValue, params string[] settingNames)
    {
        if (!TryReadOption(key, parseValue, out var value))
        {
            return;
        }

        foreach (var settingName in settingNames)
        {
            SetEditorConfigValue(settingName, key, value == true);
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
    /// rule (see <see cref="TryReadRule" />). A rule enforced only through a diagnostic severity uses Roslyn's default
    /// option value; a rule configured by severity only turns its step on.
    /// </summary>
    private void ApplyEditorConfigRules()
    {
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
                ? configuredValue
                : rule.DefaultValue;
            overrides[rule.Key] = value + ":none";
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
    /// Resolves the namespace declaration style: an enforced .editorconfig style wins, otherwise the file-scoped
    /// conversion setting decides.
    /// </summary>
    /// <returns>The namespace declaration style.</returns>
    private NamespaceDeclarationPreference ResolveNamespaceDeclarations()
    {
        if (TryReadOption("csharp_style_namespace_declarations", ParseNamespaceDeclarations, out var preference))
        {
            SetEditorConfigValue(ConvertToFileScopedNamespaceSetting, "csharp_style_namespace_declarations", preference == NamespaceDeclarationPreference.FileScoped);

            return preference.Value;
        }

        return GetBoolean(ConvertToFileScopedNamespaceSetting)
            ? NamespaceDeclarationPreference.FileScoped
            : NamespaceDeclarationPreference.Unchanged;
    }

    /// <summary>
    /// Resolves the using directive placement: an enforced .editorconfig placement wins, otherwise the move-outside
    /// setting decides.
    /// </summary>
    /// <returns>The using directive placement.</returns>
    private UsingDirectivePlacementPreference ResolveUsingDirectivePlacement()
    {
        if (TryReadOption("csharp_using_directive_placement", ParseUsingDirectivePlacement, out var preference))
        {
            SetEditorConfigValue(MoveUsingsOutsideNamespaceSetting, "csharp_using_directive_placement", preference == UsingDirectivePlacementPreference.OutsideNamespace);

            return preference.Value;
        }

        return GetBoolean(MoveUsingsOutsideNamespaceSetting)
            ? UsingDirectivePlacementPreference.OutsideNamespace
            : UsingDirectivePlacementPreference.Unchanged;
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
    private int? ReadPositiveInteger(string key)
    {
        return TryReadOption(key, ParsePositiveInteger, out var value) ? value : null;
    }

    /// <summary>
    /// Reads an .editorconfig option in the <c>value[:severity]</c> form.
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
        if (!TryReadRawOption(key, out var rawValue, out var enforced))
        {
            return false;
        }

        var parsed = parseValue(rawValue);
        if (parsed is null || !enforced)
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
    /// <param name="enforced">False when the severity does not enforce the option; true without a severity.</param>
    /// <returns>True when the option is defined with no severity or a recognized one.</returns>
    private bool TryReadRawOption(string key, out string value, out bool enforced)
    {
        value = null;
        enforced = false;
        if (!_editorConfigOptions.TryGetValue(key, out var rawValue) || rawValue is null)
        {
            return false;
        }

        enforced = true;
        var separatorIndex = rawValue.IndexOf(':');
        if (separatorIndex >= 0)
        {
            if (separatorIndex != rawValue.LastIndexOf(':') ||
                !TryParseSeverity(rawValue.Substring(separatorIndex + 1).Trim(), out enforced))
            {
                return false;
            }

            rawValue = rawValue.Substring(0, separatorIndex);
        }

        value = rawValue.Trim();

        return true;
    }

    /// <summary>
    /// Reads a Roslyn rule configured by an option and the severities of its diagnostics. As in Roslyn, a
    /// <c>dotnet_diagnostic.&lt;id&gt;.severity</c> of one of the diagnostics beats the option's own severity suffix:
    /// the rule is enforced when one of them is <c>suggestion</c> or higher, and not enforced when all of them are
    /// <c>none</c> or <c>silent</c>. Without such a severity, the option decides as in <see cref="TryReadOption{T}" />.
    /// </summary>
    /// <param name="key">The .editorconfig option name, or null for a rule configured by severity only.</param>
    /// <param name="diagnosticIds">The IDs of the rule's diagnostics.</param>
    /// <param name="isValidValue">Validates the option value.</param>
    /// <param name="value">The enforced option value, or null when the rule is enforced with its default value.</param>
    /// <param name="decidingKey">The .editorconfig option that enforces the rule.</param>
    /// <returns>True when .editorconfig enforces the rule.</returns>
    private bool TryReadRule(string key, IEnumerable<string> diagnosticIds, Func<string, bool> isValidValue, out string value, out string decidingKey)
    {
        value = null;
        decidingKey = null;

        var optionEnforced = false;
        var optionDefined = key is not null && TryReadRawOption(key, out value, out optionEnforced) && isValidValue(value);
        if (!optionDefined)
        {
            value = null;
        }

        var severityConfigured = false;
        string enforcingKey = null;
        foreach (var severityKey in diagnosticIds.Select(EditorConfigHelper.DiagnosticSeverityKey))
        {
            if (_editorConfigOptions.TryGetValue(severityKey, out var severity) && TryParseSeverity(severity.Trim(), out var enforced))
            {
                severityConfigured = true;
                enforcingKey ??= enforced ? severityKey : null;
            }
        }

        if (severityConfigured)
        {
            if (enforcingKey is null)
            {
                value = null;

                return false;
            }

            decidingKey = optionDefined ? key : enforcingKey;

            return true;
        }

        if (!optionDefined || !optionEnforced)
        {
            value = null;

            return false;
        }

        decidingKey = key;

        return true;
    }

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
    private static bool? ParseBoolean(string value)
    {
        return bool.TryParse(value, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Parses <c>true</c> or <c>false</c> and inverts it, for options that allow what the cleanup step removes.
    /// </summary>
    /// <param name="value">The option value.</param>
    /// <returns>The inverted value, or null when unrecognized.</returns>
    private static bool? ParseInvertedBoolean(string value)
    {
        return bool.TryParse(value, out var parsed) ? !parsed : null;
    }

    /// <summary>
    /// Parses an expression body preference such as <c>csharp_style_expression_bodied_lambdas</c>.
    /// </summary>
    /// <param name="value">The option value.</param>
    /// <returns>True when expression bodies are preferred (always or on a single line), false when not, or null when unrecognized.</returns>
    private static bool? ParseExpressionBodyPreference(string value)
    {
        return string.Equals(value, "when_on_single_line", StringComparison.OrdinalIgnoreCase) ? true : ParseBoolean(value);
    }

    /// <summary>
    /// Parses a positive integer.
    /// </summary>
    /// <param name="value">The option value.</param>
    /// <returns>The parsed value, or null when not a positive integer.</returns>
    private static int? ParsePositiveInteger(string value)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;
    }

    /// <summary>
    /// Parses the <c>tab</c> value of <c>indent_size</c>.
    /// </summary>
    /// <param name="value">The option value.</param>
    /// <returns>True for <c>tab</c>, or null otherwise.</returns>
    private static bool? ParseTabKeyword(string value)
    {
        return string.Equals(value, "tab", StringComparison.OrdinalIgnoreCase) ? true : null;
    }

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
    /// <returns>The namespace declaration style, or null when unrecognized.</returns>
    private static NamespaceDeclarationPreference? ParseNamespaceDeclarations(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "file_scoped":
                return NamespaceDeclarationPreference.FileScoped;

            case "block_scoped":
                return NamespaceDeclarationPreference.BlockScoped;

            default:
                return null;
        }
    }

    /// <summary>
    /// Parses <c>csharp_using_directive_placement</c>.
    /// </summary>
    /// <param name="value">The option value.</param>
    /// <returns>The using directive placement, or null when unrecognized.</returns>
    private static UsingDirectivePlacementPreference? ParseUsingDirectivePlacement(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "outside_namespace":
                return UsingDirectivePlacementPreference.OutsideNamespace;

            case "inside_namespace":
                return UsingDirectivePlacementPreference.InsideNamespace;

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
}
