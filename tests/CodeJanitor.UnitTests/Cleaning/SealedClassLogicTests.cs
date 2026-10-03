using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Threading;
using CodeJanitor.Logic.Cleaning;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Threading;
using NSubstitute;

namespace CodeJanitor.UnitTests.Cleaning;

/// <summary>
/// Tests for the checks <see cref="SealedClassLogic" /> makes on the Visual Studio solution before sealing: a solution
/// whose completeness cannot be verified is treated as incomplete, and the projects that loaded projects outside the
/// Roslyn workspace reference are read from their project files.
/// </summary>
[TestClass]
public sealed class SealedClassLogicTests
{
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
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void CompleteSolution_HasNoIncompleteSolutionReason()
    {
        RunOnVisualStudioUIThread(() =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsSolution solution = CreateSolution(fullyLoaded: true, loaded: new Hierarchies(), unloaded: new Hierarchies());

            Assert.IsNull(SealedClassLogic.GetIncompleteSolutionReason(solution));
        });
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void SolutionStillLoading_IsIncomplete()
    {
        RunOnVisualStudioUIThread(() =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsSolution solution = CreateSolution(fullyLoaded: false, loaded: new Hierarchies(), unloaded: new Hierarchies());

            Assert.IsNotNull(SealedClassLogic.GetIncompleteSolutionReason(solution));
        });
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void SolutionWithAnUnloadedProject_IsIncomplete()
    {
        RunOnVisualStudioUIThread(() =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsSolution solution = CreateSolution(fullyLoaded: true, loaded: new Hierarchies(), unloaded: new Hierarchies(CreateHierarchy("Unloaded")));

            Assert.IsNotNull(SealedClassLogic.GetIncompleteSolutionReason(solution));
        });
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void SolutionWhoseLoadStateCannotBeRead_IsIncomplete()
    {
        RunOnVisualStudioUIThread(() =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsSolution solution = CreateSolution(fullyLoaded: true, loaded: new Hierarchies(), unloaded: new Hierarchies());
            solution.GetProperty(Arg.Any<int>(), out object _).ReturnsForAnyArgs(VSConstants.E_FAIL);

            Assert.IsNotNull(SealedClassLogic.GetIncompleteSolutionReason(solution));
        });
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void SolutionWhoseUnloadedProjectsCannotBeEnumerated_IsIncomplete()
    {
        RunOnVisualStudioUIThread(() =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsSolution solution = CreateSolution(fullyLoaded: true, loaded: new Hierarchies(), unloaded: new Hierarchies());
            Guid anyType = Guid.Empty;
            solution.GetProjectEnum(Arg.Any<uint>(), ref anyType, out IEnumHierarchies _).ReturnsForAnyArgs(VSConstants.E_FAIL);

            Assert.IsNotNull(SealedClassLogic.GetIncompleteSolutionReason(solution));
        });
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ProjectsReferencedOutsideWorkspace_AreTheProjectReferencesOfTheProjectsThatAreNeitherCSharpNorVisualBasic()
    {
        string native = WriteProjectFile(@"Native\Native.vcxproj", @"..\Library\Library.csproj", @"..\Interop\Interop.vcxproj");
        string app = WriteProjectFile(@"App\App.csproj", @"..\Other\Other.csproj");
        string basic = WriteProjectFile(@"Basic\Basic.vbproj", @"..\Shared\Shared.csproj");

        RunOnVisualStudioUIThread(() =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsSolution solution = CreateSolution(
                fullyLoaded: true,
                loaded: new Hierarchies(CreateHierarchy(native), CreateHierarchy(app), CreateHierarchy(basic), CreateHierarchy("Solution Items")),
                unloaded: new Hierarchies());

            CollectionAssert.AreEquivalent(
                new[] { Path.Combine(_tempDirectory, @"Library\Library.csproj"), Path.Combine(_tempDirectory, @"Interop\Interop.vcxproj") },
                SealedClassLogic.GetProjectsReferencedOutsideWorkspace(solution).ToList());
        });
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void ProjectReferenceThatCannotBeResolved_Throws()
    {
        string native = WriteProjectFile(@"Native\Native.vcxproj", @"$(SolutionDir)Library\Library.csproj");

        RunOnVisualStudioUIThread(() =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsSolution solution = CreateSolution(fullyLoaded: true, loaded: new Hierarchies(CreateHierarchy(native)), unloaded: new Hierarchies());

            Assert.ThrowsExactly<InvalidOperationException>(() => SealedClassLogic.GetProjectsReferencedOutsideWorkspace(solution));
        });
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void LoadedProjectsThatCannotBeEnumerated_Throw()
    {
        RunOnVisualStudioUIThread(() =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsSolution solution = CreateSolution(fullyLoaded: true, loaded: new Hierarchies(), unloaded: new Hierarchies());
            Guid anyType = Guid.Empty;
            solution.GetProjectEnum(Arg.Any<uint>(), ref anyType, out IEnumHierarchies _).ReturnsForAnyArgs(VSConstants.E_FAIL);

            Assert.Throws<Exception>(() => SealedClassLogic.GetProjectsReferencedOutsideWorkspace(solution));
        });
    }

    private static IVsSolution CreateSolution(bool fullyLoaded, Hierarchies loaded, Hierarchies unloaded)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        IVsSolution solution = Substitute.For<IVsSolution>();
        solution.GetProperty(Arg.Any<int>(), out object _).ReturnsForAnyArgs(call =>
        {
            call[1] = (int)call[0] == (int)__VSPROPID4.VSPROPID_IsSolutionFullyLoaded ? fullyLoaded : null;

            return VSConstants.S_OK;
        });
        Guid anyType = Guid.Empty;
        solution.GetProjectEnum(Arg.Any<uint>(), ref anyType, out IEnumHierarchies _).ReturnsForAnyArgs(call =>
        {
            call[2] = ((uint)call[0] & (uint)__VSENUMPROJFLAGS.EPF_UNLOADEDINSOLUTION) != 0 ? unloaded : loaded;

            return VSConstants.S_OK;
        });

        return solution;
    }

    private static IVsHierarchy CreateHierarchy(string canonicalName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        IVsHierarchy hierarchy = Substitute.For<IVsHierarchy>();
        hierarchy.GetCanonicalName(Arg.Any<uint>(), out string _).ReturnsForAnyArgs(call =>
        {
            call[1] = canonicalName;

            return VSConstants.S_OK;
        });

        return hierarchy;
    }

    private string WriteProjectFile(string relativePath, params string[] projectReferences)
    {
        string filePath = Path.Combine(_tempDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));
        File.WriteAllText(
            filePath,
            "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\r\n" +
            "  <ItemGroup>\r\n" +
            string.Concat(projectReferences.Select(reference => $"    <ProjectReference Include=\"{reference}\" />\r\n")) +
            "  </ItemGroup>\r\n" +
            "</Project>\r\n");

        return filePath;
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
    /// The hierarchies of a fixed list, as <see cref="IVsSolution.GetProjectEnum" /> returns them.
    /// </summary>
    private sealed class Hierarchies : IEnumHierarchies
    {
        private readonly IVsHierarchy[] _items;
        private int _next;

        public Hierarchies(params IVsHierarchy[] items)
        {
            _items = items;
        }

        public int Next(uint celt, IVsHierarchy[] rgelt, out uint pceltFetched)
        {
            pceltFetched = 0;
            while (pceltFetched < celt && _next < _items.Length)
            {
                rgelt[pceltFetched++] = _items[_next++];
            }

            return pceltFetched == celt ? VSConstants.S_OK : VSConstants.S_FALSE;
        }

        public int Skip(uint celt)
        {
            _next += (int)celt;

            return VSConstants.S_OK;
        }

        public int Reset()
        {
            _next = 0;

            return VSConstants.S_OK;
        }

        public int Clone(out IEnumHierarchies ppenum)
        {
            ppenum = new Hierarchies(_items);

            return VSConstants.S_OK;
        }
    }
}
