using System.Collections.Generic;
using CodeJanitor.Logic.Cleaning;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public sealed class CleanupBatchPartitionerTests
{
    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Partition_ClosedFiles_GoToTheParallelPass()
    {
        var items = new[] { Item(@"C:\repo\A.cs"), Item(@"C:\repo\View.xaml") };

        var (parallel, sequential) = Partition(items);

        CollectionAssert.AreEqual(items, parallel);
        Assert.AreEqual(0, sequential.Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Partition_OpenAndUnresolvedItems_GoToTheSequentialCleanup()
    {
        var open = Item(@"C:\repo\Open.cs", isOpen: true);
        var unresolved = Item(null);

        var (parallel, sequential) = Partition(new[] { open, unresolved });

        Assert.AreEqual(0, parallel.Count);
        CollectionAssert.AreEqual(new[] { open, unresolved }, sequential);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Partition_SameFileInSeveralProjects_IsCleanedOnce()
    {
        var closed = Item(@"C:\repo\Shared.cs");
        var closedLinked = Item(@"c:\REPO\shared.cs");
        var open = Item(@"C:\repo\Open.cs", isOpen: true);
        var openLinked = Item(@"C:\repo\open.cs", isOpen: true);

        var (parallel, sequential) = Partition(new[] { closed, closedLinked, open, openLinked });

        CollectionAssert.AreEqual(new[] { closed }, parallel);
        CollectionAssert.AreEqual(new[] { open }, sequential);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Partition_SameFileUnderDifferentPathSpellings_IsCleanedOnce()
    {
        var first = Item(@"C:\repo\Shared.cs");
        var relativeSpelling = Item(@"C:\repo\sub\..\Shared.cs");

        var (parallel, sequential) = Partition(new[] { first, relativeSpelling });

        CollectionAssert.AreEqual(new[] { first }, parallel);
        Assert.AreEqual(0, sequential.Count);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Partition_ClosedItemOfAFileOpenThroughAnotherItem_IsOnlyCleanedThroughTheEditor()
    {
        var closed = Item(@"C:\repo\Shared.cs");
        var open = Item(@"C:\repo\Shared.cs", isOpen: true);

        var (parallel, sequential) = Partition(new[] { closed, open });

        Assert.AreEqual(0, parallel.Count);
        CollectionAssert.AreEqual(new[] { open }, sequential);
    }

    private static (List<TestItem> Parallel, List<TestItem> Sequential) Partition(TestItem[] items) =>
        CleanupBatchPartitioner.Partition(items, item => item.FilePath, item => item.IsOpen);

    private static TestItem Item(string filePath, bool isOpen = false) => new TestItem(filePath, isOpen);

    private sealed class TestItem
    {
        public TestItem(string filePath, bool isOpen)
        {
            FilePath = filePath;
            IsOpen = isOpen;
        }

        public string FilePath { get; }

        public bool IsOpen { get; }
    }
}
