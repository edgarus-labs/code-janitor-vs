using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CodeJanitor.Logic.Ai;
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
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task RunPerGroupAsync_ItemsOfDifferentGroups_AreProcessedAtTheSameTime()
    {
        int started = 0;
        TaskCompletionSource<bool> bothStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Each item only completes once the item of the other group has started too: sequential processing never gets there.
        await CleanupBatchPartitioner.RunPerGroupAsync(new[] { "a1", "b1" }, item => item.Substring(0, 1), 4, CancellationToken.None, async item =>
        {
            if (Interlocked.Increment(ref started) == 2)
            {
                bothStarted.TrySetResult(true);
            }

            await bothStarted.Task;
        });

        Assert.AreEqual(2, started);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task RunPerGroupAsync_SingleParallelGroup_NeverProcessesTwoGroupsAtTheSameTime()
    {
        int running = 0;
        int maxRunning = 0;
        List<string> processed = [];

        await CleanupBatchPartitioner.RunPerGroupAsync(new[] { "a1", "b1", "c1", "a2", "b2", "c2" }, item => item.Substring(0, 1), 1, CancellationToken.None, async item =>
        {
            int now = Interlocked.Increment(ref running);
            int seen;
            while ((seen = Volatile.Read(ref maxRunning)) < now && Interlocked.CompareExchange(ref maxRunning, now, seen) != seen)
            {
            }

            // Gives a concurrently started item of another group every chance to overlap.
            for (int i = 0; i < 50; i++)
            {
                await Task.Yield();
            }

            lock (processed)
            {
                processed.Add(item);
            }

            Interlocked.Decrement(ref running);
        });

        Assert.AreEqual(1, maxRunning);
        Assert.HasCount(6, processed);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public async Task RunPerGroupAsync_ItemsOfOneGroup_AreProcessedOneAtATimeInOrder()
    {
        int running = 0;
        int maxRunning = 0;
        List<string> order = [];

        await CleanupBatchPartitioner.RunPerGroupAsync(new[] { "a1", "a2", "a3" }, item => item.Substring(0, 1), 4, CancellationToken.None, async item =>
        {
            int now = Interlocked.Increment(ref running);
            maxRunning = System.Math.Max(maxRunning, now);
            lock (order)
            {
                order.Add(item);
            }

            await Task.Delay(20, TestContext.CancellationToken);
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
        List<string> processed = [];

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

            WithPackage(package, () =>
            {
                CodeCleanupManager manager = CodeCleanupManager.GetInstance(package);
                try
                {
                    CleanupProgressViewModel viewModel = new CleanupProgressViewModel(package, Array.Empty<object>());

                    // A changed file makes the batch start the build verification when it completes on this thread's
                    // dispatcher, which only runs once the dispatcher is pumped below.
                    manager.IncrementHeadlessChanged();
                    WaitForBatch(viewModel);

                    Assert.IsTrue(viewModel.DialogResult, "The dialog must close when the build verification cannot be started.");
                    _ = ide.Received().Events;
                }
                finally
                {
                    manager.ResetCleanupExecutionStats();
                }
            });
        });
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CleanupProgressViewModel_CompletingTheBatchThrows_TheDialogStillCloses()
    {
        RunOnVisualStudioUIThread(() =>
        {
            // Reading the solution fails while the batch completes, e.g. because the solution was closed meanwhile.
            DTE2 ide = Substitute.For<DTE2>();
            ide.Solution.Returns(_ => throw new COMException("The solution is closed."));
            CodeJanitorPackage package = CreatePackage(ide);

            WithPackage(package, () =>
            {
                CodeCleanupManager manager = CodeCleanupManager.GetInstance(package);
                try
                {
                    CleanupProgressViewModel viewModel = new CleanupProgressViewModel(package, Array.Empty<object>());
                    manager.IncrementHeadlessChanged();
                    WaitForBatch(viewModel);

                    Assert.IsTrue(viewModel.DialogResult, "The dialog must close even when completing the batch fails.");
                }
                finally
                {
                    manager.ResetCleanupExecutionStats();
                }
            });
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

                    Assert.IsTrue(viewModel.DialogResult, "The batch must complete.");
                });

                Assert.IsEmpty(projectItem.ReceivedCalls(), "The XML documentation must not run for a document that was not cleaned up.");
            });
        }
        finally
        {
            Settings.Default.Reset();
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void XmlDocProgressViewModel_CanceledBatch_DoesNotLeaveLaterCleanupsWithACanceledXmlDocumentationRun()
    {
        try
        {
            RunOnVisualStudioUIThread(() =>
            {
                CodeJanitorPackage package = CreatePackage(Substitute.For<DTE2>());
                List<string> errors = [];

                // Errors are recorded instead of shown in a modal message box, which would block the test run.
                XmlDocProgressViewModel viewModel = new XmlDocProgressViewModel(package, Array.Empty<EnvDTE.ProjectItem>(), errors.Add);
                viewModel.CancelCommand.Execute(null);
                WaitForBatch(viewModel);

                Assert.IsEmpty(errors, "The canceled batch must not fail.");
                Assert.IsTrue(viewModel.DialogResult, "The canceled batch must complete.");
                Assert.IsFalse(AiXmlDocumentationLogic.RunToken.IsCancellationRequested, "A canceled XML documentation batch must not cancel the XML documentation of later cleanups.");
            });
        }
        finally
        {
            AiXmlDocumentationLogic.BeginRun();
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void TopLevelTypeSplit_ReplacingTheEditorTextFails_RemovesTheFilesCreatedForTheMovedTypes()
    {
        string directory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string filePath = Path.Combine(directory, "Foo.cs");
        const string Source = "class Foo { }\r\nclass Bar { }\r\n";
        File.WriteAllText(filePath, Source);
        Settings.Default.Reset();
        Settings.Default.Cleaning_MoveTopLevelTypesToSeparateFiles = true;
        try
        {
            RunOnVisualStudioUIThread(() =>
            {
                CodeJanitorPackage package = CreatePackage(Substitute.For<DTE2>());
                EnvDTE.ProjectItem projectItem = Substitute.For<EnvDTE.ProjectItem>();
                projectItem.FileNames[1].Returns(filePath);
                EnvDTE.EditPoint startPoint = Substitute.For<EnvDTE.EditPoint>();
                startPoint.GetText(Arg.Any<object>()).Returns(Source);
                startPoint
                    .When(point => point.ReplaceText(Arg.Any<object>(), Arg.Any<string>(), Arg.Any<int>()))
                    .Do(_ => throw new IOException("The editor buffer is read-only."));
                EnvDTE.TextPoint startTextPoint = Substitute.For<EnvDTE.TextPoint>();
                startTextPoint.CreateEditPoint().Returns(startPoint);
                EnvDTE.TextPoint endTextPoint = Substitute.For<EnvDTE.TextPoint>();
                endTextPoint.CreateEditPoint().Returns(Substitute.For<EnvDTE.EditPoint>());
                EnvDTE.TextDocument textDocument = Substitute.For<EnvDTE.TextDocument>();
                textDocument.StartPoint.Returns(startTextPoint);
                textDocument.EndPoint.Returns(endTextPoint);
                EnvDTE.Document document = Substitute.For<EnvDTE.Document>();
                document.Language.Returns("CSharp");
                document.ProjectItem.Returns(projectItem);
                document.Object("TextDocument").Returns(textDocument);

                WithPackage(package, () =>
                {
                    MethodInfo split = typeof(CodeCleanupManager).GetMethod("TrySplitTopLevelTypesToSeparateFiles", BindingFlags.Instance | BindingFlags.NonPublic);

                    // The original keeps declaring Bar: a surviving Bar.cs would duplicate it (CS0101).
                    TargetInvocationException failure = Assert.ThrowsExactly<TargetInvocationException>(
                        () => split.Invoke(CodeCleanupManager.GetInstance(package), new object[] { document, EffectiveCleanupSettings.For(filePath) }));
                    Assert.IsInstanceOfType<IOException>(failure.InnerException);
                    Assert.IsFalse(File.Exists(Path.Combine(directory, "Bar.cs")), "The file created for the moved type must be removed.");
                });
            });
        }
        finally
        {
            Settings.Default.Reset();
            Directory.Delete(directory, recursive: true);
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
    /// Runs the action with fresh cleanup singletons created with the package, which they keep for their lifetime.
    /// The singletons of the test run are put back afterwards, so the fake package never outlives the action, even
    /// when the action created a singleton that did not exist before.
    /// </summary>
    private static void WithPackage(CodeJanitorPackage package, Action action)
    {
        FieldInfo[] singletonFields = Array.FindAll(
            typeof(CodeCleanupManager).Assembly.GetTypes(),
            type => type.GetMethod("GetInstance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(CodeJanitorPackage) }, null) is not null)
            .Select(type => type.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic))
            .Where(field => field is not null)
            .ToArray();
        object[] previousInstances = Array.ConvertAll(singletonFields, field => field.GetValue(null));
        foreach (FieldInfo field in singletonFields)
        {
            field.SetValue(null, null);
        }

        try
        {
            // The singletons the cleanup under test reaches through GetInstance must belong to this package.
            CodeCleanupManager.GetInstance(package);
            CodeCleanupAvailabilityLogic.GetInstance(package);
            action();
        }
        finally
        {
            for (int i = 0; i < singletonFields.Length; i++)
            {
                singletonFields[i].SetValue(null, previousInstances[i]);
            }
        }
    }

    /// <summary>
    /// Pumps the dispatcher of this thread, where the batch completes, until the dialog result is set.
    /// </summary>
    private static void WaitForBatch(BaseProgressViewModel viewModel)
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

    public TestContext TestContext { get; set; }
}
