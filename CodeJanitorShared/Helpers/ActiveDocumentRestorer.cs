using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;

namespace CodeJanitor.Helpers;

/// <summary>
/// A class that handles tracking a document and switching back to it, typically in a using
/// statement context.
/// </summary>

internal sealed class ActiveDocumentRestorer : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActiveDocumentRestorer" /> class.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    internal ActiveDocumentRestorer(CodeJanitorPackage package)
    {
        Package = package;

        StartTracking();
    }

    /// <summary>
    /// Starts tracking the active document.
    /// </summary>

    internal void StartTracking()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        // Cache the active document.
        TrackedDocument = Package.ActiveDocument;
    }

    /// <summary>
    /// Restores the tracked document if not already active.
    /// </summary>

    internal void RestoreTrackedDocument()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (TrackedDocument != null && Package.ActiveDocument != TrackedDocument)
        {
            TrackedDocument.Activate();
        }
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting
    /// unmanaged resources.
    /// </summary>

    public void Dispose()
    {
        RestoreTrackedDocument();
    }

    /// <summary>
    /// Gets or sets the hosting package.
    /// </summary>
    private CodeJanitorPackage Package { get; set; }

    /// <summary>
    /// Gets or sets the active document.
    /// </summary>
    private Document TrackedDocument { get; set; }
}
