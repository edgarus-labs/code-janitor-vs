using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using System;
using System.Reflection;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.Integration.Events;

/// <summary>
/// A class that encapsulates listening for build progress events.
/// </summary>

internal sealed class BuildProgressEventListener : BaseEventListener
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BuildProgressEventListener" /> class.
    /// </summary>
    /// <param name="package">The package hosting the event listener.</param>

    private BuildProgressEventListener(CodeJanitorPackage package)
        : base(package)
    {
        // Store access to the build events, otherwise events will not register properly via DTE.
        BuildEvents = Package.IDE.Events.BuildEvents;
    }

    /// <summary>
    /// An event raised when a build has begun.
    /// </summary>

    internal event Action<vsBuildScope, vsBuildAction> BuildBegin;

    /// <summary>
    /// An event raised when a build is done.
    /// </summary>

    internal event Action<vsBuildScope, vsBuildAction> BuildDone;

    /// <summary>
    /// An event raised when an individual project build has begun.
    /// </summary>

    internal event Action<string, string, string, string> BuildProjConfigBegin;

    /// <summary>
    /// An event raised when an individual project build is done.
    /// </summary>

    internal event Action<string, string, string, string, bool> BuildProjConfigDone;

    /// <summary>
    /// A singleton instance of this command.
    /// </summary>
    public static BuildProgressEventListener Instance { get; private set; }

    /// <summary>
    /// Gets or sets a pointer to the IDE build events.
    /// </summary>
    private BuildEvents BuildEvents { get; set; }

    private EventInfo _onBuildBeginEvent;
    private EventInfo _onBuildProjConfigBeginEvent;
    private EventInfo _onBuildProjConfigDoneEvent;
    private EventInfo _onBuildDoneEvent;

    private Delegate _onBuildBeginHandler;
    private Delegate _onBuildProjConfigBeginHandler;
    private Delegate _onBuildProjConfigDoneHandler;
    private Delegate _onBuildDoneHandler;

    /// <summary>
    /// Initializes a singleton instance of this event listener.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <returns>A task.</returns>

    public static async Task InitializeAsync(CodeJanitorPackage package)
    {
        Instance = new BuildProgressEventListener(package);
        await package.SettingsMonitor.WatchAsync(s => s.Feature_BuildProgressToolWindow, Instance.SwitchAsync);
    }

    /// <summary>
    /// Registers event handlers with the IDE.
    /// </summary>

    protected override void RegisterListeners()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var eventSourceType = BuildEvents.GetType();

        _onBuildBeginEvent = eventSourceType.GetEvent("OnBuildBegin");
        _onBuildProjConfigBeginEvent = eventSourceType.GetEvent("OnBuildProjConfigBegin");
        _onBuildProjConfigDoneEvent = eventSourceType.GetEvent("OnBuildProjConfigDone");
        _onBuildDoneEvent = eventSourceType.GetEvent("OnBuildDone");

        _onBuildBeginHandler = CreateHandler(_onBuildBeginEvent, nameof(BuildEvents_OnBuildBegin));
        _onBuildProjConfigBeginHandler = CreateHandler(_onBuildProjConfigBeginEvent, nameof(BuildEvents_OnBuildProjConfigBegin));
        _onBuildProjConfigDoneHandler = CreateHandler(_onBuildProjConfigDoneEvent, nameof(BuildEvents_OnBuildProjConfigDone));
        _onBuildDoneHandler = CreateHandler(_onBuildDoneEvent, nameof(BuildEvents_OnBuildDone));

        if (_onBuildBeginEvent != null && _onBuildBeginHandler != null)
        {
            _onBuildBeginEvent.AddEventHandler(BuildEvents, _onBuildBeginHandler);
        }

        if (_onBuildProjConfigBeginEvent != null && _onBuildProjConfigBeginHandler != null)
        {
            _onBuildProjConfigBeginEvent.AddEventHandler(BuildEvents, _onBuildProjConfigBeginHandler);
        }

        if (_onBuildProjConfigDoneEvent != null && _onBuildProjConfigDoneHandler != null)
        {
            _onBuildProjConfigDoneEvent.AddEventHandler(BuildEvents, _onBuildProjConfigDoneHandler);
        }

        if (_onBuildDoneEvent != null && _onBuildDoneHandler != null)
        {
            _onBuildDoneEvent.AddEventHandler(BuildEvents, _onBuildDoneHandler);
        }
    }

    /// <summary>
    /// Unregisters event handlers with the IDE.
    /// </summary>

    protected override void UnRegisterListeners()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (_onBuildBeginEvent != null && _onBuildBeginHandler != null)
        {
            _onBuildBeginEvent.RemoveEventHandler(BuildEvents, _onBuildBeginHandler);
        }

        if (_onBuildProjConfigBeginEvent != null && _onBuildProjConfigBeginHandler != null)
        {
            _onBuildProjConfigBeginEvent.RemoveEventHandler(BuildEvents, _onBuildProjConfigBeginHandler);
        }

        if (_onBuildProjConfigDoneEvent != null && _onBuildProjConfigDoneHandler != null)
        {
            _onBuildProjConfigDoneEvent.RemoveEventHandler(BuildEvents, _onBuildProjConfigDoneHandler);
        }

        if (_onBuildDoneEvent != null && _onBuildDoneHandler != null)
        {
            _onBuildDoneEvent.RemoveEventHandler(BuildEvents, _onBuildDoneHandler);
        }

        _onBuildBeginEvent = null;
        _onBuildProjConfigBeginEvent = null;
        _onBuildProjConfigDoneEvent = null;
        _onBuildDoneEvent = null;

        _onBuildBeginHandler = null;
        _onBuildProjConfigBeginHandler = null;
        _onBuildProjConfigDoneHandler = null;
        _onBuildDoneHandler = null;
    }

    private Delegate CreateHandler(EventInfo eventInfo, string methodName)
    {
        if (eventInfo?.EventHandlerType == null)
        {
            return null;
        }

        try
        {
            return Delegate.CreateDelegate(eventInfo.EventHandlerType, this, methodName);
        }
        catch (Exception ex)
        {
            OutputWindowHelper.WarningWriteLine(
                $"Unable to subscribe to Visual Studio build event '{eventInfo.Name}': {ex.Message}");

            return null;
        }
    }

    /// <summary>
    /// Event raised when a build begins.
    /// </summary>
    /// <param name="scope">The scope.</param>
    /// <param name="action">The action.</param>

    private void BuildEvents_OnBuildBegin(vsBuildScope scope, vsBuildAction action)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var buildBegin = BuildBegin;
        if (buildBegin != null)
        {
            OutputWindowHelper.DiagnosticWriteLine("BuildProgressEventListener.BuildBegin raised");

            buildBegin(scope, action);
        }
    }

    /// <summary>
    /// Event raised when a build is done.
    /// </summary>
    /// <param name="scope">The scope.</param>
    /// <param name="action">The action.</param>

    private void BuildEvents_OnBuildDone(vsBuildScope scope, vsBuildAction action)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var buildDone = BuildDone;
        if (buildDone != null)
        {
            OutputWindowHelper.DiagnosticWriteLine("BuildProgressEventListener.BuildDone raised");

            buildDone(scope, action);
        }
    }

    /// <summary>
    /// Event raised when the build of an individual project begins.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="projectConfig">The project config.</param>
    /// <param name="platform">The platform.</param>
    /// <param name="solutionConfig">The solution config.</param>

    private void BuildEvents_OnBuildProjConfigBegin(string project, string projectConfig, string platform, string solutionConfig)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var buildProjConfigBegin = BuildProjConfigBegin;
        if (buildProjConfigBegin != null)
        {
            OutputWindowHelper.DiagnosticWriteLine("BuildProgressEventListener.BuildProjConfigBegin raised");

            buildProjConfigBegin(project, projectConfig, platform, solutionConfig);
        }
    }

    /// <summary>
    /// Event raised when the build of an individual project is done.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="projectConfig">The project config.</param>
    /// <param name="platform">The platform.</param>
    /// <param name="solutionConfig">The solution config.</param>
    /// <param name="success">True if project build was successful, otherwise false.</param>

    private void BuildEvents_OnBuildProjConfigDone(string project, string projectConfig, string platform, string solutionConfig, bool success)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var buildProjConfigDone = BuildProjConfigDone;
        if (buildProjConfigDone != null)
        {
            OutputWindowHelper.DiagnosticWriteLine("BuildProgressEventListener.BuildProjConfigDone raised");

            buildProjConfigDone(project, projectConfig, platform, solutionConfig, success);
        }
    }
}