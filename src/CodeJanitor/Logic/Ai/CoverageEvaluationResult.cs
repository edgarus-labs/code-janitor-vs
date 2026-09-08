using System;
using System.Collections.Generic;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Result of evaluating branch coverage against generated test code.
/// </summary>
public sealed class CoverageEvaluationResult
{
    /// <summary>
    /// Gets or sets the total branches.
    /// </summary>
    public int TotalBranches { get; set; }

    /// <summary>
    /// Gets or sets the covered branches count.
    /// </summary>
    public int CoveredBranchesCount { get; set; }

    /// <summary>
    /// Gets or sets the estimated coverage percentage.
    /// </summary>
    public int EstimatedCoveragePercentage { get; set; }

    /// <summary>
    /// Gets or sets the covered branches.
    /// </summary>
    public IReadOnlyList<CodeBranchDescriptor> CoveredBranches { get; set; }

    /// <summary>
    /// Gets or sets the uncovered branches.
    /// </summary>
    public IReadOnlyList<CodeBranchDescriptor> UncoveredBranches { get; set; }

    /// <summary>
    /// Gets or sets the identified test scenarios.
    /// </summary>
    public IReadOnlyList<string> IdentifiedTestScenarios { get; set; }
}
