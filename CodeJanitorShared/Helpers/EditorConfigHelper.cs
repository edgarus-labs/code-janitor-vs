using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeJanitor.Helpers;

internal sealed class EditorConfigCSharpOptions
{
    internal bool? SortSystemDirectivesFirst { get; set; }

    internal bool? SeparateImportDirectiveGroups { get; set; }

    internal bool? TrimTrailingWhitespace { get; set; }

    internal bool? InsertFinalNewline { get; set; }

    internal string IndentStyle { get; set; }

    internal int? IndentSize { get; set; }

    internal int? TabWidth { get; set; }
}

internal static class EditorConfigHelper
{
    internal static EditorConfigCSharpOptions LoadCSharpOptions(string filePath)
    {
        var options = new EditorConfigCSharpOptions();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return options;
        }

        var configFiles = EnumerateEditorConfigFiles(filePath).ToList();
        foreach (var configFile in configFiles)
        {
            ApplyFile(configFile, filePath, options);
        }

        return options;
    }

    internal static void ApplyText(string editorConfigText, string filePath, EditorConfigCSharpOptions options)
    {
        if (string.IsNullOrWhiteSpace(editorConfigText) || options == null || string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var active = true;
        using (var reader = new StringReader(editorConfigText))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(";", StringComparison.Ordinal))
                {
                    continue;
                }

                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    var section = trimmed.Substring(1, trimmed.Length - 2).Trim();
                    active = MatchesCSharpSection(section, filePath);
                    continue;
                }

                if (!active)
                {
                    continue;
                }

                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = trimmed.Substring(0, separatorIndex).Trim();
                var value = trimmed.Substring(separatorIndex + 1).Trim();
                ApplySetting(options, key, value);
            }
        }
    }

    private static IEnumerable<string> EnumerateEditorConfigFiles(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        var found = new Stack<string>();

        while (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            var configPath = Path.Combine(directory, ".editorconfig");
            if (File.Exists(configPath))
            {
                found.Push(configPath);
                var text = File.ReadAllText(configPath);
                if (text.IndexOf("root = true", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    break;
                }
            }

            directory = Path.GetDirectoryName(directory);
        }

        return found;
    }

    private static void ApplyFile(string configPath, string filePath, EditorConfigCSharpOptions options)
    {
        ApplyText(File.ReadAllText(configPath), filePath, options);
    }

    private static bool MatchesCSharpSection(string section, string filePath)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return false;
        }

        var normalized = section.Replace(" ", string.Empty);
        if (normalized == "*")
        {
            return true;
        }

        if (normalized.IndexOf("cs", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        return string.Equals(Path.GetExtension(filePath), ".cs", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplySetting(EditorConfigCSharpOptions options, string key, string value)
    {
        switch (key)
        {
            case "trim_trailing_whitespace":
                options.TrimTrailingWhitespace = TryParseBool(value);
                break;

            case "dotnet_sort_system_directives_first":
                options.SortSystemDirectivesFirst = TryParseBool(value);
                break;

            case "dotnet_separate_import_directive_groups":
                options.SeparateImportDirectiveGroups = TryParseBool(value);
                break;

            case "insert_final_newline":
                options.InsertFinalNewline = TryParseBool(value);
                break;

            case "indent_style":
                options.IndentStyle = value;
                break;

            case "indent_size":
                options.IndentSize = TryParseInt(value);
                break;

            case "tab_width":
                options.TabWidth = TryParseInt(value);
                break;
        }
    }

    private static bool? TryParseBool(string value)
    {
        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int? TryParseInt(string value)
    {
        if (int.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}