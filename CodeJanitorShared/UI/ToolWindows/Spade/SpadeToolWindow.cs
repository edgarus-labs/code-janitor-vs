using EnvDTE;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using CodeJanitor.Model;
using CodeJanitor.Model.CodeItems;
using CodeJanitor.Model.CodeTree;
using CodeJanitor.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using CodeModel = CodeJanitor.Model.CodeModel;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.UI.ToolWindows.Spade;

/// <summary>
/// The Spade tool window pane.
/// </summary>

[Guid(PackageGuids.GuidCodeJanitorToolWindowSpadeString)]
public sealed class SpadeToolWindow : ToolWindowPane, IVsWindowFrameNotify3
{
    private readonly SpadeViewModel _viewModel;

    private CodeModelManager _codeModelManager;
    private Document _document;
    private bool _isVisible;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpadeToolWindow" /> class.
    /// </summary>

    public SpadeToolWindow()
        : base(null)
    {
        // Set the tool window caption.
        Caption = Resources.CodeJanitorSpade;

        // Set the tool window image from moniker.
        BitmapImageMoniker = new ImageMoniker
        {
            Guid = PackageGuids.GuidCodeJanitorImageMoniker,
            Id = 2,
        };

        // Create the toolbar for the tool window.
        ToolBar = new CommandID(PackageGuids.GuidCodeJanitorMenuSet, PackageIds.ToolbarIDCodeJanitorToolbarSpade);

        // Setup the associated classes.
        _viewModel = new SpadeViewModel { SortOrder = (CodeSortOrder)Settings.Default.Digging_PrimarySortOrder };

        // Register for view model requests to be refreshed.
        _viewModel.RequestingRefresh += (sender, args) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Refresh();
        };

        // Create and set the view.
        Content = new SpadeView { DataContext = _viewModel };
    }

    /// <summary>
    /// Gets or sets the current document.
    /// </summary>

    public Document Document
    {
        get
        {
            return _document;
        }
        private set
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_document != value)
            {
                _document = value;
                ConditionallyUpdateCodeModel(false);
            }
        }
    }

    /// <summary>
    /// Gets or sets the name filter.
    /// </summary>

    public string NameFilter
    {
        get { return _viewModel.NameFilter; }
        set { _viewModel.NameFilter = value; }
    }

    /// <summary>
    /// Gets or sets the package that owns the tool window.
    /// </summary>
    private new CodeJanitorPackage Package => base.Package as CodeJanitorPackage;

    public override bool SearchEnabled => true;

    /// <summary>
    /// Gets the selected items.
    /// </summary>
    public IEnumerable<BaseCodeItem> SelectedItems => _viewModel.SelectedItems;

    /// <summary>
    /// Gets or sets the sort order.
    /// </summary>

    public CodeSortOrder SortOrder
    {
        get
        {
            return _viewModel.SortOrder;
        }
        set
        {
            if (_viewModel.SortOrder != value)
            {
                Settings.Default.Digging_PrimarySortOrder = (int)value;
                Settings.Default.Save();
            }
        }
    }

    /// <summary>
    /// Gets or sets a flag indicating if this tool window is visible.
    /// </summary>

    private bool IsVisible
    {
        get
        {
            return _isVisible;
        }
        set
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_isVisible != value)
            {
                _isVisible = value;
                ConditionallyUpdateCodeModel(false);
            }
        }
    }

    /// <summary>
    /// Clears the search by setting NameFilter to null, which removes any active name-based filtering; no exceptions are thrown.
    /// </summary>

    public override void ClearSearch()
    {
        NameFilter = null;
    }

    /// <summary>
    /// This method verifies the caller is on the UI thread, then closes the Frame as an IVsWindowFrame using FRAMECLOSE_NoSave, causing the window to close without.
    /// </summary>

    public void Close()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        (Frame as IVsWindowFrame).CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave);
    }

    /// <summary>
    /// Overrides CreateSearch to return a new MemberSearchTask with the given cookie, query, and callback, where the callback sets the NameFilter property to the search result.
    /// </summary>
    /// <param name="dwCookie">The dw cookie.</param>
    /// <param name="pSearchQuery">The p search query.</param>
    /// <param name="pSearchCallback">The p search callback.</param>
    /// <returns>A IVsSearchTask value produced by this method.</returns>

    public override IVsSearchTask CreateSearch(uint dwCookie, IVsSearchQuery pSearchQuery, IVsSearchCallback pSearchCallback)
            => new MemberSearchTask(dwCookie, pSearchQuery, pSearchCallback, x => NameFilter = x);

    /// <summary>
    /// A method to be called to notify the tool window about the current active document.
    /// </summary>
    /// <param name="document">The active document.</param>

    public void NotifyActiveDocument(Document document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        Document = document;
    }

    /// <summary>
    /// A method to be called to notify the tool window that has a document has been saved.
    /// </summary>
    /// <param name="document">The document.</param>

    public void NotifyDocumentSave(Document document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (Document == document)
        {
            // Refresh the document if active.
            Refresh();
        }
    }

    /// <summary>
    /// This method always returns VSConstants.S_OK, indicating success, without modifying the pgrfSaveOptions reference or throwing any exceptions, and has no other side effects.
    /// </summary>
    /// <param name="pgrfSaveOptions">The pgrf save options.</param>
    /// <returns>A int value produced by this method.</returns>

    public int OnClose(ref uint pgrfSaveOptions) => VSConstants.S_OK;

    /// <summary>
    /// Always returns VSConstants.S_OK, ignoring all parameters and performing no work or side effects.
    /// </summary>
    /// <param name="fDockable">The f dockable.</param>
    /// <param name="x">The x.</param>
    /// <param name="y">The y.</param>
    /// <param name="w">The w.</param>
    /// <param name="h">The h.</param>
    /// <returns>A int value produced by this method.</returns>

    public int OnDockableChange(int fDockable, int x, int y, int w, int h) => VSConstants.S_OK;

    /// <summary>
    /// Method always returns VSConstants.S_OK regardless of input parameters, performing no operations, causing no side effects, and throwing no exceptions.
    /// </summary>
    /// <param name="x">The x.</param>
    /// <param name="y">The y.</param>
    /// <param name="w">The w.</param>
    /// <param name="h">The h.</param>
    /// <returns>A int value produced by this method.</returns>

    public int OnMove(int x, int y, int w, int h) => VSConstants.S_OK;

    /// <summary>
    /// Updates the IsVisible property based on the frame show state for shown or hidden events, returns S_OK, and assumes the caller is on the UI thread.
    /// </summary>
    /// <param name="fShow">The f show.</param>
    /// <returns>A int value produced by this method.</returns>

    public int OnShow(int fShow)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // Track the visibility of this tool window.
        switch ((__FRAMESHOW)fShow)
        {
            case __FRAMESHOW.FRAMESHOW_WinShown:
                IsVisible = true;
                break;

            case __FRAMESHOW.FRAMESHOW_WinHidden:
                IsVisible = false;
                break;
        }

        return VSConstants.S_OK;
    }

    /// <summary>
    /// Returns S_OK unconditionally to signal successful handling of the size notification, with no side effects or additional logic.
    /// </summary>
    /// <param name="x">The x.</param>
    /// <param name="y">The y.</param>
    /// <param name="w">The w.</param>
    /// <param name="h">The h.</param>
    /// <returns>A int value produced by this method.</returns>

    public int OnSize(int x, int y, int w, int h) => VSConstants.S_OK;

    /// <summary>
    /// This method can be overriden by the derived class to execute any code that needs to run
    /// after the IVsWindowFrame is created. If the toolwindow has a toolbar with a combobox, it
    /// should make sure its command handler are set by the time they return from this method.
    /// This is called when someone set the Frame property.
    /// </summary>

    public override void OnToolWindowCreated()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        base.OnToolWindowCreated();

        // Register for events to this window.
        ((IVsWindowFrame)Frame).SetProperty((int)__VSFPROPID.VSFPROPID_ViewHelper, this);

        // Package is not available at constructor time.
        if (Package != null)
        {
            // Get an instance of the code model manager.
            _codeModelManager = CodeModelManager.GetInstance(Package);

            Package.JoinableTaskFactory.RunAsync(async () =>
                await Package.SettingsMonitor.WatchAsync(s => s.Feature_SpadeToolWindow, on =>
                {
                    if (on)
                    {
                        _codeModelManager.CodeModelBuilt += OnCodeModelBuilt;

                        // Register for changes to settings.
                        Settings.Default.SettingsLoaded += OnSettingsLoaded;
                        Settings.Default.SettingsSaving += OnSettingsSaving;
                    }
                    else
                    {
                        _codeModelManager.CodeModelBuilt -= OnCodeModelBuilt;

                        Settings.Default.SettingsLoaded -= OnSettingsLoaded;
                        Settings.Default.SettingsSaving -= OnSettingsSaving;
                    }

                    return Task.CompletedTask;
                }));

            // Pass the package over to the view model.
            _viewModel.Package = Package;

            // Attempt to initialize the Document, may have been set before Spade was created.
            if (Document == null)
            {
                Document = Package.ActiveDocument;
            }

            if (Content is FrameworkElement spadeContent)
            {
                _viewModel.Dispatcher = spadeContent.Dispatcher;

                spadeContent.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Package.ThemeManager.ApplyTheme));
            }
        }
    }

    /// <summary>
    /// Overrides search settings by enforcing UI thread execution, delegating to the base implementation, and then setting the search control&apos;s minimum width to 200, maximum width to the maximum unsigned integer value, and watermark text to a localized resource string, thereby mutating the provided search settings data source.
    /// </summary>
    /// <param name="pSearchSettings">The p search settings.</param>

    public override void ProvideSearchSettings(IVsUIDataSource pSearchSettings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        base.ProvideSearchSettings(pSearchSettings);

        Utilities.SetValue(pSearchSettings, SearchSettingsDataSource.PropertyNames.ControlMinWidth, 200U);
        Utilities.SetValue(pSearchSettings, SearchSettingsDataSource.PropertyNames.ControlMaxWidth, uint.MaxValue);
        Utilities.SetValue(pSearchSettings, SearchSettingsDataSource.PropertyNames.SearchWatermark, Resources.SearchCodeJanitorSpadeCtrlM);
    }

    /// <summary>
    /// Refresh the Spade tool window.
    /// </summary>

    public void Refresh()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        Package?.ThemeManager.ApplyTheme();

        ConditionallyUpdateCodeModel(true);
    }

    /// <summary>
    /// Conditionally updates the code model.
    /// </summary>
    /// <param name="isRefresh">True if refreshing a document, otherwise false.</param>

    private void ConditionallyUpdateCodeModel(bool isRefresh)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!IsVisible) return;

        _viewModel.Document = Document;
        _viewModel.IsLoading = false;
        _viewModel.IsRefreshing = false;

        if (Document == null || !isRefresh)
        {
            _viewModel.RawCodeItems = null;
        }

        if (Document != null)
        {
            if (isRefresh)
            {
                _codeModelManager.OnDocumentChanged(Document);
                _viewModel.IsRefreshing = true;
            }
            else
            {
                _viewModel.IsLoading = true;
            }

            var codeItems = _codeModelManager.RetrieveAllCodeItemsAsync(Document, true);
            if (codeItems != null)
            {
                UpdateViewModelRawCodeItems(codeItems);
            }
        }
    }

    /// <summary>
    /// An event handler called when the <see cref="CodeModelManager" /> raises a <see
    /// cref="CodeModelManager.CodeModelBuilt" /> event. If the code model was built for the
    /// document currently being shown by Spade, the raw code items will be processed and displayed.
    /// </summary>
    /// <param name="codeModel">The code model.</param>

    private void OnCodeModelBuilt(CodeModel codeModel)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (Document == codeModel.Document)
        {
            UpdateViewModelRawCodeItems(codeModel.CodeItems);
        }
    }

    /// <summary>
    /// An event handler called when settings are changed.
    /// </summary>

    private void OnSettingsChange()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _viewModel.SortOrder = (CodeSortOrder)Settings.Default.Digging_PrimarySortOrder;
        Refresh();
    }

    /// <summary>
    /// This method enforces execution on the UI thread via ThreadHelper.ThrowIfNotOnUIThread and then triggers the OnSettingsChange callback as a side effect.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The e.</param>

    private void OnSettingsLoaded(object sender, System.Configuration.SettingsLoadedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        OnSettingsChange();
    }

    /// <summary>
    /// This event handler enforces execution on the UI thread via ThreadHelper.ThrowIfNotOnUIThread and then invokes OnSettingsChange to process settings changes.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The e.</param>

    private void OnSettingsSaving(object sender, System.ComponentModel.CancelEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        OnSettingsChange();
    }

    /// <summary>
    /// Update the view model's raw set of code items based on the specified code items.
    /// </summary>
    /// <param name="codeItems">The code items.</param>

    private void UpdateViewModelRawCodeItems(SetCodeItems codeItems)
    {
        // Create a copy of the original collection, filtering out undesired items.
        var filteredCodeItems = new SetCodeItems(
            codeItems.Where(x => !(x is CodeItemUsingStatement || x is CodeItemNamespace)));

        _viewModel.RawCodeItems = filteredCodeItems;
        _viewModel.IsLoading = false;
        _viewModel.IsRefreshing = false;
    }
}
