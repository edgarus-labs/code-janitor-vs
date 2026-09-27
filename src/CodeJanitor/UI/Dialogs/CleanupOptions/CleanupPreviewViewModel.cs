using System.Collections.Generic;
using System.Linq;

namespace CodeJanitor.UI.Dialogs.CleanupOptions;

internal sealed class CleanupPreviewViewModel : Bindable
{
    internal CleanupPreviewViewModel(IReadOnlyList<CleanupPreviewFile> files)
    {
        Files = files;
        SelectedFile = files.FirstOrDefault();
        ApplyCommand = new DelegateCommand(_ => DialogResult = true,
            _ => Files.Any(file => file.Include && file.CanApply));
        CancelCommand = new DelegateCommand(_ => DialogResult = false);
        foreach (var file in files)
        {
            file.PropertyChanged += (_, __) => ApplyCommand.RaiseCanExecuteChanged();
        }
    }

    public IReadOnlyList<CleanupPreviewFile> Files { get; }

    public CleanupPreviewFile SelectedFile
    {
        get => GetPropertyValue<CleanupPreviewFile>();
        set => SetPropertyValue(value);
    }

    public bool? DialogResult
    {
        get => GetPropertyValue<bool?>();
        set => SetPropertyValue(value);
    }

    public DelegateCommand ApplyCommand { get; }

    public DelegateCommand CancelCommand { get; }
}
