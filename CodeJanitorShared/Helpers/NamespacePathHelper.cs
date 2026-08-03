using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeJanitor.Helpers
{
    /// <summary>
    /// Helpers for deriving the expected namespace for a project item.
    /// </summary>
    internal static class NamespacePathHelper
    {
        private static readonly HashSet<string> ExcludedDirectoryNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "generated" };

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
}