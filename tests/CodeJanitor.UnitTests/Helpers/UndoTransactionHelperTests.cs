using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Threading;
using CodeJanitor.Helpers;
using CodeJanitor.Integration.Commands;
using CodeJanitor.Properties;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Threading;
using NSubstitute;

namespace CodeJanitor.UnitTests.Helpers;

[TestClass]
[DoNotParallelize]
public sealed class UndoTransactionHelperTests
{
    [TestCleanup]
    public void Cleanup() => Settings.Default.Reset();

    [TestMethod]
    [DataRow(typeof(CommentFormatCommand))]
    [DataRow(typeof(JoinLinesCommand))]
    [DataRow(typeof(SortLinesCommand))]
    [DataRow(typeof(SpadeContextDeleteCommand))]
    [DataRow(typeof(SpadeContextInsertRegionCommand))]
    public void CommandConstructor_DoesNotOpenUndoContext(Type commandType)
    {
        RunOnVisualStudioUIThread(() =>
        {
            Settings.Default.General_UseUndoTransactions = true;
            UndoContext undoContext = Substitute.For<UndoContext>();
            CodeJanitorPackage package = CreatePackage(undoContext);

            commandType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(CodeJanitorPackage) }, null)
                       .Invoke(new object[] { package });

            undoContext.DidNotReceiveWithAnyArgs().Open(default, default);
        });
    }

    [TestMethod]
    [DataRow(typeof(JoinLinesCommand))]
    [DataRow(typeof(SortLinesCommand))]
    public void CommandExecute_OpensUndoContextOnEveryExecution(Type commandType)
    {
        RunOnVisualStudioUIThread(() =>
        {
            Settings.Default.General_UseUndoTransactions = true;
            UndoContext undoContext = Substitute.For<UndoContext>();
            CodeJanitorPackage package = CreatePackage(undoContext);
            TextDocument textDocument = Substitute.For<TextDocument>();
            package.IDE.ActiveDocument.Object("TextDocument").Returns(textDocument);
            object command = commandType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(CodeJanitorPackage) }, null)
                                        .Invoke(new object[] { package });
            MethodInfo execute = commandType.GetMethod("OnExecute", BindingFlags.Instance | BindingFlags.NonPublic);

            execute.Invoke(command, null);
            execute.Invoke(command, null);

            undoContext.ReceivedWithAnyArgs(2).Open(default, default);
        });
    }

    [TestMethod]
    public void Constructor_DoesNotOpenUndoContext()
    {
        RunOnVisualStudioUIThread(() =>
        {
            Settings.Default.General_UseUndoTransactions = true;
            UndoContext undoContext = Substitute.For<UndoContext>();

            _ = new UndoTransactionHelper(CreatePackage(undoContext), "Test");

            undoContext.DidNotReceiveWithAnyArgs().Open(default, default);
        });
    }

    [TestMethod]
    public void Run_WhenActionSucceeds_OpensAndClosesUndoContextAroundAction()
    {
        RunOnVisualStudioUIThread(() =>
        {
            Settings.Default.General_UseUndoTransactions = true;
            UndoContext undoContext = Substitute.For<UndoContext>();
            UndoTransactionHelper helper = new UndoTransactionHelper(CreatePackage(undoContext), "Test");

            helper.Run(() => undoContext.Received(1).Open("Test", false));

            undoContext.Received(1).Close();
            undoContext.DidNotReceive().SetAborted();
        });
    }

    [TestMethod]
    public void Run_WhenActionThrows_AbortsUndoContext()
    {
        RunOnVisualStudioUIThread(() =>
        {
            Settings.Default.General_UseUndoTransactions = true;
            UndoContext undoContext = Substitute.For<UndoContext>();
            UndoTransactionHelper helper = new UndoTransactionHelper(CreatePackage(undoContext), "Test");

            helper.Run(() => throw new InvalidOperationException("Expected by the test."));

            undoContext.Received(1).Open("Test", false);
            undoContext.Received(1).SetAborted();
            undoContext.DidNotReceive().Close();
        });
    }

    [TestMethod]
    public void Run_WhenAlreadyRun_ThrowsWithoutOpeningUndoContext()
    {
        RunOnVisualStudioUIThread(() =>
        {
            Settings.Default.General_UseUndoTransactions = true;
            UndoContext undoContext = Substitute.For<UndoContext>();
            UndoTransactionHelper helper = new UndoTransactionHelper(CreatePackage(undoContext), "Test");
            helper.Run(() => { });
            undoContext.ClearReceivedCalls();

            Assert.ThrowsExactly<ObjectDisposedException>(() => helper.Run(() => { }));

            undoContext.DidNotReceiveWithAnyArgs().Open(default, default);
        });
    }

    [TestMethod]
    public void Begin_KeepsUndoContextOpenUntilDisposed()
    {
        RunOnVisualStudioUIThread(() =>
        {
            Settings.Default.General_UseUndoTransactions = true;
            UndoContext undoContext = Substitute.For<UndoContext>();

            using (UndoTransactionHelper.Begin(CreatePackage(undoContext), "Test"))
            {
                undoContext.Received(1).Open("Test", false);
                undoContext.DidNotReceive().Close();
            }

            undoContext.Received(1).Close();
        });
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
        System.Threading.Thread uiThread = new System.Threading.Thread(() =>
        {
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
    /// Creates a package without its constructor, which needs a running Visual Studio, whose IDE exposes <paramref name="undoContext" />.
    /// </summary>
    private static CodeJanitorPackage CreatePackage(UndoContext undoContext)
    {
        DTE2 ide = Substitute.For<DTE2>();
        ide.UndoContext.Returns(undoContext);
        CodeJanitorPackage package = (CodeJanitorPackage)FormatterServices.GetUninitializedObject(typeof(CodeJanitorPackage));
        typeof(CodeJanitorPackage).GetField("_ide", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(package, ide);

        return package;
    }
}
