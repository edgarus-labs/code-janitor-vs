using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.CleanupProgress;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Threading;
using NSubstitute;

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

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CleanupProgressViewModel_BuildVerificationCannotStart_TheDialogStillCloses()
    {
        RunOnVisualStudioUIThread(() =>
        {
            DTE2 ide = Substitute.For<DTE2>();
            ide.Events.Returns(_ => throw new COMException("The build events are not available."));
            CodeJanitorPackage package = CreatePackage(ide);

            CleanupProgressViewModel viewModel = new CleanupProgressViewModel(package, Array.Empty<object>());

            // A changed file makes the batch start the build verification when it completes on this thread's
            // dispatcher, which only runs once the dispatcher is pumped below.
            CodeCleanupManager.GetInstance(package).IncrementHeadlessChanged();
            WaitForBatch(viewModel);

            Assert.IsTrue(viewModel.DialogResult == true, "The dialog must close when the build verification cannot be started.");
            _ = ide.Received().Events;
        });
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CleanupProgressViewModel_DocumentThatCannotBeCleanedUp_GetsNoXmlDocumentation()
    {
        Settings.Default.Cleaning_AiXmlDocumentationEnabled = true;
        Settings.Default.Cleaning_AiXmlDocumentationRunDuringCleanup = true;
        try
        {
            RunOnVisualStudioUIThread(() =>
            {
                // Cleanup is not available while debugging.
                DTE2 ide = Substitute.For<DTE2>();
                ide.Debugger.CurrentMode.Returns(EnvDTE.dbgDebugMode.dbgRunMode);
                CodeJanitorPackage package = CreatePackage(ide);
                EnvDTE.ProjectItem projectItem = Substitute.For<EnvDTE.ProjectItem>();
                EnvDTE.Document document = Substitute.For<EnvDTE.Document>();
                document.ProjectItem.Returns(projectItem);

                WithPackage(package, () =>
                {
                    CleanupProgressViewModel viewModel = new CleanupProgressViewModel(package, new object[] { document });
                    WaitForBatch(viewModel);

                    Assert.IsTrue(viewModel.DialogResult == true, "The batch must complete.");
                });

                Assert.IsEmpty(projectItem.ReceivedCalls(), "The XML documentation must not run for a document that was not cleaned up.");
            });
        }
        finally
        {
            Settings.Default.Reset();
        }
    }

    /// <summary>
    /// Runs the test on an STA thread that the Visual Studio <see cref="ThreadHelper" /> treats as its UI thread, and
    /// restores the <see cref="ThreadHelper" /> state afterwards.
    /// </summary>
    private static void RunOnVisualStudioUIThread(Action test)
    {
        Exception failure = null;
        FieldInfo uiThreadDispatcherField = typeof(ThreadHelper).GetField("uiThreadDispatcher", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo joinableTaskContextField = typeof(ThreadHelper).GetField("_joinableTaskContextCache", BindingFlags.Static | BindingFlags.NonPublic);
        object previousUIThreadDispatcher = uiThreadDispatcherField.GetValue(null);
        object previousJoinableTaskContext = joinableTaskContextField.GetValue(null);
        Thread uiThread = new Thread(() =>
        {
            // Without Visual Studio, no thread is its UI thread: this thread becomes it for the test, with a joinable
            // task context whose main thread is this thread.
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
#pragma warning disable VSSDK005 // The test stands in for Visual Studio, which owns the ThreadHelper singleton.
            using JoinableTaskContext joinableTaskContext = new JoinableTaskContext();
#pragma warning restore VSSDK005
            try
            {
                typeof(ThreadHelper).GetMethod("SetUIThread", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                joinableTaskContextField.SetValue(null, joinableTaskContext);

                test();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                uiThreadDispatcherField.SetValue(null, previousUIThreadDispatcher);
                joinableTaskContextField.SetValue(null, previousJoinableTaskContext);
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        uiThread.IsBackground = true;
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        if (!uiThread.Join(TimeSpan.FromSeconds(60)))
        {
            Assert.Fail("The test did not complete.");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>
    /// Creates a package without its constructor, which needs a running Visual Studio.
    /// </summary>
    private static CodeJanitorPackage CreatePackage(DTE2 ide)
    {
        CodeJanitorPackage package = (CodeJanitorPackage)FormatterServices.GetUninitializedObject(typeof(CodeJanitorPackage));
        typeof(CodeJanitorPackage).GetField("_ide", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(package, ide);

        return package;
    }

    /// <summary>
    /// Runs the action with the package in the cleanup singletons, which keep the package they were first created with.
    /// </summary>
    private static void WithPackage(CodeJanitorPackage package, Action action)
    {
        object[] singletons = { CodeCleanupManager.GetInstance(package), CodeCleanupAvailabilityLogic.GetInstance(package) };
        FieldInfo[] packageFields = Array.ConvertAll(singletons, singleton => singleton.GetType().GetField("_package", BindingFlags.Instance | BindingFlags.NonPublic));
        object[] previousPackages = new object[singletons.Length];
        for (int i = 0; i < singletons.Length; i++)
        {
            previousPackages[i] = packageFields[i].GetValue(singletons[i]);
            packageFields[i].SetValue(singletons[i], package);
        }

        try
        {
            action();
        }
        finally
        {
            for (int i = 0; i < singletons.Length; i++)
            {
                packageFields[i].SetValue(singletons[i], previousPackages[i]);
            }
        }
    }

    /// <summary>
    /// Pumps the dispatcher of this thread, where the batch completes, until the dialog result is set.
    /// </summary>
    private static void WaitForBatch(CleanupProgressViewModel viewModel)
    {
        DispatcherFrame frame = new DispatcherFrame();
        DispatcherTimer poll = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Background, (_, _) => frame.Continue = viewModel.DialogResult is null, Dispatcher.CurrentDispatcher);
        DispatcherTimer timeout = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Normal, (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        Dispatcher.PushFrame(frame);
        poll.Stop();
        timeout.Stop();
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
