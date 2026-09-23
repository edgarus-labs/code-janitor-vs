namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Final result produced by the AI target coverage test generator.
/// </summary>
public sealed class AiCoverageResult
{
    /// <summary>
    /// Gets or sets the success.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the target met.
    /// </summary>
    public bool TargetMet { get; set; }

    /// <summary>
    /// Gets or sets the achieved coverage percentage.
    /// </summary>
    public int AchievedCoveragePercentage { get; set; }

    /// <summary>
    /// Gets or sets the target coverage percentage.
    /// </summary>
    public int TargetCoveragePercentage { get; set; }

    /// <summary>
    /// Gets or sets the iterations used.
    /// </summary>
    public int IterationsUsed { get; set; }

    /// <summary>
    /// Gets or sets the generated test code.
    /// </summary>
    public string GeneratedTestCode { get; set; }

    /// <summary>
    /// Gets or sets the coverage summary report.
    /// </summary>
    public string CoverageSummaryReport { get; set; }

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string ErrorMessage { get; set; }
}
