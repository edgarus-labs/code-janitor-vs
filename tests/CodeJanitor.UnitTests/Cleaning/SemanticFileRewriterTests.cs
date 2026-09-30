using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Logic.Transformations;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Threading;

namespace CodeJanitor.UnitTests.Cleaning;

/// <summary>
/// Tests for the data-safety flows of <see cref="SemanticFileRewriter" /> on closed files (nothing is written unless
/// the whole analysis succeeded and the file was neither opened nor edited meanwhile), and for the deferred binding of
/// the Roslyn workspace types by the logic classes built on it.
/// </summary>
[TestClass]
public sealed class SemanticFileRewriterTests
{
    private const string Original = "class C { }";
    private const string Rewritten = "sealed class C { }";

    private string _tempDirectory;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void RewriteClosedFile_AnalysisSucceeded_WritesTheRewrittenTextKeepingTheByteOrderMark()
    {
        string filePath = WriteFile(Original, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        SemanticFileRewriter rewriter = CreateRewriter((_, _, _, _) => Task.FromResult(Rewritten));

        bool rewritten = Rewrite(rewriter, filePath, isDocumentOpen: () => false, CancellationToken.None);

        Assert.IsTrue(rewritten);
        Assert.AreSequenceEqual(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(Rewritten)).ToArray(), File.ReadAllBytes(filePath));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void RewriteClosedFile_AnalysisCanceled_ThrowsAndLeavesTheFileUnchanged()
    {
        string filePath = WriteFile(Original, new UTF8Encoding(false));
        byte[] before = File.ReadAllBytes(filePath);
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        SemanticFileRewriter rewriter = CreateRewriter(async (_, _, _, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);

            return Rewritten;
        });

        Assert.Throws<OperationCanceledException>(() => Rewrite(rewriter, filePath, isDocumentOpen: () => false, cancellation.Token));

        Assert.AreSequenceEqual(before, File.ReadAllBytes(filePath));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void RewriteClosedFile_FileOpenedDuringTheAnalysis_IsLeftUnchanged()
    {
        string filePath = WriteFile(Original, new UTF8Encoding(false));
        byte[] before = File.ReadAllBytes(filePath);
        bool analyzed = false;
        SemanticFileRewriter rewriter = CreateRewriter((_, _, _, _) =>
        {
            analyzed = true;

            return Task.FromResult(Rewritten);
        });

        // The document is checked after the analysis: it is closed before the analysis starts and open when it ends.
        bool rewritten = Rewrite(rewriter, filePath, isDocumentOpen: () => analyzed, CancellationToken.None);

        Assert.IsFalse(rewritten);
        Assert.AreSequenceEqual(before, File.ReadAllBytes(filePath));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void RewriteClosedFile_FileEditedDuringTheAnalysis_IsNotOverwritten()
    {
        string filePath = WriteFile(Original, new UTF8Encoding(false));
        SemanticFileRewriter rewriter = CreateRewriter((path, _, _, _) =>
        {
            File.WriteAllText(path, "class Edited { }");

            return Task.FromResult(Rewritten);
        });

        bool rewritten = Rewrite(rewriter, filePath, isDocumentOpen: () => false, CancellationToken.None);

        Assert.IsFalse(rewritten);
        Assert.AreEqual("class Edited { }", File.ReadAllText(filePath));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(typeof(TypeLoadException), DisplayName = "Roslyn binding failure")]
    [DataRow(typeof(InvalidOperationException), DisplayName = "analysis failure")]
    public void RewriteClosedFile_AnalysisFailed_ReturnsFalseAndLeavesTheFileUnchanged(Type exceptionType)
    {
        string filePath = WriteFile(Original, new UTF8Encoding(false));
        byte[] before = File.ReadAllBytes(filePath);
        SemanticFileRewriter rewriter = CreateRewriter((_, _, _, _) => throw (Exception)Activator.CreateInstance(exceptionType));

        bool rewritten = Rewrite(rewriter, filePath, isDocumentOpen: () => false, CancellationToken.None);

        Assert.IsFalse(rewritten);
        Assert.AreSequenceEqual(before, File.ReadAllBytes(filePath));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void RewriteClosedFile_StepChangedNothing_ReturnsFalseAndLeavesTheFileUnchanged()
    {
        string filePath = WriteFile(Original, new UTF8Encoding(false));
        byte[] before = File.ReadAllBytes(filePath);
        SemanticFileRewriter rewriter = CreateRewriter((_, _, currentText, _) => Task.FromResult(currentText));

        bool rewritten = Rewrite(rewriter, filePath, isDocumentOpen: () => false, CancellationToken.None);

        Assert.IsFalse(rewritten);
        Assert.AreSequenceEqual(before, File.ReadAllBytes(filePath));
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void SealedClassLogic_Constructor_DoesNotBindTheRoslynWorkspaceTypes()
    {
        // Singletons are created by the CodeCleanupManager constructor, outside any try/catch: a host whose Roslyn
        // cannot be bound must fail only inside the deferred workspace method, where it is reported as a warning.
        // Creating the converter loads its ConditionalWeakTable<Solution, ...> field type, which binds
        // Microsoft.CodeAnalysis.Workspaces.
        ConstructorInfo constructor = typeof(SealedClassLogic).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();

        Assert.IsFalse(CreatesInstanceOf(constructor, typeof(ClassSealingConverter)), "The constructor must not create the ClassSealingConverter.");
    }

    /// <summary>
    /// Determines whether the IL of the method contains a <c>newobj</c> of a constructor of <paramref name="type" />.
    /// The scan reads every byte that could be the <c>newobj</c> opcode and resolves the token after it, so it cannot
    /// miss an instruction, and a false hit needs bytes that also resolve to a constructor of exactly that type.
    /// </summary>
    private static bool CreatesInstanceOf(MethodBase method, Type type)
    {
        const byte NewObj = 0x73;
        byte[] il = method.GetMethodBody().GetILAsByteArray();

        for (int index = 0; index + 4 < il.Length; index++)
        {
            if (il[index] != NewObj)
            {
                continue;
            }

            try
            {
                if (method.Module.ResolveMethod(BitConverter.ToInt32(il, index + 1)) is ConstructorInfo constructor && constructor.DeclaringType == type)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // Not a method token: the byte was an operand of another instruction.
            }
        }

        return false;
    }

    private static SemanticFileRewriter CreateRewriter(Func<string, string, string, CancellationToken, Task<string>> rewriteInWorkspaceAsync) =>
        new SemanticFileRewriter(null, "Cleaning_SealClassesWhenSafe", "Nothing was rewritten", "Rewrote nothing", "Rewrote", rewriteInWorkspaceAsync);

    /// <summary>
    /// Runs <see cref="SemanticFileRewriter.RewriteClosedFileAsync" /> on a thread that stands in for the Visual Studio
    /// UI thread.
    /// </summary>
    private static bool Rewrite(SemanticFileRewriter rewriter, string filePath, Func<bool> isDocumentOpen, CancellationToken cancellationToken)
    {
        bool rewritten = false;
        RunOnVisualStudioUIThread(() =>
            rewritten = ThreadHelper.JoinableTaskFactory.Run(() => rewriter.RewriteClosedFileAsync(filePath, null, isDocumentOpen, cancellationToken)));

        return rewritten;
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

    private string WriteFile(string text, Encoding encoding)
    {
        string filePath = Path.Combine(_tempDirectory, "Sample.cs");
        File.WriteAllText(filePath, text, encoding);

        return filePath;
    }
}
