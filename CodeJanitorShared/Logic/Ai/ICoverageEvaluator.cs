using System;
using System.Collections.Generic;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Result of evaluating branch coverage against generated test code.
/// </summary>
public sealed class CoverageEvaluationResult
{
    public int TotalBranches { get; set; }
    public int CoveredBranchesCount { get; set; }
    public int EstimatedCoveragePercentage { get; set; }
    public IReadOnlyList<CodeBranchDescriptor> CoveredBranches { get; set; }
    public IReadOnlyList<CodeBranchDescriptor> UncoveredBranches { get; set; }
    public IReadOnlyList<string> IdentifiedTestScenarios { get; set; }
}

/// <summary>
/// Abstraction for evaluating test code against identified code branches (SRP & DIP).
/// </summary>
public interface ICoverageEvaluator
{
    CoverageEvaluationResult Evaluate(IReadOnlyList<CodeBranchDescriptor> allBranches, string testCode);
}
