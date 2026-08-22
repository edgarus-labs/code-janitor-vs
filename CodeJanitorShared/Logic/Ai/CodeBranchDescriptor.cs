using System;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Identifies the kind of decision or branch point in code.
/// </summary>
public enum CodeBranchType
{
    IfBranch,
    ElseBranch,
    SwitchCase,
    Ternary,
    NullCoalescing,
    CatchBlock,
    ThrowException,
    GuardClause,
    Loop
}

/// <summary>
/// Describes a single branch, path, or edge condition extracted from source code.
/// </summary>
public sealed class CodeBranchDescriptor
{
    public string Id { get; set; }
    public CodeBranchType BranchType { get; set; }
    public string Description { get; set; }
    public string ConditionSnippet { get; set; }
    public int LineNumber { get; set; }

    public override string ToString() => $"[{BranchType}] {Description} (Line {LineNumber})";
}
