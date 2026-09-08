using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using CodeJanitor.Helpers;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Differencing;

namespace CodeJanitor.UI.Dialogs.CleanupOptions;

public partial class CleanupPreviewWindow
{
    private CleanupPreviewViewModel _viewModel;
    private CleanupPreviewFile _selectedFile;
    private IWpfDifferenceViewer _differenceViewer;
    private IDifferenceBuffer _differenceBuffer;

    public CleanupPreviewWindow()
    {
        Application.ResourceAssembly = Assembly.GetExecutingAssembly();
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }

        _viewModel = DataContext as CleanupPreviewViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
        }

        SelectFile();
    }

    private void OnViewModelChanged(object sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CleanupPreviewViewModel.SelectedFile))
        {
            SelectFile();
        }
    }

    private void SelectFile()
    {
        if (_selectedFile is not null)
        {
            _selectedFile.PropertyChanged -= OnFileChanged;
        }

        _selectedFile = _viewModel?.SelectedFile;
        if (_selectedFile is not null)
        {
            _selectedFile.PropertyChanged += OnFileChanged;
        }

        UpdateDifferenceView();
    }

    private void OnFileChanged(object sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CleanupPreviewFile.UpdatedSource))
        {
            UpdateDifferenceView();
        }
    }

    private void UpdateDifferenceView()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        CloseDifferenceView();
        TextFallback.Visibility = Visibility.Visible;
        if (_selectedFile?.OriginalSource is null)
        {
            return;
        }

        try
        {
            var componentModel = Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
            var bufferFactory = componentModel?.GetService<ITextBufferFactoryService>();
            var differenceFactory = componentModel?.GetService<IDifferenceBufferFactoryService>();
            var viewerFactory = componentModel?.GetService<IWpfDifferenceViewerFactoryService>();
            if (bufferFactory is null || differenceFactory is null || viewerFactory is null)
            {
                return;
            }

            var before = bufferFactory.CreateTextBuffer(_selectedFile.OriginalSource, bufferFactory.TextContentType);
            var after = bufferFactory.CreateTextBuffer(_selectedFile.UpdatedSource, bufferFactory.TextContentType);
            _differenceBuffer = differenceFactory.CreateDifferenceBuffer(before, after,
                new StringDifferenceOptions(), disableEditing: true);
            _differenceViewer = viewerFactory.CreateDifferenceView(_differenceBuffer);
            _differenceViewer.ViewMode = DifferenceViewMode.SideBySide;
            DifferenceHost.Content = _differenceViewer.VisualElement;
            TextFallback.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            CloseDifferenceView();
            OutputWindowHelper.ExceptionWriteLine("Cleanup preview: native diff unavailable; using text comparison.", exception);
        }
    }

    private void CloseDifferenceView()
    {
        DifferenceHost.Content = null;
        _differenceViewer?.Close();
        _differenceViewer = null;
        _differenceBuffer?.Dispose();
        _differenceBuffer = null;
    }

    private void OnClosed(object sender, EventArgs args)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }

        if (_selectedFile is not null)
        {
            _selectedFile.PropertyChanged -= OnFileChanged;
        }

        CloseDifferenceView();
    }
}
