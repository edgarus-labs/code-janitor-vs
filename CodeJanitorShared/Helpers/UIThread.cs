using Microsoft.VisualStudio.Shell;
using System;

namespace CodeJanitor.Helpers;

/// <summary>
/// UIThread represents a dispatcher for executing actions on a user interface thread.
/// </summary>
internal static class UIThread
{
    /// <summary>
    /// Executes the provided action synchronously on the main thread, running it directly if already on the main thread or blocking the calling thread while marshaling to the main thread via JoinableTaskFactory otherwise, with any action exception propagating to the caller.
    /// </summary>
    /// <param name="action">The action.</param>

    public static void Run(Action action)
    {
        if (ThreadHelper.CheckAccess())
        {
            action();
        }
        else
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                action();
            });
        }
    }

    /// <summary>
    /// Executes the provided function on the main thread, invoking it directly if already on the main thread, otherwise switching to the main thread via JoinableTaskFactory and blocking until completion.
    /// </summary>
    /// <param name="func">The func.</param>
    /// <returns>A T value produced by this method.</returns>

    public static T Run<T>(Func<T> func)
    {
        if (ThreadHelper.CheckAccess())
        {
            return func();
        }

        return ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            return func();
        });
    }
}
