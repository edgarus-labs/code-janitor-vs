using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Transformations;
using System;
using System.IO;
using System.Text;

namespace CodeJanitor.Logic.Cleaning
{
    /// <summary>
    /// Applies a namespace fix to the active C# document.
    /// </summary>
    internal class NamespaceFixerLogic
    {
        private readonly NamespaceFixerConverter _converter;

        private static NamespaceFixerLogic _instance;

        internal static NamespaceFixerLogic GetInstance(CodeJanitorPackage package)
        {
            return _instance ?? (_instance = new NamespaceFixerLogic(package));
        }

        private NamespaceFixerLogic(CodeJanitorPackage package)
        {
            _converter = new NamespaceFixerConverter();
        }

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

        internal bool FixNamespace(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            return FixNamespace(document, null);
        }

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
}