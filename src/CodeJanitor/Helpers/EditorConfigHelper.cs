using Microsoft.CodeAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;

namespace CodeJanitor.Helpers;

/// <summary>
/// Reads the .editorconfig options that apply to a source file through Roslyn's editorconfig engine, so section
/// globs, nearest-file-wins ordering and <c>root = true</c> behave exactly as in the compiler and the IDE.
/// </summary>
internal static class EditorConfigHelper
{
    /// <summary>
    /// The editorconfig file name searched in the source file's directory and its ancestors.
    /// </summary>
    internal const string EditorConfigFileName = ".editorconfig";

    /// <summary>
    /// Parsed editorconfig files keyed by their exact full path, validated against the file's last write time and
    /// length before reuse. The comparison is ordinal on purpose: Roslyn matches the source path against the config
    /// directory ordinally, so a config parsed under a differently-cased path must not be reused.
    /// </summary>
    private static readonly ConcurrentDictionary<string, (DateTime LastWriteTimeUtc, long Length, AnalyzerConfig Config)> ParsedConfigs =
        new ConcurrentDictionary<string, (DateTime, long, AnalyzerConfig)>(StringComparer.Ordinal);

    /// <summary>
    /// Loads the editorconfig options that apply to the specified source file, collecting every .editorconfig from
    /// the file's directory up to the file system root and letting Roslyn resolve sections, precedence and
    /// <c>root = true</c>. Keys are the lower-cased option names and values are the raw option values, including
    /// any <c>:severity</c> suffix. Never throws: blank or invalid paths and unreadable files yield no options.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    /// <returns>The applicable options, or an empty dictionary when no .editorconfig applies.</returns>

    internal static IReadOnlyDictionary<string, string> LoadOptions(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        try
        {
            // Config paths are derived from the same full path string as the source path, so Roslyn's ordinal
            // directory prefix match is not defeated by casing or relative segments.
            var fullPath = Path.GetFullPath(filePath);
            var configs = new List<AnalyzerConfig>();

            for (var directory = Path.GetDirectoryName(fullPath); !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
            {
                var config = TryLoadConfig(Path.Combine(directory, EditorConfigFileName));
                if (config is not null)
                {
                    configs.Add(config);
                }
            }

            return configs.Count == 0
                ? ImmutableDictionary<string, string>.Empty
                : AnalyzerConfigSet.Create(configs).GetOptionsForSourcePath(fullPath).AnalyzerOptions;
        }
        catch (Exception)
        {
            return ImmutableDictionary<string, string>.Empty;
        }
    }

    /// <summary>
    /// Returns the parsed editorconfig at the specified path, reusing the cached parse while the file's last write
    /// time and length are unchanged.
    /// </summary>
    /// <param name="configPath">The full .editorconfig path.</param>
    /// <returns>The parsed config, or null when the file does not exist or cannot be read.</returns>

    private static AnalyzerConfig TryLoadConfig(string configPath)
    {
        try
        {
            var file = new FileInfo(configPath);
            if (!file.Exists)
            {
                return null;
            }

            if (ParsedConfigs.TryGetValue(configPath, out var cached) &&
                cached.LastWriteTimeUtc == file.LastWriteTimeUtc &&
                cached.Length == file.Length)
            {
                return cached.Config;
            }

            var config = AnalyzerConfig.Parse(File.ReadAllText(configPath), configPath);
            ParsedConfigs[configPath] = (file.LastWriteTimeUtc, file.Length, config);
            return config;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
