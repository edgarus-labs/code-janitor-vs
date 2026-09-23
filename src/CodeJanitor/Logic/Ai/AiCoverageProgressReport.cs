namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Live progress report sent during iterative test generation to achieve target code coverage.
/// </summary>
public sealed class AiCoverageProgressReport
{
    /// <summary>
    /// Gets or sets the current iteration.
    /// </summary>
    public int CurrentIteration { get; set; }

    /// <summary>
    /// Gets or sets the max iterations.
    /// </summary>
    public int MaxIterations { get; set; }

    /// <summary>
    /// Gets or sets the target coverage percentage.
    /// </summary>
    public int TargetCoveragePercentage { get; set; }

    /// <summary>
    /// Gets or sets the current coverage percentage.
    /// </summary>
    public int CurrentCoveragePercentage { get; set; }

    /// <summary>
    /// Gets or sets the total branches count.
    /// </summary>
    public int TotalBranchesCount { get; set; }

    /// <summary>
    /// Gets or sets the covered branches count.
    /// </summary>
    public int CoveredBranchesCount { get; set; }

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    public string StatusMessage { get; set; }
}
