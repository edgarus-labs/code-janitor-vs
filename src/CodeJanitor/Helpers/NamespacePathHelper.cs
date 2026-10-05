using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeJanitor.Helpers;

/// <summary>
/// Helpers for the folders of source files.
/// </summary>
internal static class NamespacePathHelper
{
    private static readonly HashSet<string> ExcludedDirectoryNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "generated" };

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
}
