using Microsoft.VisualStudio.Shell;
using CodeJanitor.Properties;
using System;

namespace CodeJanitor.Helpers;

/// <summary>
/// A helper class for performing actions within the context of an undo transaction.
/// </summary>
public class UndoTransactionHelper : IDisposable
{
    private readonly CodeJanitorPackage _package;
    private readonly string _transactionName;
    private bool _shouldCloseUndoContext;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="UndoTransactionHelper" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <param name="transactionName">The name of the transaction.</param>
    public UndoTransactionHelper(CodeJanitorPackage package, string transactionName)
    {
        _package = package;
        _transactionName = transactionName;

        if (package != null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Settings.Default.General_UseUndoTransactions && !package.IDE.UndoContext.IsOpen &&
                !(package.IsAutoSaveContext && Settings.Default.General_SkipUndoTransactionsDuringAutoCleanupOnSave))
            {
                package.IDE.UndoContext.Open(transactionName);
                _shouldCloseUndoContext = true;
            }
        }
    }

    /// <summary>
    /// Runs the specified try action within a try block, and conditionally the catch action
    /// within a catch block all conditionally within the context of an undo transaction.
    /// </summary>
    /// <param name="tryAction">The action to be performed within a try block.</param>
    /// <param name="catchAction">The action to be performed within a catch block.</param>
    public void Run(Action tryAction, Action<Exception> catchAction = null)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            tryAction();
        }
        catch (Exception ex)
        {
            var message = $"{_transactionName}{Resources.WasStopped}";
            OutputWindowHelper.ExceptionWriteLine(message, ex);
            if (_package?.IDE?.StatusBar != null)
            {
                _package.IDE.StatusBar.Text = $"{message}{Resources.SeeOutputWindowForMoreDetails}";
            }

            catchAction?.Invoke(ex);

            if (_shouldCloseUndoContext && _package?.IDE?.UndoContext != null)
            {
                _package.IDE.UndoContext.SetAborted();
                _shouldCloseUndoContext = false;
            }
        }
        finally
        {
            Dispose();
        }
    }

    /// <summary>
    /// Closes the undo transaction.
    /// </summary>
    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            if (_shouldCloseUndoContext && _package?.IDE?.UndoContext != null)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                _package.IDE.UndoContext.Close();
                _shouldCloseUndoContext = false;
            }
        }
    }
}
