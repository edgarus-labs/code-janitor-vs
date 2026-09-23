using CodeJanitor.Properties;
using CodeJanitor.UI.Enumerations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// Represents cleanup overrides loaded from a repository-level policy file (.codejanitor), mapping
/// the shared CodeJanitor VS Code configuration schema onto Visual Studio setting property names.
/// </summary>
internal sealed class RepositoryCleanupOverrides
{
    /// <summary>
    /// An empty overrides instance used when no repository policy file is present.
    /// </summary>
    internal static readonly RepositoryCleanupOverrides Empty = new RepositoryCleanupOverrides(
        new Dictionary<string, object>(StringComparer.Ordinal), removeRegions: null, organizeUsings: null);

    private readonly IReadOnlyDictionary<string, object> _values;

    /// <summary>
    /// Initializes a new instance of the <see cref="RepositoryCleanupOverrides" /> class.
    /// </summary>
    /// <param name="values">The overridden setting values keyed by Visual Studio setting property name.</param>
    /// <param name="removeRegions">The repository policy for region removal, if specified.</param>
    /// <param name="organizeUsings">The repository policy for forcing using directive organization, if specified.</param>

    internal RepositoryCleanupOverrides(IReadOnlyDictionary<string, object> values, bool? removeRegions, bool? organizeUsings)
    {
        _values = values;
        RemoveRegions = removeRegions;
        OrganizeUsings = organizeUsings;
    }

    /// <summary>
    /// Gets the overridden setting values keyed by Visual Studio setting property name.
    /// </summary>
    internal IReadOnlyDictionary<string, object> Values => _values;

    /// <summary>
    /// Gets the number of overridden settings.
    /// </summary>
    internal int Count => _values.Count;

    /// <summary>
    /// Gets the repository policy for region removal, if specified. Visual Studio has no user setting
    /// for this; the policy only takes effect when the repository file defines it.
    /// </summary>
    internal bool? RemoveRegions { get; }

    /// <summary>
    /// Gets the repository policy for forcing using directive organization independent of .editorconfig,
    /// if specified.
    /// </summary>
    internal bool? OrganizeUsings { get; }

    /// <summary>
    /// Gets an overridden boolean setting value, falling back when the repository policy does not define it.
    /// </summary>
    /// <param name="settingName">The Visual Studio setting property name.</param>
    /// <param name="fallback">The fallback value.</param>
    /// <returns>The effective value.</returns>

    internal bool TryGetBoolean(string settingName, bool fallback)
    {
        return _values.TryGetValue(settingName, out var value) && value is bool boolean ? boolean : fallback;
    }

    /// <summary>
    /// Gets an overridden string setting value, falling back when the repository policy does not define it.
    /// </summary>
    /// <param name="settingName">The Visual Studio setting property name.</param>
    /// <param name="fallback">The fallback value.</param>
    /// <returns>The effective value.</returns>

    internal string TryGetString(string settingName, string fallback)
    {
        return _values.TryGetValue(settingName, out var value) && value is string text ? text : fallback;
    }

    /// <summary>
    /// Gets an overridden integer setting value, falling back when the repository policy does not define it.
    /// </summary>
    /// <param name="settingName">The Visual Studio setting property name.</param>
    /// <param name="fallback">The fallback value.</param>
    /// <returns>The effective value.</returns>

    internal int TryGetInt32(string settingName, int fallback)
    {
        return _values.TryGetValue(settingName, out var value) && value is int number ? number : fallback;
    }
}

/// <summary>
/// Reads and writes the repository-level cleanup policy file (.codejanitor) shared with the VS Code
/// extension. Unknown keys, wrong value types and invalid JSON are ignored, mirroring the VS Code behavior.
/// </summary>
internal static class RepositoryCleanupSettings
{
    /// <summary>
    /// The primary repository policy file name, also used by the VS Code extension.
    /// </summary>
    internal const string PrimaryConfigFileName = ".codejanitor";

    /// <summary>
    /// The recognized repository policy file names, checked in order within each directory.
    /// </summary>
    internal static readonly string[] ConfigFileNames = { PrimaryConfigFileName, ".code-janitor.json" };

    /// <summary>
    /// Setting names that do not follow the Cleaning_* PascalCase mapping rule.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> SpecialSettingNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["formatComments"] = "Formatting_CommentRunDuringCleanup",
        ["fileHeaderCSharp"] = "Cleaning_UpdateFileHeaderCSharp",
        ["fileHeaderPosition"] = "Cleaning_UpdateFileHeader_HeaderPosition",
        ["fileHeaderUpdateMode"] = "Cleaning_UpdateFileHeader_HeaderUpdateMode",
        // Accept both the canonical key and the VS Code settings alias for the same flag.
        ["insertBlankLineBeforeReturnAndThrow"] = "Cleaning_InsertBlankLineBeforeReturnAndThrowStatements",
    };

    /// <summary>
    /// The target settings of the insertBlankLinePadding group alias, mirroring the VS Code extension.
    /// </summary>
    private static readonly string[] BlankLinePaddingAliasTargets =
    {
        "insertBlankLinePaddingBeforeClasses", "insertBlankLinePaddingAfterClasses",
        "insertBlankLinePaddingBeforeDelegates", "insertBlankLinePaddingAfterDelegates",
        "insertBlankLinePaddingBeforeEnumerations", "insertBlankLinePaddingAfterEnumerations",
        "insertBlankLinePaddingBeforeEvents", "insertBlankLinePaddingAfterEvents",
        "insertBlankLinePaddingBeforeFieldsMultiLine", "insertBlankLinePaddingAfterFieldsMultiLine",
        "insertBlankLinePaddingBeforeInterfaces", "insertBlankLinePaddingAfterInterfaces",
        "insertBlankLinePaddingBeforeMethods", "insertBlankLinePaddingAfterMethods",
        "insertBlankLinePaddingBeforeNamespaces", "insertBlankLinePaddingAfterNamespaces",
        "insertBlankLinePaddingBeforePropertiesMultiLine", "insertBlankLinePaddingAfterPropertiesMultiLine",
        "insertBlankLinePaddingBeforeStructs", "insertBlankLinePaddingAfterStructs",
        "insertBlankLinePaddingBeforeRegionTags", "insertBlankLinePaddingAfterRegionTags",
        "insertBlankLinePaddingBeforeEndRegionTags", "insertBlankLinePaddingAfterEndRegionTags",
        "insertBlankLinePaddingBeforeUsingStatementBlocks", "insertBlankLinePaddingAfterUsingStatementBlocks",
        "insertBlankLinePaddingBeforeCaseStatements",
    };

    /// <summary>
    /// The target settings of the insertExplicitAccessModifiers group alias, mirroring the VS Code extension.
    /// </summary>
    private static readonly string[] ExplicitAccessModifierAliasTargets =
    {
        "insertExplicitAccessModifiersOnClasses", "insertExplicitAccessModifiersOnDelegates",
        "insertExplicitAccessModifiersOnEnumerations", "insertExplicitAccessModifiersOnEvents",
        "insertExplicitAccessModifiersOnFields", "insertExplicitAccessModifiersOnInterfaces",
        "insertExplicitAccessModifiersOnMethods", "insertExplicitAccessModifiersOnProperties",
        "insertExplicitAccessModifiersOnStructs",
    };

    /// <summary>
    /// The canonical boolean policy keys in the order used by the VS Code extension schema.
    /// </summary>
    private static readonly string[] CanonicalBooleanKeys =
    {
        "insertBlankLinePaddingBeforeClasses", "insertBlankLinePaddingAfterClasses",
        "insertBlankLinePaddingBeforeDelegates", "insertBlankLinePaddingAfterDelegates",
        "insertBlankLinePaddingBeforeEnumerations", "insertBlankLinePaddingAfterEnumerations",
        "insertBlankLinePaddingBeforeEvents", "insertBlankLinePaddingAfterEvents",
        "insertBlankLinePaddingBeforeFieldsMultiLine", "insertBlankLinePaddingAfterFieldsMultiLine",
        "insertBlankLinePaddingBeforeFieldsSingleLine", "insertBlankLinePaddingAfterFieldsSingleLine",
        "insertBlankLinePaddingBeforeInterfaces", "insertBlankLinePaddingAfterInterfaces",
        "insertBlankLinePaddingBeforeMethods", "insertBlankLinePaddingAfterMethods",
        "insertBlankLinePaddingBeforeNamespaces", "insertBlankLinePaddingAfterNamespaces",
        "insertBlankLinePaddingBeforePropertiesMultiLine", "insertBlankLinePaddingAfterPropertiesMultiLine",
        "insertBlankLinePaddingBeforePropertiesSingleLine", "insertBlankLinePaddingAfterPropertiesSingleLine",
        "insertBlankLinePaddingBeforeStructs", "insertBlankLinePaddingAfterStructs",
        "insertBlankLinePaddingBeforeRegionTags", "insertBlankLinePaddingAfterRegionTags",
        "insertBlankLinePaddingBeforeEndRegionTags", "insertBlankLinePaddingAfterEndRegionTags",
        "insertBlankLinePaddingBeforeUsingStatementBlocks", "insertBlankLinePaddingAfterUsingStatementBlocks",
        "insertBlankLinePaddingBeforeCaseStatements", "insertBlankLinePaddingBeforeSingleLineComments",

        "insertExplicitAccessModifiersOnClasses", "insertExplicitAccessModifiersOnDelegates",
        "insertExplicitAccessModifiersOnEnumerations", "insertExplicitAccessModifiersOnEvents",
        "insertExplicitAccessModifiersOnFields", "insertExplicitAccessModifiersOnInterfaces",
        "insertExplicitAccessModifiersOnMethods", "insertExplicitAccessModifiersOnProperties",
        "insertExplicitAccessModifiersOnStructs",

        "convertToFileScopedNamespace", "convertToVarWhenApparent",
        "makeFieldsReadonlyWhenSafe", "sealClassesWhenSafe",
        "insertBlankLineBeforeReturnAndThrowStatements",
        "convertToCollectionExpressions", "reuseJsonSerializerOptionsForCA1869",
        "simplifySingleStatementLambdas", "convertToPatternMatchingNullChecks",
        "convertStringFormatToInterpolation", "convertToStringNameOf",
        "inlineOutVariableDeclarations",

        "applyEditorConfigFormatting", "applyEditorConfigNaming",
        "applyEditorConfigCodeStyle", "applyAnalyzerCodeFixes",

        "moveUsingsOutsideNamespace", "organizeUsings",

        "updateEndRegionDirectives", "updateSingleLineMethods",
        "updateAccessorsToBothBeSingleLineOrMultiLine",

        "formatComments",

        "removeRegions", "removeByteOrderMark", "removeEndOfLineWhitespace",
        "removeBlankLinesAtTop", "removeBlankLinesAtBottom",
        "removeBlankLinesAfterAttributes", "removeBlankLinesAfterOpeningBrace",
        "removeBlankLinesBeforeClosingBrace", "removeBlankLinesBetweenChainedStatements",
        "removeMultipleConsecutiveBlankLines",
    };

    /// <summary>
    /// Loads the repository cleanup overrides that apply to the specified file, searching the file's
    /// directory and its ancestors for a policy file. The nearest policy file wins.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    /// <returns>The loaded overrides, or an empty instance when no policy file is present.</returns>

    internal static RepositoryCleanupOverrides LoadForFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return RepositoryCleanupOverrides.Empty;
        }

        string directory;
        try
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        }
        catch (Exception)
        {
            return RepositoryCleanupOverrides.Empty;
        }

        return LoadFromDirectory(directory);
    }

    /// <summary>
    /// Loads the repository cleanup overrides that apply to the specified directory, searching the
    /// directory and its ancestors for a policy file. The nearest policy file wins.
    /// </summary>
    /// <param name="startDirectory">The directory to start searching from.</param>
    /// <returns>The loaded overrides, or an empty instance when no policy file is present.</returns>

    internal static RepositoryCleanupOverrides LoadFromDirectory(string startDirectory)
    {
        return TryFindConfigFile(startDirectory, out var configPath)
            ? ParseFile(configPath)
            : RepositoryCleanupOverrides.Empty;
    }

    /// <summary>
    /// Finds the nearest repository policy file for the specified directory, walking up to the root.
    /// </summary>
    /// <param name="startDirectory">The directory to start searching from.</param>
    /// <param name="configPath">The located policy file path, if any.</param>
    /// <returns>True when a policy file was found, otherwise false.</returns>

    internal static bool TryFindConfigFile(string startDirectory, out string configPath)
    {
        configPath = null;
        var directory = startDirectory;

        while (!string.IsNullOrEmpty(directory))
        {
            foreach (var name in ConfigFileNames)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    configPath = candidate;
                    return true;
                }
            }

            directory = Path.GetDirectoryName(directory);
        }

        return false;
    }

    /// <summary>
    /// Parses repository cleanup overrides from policy file content. Unknown keys, wrong value types
    /// and invalid JSON are ignored.
    /// </summary>
    /// <param name="json">The policy file content.</param>
    /// <returns>The parsed overrides.</returns>

    internal static RepositoryCleanupOverrides Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return RepositoryCleanupOverrides.Empty;
        }

        Dictionary<string, object> root;
        try
        {
            root = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }
                .Deserialize<Dictionary<string, object>>(json);
        }
        catch (Exception)
        {
            return RepositoryCleanupOverrides.Empty;
        }

        if (root is null ||
            !root.TryGetValue("cleanup", out var cleanupObject) ||
            !(cleanupObject is Dictionary<string, object> cleanup))
        {
            return RepositoryCleanupOverrides.Empty;
        }

        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        bool? removeRegions = null;
        bool? organizeUsings = null;

        // Group aliases are applied first so that individual keys override them, mirroring VS Code.
        ApplyBooleanAlias(cleanup, values, "insertBlankLinePadding", BlankLinePaddingAliasTargets);
        ApplyBooleanAlias(cleanup, values, "insertExplicitAccessModifiers", ExplicitAccessModifierAliasTargets);

        foreach (var pair in cleanup)
        {
            switch (pair.Key)
            {
                case "insertBlankLinePadding":
                case "insertExplicitAccessModifiers":
                    continue;

                case "removeRegions":
                    if (pair.Value is bool removeRegionsValue)
                    {
                        removeRegions = removeRegionsValue;
                    }

                    continue;

                case "organizeUsings":
                    if (pair.Value is bool organizeUsingsValue)
                    {
                        organizeUsings = organizeUsingsValue;
                    }

                    continue;

                case "fileHeaderPosition":
                    AddEnumOverride(values, "Cleaning_UpdateFileHeader_HeaderPosition", pair.Value,
                        "afterUsings", (int)HeaderPosition.AfterUsings,
                        "documentStart", (int)HeaderPosition.DocumentStart);
                    continue;

                case "fileHeaderUpdateMode":
                    AddEnumOverride(values, "Cleaning_UpdateFileHeader_HeaderUpdateMode", pair.Value,
                        "replace", (int)HeaderUpdateMode.Replace,
                        "insert", (int)HeaderUpdateMode.Insert);
                    continue;
            }

            var settingName = ResolveSettingName(pair.Key);
            if (settingName is null)
            {
                continue;
            }

            var property = Settings.Default.Properties[settingName];
            if (property is null)
            {
                continue;
            }

            if (property.PropertyType == typeof(bool) && pair.Value is bool)
            {
                values[settingName] = pair.Value;
            }
            else if (property.PropertyType == typeof(string) && pair.Value is string)
            {
                values[settingName] = pair.Value;
            }
            else if (property.PropertyType == typeof(int) && pair.Value is int)
            {
                values[settingName] = pair.Value;
            }
        }

        return new RepositoryCleanupOverrides(values, removeRegions, organizeUsings);
    }

    /// <summary>
    /// Builds the repository policy file content from the current Visual Studio settings, using the
    /// shared VS Code configuration schema.
    /// </summary>
    /// <param name="settings">The settings instance to export.</param>
    /// <returns>The policy file JSON content.</returns>

    internal static string BuildJson(Settings settings)
    {
        var builder = new StringBuilder();
        builder.AppendLine("{");
        builder.AppendLine("  \"cleanup\": {");

        var entries = new List<string>();
        foreach (var key in CanonicalBooleanKeys)
        {
            switch (key)
            {
                case "organizeUsings":
                    // No Visual Studio user setting backs this policy; it is repository-only.
                    continue;

                case "removeRegions":
                    // Visual Studio always removes region directives during headless cleanup.
                    entries.Add("    \"removeRegions\": true");
                    continue;
            }

            var settingName = ResolveSettingName(key);
            if (settingName is null || settings.Properties[settingName] is null)
            {
                continue;
            }

            var value = settings[settingName];
            if (value is bool boolean)
            {
                entries.Add($"    \"{key}\": {(boolean ? "true" : "false")}");
            }
        }

        entries.Add($"    \"fileHeaderCSharp\": \"{EscapeJsonString(settings.Cleaning_UpdateFileHeaderCSharp ?? string.Empty)}\"");
        entries.Add($"    \"fileHeaderPosition\": \"{(settings.Cleaning_UpdateFileHeader_HeaderPosition == (int)HeaderPosition.AfterUsings ? "afterUsings" : "documentStart")}\"");
        entries.Add($"    \"fileHeaderUpdateMode\": \"{(settings.Cleaning_UpdateFileHeader_HeaderUpdateMode == (int)HeaderUpdateMode.Replace ? "replace" : "insert")}\"");

        builder.AppendLine(string.Join("," + Environment.NewLine, entries));
        builder.AppendLine("  }");
        builder.AppendLine("}");

        return builder.ToString();
    }

    /// <summary>
    /// Applies repository cleanup overrides to the specified settings instance. Policy-only entries
    /// without a Visual Studio user setting are skipped. The caller is responsible for saving.
    /// </summary>
    /// <param name="overrides">The overrides to apply.</param>
    /// <param name="settings">The settings instance to update.</param>
    /// <returns>The number of applied settings.</returns>

    internal static int ApplyToSettings(RepositoryCleanupOverrides overrides, Settings settings)
    {
        var applied = 0;

        foreach (var pair in overrides.Values)
        {
            if (settings.Properties[pair.Key] is null)
            {
                continue;
            }

            settings[pair.Key] = pair.Value;
            applied++;
        }

        return applied;
    }

    /// <summary>
    /// Parses a policy file, returning empty overrides when the file cannot be read.
    /// </summary>
    /// <param name="configPath">The policy file path.</param>
    /// <returns>The parsed overrides.</returns>

    private static RepositoryCleanupOverrides ParseFile(string configPath)
    {
        try
        {
            return Parse(File.ReadAllText(configPath));
        }
        catch (Exception)
        {
            return RepositoryCleanupOverrides.Empty;
        }
    }

    /// <summary>
    /// Applies a group alias value to all of its target settings.
    /// </summary>
    /// <param name="cleanup">The parsed cleanup section.</param>
    /// <param name="values">The values being accumulated.</param>
    /// <param name="aliasKey">The alias key.</param>
    /// <param name="targetKeys">The alias target keys.</param>

    private static void ApplyBooleanAlias(
        IReadOnlyDictionary<string, object> cleanup,
        IDictionary<string, object> values,
        string aliasKey,
        IEnumerable<string> targetKeys)
    {
        if (!cleanup.TryGetValue(aliasKey, out var aliasValue) || !(aliasValue is bool boolean))
        {
            return;
        }

        foreach (var targetKey in targetKeys)
        {
            var settingName = ResolveSettingName(targetKey);
            if (settingName is not null && Settings.Default.Properties[settingName] is not null)
            {
                values[settingName] = boolean;
            }
        }
    }

    /// <summary>
    /// Adds an enumeration override encoded as a string, ignoring unrecognized values.
    /// </summary>
    /// <param name="values">The values being accumulated.</param>
    /// <param name="settingName">The Visual Studio setting property name.</param>
    /// <param name="value">The raw JSON value.</param>
    /// <param name="firstName">The first recognized string.</param>
    /// <param name="firstValue">The first mapped value.</param>
    /// <param name="secondName">The second recognized string.</param>
    /// <param name="secondValue">The second mapped value.</param>

    private static void AddEnumOverride(
        IDictionary<string, object> values,
        string settingName,
        object value,
        string firstName,
        int firstValue,
        string secondName,
        int secondValue)
    {
        if (!(value is string text) || Settings.Default.Properties[settingName] is null)
        {
            return;
        }

        if (string.Equals(text, firstName, StringComparison.Ordinal))
        {
            values[settingName] = firstValue;
        }
        else if (string.Equals(text, secondName, StringComparison.Ordinal))
        {
            values[settingName] = secondValue;
        }
    }

    /// <summary>
    /// Resolves a camelCase policy key to its Visual Studio setting property name.
    /// </summary>
    /// <param name="jsonKey">The policy file key.</param>
    /// <returns>The setting property name, or null when the key has no Visual Studio counterpart.</returns>

    private static string ResolveSettingName(string jsonKey)
    {
        if (string.IsNullOrEmpty(jsonKey))
        {
            return null;
        }

        if (SpecialSettingNames.TryGetValue(jsonKey, out var specialName))
        {
            return specialName;
        }

        return "Cleaning_" + char.ToUpperInvariant(jsonKey[0]) + jsonKey.Substring(1);
    }

    /// <summary>
    /// Escapes a string for inclusion in JSON output.
    /// </summary>
    /// <param name="value">The value to escape.</param>
    /// <returns>The escaped value.</returns>

    private static string EscapeJsonString(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            switch (character)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\r': builder.Append("\\r"); break;
                case '\n': builder.Append("\\n"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    builder.Append(character < ' ' ? $"\\u{(int)character:x4}" : character.ToString());
                    break;
            }
        }

        return builder.ToString();
    }
}
