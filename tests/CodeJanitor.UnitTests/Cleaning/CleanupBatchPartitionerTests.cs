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
        TestItem[] items = [Item(@"C:\repo\A.cs"), Item(@"C:\repo\View.xaml")];

        (List<TestItem> parallel, List<TestItem> sequential) = Partition(items);

        Assert.AreSequenceEqual(items, parallel);
        Assert.IsEmpty(sequential);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Partition_OpenAndUnresolvedItems_GoToTheSequentialCleanup()
    {
        TestItem open = Item(@"C:\repo\Open.cs", isOpen: true);
        TestItem unresolved = Item(null);

        (List<TestItem> parallel, List<TestItem> sequential) = Partition(new[] { open, unresolved });

        Assert.IsEmpty(parallel);
        Assert.AreSequenceEqual(new[] { open, unresolved }, sequential);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Partition_SameFileInSeveralProjects_IsCleanedOnce()
    {
        TestItem closed = Item(@"C:\repo\Shared.cs");
        TestItem closedLinked = Item(@"c:\REPO\shared.cs");
        TestItem open = Item(@"C:\repo\Open.cs", isOpen: true);
        TestItem openLinked = Item(@"C:\repo\open.cs", isOpen: true);

        (List<TestItem> parallel, List<TestItem> sequential) = Partition(new[] { closed, closedLinked, open, openLinked });

        Assert.AreSequenceEqual(new[] { closed }, parallel);
        Assert.AreSequenceEqual(new[] { open }, sequential);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Partition_SameFileUnderDifferentPathSpellings_IsCleanedOnce()
    {
        TestItem first = Item(@"C:\repo\Shared.cs");
        TestItem relativeSpelling = Item(@"C:\repo\sub\..\Shared.cs");

        (List<TestItem> parallel, List<TestItem> sequential) = Partition(new[] { first, relativeSpelling });

        Assert.AreSequenceEqual(new[] { first }, parallel);
        Assert.IsEmpty(sequential);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Partition_ClosedItemOfAFileOpenThroughAnotherItem_IsOnlyCleanedThroughTheEditor()
    {
        TestItem closed = Item(@"C:\repo\Shared.cs");
        TestItem open = Item(@"C:\repo\Shared.cs", isOpen: true);

        (List<TestItem> parallel, List<TestItem> sequential) = Partition(new[] { closed, open });

        Assert.IsEmpty(parallel);
        Assert.AreSequenceEqual(new[] { open }, sequential);
    }

    private static (List<TestItem> Parallel, List<TestItem> Sequential) Partition(TestItem[] items)
        => CleanupBatchPartitioner.Partition(items, item => item.FilePath, item => item.IsOpen);

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
