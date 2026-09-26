using CodeJanitor.Helpers;
using CodeJanitor.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// The namespace declaration style the cleanup enforces.
/// </summary>
internal enum NamespaceDeclarationPreference
{
    /// <summary>
    /// Namespace declarations are left as they are.
    /// </summary>
    Unchanged,

    /// <summary>
    /// Block-scoped namespaces are converted to file-scoped namespaces.
    /// </summary>
    FileScoped,

    /// <summary>
    /// File-scoped namespaces are converted to block-scoped namespaces.
    /// </summary>
    BlockScoped,
}

/// <summary>
/// The using directive placement the cleanup enforces.
/// </summary>
internal enum UsingDirectivePlacementPreference
{
    /// <summary>
    /// Using directives are left where they are.
    /// </summary>
    Unchanged,

    /// <summary>
    /// Using directives are moved outside the namespace.
    /// </summary>
    OutsideNamespace,

    /// <summary>
    /// Using directives are moved inside the namespace.
    /// </summary>
    InsideNamespace,
}

/// <summary>
/// The leading indentation style the cleanup enforces.
/// </summary>
internal enum IndentationPreference
{
    /// <summary>
    /// Indentation is left as it is.
    /// </summary>
    Unchanged,

    /// <summary>
    /// Tab indentation is converted to spaces.
    /// </summary>
    Spaces,

    /// <summary>
    /// Space indentation is converted to tabs.
    /// </summary>
    Tabs,
}

/// <summary>
/// Resolves the cleanup settings that apply to one file for both the editor and the closed-file cleanup:
/// a setting defined by .editorconfig wins, otherwise the repository policy (.codejanitor) wins, otherwise the
/// user's Visual Studio setting applies. An .editorconfig option whose severity suffix is <c>:none</c> is ignored,
/// as are options with unrecognized values or severities, so the next source decides; any other severity, or no
/// suffix, enforces the value.
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
        ApplyEditorConfigFileHeader(filePath);

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
            _editorConfigValues["Cleaning_InsertEndOfFileTrailingNewLine"] = insertFinalNewline == true;
            _editorConfigValues["Cleaning_RemoveEndOfFileTrailingNewLine"] = insertFinalNewline == false;
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
            _editorConfigValues[settingName] = value == true;
        }
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
            _editorConfigValues[FileHeaderSetting] = string.Empty;
            return;
        }

        var lines = template
            .Replace("{fileName}", Path.GetFileName(filePath))
            .Split(new[] { "\\n" }, StringSplitOptions.None)
            .Select(line => line.Length == 0 ? "//" : "// " + line);

        _editorConfigValues[FileHeaderSetting] = string.Join(Environment.NewLine, lines);
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
            _editorConfigValues[ConvertToFileScopedNamespaceSetting] = preference == NamespaceDeclarationPreference.FileScoped;
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
            _editorConfigValues[MoveUsingsOutsideNamespaceSetting] = preference == UsingDirectivePlacementPreference.OutsideNamespace;
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
    /// True when the option is defined with a recognized value and a severity other than <c>none</c>; otherwise false,
    /// so the option is ignored.
    /// </returns>

    private bool TryReadOption<T>(string key, Func<string, T?> parseValue, out T? value)
        where T : struct
    {
        value = null;
        if (!_editorConfigOptions.TryGetValue(key, out var rawValue) || rawValue is null)
        {
            return false;
        }

        var enforced = true;
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

        var parsed = parseValue(rawValue.Trim());
        if (parsed is null || !enforced)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>
    /// Parses an .editorconfig option severity suffix.
    /// </summary>
    /// <param name="severity">The severity text.</param>
    /// <param name="enforced">False for <c>none</c> (the option is ignored); true for every other recognized severity.</param>
    /// <returns>True when the severity is recognized.</returns>

    private static bool TryParseSeverity(string severity, out bool enforced)
    {
        enforced = true;
        switch (severity.ToLowerInvariant())
        {
            case "none":
                enforced = false;
                return true;

            case "silent":
            case "refactoring":
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
