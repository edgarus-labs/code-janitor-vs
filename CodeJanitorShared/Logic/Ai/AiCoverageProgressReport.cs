namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Live progress report sent during iterative test generation to achieve target code coverage.
/// </summary>
public sealed class AiCoverageProgressReport
{
    public int CurrentIteration { get; set; }
    public int MaxIterations { get; set; }
    public int TargetCoveragePercentage { get; set; }
    public int CurrentCoveragePercentage { get; set; }
    public int TotalBranchesCount { get; set; }
    public int CoveredBranchesCount { get; set; }
    public string StatusMessage { get; set; }
}

/// <summary>
/// Final result produced by the AI target coverage test generator.
/// </summary>
public sealed class AiCoverageResult
{
    public bool Success { get; set; }
    public bool TargetMet { get; set; }
    public int AchievedCoveragePercentage { get; set; }
    public int TargetCoveragePercentage { get; set; }
    public int IterationsUsed { get; set; }
    public string GeneratedTestCode { get; set; }
    public string CoverageSummaryReport { get; set; }
    public string ErrorMessage { get; set; }
}
