using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Integration.Commands;
using CodeJanitor.Integration.Events;
using CodeJanitor.Integration.Options;
using CodeJanitor.Model;
using CodeJanitor.Properties;
using CodeJanitor.UI;
using CodeJanitor.UI.ToolWindows.BuildProgress;
using CodeJanitor.UI.ToolWindows.Spade;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Task = System.Threading.Tasks.Task;
using VSColorTheme = Microsoft.VisualStudio.PlatformUI.VSColorTheme;

namespace CodeJanitor;

/// <summary>
/// This is the class that implements the package exposed by this assembly.
/// </summary>
/// <remarks>
/// <para>
/// The minimum requirement for a class to be considered a valid package for Visual Studio is to
/// implement the IVsPackage interface and register itself with the shell. This package uses the
/// helper classes defined inside the Managed Package Framework (MPF) to do it: it derives from
/// the Package class that provides the implementation of the IVsPackage interface and uses the
/// registration attributes defined in the framework to register itself and its components with
/// the shell. These attributes tell the pkgdef creation utility what data to put into .pkgdef file.
/// </para>
/// <para>
/// To get loaded into VS, the package must be referred by &lt;Asset
/// Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
/// </para>
/// </remarks>

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)] // Tells Visual Studio utilities that this is a package that needs registered.
[InstalledProductRegistration("#110", "#112", Vsix.Version, IconResourceID = 400, LanguageIndependentName = "CodeJanitor")] // VS Help/About details (Name, Description, Version, Icon).
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExistsAndFullyLoaded_string, PackageAutoLoadFlags.BackgroundLoad)] // Trigger CodeJanitor to load on solution open so menu items can determine their state.
[ProvideBindingPath]
[ProvideMenuResource("Menus.ctmenu", 1)] // This attribute is needed to let the shell know that this package exposes some menus.
// Native VS Options pages — one per settings section; VS owns the tree navigation.
// Resource IDs (category=113, page=114-133) map to display strings in source.extension.resx.
// General/Cleaning/Reorganizing/Navigation each host their former sibling pages stacked on
// one panel (see CompositeOptionsPageViewModel) instead of separate tree nodes, to keep the
// tree shallow. Digging (Spade settings) stays standalone for now (undecided future).
[ProvideOptionPage(typeof(CodeJanitorGeneralPage), "Code Janitor", "General", 113, 114, true)]
[ProvideOptionPage(typeof(CodeJanitorCleaningParentPage), "Code Janitor", "Cleaning", 113, 116, true)]
[ProvideOptionPage(typeof(CodeJanitorNavigationPage), "Code Janitor", "Navigation", 113, 123, true)]
[ProvideOptionPage(typeof(CodeJanitorDiggingPage), "Code Janitor", "Digging", 113, 124, true)]
[ProvideOptionPage(typeof(CodeJanitorFormattingPage), "Code Janitor", "Formatting", 113, 126, true)]
[ProvideOptionPage(typeof(CodeJanitorProgressingPage), "Code Janitor", "Progressing", 113, 127, true)]
[ProvideOptionPage(typeof(CodeJanitorReorganizingParentPage), "Code Janitor", "Reorganizing", 113, 128, true)]
[ProvideOptionPage(typeof(CodeJanitorXmlDocPage), "Code Janitor", "AI Assistant", 113, 134, true)]
[ProvideOptionPage(typeof(CodeJanitorThirdPartyPage), "Code Janitor", "Third Party", 113, 133, true)]
[ProvideToolWindow(typeof(BuildProgressToolWindow), MultiInstances = false, Height = 40, Width = 500, Style = VsDockStyle.Tabbed, Orientation = ToolWindowOrientation.Bottom, Window = EnvDTE.Constants.vsWindowKindMainWindow)]
[ProvideToolWindow(typeof(SpadeToolWindow), MultiInstances = false, Style = VsDockStyle.Tabbed, Orientation = ToolWindowOrientation.Left, Window = EnvDTE.Constants.vsWindowKindSolutionExplorer)]
[Guid(PackageGuids.GuidCodeJanitorPackageString)] // Package unique GUID.
public sealed class CodeJanitorPackage : AsyncPackage
{
    /// <summary>
    /// The build progress tool window.
    /// </summary>
    private BuildProgressToolWindow _buildProgress;

    /// <summary>
    /// The IComponentModel service.
    /// </summary>
    private IComponentModel _componentModel;

    /// <summary>
    /// The top level application instance of the VS IDE that is executing this package.
    /// </summary>
    private DTE2 _ide;

    /// <summary>
    /// The Spade tool window.
    /// </summary>
    private SpadeToolWindow _spade;

    /// <summary>
    /// The theme manager.
    /// </summary>
    private ThemeManager _themeManager;

    /// <summary>
    /// Fully initialized package instance.
    /// </summary>
    internal static CodeJanitorPackage Instance;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeJanitorPackage"/> class.
    /// </summary>
    /// <remarks>
    /// Inside this method you can place any initialization code that does not require any Visual
    /// Studio service because at this point the package object is created but not sited yet
    /// inside Visual Studio environment. The place to do all the other initialization is the
    /// Initialize method.
    /// </remarks>

    public CodeJanitorPackage()
    {
        Trace.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering constructor for: {0}", this));

        // Make the package available for options page activation paths that can occur
        // before InitializeAsync reaches the main thread.
        Instance = this;

        if (Application.Current is not null)
        {
            Application.Current.DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        // If an existing user settings file cannot be found, perform a one-time settings upgrade.
        if (!File.Exists(SettingsContextHelper.GetUserSettingsPath()))
        {
            Settings.Default.Upgrade();
            Settings.Default.Save();
        }

        if (Settings.Default.MigrateSensitiveSettings())
        {
            Settings.Default.Save();
        }
    }

    /// <summary>
    /// Gets the currently active document, otherwise null.
    /// </summary>

    public Document ActiveDocument
    {
        get
        {
            try
            {
                return IDE.ActiveDocument;
            }
            catch (Exception)
            {
                // If a project property page is active, accessing the ActiveDocument causes an exception.
                return null;
            }
        }
    }

    /// <summary>
    /// Gets the build progress tool window, if it already exists.
    /// </summary>

    public BuildProgressToolWindow BuildProgress =>
        _buildProgress ?? (_buildProgress = (FindToolWindow(typeof(BuildProgressToolWindow), 0, false) as BuildProgressToolWindow));

    /// <summary>
    /// Gets the build progress tool window, creating it if necessary.
    /// </summary>

    public BuildProgressToolWindow BuildProgressForceLoad =>
        _buildProgress ?? (_buildProgress = (FindToolWindow(typeof(BuildProgressToolWindow), 0, true) as BuildProgressToolWindow));

    /// <summary>
    /// Gets the IComponentModel service.
    /// </summary>

    public IComponentModel ComponentModel =>
        _componentModel ?? (_componentModel = GetGlobalService(typeof(SComponentModel)) as IComponentModel);

    /// <summary>
    /// Gets the top level application instance of the VS IDE that is executing this package.
    /// </summary>
    public DTE2 IDE => _ide ?? (_ide = (DTE2)GetService(typeof(DTE)));

    /// <summary>
    /// Gets the version of the running IDE instance.
    /// </summary>
    public double IDEVersion => Convert.ToDouble(IDE.Version, CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets or sets a flag indicating if CodeJanitor is running inside an AutoSave context.
    /// </summary>
    public bool IsAutoSaveContext { get; set; }

    /// <summary>
    /// Gets or sets the settings monitor.
    /// </summary>
    public SettingsMonitor<Settings> SettingsMonitor { get; private set; }

    /// <summary>
    /// Gets the Spade tool window, if it already exists.
    /// </summary>

    public SpadeToolWindow Spade =>
        _spade ?? (_spade = (FindToolWindow(typeof(SpadeToolWindow), 0, false) as SpadeToolWindow));

    /// <summary>
    /// Gets the Spade tool window, creating it if necessary.
    /// </summary>

    public SpadeToolWindow SpadeForceLoad =>
        _spade ?? (_spade = (FindToolWindow(typeof(SpadeToolWindow), 0, true) as SpadeToolWindow));

    /// <summary>
    /// Gets the theme manager.
    /// </summary>
    public ThemeManager ThemeManager => _themeManager ?? (_themeManager = ThemeManager.GetInstance(this));

    /// <summary>
    /// Initialization of the package; this method is called right after the package is sited, so
    /// this is the place where you can put all the initialization code that rely on services
    /// provided by VisualStudio.
    /// </summary>
    /// <param name="cancellationToken">
    /// A cancellation token to monitor for initialization cancellation, which can occur when VS
    /// is shutting down.
    /// </param>
    /// <param name="progress">A provider for progress updates.</param>
    /// <returns>
    /// A task representing the async work of package initialization, or an already completed
    /// task if there is none. Do not return null from this method.
    /// </returns>

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        try
        {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Make the package instance available as early as possible for Options page activation paths.
            Instance = this;

            // Create the CodeJanitor output pane immediately so it's visible in the Output
            // window's "Show output from" list from startup, rather than only appearing
            // lazily the first time something is logged.
            OutputWindowHelper.EnsurePaneCreated();

            SettingsMonitor = new SettingsMonitor<Settings>(Settings.Default, JoinableTaskFactory);

            try
            {
                CSharpLanguageVersionSupport.UseVisualStudioWorkspace(this);
            }
            catch (Exception ex)
            {
                // Without the resolver no cleanup step emits syntax newer than C# 7.3 (each skip is reported).
                OutputWindowHelper.ExceptionWriteLine("CodeJanitor could not use the Roslyn workspace to read C# language versions.", ex);
            }

            await RegisterCommandsAsync();
            await RegisterEventListenersAsync();

        }
        catch (Exception ex)
        {
            try
            {
                var diagnosticPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodeJanitor", "package-init-error.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(diagnosticPath));
                File.WriteAllText(diagnosticPath, ex.ToString());
            }
            catch
            {
            }

            throw;
        }
    }


    /// <summary>
    /// Called when a DispatcherUnhandledException is raised by Visual Studio.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">
    /// The <see cref="DispatcherUnhandledExceptionEventArgs" /> instance containing the event data.
    /// </param>

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (!Settings.Default.General_DiagnosticsMode) return;

        OutputWindowHelper.ExceptionWriteLine("Diagnostics mode caught and marked as handled the following DispatcherUnhandledException raised in Visual Studio", e.Exception);
        e.Handled = true;
    }

    /// <summary>
    /// Called when a solution is closed to conditionally show the start page.
    /// </summary>

    private void OnSolutionClosedShowStartPage()
    {
        if (!Settings.Default.General_ShowStartPageOnSolutionClose) return;

        IDE.ExecuteCommand("View.StartPage");
    }

    /// <summary>
    /// Register the package commands (which must exist in the .vsct file).
    /// </summary>

    private async Task RegisterCommandsAsync()
    {
        await AboutCommand.InitializeAsync(this);
        await AddXmlDocCommand.InitializeAsync(this);
        await RemoveXmlDocCommand.InitializeAsync(this);
        await AiExplainCommand.InitializeAsync(this);
        await AiGenerateUnitTestsCommand.InitializeAsync(this);
        await AiCleanRefactorCommand.InitializeAsync(this);
        await AiCodeReviewCommand.InitializeAsync(this);
        await AiTargetCoverageCommand.InitializeAsync(this);
        await BuildProgressToolWindowCommand.InitializeAsync(this);
        await CleanupActiveCodeCommand.InitializeAsync(this);
        await CleanupAllCodeCommand.InitializeAsync(this);
        await CleanupChangedFilesCommand.InitializeAsync(this);
        await CleanupOpenCodeCommand.InitializeAsync(this);
        await CleanupSelectedCodeCommand.InitializeAsync(this);
        await CloseAllReadOnlyCommand.InitializeAsync(this);
        await CollapseAllSolutionExplorerCommand.InitializeAsync(this);
        await CollapseSelectedSolutionExplorerCommand.InitializeAsync(this);
        await CommentFormatCommand.InitializeAsync(this);
        await FixNamespaceCommand.InitializeAsync(this);
        await FindInSolutionExplorerCommand.InitializeAsync(this);
        await JoinLinesCommand.InitializeAsync(this);
        await ExportRepositorySettingsCommand.InitializeAsync(this);
        await ImportRepositorySettingsCommand.InitializeAsync(this);
        await OptionsCommand.InitializeAsync(this);
        await ReadOnlyToggleCommand.InitializeAsync(this);
        await RemoveRegionCommand.InitializeAsync(this);
        await ReorganizeActiveCodeCommand.InitializeAsync(this);
        await SettingCleanupOnSaveCommand.InitializeAsync(this);
        await SortLinesCommand.InitializeAsync(this);
        await SpadeContextDeleteCommand.InitializeAsync(this);
        await SpadeContextFindReferencesCommand.InitializeAsync(this);
        await SpadeContextInsertRegionCommand.InitializeAsync(this);
        await SpadeContextRemoveRegionCommand.InitializeAsync(this);
        await SpadeOptionsCommand.InitializeAsync(this);
        await SpadeRefreshCommand.InitializeAsync(this);
        await SpadeSearchCommand.InitializeAsync(this);
        await SpadeSortOrderAlphaCommand.InitializeAsync(this);
        await SpadeSortOrderFileCommand.InitializeAsync(this);
        await SpadeSortOrderTypeCommand.InitializeAsync(this);
        await SpadeToolWindowCommand.InitializeAsync(this);
        await SwitchFileCommand.InitializeAsync(this);
    }

    /// <summary>
    /// Register the package event listeners.
    /// </summary>
    /// <remarks>
    /// Every event listener registers VS events by itself.
    /// </remarks>

    private async Task RegisterEventListenersAsync()
    {
        var codeModelManager = CodeModelManager.GetInstance(this);
        var settingsContextHelper = SettingsContextHelper.GetInstance(this);

        VSColorTheme.ThemeChanged += _ => ThemeManager.ApplyTheme();

        await BuildProgressEventListener.InitializeAsync(this);
        BuildProgressEventListener.Instance.BuildBegin += BuildProgressToolWindowCommand.Instance.OnBuildBegin;
        BuildProgressEventListener.Instance.BuildProjConfigBegin += BuildProgressToolWindowCommand.Instance.OnBuildProjConfigBegin;
        BuildProgressEventListener.Instance.BuildProjConfigDone += BuildProgressToolWindowCommand.Instance.OnBuildProjConfigDone;
        BuildProgressEventListener.Instance.BuildDone += BuildProgressToolWindowCommand.Instance.OnBuildDone;

        await DocumentEventListener.InitializeAsync(this);
        DocumentEventListener.Instance.OnDocumentClosing += codeModelManager.OnDocumentClosing;

        await RunningDocumentTableEventListener.InitializeAsync(this);
        await SettingsMonitor.WatchAsync(s => s.Feature_SettingCleanupOnSave, on =>
        {
            if (on)
            {
                RunningDocumentTableEventListener.Instance.BeforeSave += CleanupActiveCodeCommand.Instance.OnBeforeDocumentSave;
            }
            else
            {
                RunningDocumentTableEventListener.Instance.BeforeSave -= CleanupActiveCodeCommand.Instance.OnBeforeDocumentSave;
            }

            return Task.CompletedTask;
        });
        await SettingsMonitor.WatchAsync(s => s.Feature_SpadeToolWindow, on =>
        {
            if (on)
            {
                RunningDocumentTableEventListener.Instance.AfterSave += SpadeToolWindowCommand.Instance.OnAfterDocumentSave;
            }
            else
            {
                RunningDocumentTableEventListener.Instance.AfterSave -= SpadeToolWindowCommand.Instance.OnAfterDocumentSave;
            }

            return Task.CompletedTask;
        });

        await SolutionEventListener.InitializeAsync(this);
        await SettingsMonitor.WatchAsync(s => s.Feature_CollapseAllSolutionExplorer, on =>
        {
            if (on)
            {
                SolutionEventListener.Instance.OnSolutionOpened += CollapseAllSolutionExplorerCommand.Instance.OnSolutionOpened;
            }
            else
            {
                SolutionEventListener.Instance.OnSolutionOpened -= CollapseAllSolutionExplorerCommand.Instance.OnSolutionOpened;
            }

            return Task.CompletedTask;
        });
        SolutionEventListener.Instance.OnSolutionOpened += settingsContextHelper.OnSolutionOpened;
        SolutionEventListener.Instance.OnSolutionClosed += settingsContextHelper.OnSolutionClosed;
        SolutionEventListener.Instance.OnSolutionClosed += OnSolutionClosedShowStartPage;

        await TextEditorEventListener.InitializeAsync(this);
        TextEditorEventListener.Instance.OnLineChanged += codeModelManager.OnDocumentChanged;

        await WindowEventListener.InitializeAsync(this);
        WindowEventListener.Instance.OnWindowChange += SpadeToolWindowCommand.Instance.OnWindowChange;

        // Check if a solution has already been opened before CodeJanitor was initialized.
        if (IDE.Solution?.IsOpen == true)
        {
            SolutionEventListener.Instance.FireSolutionOpenedEvent();
        }
    }
}
