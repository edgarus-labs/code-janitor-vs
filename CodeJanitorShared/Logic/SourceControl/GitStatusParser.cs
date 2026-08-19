using System;
using System.Collections.Generic;
using System.IO;

namespace CodeJanitor.Logic.SourceControl;

/// <summary>
/// Parses <c>git status --porcelain</c> (v1) output into absolute file paths of changed,
/// on-disk files. Deleted and ignored entries are excluded; for renames the new path is used.
/// </summary>
/// <remarks>
/// Pure logic with no dependency on a git process or Visual Studio, unit-testable in isolation
/// (see ADR-0005 / ADR-0007).
/// </remarks>

public class GitStatusParser : IGitStatusParser
{
    private static readonly string[] RenameSeparator = { " -> " };

    /// <inheritdoc />

    public IReadOnlyList<string> Parse(string porcelainOutput, string repositoryRoot)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(porcelainOutput))
        {
            return results;
        }

        var lines = porcelainOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            if (line.Length < 4)
            {
                continue;
            }

            var x = line[0];
            var y = line[1];
            var rest = line.Substring(3);

            // Ignored files.
            if (x == '!' && y == '!')
            {
                continue;
            }

            string relativePath;

            if (x == '?' && y == '?')
            {
                // Untracked.
                relativePath = rest;
            }
            else if (x == 'R' || y == 'R' || x == 'C' || y == 'C')
            {
                // Rename/copy: "orig -> new" - use the new path.
                var parts = rest.Split(RenameSeparator, StringSplitOptions.None);
                relativePath = parts.Length == 2 ? parts[1] : rest;
            }
            else if (x == 'D' || y == 'D')
            {
                // Deleted - file no longer exists on disk.
                continue;
            }
            else
            {
                relativePath = rest;
            }

            relativePath = Unquote(relativePath);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            results.Add(Path.Combine(repositoryRoot, normalized));
        }

        return results;
    }

    /// <summary>
    /// Removes surrounding quotes that git adds for paths with special characters and
    /// unescapes the common escape sequences.
    /// </summary>

    private static string Unquote(string path)
    {
        if (path.Length >= 2 && path[0] == '"' && path[path.Length - 1] == '"')
        {
            path = path.Substring(1, path.Length - 2);
            path = path.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        return path;
    }
}