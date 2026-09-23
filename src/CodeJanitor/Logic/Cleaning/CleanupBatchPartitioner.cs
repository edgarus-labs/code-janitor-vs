using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeJanitor.Logic.Cleaning;

internal static class CleanupBatchPartitioner
{
    internal static (List<T> Parallel, List<T> Sequential) Partition<T>(IEnumerable<T> items, Func<T, string> getFilePath, Func<T, bool> isOpen)
    {
        var itemList = items.ToList();
        string GetFullPath(T item)
        {
            var filePath = getFilePath(item);

            return filePath == null ? null : Path.GetFullPath(filePath);
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
            if (filePath == null)
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
}
