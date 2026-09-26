using Microsoft.CodeAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

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
            var fullPath = Path.GetFullPath(filePath);
            var chain = LoadChain(fullPath);

            return MergeOptions(chain.Select(entry => entry.Config).ToList(), fullPath);
        }
        catch (Exception)
        {
            return ImmutableDictionary<string, string>.Empty;
        }
    }

    /// <summary>
    /// Finds the .editorconfig that supplies the specified option for the specified source file: the nearest file,
    /// among those Roslyn applies (none above a <c>root = true</c> file), whose matching sections define the option.
    /// Never throws: blank or invalid paths and unreadable files yield null.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    /// <param name="key">The lower-cased option name.</param>
    /// <returns>The full path of the defining .editorconfig, or null when no applicable .editorconfig defines the option.</returns>

    internal static string FindDefiningConfigPath(string filePath, string key)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(filePath);
            var chain = LoadChain(fullPath);

            // Roslyn ignores the files above a root file, so an option absent from the merged options is not applied,
            // even when such a file defines it; otherwise the nearest defining file is the one whose value wins.
            if (!MergeOptions(chain.Select(entry => entry.Config).ToList(), fullPath).ContainsKey(key))
            {
                return null;
            }

            return chain.First(entry => MergeOptions(new[] { entry.Config }, fullPath).ContainsKey(key)).Path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Merges the options that the specified configs apply to a source file; Roslyn orders the configs by directory.
    /// </summary>
    /// <param name="configs">The parsed configs.</param>
    /// <param name="fullPath">The full source file path.</param>
    /// <returns>The applicable options, or an empty dictionary when there are no configs.</returns>

    private static IReadOnlyDictionary<string, string> MergeOptions(IReadOnlyCollection<AnalyzerConfig> configs, string fullPath)
    {
        return configs.Count == 0
            ? ImmutableDictionary<string, string>.Empty
            : AnalyzerConfigSet.Create(configs).GetOptionsForSourcePath(fullPath).AnalyzerOptions;
    }

    /// <summary>
    /// Loads every readable .editorconfig from the file's directory up to the file system root, nearest first.
    /// Config paths are derived from the same full path string as the source path, so Roslyn's ordinal directory
    /// prefix match is not defeated by casing or relative segments.
    /// </summary>
    /// <param name="fullPath">The full source file path.</param>
    /// <returns>The config paths and parsed configs, nearest first.</returns>

    private static List<(string Path, AnalyzerConfig Config)> LoadChain(string fullPath)
    {
        var chain = new List<(string Path, AnalyzerConfig Config)>();

        for (var directory = Path.GetDirectoryName(fullPath); !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
        {
            var configPath = Path.Combine(directory, EditorConfigFileName);
            var config = TryLoadConfig(configPath);
            if (config is not null)
            {
                chain.Add((configPath, config));
            }
        }

        return chain;
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
