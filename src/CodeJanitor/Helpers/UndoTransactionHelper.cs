using CodeJanitor.Properties;
using Microsoft.VisualStudio.Shell;
using System;

namespace CodeJanitor.Helpers;

/// <summary>
/// A helper class for performing actions within the context of an undo transaction.
/// </summary>
public sealed class UndoTransactionHelper : IDisposable
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
    }

    /// <summary>
    /// Creates a helper whose undo transaction is open until the helper is disposed.
    /// </summary>
    /// <param name="package">The hosting package.</param>
    /// <param name="transactionName">The name of the transaction.</param>
    /// <returns>The helper, to be disposed when the transaction ends.</returns>
    public static UndoTransactionHelper Begin(CodeJanitorPackage package, string transactionName)
    {
        var helper = new UndoTransactionHelper(package, transactionName);
        helper.Open();

        return helper;
    }

    /// <summary>
    /// Runs the specified try action within a try block, and conditionally the catch action
    /// within a catch block all conditionally within the context of an undo transaction.
    /// </summary>
    /// <param name="tryAction">The action to be performed within a try block.</param>
    /// <param name="catchAction">The action to be performed within a catch block.</param>
    /// <exception cref="ObjectDisposedException">The helper has already been run or disposed.</exception>
    public void Run(Action tryAction, Action<Exception> catchAction = null)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Open();

        try
        {
            tryAction();
        }
        catch (Exception ex)
        {
            var message = $"{_transactionName}{Resources.WasStopped}";
            OutputWindowHelper.ExceptionWriteLine(message, ex);
            if (_package?.IDE?.StatusBar is not null)
            {
                _package.IDE.StatusBar.Text = $"{message}{Resources.SeeOutputWindowForMoreDetails}";
            }

            catchAction?.Invoke(ex);

            if (_shouldCloseUndoContext && _package?.IDE?.UndoContext is not null)
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
            if (_shouldCloseUndoContext && _package?.IDE?.UndoContext is not null)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                _package.IDE.UndoContext.Close();
                _shouldCloseUndoContext = false;
            }
        }
    }

    /// <summary>
    /// Opens the undo transaction unless undo transactions are disabled or another one is already open.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The helper has already been run or disposed.</exception>
    private void Open()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(UndoTransactionHelper));
        }

        if (_package is not null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Settings.Default.General_UseUndoTransactions && !_package.IDE.UndoContext.IsOpen &&
                !(_package.IsAutoSaveContext && Settings.Default.General_SkipUndoTransactionsDuringAutoCleanupOnSave))
            {
                _package.IDE.UndoContext.Open(_transactionName);
                _shouldCloseUndoContext = true;
            }
        }
    }
}
