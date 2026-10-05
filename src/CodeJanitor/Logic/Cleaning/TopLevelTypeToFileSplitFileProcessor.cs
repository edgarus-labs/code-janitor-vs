using CodeJanitor.Helpers;
using CodeJanitor.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A file processor that splits top-level types into individual files by applying a planned mapping and writing each resulting file atomically.
/// </summary>
internal sealed class TopLevelTypeToFileSplitFileProcessor
{
    private static readonly Regex UniqueSuffix = new Regex(@"~\d+$", RegexOptions.Compiled);

    /// <summary>
    /// Represents the outcome of an apply operation, capturing whether a change occurred, the updated source content, any newly created files, and the reason for skipping if no changes were made.
    /// </summary>
    internal sealed class ApplyResult
    {
        internal ApplyResult(
            bool changed,
            string updatedSource,
            IReadOnlyList<string> createdFiles,
            TopLevelTypeSplitSkipReason skipReason)
        {
            Changed = changed;
            UpdatedSource = updatedSource;
            CreatedFiles = createdFiles;
            SkipReason = skipReason;
        }

        /// <summary>
        /// Gets the changed.
        /// </summary>
        internal bool Changed { get; }

        /// <summary>
        /// Gets the updated source.
        /// </summary>
        internal string UpdatedSource { get; }

        /// <summary>
        /// Gets the created files.
        /// </summary>
        internal IReadOnlyList<string> CreatedFiles { get; }

        /// <summary>
        /// Gets the skip reason.
        /// </summary>
        internal TopLevelTypeSplitSkipReason SkipReason { get; }
    }

    private readonly TopLevelTypeToFileSplitPlanner _planner;
    private readonly Action<string> _reportWarning;

    /// <summary>
    /// Initializes a new instance of the <see cref="TopLevelTypeToFileSplitFileProcessor" /> class.
    /// </summary>
    /// <param name="planner">The planner; a new one when null.</param>
    /// <param name="reportWarning">Receives the warnings of <see cref="Apply" />; the output pane when null.</param>
    internal TopLevelTypeToFileSplitFileProcessor(TopLevelTypeToFileSplitPlanner planner = null, Action<string> reportWarning = null)
    {
        _planner = planner ?? new TopLevelTypeToFileSplitPlanner();
        _reportWarning = reportWarning ?? OutputWindowHelper.WarningWriteLine;
    }

    /// <summary>
    /// We need to analyze the C# method and produce exactly one concise summary sentence, plain text only, no XML, no quotes. Mention key behavior and side effects. The method: internal ApplyResult Apply(string source, string filePath, Encoding encoding, Func&lt;string, string, string&gt; transformSource, bool transformUpdatedSource = true) Body: creates a split plan from planner. If no changes, returns ApplyResult(false, source, empty array, skipReason). Otherwise, for each plannedFile in splitPlan.NewFiles, transformSource is applied if not null, then WriteAllTextAtomically to filePath with transformed source and encoding, adds to createdFiles. Then updatedSource is transformed if transformSource != null and transformUpdatedSource, else splitPlan.UpdatedSource. Returns ApplyResult(true, updatedSource, createdFiles, TopLevelTypeSplitSkipReason.None). Key behaviors: applies a transformation to each new file&apos;s content before writing atomically; writes files atomically; returns result with updated source; side effect is writing files to disk. Need one sentence. Mention key behavior and side effects. Example: &quot;This method creates a split plan, writes new files atomically with optional source transformation, returns an ApplyResult with updated source and created file list, or reports.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="filePath">The file path.</param>
    /// <param name="encoding">The encoding.</param>
    /// <param name="transformSource">The transform source.</param>
    /// <param name="transformUpdatedSource">The transform updated source.</param>
    /// <returns>A ApplyResult value produced by this method.</returns>
    internal ApplyResult Apply(
        string source,
        string filePath,
        Encoding encoding,
        Func<string, string, string> transformSource,
        bool transformUpdatedSource = true,
        Func<string, string, string> transformCreatedFile = null)
    {
        var splitPlan = _planner.CreatePlan(source, filePath);
        if (!splitPlan.HasChanges)
        {
            return new ApplyResult(false, source, Array.Empty<string>(), splitPlan.SkipReason);
        }

        var createdFileTransform = transformCreatedFile ?? transformSource;
        var createdFiles = new List<string>();
        try
        {
            foreach (var plannedFile in splitPlan.NewFiles)
            {
                var transformedSource = createdFileTransform is not null
                    ? createdFileTransform(plannedFile.Content, plannedFile.FilePath)
                    : plannedFile.Content;

                createdFiles.Add(WriteAllTextAtomically(plannedFile.FilePath, transformedSource, encoding));
            }

            var updatedSource = transformSource is not null && transformUpdatedSource
                ? transformSource(splitPlan.UpdatedSource, filePath)
                : splitPlan.UpdatedSource;

            return new ApplyResult(true, updatedSource, createdFiles, TopLevelTypeSplitSkipReason.None);
        }
        catch
        {
            var notDeletedFiles = DeleteCreatedFiles(createdFiles);
            if (notDeletedFiles.Count > 0)
            {
                _reportWarning(FormatNotDeletedFilesWarning(filePath, notDeletedFiles));
            }

            throw;
        }
    }

    /// <summary>
    /// Best-effort removal of the files this processor created, so that the original source (which still contains
    /// the moved types) is not left with duplicate declarations in sibling files. Used when a later write failed, and
    /// by callers that fail to persist the updated original source after <see cref="Apply" /> returned.
    /// Only the given paths (those returned by the writer) are deleted. A file that cannot be deleted does not throw, so
    /// that the original exception surfaces; it is returned instead, for the caller to report.
    /// </summary>
    /// <param name="createdFiles">The files created so far.</param>
    /// <returns>The files that could not be deleted.</returns>
    internal static IReadOnlyList<string> DeleteCreatedFiles(IEnumerable<string> createdFiles)
    {
        var notDeleted = new List<string>();
        foreach (var createdFile in createdFiles)
        {
            try
            {
                File.Delete(createdFile);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                notDeleted.Add(createdFile);
            }
        }

        return notDeleted;
    }

    /// <summary>
    /// Formats the warning that files created by the split could not be deleted and duplicate types of the original.
    /// </summary>
    /// <param name="filePath">The path of the split file.</param>
    /// <param name="notDeletedFiles">The created files that could not be deleted.</param>
    /// <returns>The warning.</returns>
    internal static string FormatNotDeletedFilesWarning(string filePath, IEnumerable<string> notDeletedFiles) => $"These file(s) created by the top-level type split could not be removed and duplicate types of '{filePath}'; delete them: {string.Join(", ", notDeletedFiles)}";

    /// <summary>
    /// Writes content to a new file atomically by creating missing directories, writing to a unique temporary file in
    /// the same location and moving it into place without overwriting anything: when a file with the target name has
    /// appeared since planning, it is kept and the next free name (<c>Name~1.cs</c>, <c>Name~2.cs</c>, ...) is used.
    /// The temporary file is removed if any operation fails before the exception is rethrown.
    /// </summary>
    /// <param name="targetFilePath">The target file path.</param>
    /// <param name="content">The content.</param>
    /// <param name="encoding">The encoding.</param>
    /// <returns>The path of the written file.</returns>
    private static string WriteAllTextAtomically(string targetFilePath, string content, Encoding encoding)
    {
        var targetEncoding = Settings.Default.Cleaning_RemoveByteOrderMark
            ? new UTF8Encoding(false)
            : encoding;

        var directoryPath = Path.GetDirectoryName(targetFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var tempFilePath = targetFilePath + ".codejanitor.tmp." + Guid.NewGuid().ToString("N");

        try
        {
            FileTextStyle.WriteAllText(tempFilePath, content, targetEncoding, null, targetFilePath);

            // The planned name may already carry a suffix (Name~1.cs); the next free name continues the suffixes of the
            // type's own name. A C# identifier has no '~', so the suffix is unambiguous.
            var targetFileName = Path.GetFileName(targetFilePath);
            var typeFileName = UniqueSuffix.Replace(Path.GetFileNameWithoutExtension(targetFileName), string.Empty) + Path.GetExtension(targetFileName);
            var takenFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var filePath = targetFilePath;
            while (true)
            {
                try
                {
                    File.Move(tempFilePath, filePath);

                    return filePath;
                }
                catch (IOException) when (File.Exists(filePath))
                {
                    takenFileNames.Add(Path.GetFileName(filePath));
                    filePath = Path.Combine(directoryPath ?? string.Empty, TopLevelTypeToFileSplitPlanner.MakeFileNameUnique(typeFileName, takenFileNames));
                }
            }
        }
        catch
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }

            throw;
        }
    }
}
