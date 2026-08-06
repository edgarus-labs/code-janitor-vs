using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodeJanitor.Logic.Cleaning
{
    internal sealed class TopLevelTypeToFileSplitFileProcessor
    {
        internal sealed class ApplyResult
        {
            internal ApplyResult(bool changed, string updatedSource, IReadOnlyList<string> createdFiles)
            {
                Changed = changed;
                UpdatedSource = updatedSource;
                CreatedFiles = createdFiles;
            }

            internal bool Changed { get; }

            internal string UpdatedSource { get; }

            internal IReadOnlyList<string> CreatedFiles { get; }
        }

        private readonly TopLevelTypeToFileSplitPlanner _planner;

        internal TopLevelTypeToFileSplitFileProcessor(TopLevelTypeToFileSplitPlanner planner = null)
        {
            _planner = planner ?? new TopLevelTypeToFileSplitPlanner();
        }

        internal ApplyResult Apply(string source, string filePath, Encoding encoding, Func<string, string, string> transformSource)
        {
            var splitPlan = _planner.CreatePlan(source, filePath);
            if (!splitPlan.HasChanges)
            {
                return new ApplyResult(false, source, Array.Empty<string>());
            }

            var createdFiles = new List<string>();
            foreach (var plannedFile in splitPlan.NewFiles)
            {
                var transformedSource = transformSource != null
                    ? transformSource(plannedFile.Content, plannedFile.FilePath)
                    : plannedFile.Content;

                File.WriteAllText(plannedFile.FilePath, transformedSource, encoding);
                createdFiles.Add(plannedFile.FilePath);
            }

            var updatedSource = transformSource != null
                ? transformSource(splitPlan.UpdatedSource, filePath)
                : splitPlan.UpdatedSource;

            return new ApplyResult(true, updatedSource, createdFiles);
        }
    }
}
