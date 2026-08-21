using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CodeJanitor.Properties;

namespace CodeJanitor.Logic.Cleaning;

internal sealed class TopLevelTypeToFileSplitFileProcessor
{
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

        internal bool Changed { get; }

        internal string UpdatedSource { get; }

        internal IReadOnlyList<string> CreatedFiles { get; }

        internal TopLevelTypeSplitSkipReason SkipReason { get; }
    }

    private readonly TopLevelTypeToFileSplitPlanner _planner;

    internal TopLevelTypeToFileSplitFileProcessor(TopLevelTypeToFileSplitPlanner planner = null)
    {
        _planner = planner ?? new TopLevelTypeToFileSplitPlanner();
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
        foreach (var plannedFile in splitPlan.NewFiles)
        {
            var transformedSource = createdFileTransform != null
                ? createdFileTransform(plannedFile.Content, plannedFile.FilePath)
                : plannedFile.Content;

            WriteAllTextAtomically(plannedFile.FilePath, transformedSource, encoding);
            createdFiles.Add(plannedFile.FilePath);
        }

        var updatedSource = transformSource != null && transformUpdatedSource
            ? transformSource(splitPlan.UpdatedSource, filePath)
            : splitPlan.UpdatedSource;

        return new ApplyResult(true, updatedSource, createdFiles, TopLevelTypeSplitSkipReason.None);
    }

    /// <summary>
    /// Writes content to a file atomically by creating missing directories, writing to a unique temporary file in the same location, deleting any existing target file, then moving the temp file into place, and cleaning up the temp file if any operation fails before rethrowing the exception.
    /// </summary>
    /// <param name="targetFilePath">The target file path.</param>
    /// <param name="content">The content.</param>
    /// <param name="encoding">The encoding.</param>

    private static void WriteAllTextAtomically(string targetFilePath, string content, Encoding encoding)
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
            File.WriteAllText(tempFilePath, content, targetEncoding);

            if (File.Exists(targetFilePath))
            {
                File.Delete(targetFilePath);
            }

            File.Move(tempFilePath, targetFilePath);
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
