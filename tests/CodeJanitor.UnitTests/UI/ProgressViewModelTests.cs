using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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

namespace CodeJanitor.UnitTests.UI;

/// <summary>
/// Tests for the batch progress view models. Their UI-thread work must go through the
/// <see cref="ThreadHelper.JoinableTaskFactory" />, so it is also serviced while Visual Studio waits in a joinable
/// task: the tests pump the UI thread through nothing but the joinable task context's synchronization context, and
/// never pump the WPF dispatcher of that thread.
/// </summary>
[TestClass]
public sealed class ProgressViewModelTests
{
    private static readonly TimeSpan PumpTimeout = TimeSpan.FromSeconds(10);

    [TestMethod]
    [TestCategory("UI UnitTests")]
    public void CleanupProgressViewModel_BatchCompletesOnTheJoinableTaskFactoryUiThread_WithoutPumpingTheDispatcher() => RunOnVisualStudioUIThread(pump =>
                                                                                                                              {
                                                                                                                                  CleanupProgressViewModel viewModel = new CleanupProgressViewModel(null, Array.Empty<object>());

                                                                                                                                  bool completed = pump.PumpUntil(() => viewModel.DialogResult is not null, PumpTimeout);

                                                                                                                                  Assert.IsTrue(completed, "The batch must reach the UI thread through the joinable task factory, not a captured dispatcher.");
                                                                                                                                  Assert.IsTrue(viewModel.DialogResult, "The batch must complete.");
                                                                                                                              });

    [TestMethod]
    [TestCategory("UI UnitTests")]
    public void CleanupProgressViewModel_CanceledBatch_DoesNotLeaveLaterCleanupsWithACanceledXmlDocumentationRun()
    {
        try
        {
            RunOnVisualStudioUIThread(pump =>
            {
                CleanupProgressViewModel viewModel = new CleanupProgressViewModel(null, Array.Empty<object>());
                viewModel.CancelCommand.Execute(null);

                bool completed = pump.PumpUntil(() => viewModel.DialogResult is not null, PumpTimeout);

                Assert.IsTrue(completed, "The canceled batch must complete.");
                Assert.IsTrue(viewModel.DialogResult, "The canceled batch must complete.");
                Assert.IsFalse(AiXmlDocumentationLogic.RunToken.IsCancellationRequested, "A canceled batch must not cancel the XML documentation of later single-document cleanups.");
            });
        }
        finally
        {
            AiXmlDocumentationLogic.BeginRun();
        }
    }

    [TestMethod]
    [TestCategory("UI UnitTests")]
    public void CleanupProgressViewModel_CreatedOffTheUiThread_ThrowsInsteadOfWaitingForADispatcherThatIsNeverPumped()
    {
        Exception failure = null;
        Thread thread = new Thread(() =>
        {
            try
            {
                _ = new CleanupProgressViewModel(null, Array.Empty<object>());
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.IsInstanceOfType<COMException>(failure, "A view model that is not created on the UI thread must fail fast: its UI-thread work could never run.");
    }

    [TestMethod]
    [TestCategory("UI UnitTests")]
    public void XmlDocProgressViewModel_CancelPressedAfterTheWorkFinished_IsReportedAsCanceled()
    {
        try
        {
            RunOnVisualStudioUIThread(pump =>
            {
                DTE2 ide = Substitute.For<DTE2>();
                CodeJanitorPackage package = CreatePackage(ide);
                List<string> errors = [];

                XmlDocProgressViewModel viewModel = new XmlDocProgressViewModel(package, Array.Empty<EnvDTE.ProjectItem>(), errors.Add);

                // The background work is done once its completion is queued for the UI thread. The completion is
                // handled only after that, so the cancel arrives while the batch is still being wrapped up.
                Assert.IsTrue(pump.WaitUntilQueued(PumpTimeout), "The background work must finish.");
                viewModel.CancelCommand.Execute(null);
                bool completed = pump.PumpUntil(() => viewModel.DialogResult is not null, PumpTimeout);

                Assert.IsTrue(completed, "The batch must complete.");
                Assert.IsEmpty(errors, "The canceled batch must not fail.");
                ide.StatusBar.Received().Text = Arg.Is<string>(text => text.Contains("canceled"));
                ide.StatusBar.DidNotReceive().Text = Arg.Is<string>(text => text.Contains("completed"));
            });
        }
        finally
        {
            AiXmlDocumentationLogic.BeginRun();
        }
    }

    [TestMethod]
    [TestCategory("UI UnitTests")]
    public void CleanupProgressViewModel_FileWhoseHeadlessCleanupFailed_IsCountedAsOneFailureAndSkipsTheDiagnosticPass()
    {
        string directory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Settings.Default.Reset();
        Settings.Default.Cleaning_MoveTopLevelTypesToSeparateFiles = true;
        try
        {
            string filePath = Path.Combine(directory, "Foo.cs");
            File.WriteAllText(filePath, "class Foo { }\r\nclass Bar { }\r\n");

            RunOnVisualStudioUIThread(pump =>
            {
                // Adding the file split out of Foo.cs to the project fails, so the headless cleanup of Foo.cs fails. The
                // diagnostic pass cannot succeed either without a Roslyn workspace: it would record a second failure.
                DTE2 ide = Substitute.For<DTE2>();
                ide.Solution.Returns(_ => throw new COMException("The solution is closed."));
                CodeJanitorPackage package = CreatePackage(ide);
                EnvDTE.ProjectItem projectItem = Substitute.For<EnvDTE.ProjectItem>();
                projectItem.FileNames[1].Returns(filePath);
                List<string> messages = [];

                WithManagerPackage(package, manager =>
                {
                    CleanupProgressViewModel viewModel = new CleanupProgressViewModel(package, new object[] { projectItem }, messages.Add);

                    bool completed = pump.PumpUntil(() => viewModel.DialogResult is not null, PumpTimeout);

                    Assert.IsTrue(completed, "The batch must complete.");
                    Assert.IsTrue(File.Exists(Path.Combine(directory, "Bar.cs")), "The failure must come from the headless cleanup, after its split.");
                    Assert.AreEqual(1, manager.GetCleanupExecutionStats().FailedItems, "A failed file must be counted once, not once per pass.");
                    Assert.HasCount(1, messages);
                    Assert.Contains("1 failed item(s)", messages[0]);
                });
            });
        }
        finally
        {
            Settings.Default.Reset();
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    [TestCategory("UI UnitTests")]
    public void CodeCleanupManager_FileRewrittenBySemanticStepThatFallsBackToTheEditor_IsNotCountedAsChanged()
    {
        string isolationDirectory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        string directory = Path.Combine(isolationDirectory, "Work");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(isolationDirectory, ".editorconfig"), "root = true\n");
        Settings.Default.Reset();
        Settings.Default.Cleaning_MoveTopLevelTypesToSeparateFiles = true;
        try
        {
            string filePath = Path.Combine(directory, "Foo.cs");
            File.WriteAllText(filePath, "class Foo { }\r\nclass Bar { }\r\n");
            Directory.CreateDirectory(Path.Combine(directory, "Bar.cs"));

            RunOnVisualStudioUIThread(pump =>
            {
                // The split cannot write Bar.cs, which is a directory, so the headless cleanup fails after the
                // semantic step rewrote the file, and the file goes on to the editor cleanup, which cannot open it.
                DTE2 ide = Substitute.For<DTE2>();
                CodeJanitorPackage package = CreatePackage(ide);
                EnvDTE.ProjectItem projectItem = Substitute.For<EnvDTE.ProjectItem>();
                projectItem.FileNames[1].Returns(filePath);
                projectItem.Document.Returns((EnvDTE.Document)null);

                WithManagerPackage(package, manager =>
                {
                    manager.ResetCleanupExecutionStats();

                    Task cleanup = manager.CleanupAvailableProjectItemAsync(
                        projectItem,
                        new Func<EnvDTE.ProjectItem, CancellationToken, Task<bool>>[] { (_, _) => Task.FromResult(true) },
                        CancellationToken.None);

                    Assert.IsTrue(pump.PumpUntil(() => cleanup.IsCompleted, PumpTimeout), "The cleanup must complete.");
                    Assert.AreEqual(TaskStatus.RanToCompletion, cleanup.Status, cleanup.Exception?.ToString());

                    CodeCleanupManager.CleanupExecutionStats stats = manager.GetCleanupExecutionStats();
                    Assert.AreEqual(1, stats.FailedItems, "The file the editor cleanup cannot open must be counted as failed.");
                    Assert.AreEqual(0, stats.HeadlessChangedItems, "A file left to the editor cleanup must not also be counted as changed.");
                });
            });
        }
        finally
        {
            Settings.Default.Reset();
            Directory.Delete(isolationDirectory, true);
        }
    }

    /// <summary>
    /// Runs the test on an STA thread that the Visual Studio <see cref="ThreadHelper" /> treats as its UI thread, with
    /// a joinable task context whose main thread is serviced only by the returned pump, and restores the
    /// <see cref="ThreadHelper" /> state afterwards.
    /// </summary>
    private static void RunOnVisualStudioUIThread(Action<UiThreadPump> test)
    {
        Exception failure = null;
        FieldInfo uiThreadDispatcherField = typeof(ThreadHelper).GetField("uiThreadDispatcher", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo joinableTaskContextField = typeof(ThreadHelper).GetField("_joinableTaskContextCache", BindingFlags.Static | BindingFlags.NonPublic);
        object previousUIThreadDispatcher = uiThreadDispatcherField.GetValue(null);
        object previousJoinableTaskContext = joinableTaskContextField.GetValue(null);
        Thread uiThread = new Thread(() =>
        {
            UiThreadPump pump = new UiThreadPump();
            SynchronizationContext.SetSynchronizationContext(pump);
#pragma warning disable VSSDK005 // The test stands in for Visual Studio, which owns the ThreadHelper singleton.
            using JoinableTaskContext joinableTaskContext = new JoinableTaskContext();
#pragma warning restore VSSDK005
            try
            {
                typeof(ThreadHelper).GetMethod("SetUIThread", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                joinableTaskContextField.SetValue(null, joinableTaskContext);

                test(pump);
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
    /// Runs the action with the package in the cleanup manager singleton, which keeps the package it was first created
    /// with.
    /// </summary>
    private static void WithManagerPackage(CodeJanitorPackage package, Action<CodeCleanupManager> action)
    {
        CodeCleanupManager manager = CodeCleanupManager.GetInstance(package);
        FieldInfo packageField = typeof(CodeCleanupManager).GetField("_package", BindingFlags.Instance | BindingFlags.NonPublic);
        object previousPackage = packageField.GetValue(manager);
        packageField.SetValue(manager, package);
        try
        {
            action(manager);
        }
        finally
        {
            packageField.SetValue(manager, previousPackage);
            manager.ResetCleanupExecutionStats();
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
    /// The synchronization context of the test's UI thread: work posted to it runs only when the test pumps it.
    /// </summary>
    private sealed class UiThreadPump : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object State)> _queue = new BlockingCollection<(SendOrPostCallback, object)>();
        private readonly int _threadId = Environment.CurrentManagedThreadId;

        public override void Post(SendOrPostCallback d, object state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object state)
        {
            if (Environment.CurrentManagedThreadId == _threadId)
            {
                d(state);

                return;
            }

            using ManualResetEventSlim done = new ManualResetEventSlim();
            Exception failure = null;
            Post(_ =>
            {
                try
                {
                    d(state);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    done.Set();
                }
            }, null);
            done.Wait();
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        public override SynchronizationContext CreateCopy() => this;

        /// <summary>
        /// Waits until work is queued, without running it.
        /// </summary>
        public bool WaitUntilQueued(TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (_queue.Count == 0)
            {
                if (DateTime.UtcNow > deadline)
                {
                    return false;
                }

                Thread.Sleep(5);
            }

            return true;
        }

        /// <summary>
        /// Runs the queued work until the condition holds.
        /// </summary>
        public bool PumpUntil(Func<bool> condition, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (!condition())
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    return false;
                }

                if (_queue.TryTake(out (SendOrPostCallback Callback, object State) work, (int)Math.Min(20, remaining.TotalMilliseconds)))
                {
                    work.Callback(work.State);
                }
            }

            return true;
        }
    }
}
