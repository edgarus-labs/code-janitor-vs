using System;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Describes a single branch, path, or edge condition extracted from source code.
/// </summary>
public sealed class CodeBranchDescriptor
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the branch type.
    /// </summary>
    public CodeBranchType BranchType { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the condition snippet.
    /// </summary>
    public string ConditionSnippet { get; set; }

    /// <summary>
    /// Gets or sets the line number.
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// Overrides ToString to return an interpolated string of the form [BranchType] Description (Line LineNumber) with no side effects.
    /// </summary>
    /// <returns>A string value produced by this method.</returns>
    public override string ToString() => $"[{BranchType}] {Description} (Line {LineNumber})";
}
