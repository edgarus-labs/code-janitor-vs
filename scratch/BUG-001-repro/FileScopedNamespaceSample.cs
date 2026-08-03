using System;
using System.Collections.Generic;

namespace CodeJanitor.Scratch.Bug001;

/// <summary>
/// Minimal repro for BUG-001: running CodeJanitor Cleanup on a file with a
/// file-scoped namespace (C# 10+) should NOT remove or corrupt the
/// "namespace CodeJanitor.Scratch.Bug001;" declaration line above.
/// </summary>
public class FileScopedNamespaceSample
{
    private readonly List<string> _items = new List<string>();

    public void DoWork()
    {
        Console.WriteLine("hello");
    }
}

public class SecondClassInSameNamespace
{
    public int Value { get; set; }
}
