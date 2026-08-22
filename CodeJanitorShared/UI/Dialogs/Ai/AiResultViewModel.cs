using CodeJanitor.UI;
using System;
using System.Windows;

namespace CodeJanitor.UI.Dialogs.Ai;

/// <summary>
/// The view model for the interactive AI Result Window.
/// </summary>
public class AiResultViewModel : Bindable
{
    private readonly Action<string> _onApply;
    private DelegateCommand _copyToClipboardCommand;
    private DelegateCommand _applyCommand;
    private DelegateCommand _closeCommand;

    public AiResultViewModel(
        string title,
        string header,
        string contentText,
        string codeSnippet = null,
        Action<string> onApply = null,
        string applyButtonText = "Apply")
    {
        Title = title ?? "CodeJanitor AI Assistant";
        Header = header ?? string.Empty;
        ContentText = contentText ?? string.Empty;
        CodeSnippet = codeSnippet ?? string.Empty;
        HasCodeSnippet = !string.IsNullOrWhiteSpace(CodeSnippet);
        _onApply = onApply;
        CanApply = onApply != null && (!string.IsNullOrWhiteSpace(CodeSnippet) || !string.IsNullOrWhiteSpace(ContentText));
        ApplyButtonText = applyButtonText;
        StatusMessage = "Ready";
    }

    public string Title
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    public string Header
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    public string ContentText
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    public string CodeSnippet
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    public bool HasCodeSnippet
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    public bool CanApply
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    public string ApplyButtonText
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    public string StatusMessage
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    public DelegateCommand CopyToClipboardCommand => _copyToClipboardCommand
        ?? (_copyToClipboardCommand = new DelegateCommand(OnCopyToClipboard));

    public DelegateCommand ApplyCommand => _applyCommand
        ?? (_applyCommand = new DelegateCommand(OnApply, _ => CanApply));

    public DelegateCommand CloseCommand => _closeCommand
        ?? (_closeCommand = new DelegateCommand(OnClose));

    public event EventHandler RequestClose;

    private void OnCopyToClipboard(object parameter)
    {
        try
        {
            var textToCopy = HasCodeSnippet && !string.IsNullOrWhiteSpace(CodeSnippet)
                ? CodeSnippet
                : ContentText;

            Clipboard.SetText(textToCopy);
            StatusMessage = "Copied to clipboard!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to copy: {ex.Message}";
        }
    }

    private void OnApply(object parameter)
    {
        try
        {
            var textToApply = HasCodeSnippet && !string.IsNullOrWhiteSpace(CodeSnippet)
                ? CodeSnippet
                : ContentText;

            _onApply?.Invoke(textToApply);
            StatusMessage = "Applied successfully!";
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Apply failed: {ex.Message}";
        }
    }

    private void OnClose(object parameter)
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
