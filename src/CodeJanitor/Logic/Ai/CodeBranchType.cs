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
