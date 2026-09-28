using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Cleaning;

internal static class CleanupBatchPartitioner
{
    internal static (List<T> Parallel, List<T> Sequential) Partition<T>(IEnumerable<T> items, Func<T, string> getFilePath, Func<T, bool> isOpen)
    {
        var itemList = items.ToList();
        string GetFullPath(T item)
        {
            var filePath = getFilePath(item);

            return filePath is null ? null : Path.GetFullPath(filePath);
        }

        var openPaths = new HashSet<string>(
            itemList.Where(isOpen).Select(GetFullPath).Where(path => path != null),
            StringComparer.OrdinalIgnoreCase);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parallel = new List<T>();
        var sequential = new List<T>();

        foreach (var item in itemList)
        {
            var filePath = GetFullPath(item);
            if (filePath is null)
            {
                sequential.Add(item);
            }
            else if (isOpen(item))
            {
                if (seenPaths.Add(filePath))
                {
                    sequential.Add(item);
                }
            }
            else if (!openPaths.Contains(filePath) && seenPaths.Add(filePath))
            {
                parallel.Add(item);
            }
        }

        return (parallel, sequential);
    }

    /// <summary>
    /// Processes the items of different groups at the same time (at most <paramref name="maxParallelGroups" /> groups)
    /// and the items of one group one at a time, in order. The cleanup groups files by project: the semantic analysis
    /// and the compile checks of a file work on the compilation of its whole project, so files of the same project
    /// processed together would compute and verify their changes against each other's old text.
    /// </summary>
    /// <param name="items">The items.</param>
    /// <param name="getGroupKey">Gets the group of an item; null is a group too.</param>
    /// <param name="maxParallelGroups">The maximum number of groups processed at the same time.</param>
    /// <param name="cancellationToken">Stops taking new items; the item being processed completes.</param>
    /// <param name="processAsync">Processes one item; must not throw.</param>
    /// <returns>A task.</returns>
    internal static async Task RunPerGroupAsync<T>(IEnumerable<T> items, Func<T, string> getGroupKey, int maxParallelGroups, CancellationToken cancellationToken, Func<T, Task> processAsync)
    {
        using (var throttle = new SemaphoreSlim(Math.Max(1, maxParallelGroups)))
        {
            var groups = items.GroupBy(item => getGroupKey(item) ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            await Task.WhenAll(groups.Select(async group =>
            {
                await throttle.WaitAsync().ConfigureAwait(false);
                try
                {
                    foreach (var item in group)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }

                        await processAsync(item).ConfigureAwait(false);
                    }
                }
                finally
                {
                    throttle.Release();
                }
            })).ConfigureAwait(false);
        }
    }
}
