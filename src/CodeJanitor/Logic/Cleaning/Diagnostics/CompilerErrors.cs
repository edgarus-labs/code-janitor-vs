using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Cleaning.Diagnostics;

/// <summary>
/// Reads and compares the Error-severity compiler diagnostics of a project. A change introduces an error when the
/// errors after it are not a sub-multiset of the errors before it, comparing each error by id, file path and message
/// (never by position, so code that only moves keeps its errors). Removing one error while adding a different one is
/// therefore still an introduced error.
/// </summary>
internal static class CompilerErrors
{
    /// <summary>
    /// Gets the Error-severity compiler diagnostics of the project.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The errors, empty when the project has no compilation.</returns>
    internal static async Task<IReadOnlyList<Diagnostic>> GetAsync(Project project, CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

        return compilation is null
            ? Array.Empty<Diagnostic>()
            : compilation.GetDiagnostics(cancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
    }

    /// <summary>
    /// Finds the first error of <paramref name="after" /> that <paramref name="before" /> does not have, counting
    /// duplicates.
    /// </summary>
    /// <param name="before">The errors before the change.</param>
    /// <param name="after">The errors after the change.</param>
    /// <returns>The first new error, otherwise null.</returns>
    internal static Diagnostic FindFirstNew(IReadOnlyList<Diagnostic> before, IReadOnlyList<Diagnostic> after)
    {
        var remaining = before
            .GroupBy(GetKey)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (var error in after)
        {
            var key = GetKey(error);
            if (!remaining.TryGetValue(key, out var count) || count == 0)
            {
                return error;
            }

            remaining[key] = count - 1;
        }

        return null;
    }

    private static (string Id, string FilePath, string Message) GetKey(Diagnostic diagnostic) =>
        (diagnostic.Id, diagnostic.Location.SourceTree?.FilePath, diagnostic.GetMessage(CultureInfo.InvariantCulture));
}
