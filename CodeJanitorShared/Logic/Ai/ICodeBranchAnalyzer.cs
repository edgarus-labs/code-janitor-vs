using System.Collections.Generic;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Abstraction for analyzing execution branches and paths in code (SRP & DIP).
/// </summary>
public interface ICodeBranchAnalyzer
{
    /// <summary>
    /// Analyzes the source code snippet and returns all identified branch points and decision conditions.
    /// </summary>
    IReadOnlyList<CodeBranchDescriptor> AnalyzeBranches(string sourceCode);
}
