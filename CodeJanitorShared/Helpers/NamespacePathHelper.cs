using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeJanitor.Helpers;

/// <summary>
/// Helpers for deriving the expected namespace for a project item.
/// </summary>

internal static class NamespacePathHelper
{
    private static readonly HashSet<string> ExcludedDirectoryNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "generated" };

    /// <summary>
    /// Computes the expected namespace for a project item by deriving a root namespace from the project&apos;s settings or name, falling back to null when the project or namespace is unavailable, and enforces a UI-thread requirement via ThreadHelper.ThrowIfNotOnUIThread.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>A string value produced by this method.</returns>

    internal static string GetExpectedNamespace(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var project = projectItem?.ContainingProject;
        if (project == null)
        {
            return null;
        }

        var rootNamespace = GetProjectRootNamespace(project);
        if (string.IsNullOrWhiteSpace(rootNamespace))
        {
            rootNamespace = project.Name;
        }

        if (string.IsNullOrWhiteSpace(rootNamespace))
        {
            return null;
        }

        return BuildExpectedNamespace(rootNamespace, GetProjectDirectory(project), projectItem.GetFileName());
    }

    /// <summary>
    /// Determines whether the specified file path is inside an excluded directory.
    /// </summary>

    internal static bool IsInExcludedDirectory(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return true;
        }

        var directoryPath = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return false;
        }

        var segments = directoryPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(segment => ExcludedDirectoryNames.Contains(segment));
    }

    /// <summary>
    /// Builds a dotted namespace by combining the split root namespace with normalized non-empty segments from the file&apos;s directory relative to the project directory, returning the joined string with no side effects.
    /// </summary>
    /// <param name="rootNamespace">The root namespace.</param>
    /// <param name="projectDirectory">The project directory.</param>
    /// <param name="filePath">The file path.</param>
    /// <returns>A string value produced by this method.</returns>

    internal static string BuildExpectedNamespace(string rootNamespace, string projectDirectory, string filePath)
    {
        var namespaceSegments = new List<string>();
        namespaceSegments.AddRange(SplitNamespace(rootNamespace));

        var relativeDirectory = GetRelativeDirectory(projectDirectory, filePath);
        if (!string.IsNullOrWhiteSpace(relativeDirectory))
        {
            namespaceSegments.AddRange(
                relativeDirectory.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(NormalizeNamespaceSegment)
                    .Where(segment => !string.IsNullOrWhiteSpace(segment)));
        }

        return string.Join(".", namespaceSegments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
    }

    /// <summary>
    /// Splits a namespace string on dots, normalizes each non-empty segment, and lazily yields the normalized segments, returning nothing for null, empty, or whitespace input, with no side effects or exceptions.
    /// </summary>
    /// <param name="namespaceName">The namespace name.</param>
    /// <returns>A IEnumerable&lt;string&gt; value produced by this method.</returns>

    private static IEnumerable<string> SplitNamespace(string namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            yield break;
        }

        foreach (var segment in namespaceName.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = NormalizeNamespaceSegment(segment);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                yield return normalized;
            }
        }
    }

    /// <summary>
    /// Throws if not on the UI thread, then returns the directory name of the project&apos;s full path if present and non-whitespace, otherwise returns null while silently ignoring any exceptions from accessing the path.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string GetProjectDirectory(Project project)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            if (!string.IsNullOrWhiteSpace(project.FullName))
            {
                return Path.GetDirectoryName(project.FullName);
            }
        }
        catch
        {
            // Ignore project systems that do not expose a full path.
        }

        return null;
    }

    /// <summary>
    /// Attempts to return the project&apos;s RootNamespace or DefaultNamespace property value (whichever is first non-whitespace), returning null for null project/properties or missing/invalid properties, and throws via ThreadHelper if not called on the UI thread.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string GetProjectRootNamespace(Project project)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (project?.Properties == null)
        {
            return null;
        }

        foreach (var propertyName in new[] { "RootNamespace", "DefaultNamespace" })
        {
            try
            {
                var property = project.Properties.Item(propertyName);
                var value = property?.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            catch
            {
                // Ignore missing properties or project system quirks.
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the relative directory of a file path under a project directory, or null if inputs are blank, the file is outside the project directory, or path resolution fails, with no side effects.
    /// </summary>
    /// <param name="projectDirectory">The project directory.</param>
    /// <param name="filePath">The file path.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string GetRelativeDirectory(string projectDirectory, string filePath)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory) || string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        try
        {
            var normalizedProjectDirectory = EnsureTrailingDirectorySeparator(Path.GetFullPath(projectDirectory));
            var normalizedFilePath = Path.GetFullPath(filePath);

            if (!normalizedFilePath.StartsWith(normalizedProjectDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var relativePath = normalizedFilePath.Substring(normalizedProjectDirectory.Length);

            return Path.GetDirectoryName(relativePath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns null for null or whitespace input, otherwise trims the segment, replaces every non-alphanumeric or non-underscore character with an underscore, and prefixes an underscore if the result starts with a digit, with no side effects.
    /// </summary>
    /// <param name="segment">The segment.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string NormalizeNamespaceSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return null;
        }

        var normalized = Regex.Replace(segment.Trim(), "[^A-Za-z0-9_]", "_");
        if (normalized.Length > 0 && char.IsDigit(normalized[0]))
        {
            normalized = "_" + normalized;
        }

        return normalized;
    }

    /// <summary>
    /// Returns the input path unchanged if it is null, whitespace, or already ends with either directory separator; otherwise appends the platform-specific directory separator character, with no exceptions thrown.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
            path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}
