using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task RunPerGroupAsync_ItemsOfDifferentGroups_AreProcessedAtTheSameTime()
    {
        int started = 0;
        TaskCompletionSource<bool> bothStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ConcurrentBag<bool> sawTheOtherGroup = new ConcurrentBag<bool>();

        await CleanupBatchPartitioner.RunPerGroupAsync(new[] { "a1", "b1" }, item => item.Substring(0, 1), 4, CancellationToken.None, async item =>
        {
            if (Interlocked.Increment(ref started) == 2)
            {
                bothStarted.TrySetResult(true);
            }

            sawTheOtherGroup.Add(await Task.WhenAny(bothStarted.Task, Task.Delay(1000)) == bothStarted.Task);
        });

        Assert.AreSequenceEqual(new[] { true, true }, sawTheOtherGroup.ToArray());
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task RunPerGroupAsync_ItemsOfOneGroup_AreProcessedOneAtATimeInOrder()
    {
        int running = 0;
        int maxRunning = 0;
        List<string> order = new List<string>();

        await CleanupBatchPartitioner.RunPerGroupAsync(new[] { "a1", "a2", "a3" }, item => item.Substring(0, 1), 4, CancellationToken.None, async item =>
        {
            int now = Interlocked.Increment(ref running);
            maxRunning = System.Math.Max(maxRunning, now);
            lock (order)
            {
                order.Add(item);
            }

            await Task.Delay(20);
            Interlocked.Decrement(ref running);
        });

        Assert.AreEqual(1, maxRunning);
        Assert.AreSequenceEqual(new[] { "a1", "a2", "a3" }, order);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task RunPerGroupAsync_Canceled_TakesNoFurtherItems()
    {
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        List<string> processed = new List<string>();

        await CleanupBatchPartitioner.RunPerGroupAsync(new[] { "a1", "a2", "a3" }, item => item.Substring(0, 1), 4, cancellation.Token, item =>
        {
            processed.Add(item);
            cancellation.Cancel();

            return Task.CompletedTask;
        });

        Assert.AreSequenceEqual(new[] { "a1" }, processed);
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
