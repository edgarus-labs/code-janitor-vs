using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Transformations;
using System;
using System.IO;
using System.Text;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// Applies a namespace fix to the active C# document.
/// </summary>

internal sealed class NamespaceFixerLogic
{
    private readonly NamespaceFixerConverter _converter;

    private static NamespaceFixerLogic _instance;

    /// <summary>
    /// Lazily creates and caches a singleton NamespaceFixerLogic instance using the provided package, returning the existing instance on subsequent calls.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>A NamespaceFixerLogic value produced by this method.</returns>

    internal static NamespaceFixerLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new NamespaceFixerLogic(package));
    }

    private NamespaceFixerLogic(CodeJanitorPackage package)
    {
        _converter = new NamespaceFixerConverter();
    }

    /// <summary>
    /// Returns true only for a non-null, physical, .cs project item with a non-empty file path outside excluded directories, throwing ThreadHelper.ThrowIfNotOnUIThread on non.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>A bool value produced by this method.</returns>

    internal bool CanFixNamespaceProjectItem(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (projectItem == null || !projectItem.IsPhysicalFile())
        {
            return false;
        }

        if (!string.Equals(Path.GetExtension(projectItem.Name), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var filePath = projectItem.GetFileName();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        if (NamespacePathHelper.IsInExcludedDirectory(filePath))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Attempts to fix a project item&apos;s namespace to its expected value, returning false if impossible or unchanged, and otherwise updating the open document or rewriting the file&apos;s encoding-preserved content while logging the change.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>A bool value produced by this method.</returns>

    internal bool FixNamespace(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!CanFixNamespaceProjectItem(projectItem))
        {
            return false;
        }

        var expectedNamespace = NamespacePathHelper.GetExpectedNamespace(projectItem);
        if (string.IsNullOrWhiteSpace(expectedNamespace))
        {
            return false;
        }

        var document = projectItem.Document;
        if (document != null)
        {
            return FixNamespace(document, expectedNamespace);
        }

        var filePath = projectItem.GetFileName();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        string originalText;
        Encoding encoding;

        using (var reader = new StreamReader(filePath, true))
        {
            originalText = reader.ReadToEnd();
            encoding = reader.CurrentEncoding;
        }

        var updatedText = _converter.FixNamespace(originalText, expectedNamespace);
        if (updatedText == originalText)
        {
            return false;
        }

        File.WriteAllText(filePath, updatedText, encoding);
        OutputWindowHelper.InfoWriteLine($"NamespaceFixerLogic.FixNamespace updated '{filePath}' to namespace '{expectedNamespace}'.");

        return true;
    }

    /// <summary>
    /// Ensures the call is on the UI thread by throwing if not, then delegates to the overloaded FixNamespace with a null second argument and returns its result.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>A bool value produced by this method.</returns>

    internal bool FixNamespace(Document document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        return FixNamespace(document, null);
    }

    /// <summary>
    /// This method verifies prerequisites and, if the expected namespace differs, rewrites the document&apos;s entire text to correct its namespace, logging each skip or update and returning true only when the document is changed.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="expectedNamespace">The expected namespace.</param>
    /// <returns>A bool value produced by this method.</returns>

    private bool FixNamespace(Document document, string expectedNamespace)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (document == null)
        {
            return false;
        }

        var projectItem = document.ProjectItem;
        if (projectItem == null)
        {
            OutputWindowHelper.InfoWriteLine($"NamespaceFixerLogic.FixNamespace skipped for '{document.FullName}' because it is not part of a project item.");

            return false;
        }

        if (!CanFixNamespaceProjectItem(projectItem))
        {
            return false;
        }

        expectedNamespace = expectedNamespace ?? NamespacePathHelper.GetExpectedNamespace(projectItem);
        if (string.IsNullOrWhiteSpace(expectedNamespace))
        {
            OutputWindowHelper.InfoWriteLine($"NamespaceFixerLogic.FixNamespace skipped for '{document.FullName}' because the expected namespace could not be determined.");

            return false;
        }

        var textDocument = document.GetTextDocument();
        if (textDocument == null)
        {
            OutputWindowHelper.InfoWriteLine($"NamespaceFixerLogic.FixNamespace skipped for '{document.FullName}' because no text document was available.");

            return false;
        }

        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var originalText = startPoint.GetText(textDocument.EndPoint);
        var updatedText = _converter.FixNamespace(originalText, expectedNamespace);

        if (updatedText == originalText)
        {
            OutputWindowHelper.InfoWriteLine($"NamespaceFixerLogic.FixNamespace made no change for '{document.FullName}'.");

            return false;
        }

        var endPoint = textDocument.EndPoint.CreateEditPoint();
        startPoint.ReplaceText(endPoint, updatedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);

        OutputWindowHelper.InfoWriteLine($"NamespaceFixerLogic.FixNamespace updated '{document.FullName}' to namespace '{expectedNamespace}'.");

        return true;
    }
}
