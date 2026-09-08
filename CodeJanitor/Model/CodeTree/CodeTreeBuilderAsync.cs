using CodeJanitor.Model.CodeItems;
using System;
using System.ComponentModel;

namespace CodeJanitor.Model.CodeTree;

/// <summary>
/// A helper class for performing code tree building in an asynchronous context.
/// </summary>

internal sealed class CodeTreeBuilderAsync
{
    private readonly BackgroundWorker _bw;
    private readonly Action<SnapshotCodeItems> _callback;
    private CodeTreeRequest _pendingRequest;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeTreeBuilderAsync" /> class.
    /// </summary>
    /// <param name="callback">The callback for results.</param>

    internal CodeTreeBuilderAsync(Action<SnapshotCodeItems> callback)
    {
        _bw = new BackgroundWorker { WorkerSupportsCancellation = true };
        _bw.DoWork += OnDoWork;
        _bw.RunWorkerCompleted += OnRunWorkerCompleted;

        _callback = callback;
    }

    /// <summary>
    /// Builds a code tree asynchronously from the specified request.
    /// </summary>
    /// <param name="request">The request.</param>

    internal void RetrieveCodeTreeAsync(CodeTreeRequest request)
    {
        if (_bw.IsBusy)
        {
            _pendingRequest = request;
            _bw.CancelAsync();
        }
        else
        {
            _pendingRequest = null;
            _bw.RunWorkerAsync(request);
        }
    }

    /// <summary>
    /// Called when the background worker should perform its work.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">
    /// The <see cref="System.ComponentModel.DoWorkEventArgs" /> instance containing the event data.
    /// </param>

    private static void OnDoWork(object sender, DoWorkEventArgs e)
    {
        if (!(e.Argument is CodeTreeRequest request) || request.RawCodeItems is null)
        {
            return;
        }

        var codeItems = CodeTreeBuilder.RetrieveCodeTree(request);

        if (!e.Cancel)
        {
            e.Result = new SnapshotCodeItems(request.Document, codeItems);
        }
    }

    /// <summary>
    /// Called when the background worker has completed.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">
    /// The <see cref="System.ComponentModel.RunWorkerCompletedEventArgs" /> instance containing
    /// the event data.
    /// </param>

    private void OnRunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
    {
        if (_pendingRequest is not null)
        {
            RetrieveCodeTreeAsync(_pendingRequest);
        }
        else if (e.Error is null)
        {
            if (e.Result is SnapshotCodeItems snapshot)
            {
                _callback(snapshot);
            }
        }
    }
}
