using CodeJanitor.UI;
using System;
using System.Windows;

namespace CodeJanitor.UI.Dialogs.Ai;

/// <summary>
/// The view model for the interactive AI Result Window.
/// </summary>
public sealed class AiResultViewModel : Bindable
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
        CanApply = onApply is not null && (!string.IsNullOrWhiteSpace(CodeSnippet) || !string.IsNullOrWhiteSpace(ContentText));
        ApplyButtonText = applyButtonText;
        StatusMessage = "Ready";
    }

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the header.
    /// </summary>
    public string Header
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the content text.
    /// </summary>
    public string ContentText
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the code snippet.
    /// </summary>
    public string CodeSnippet
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the has code snippet.
    /// </summary>
    public bool HasCodeSnippet
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the can apply.
    /// </summary>
    public bool CanApply
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the apply button text.
    /// </summary>
    public string ApplyButtonText
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    public string StatusMessage
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets the copy to clipboard command.
    /// </summary>
    public DelegateCommand CopyToClipboardCommand => _copyToClipboardCommand
        ?? (_copyToClipboardCommand = new DelegateCommand(OnCopyToClipboard));

    /// <summary>
    /// Gets the apply command.
    /// </summary>
    public DelegateCommand ApplyCommand => _applyCommand
        ?? (_applyCommand = new DelegateCommand(OnApply, _ => CanApply));

    /// <summary>
    /// Gets the close command.
    /// </summary>
    public DelegateCommand CloseCommand => _closeCommand
        ?? (_closeCommand = new DelegateCommand(OnClose));

    /// <summary>
    /// Occurs when request close.
    /// </summary>
    public event EventHandler RequestClose;

    /// <summary>
    /// Copies either the code snippet (if available and non-empty) or the content text to the system clipboard, updating StatusMessage to indicate success or failure.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
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

    /// <summary>
    /// Apply attempts to invoke the _onApply callback with either the configured code snippet or content text, sets a success status message and requests closure of the dialog, or captures any exception into an error status message.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
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

    /// <summary>
    /// the RequestClose event with the current instance as sender to signal a close request.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
    private void OnClose(object parameter)
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
