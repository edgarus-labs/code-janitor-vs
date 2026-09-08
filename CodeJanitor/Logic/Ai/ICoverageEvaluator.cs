using System;
using System.Collections.Generic;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Abstraction for evaluating test code against identified code branches (SRP & DIP).
/// </summary>
public interface ICoverageEvaluator
{
    CoverageEvaluationResult Evaluate(IReadOnlyList<CodeBranchDescriptor> allBranches, string testCode);
}
