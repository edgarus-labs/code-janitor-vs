using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Evaluates code branch coverage achieved by generated test classes.
/// </summary>
public sealed class BranchCoverageEvaluator : ICoverageEvaluator
{
    public CoverageEvaluationResult Evaluate(IReadOnlyList<CodeBranchDescriptor> allBranches, string testCode)
    {
        if (allBranches == null || allBranches.Count == 0)
        {
            return new CoverageEvaluationResult
            {
                TotalBranches = 0,
                CoveredBranchesCount = 0,
                EstimatedCoveragePercentage = 100,
                CoveredBranches = Array.Empty<CodeBranchDescriptor>(),
                UncoveredBranches = Array.Empty<CodeBranchDescriptor>(),
                IdentifiedTestScenarios = Array.Empty<string>()
            };
        }

        if (string.IsNullOrWhiteSpace(testCode))
        {
            return new CoverageEvaluationResult
            {
                TotalBranches = allBranches.Count,
                CoveredBranchesCount = 0,
                EstimatedCoveragePercentage = 0,
                CoveredBranches = Array.Empty<CodeBranchDescriptor>(),
                UncoveredBranches = allBranches,
                IdentifiedTestScenarios = Array.Empty<string>()
            };
        }

        var testScenarios = ExtractTestScenarios(testCode);
        var covered = new HashSet<CodeBranchDescriptor>();

        // Heuristic & Semantic matching of test scenarios / assertions to branch descriptors
        foreach (var branch in allBranches)
        {
            if (IsBranchCoveredByTests(branch, testScenarios, testCode))
            {
                covered.Add(branch);
            }
        }

        // If at least one happy path test exists and no branches were specifically matched, count happy path
        if (testScenarios.Count > 0 && covered.Count == 0 && allBranches.Count > 0)
        {
            covered.Add(allBranches[0]);
        }

        var coveredList = covered.ToList();
        var uncoveredList = allBranches.Where(b => !covered.Contains(b)).ToList();

        var pct = (int)Math.Round((double)coveredList.Count / allBranches.Count * 100);

        return new CoverageEvaluationResult
        {
            TotalBranches = allBranches.Count,
            CoveredBranchesCount = coveredList.Count,
            EstimatedCoveragePercentage = Math.Min(100, pct),
            CoveredBranches = coveredList,
            UncoveredBranches = uncoveredList,
            IdentifiedTestScenarios = testScenarios
        };
    }

    private static List<string> ExtractTestScenarios(string testCode)
    {
        var scenarios = new List<string>();
        try
        {
            var tree = CSharpSyntaxTree.ParseText(testCode);
            var root = tree.GetRoot();
            var testMethods = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(m => m.AttributeLists.Any(a => a.Attributes.Any(attr =>
                    attr.Name.ToString().Contains("Fact") ||
                    attr.Name.ToString().Contains("Test") ||
                    attr.Name.ToString().Contains("Theory"))));

            foreach (var tm in testMethods)
            {
                scenarios.Add(tm.Identifier.Text);
            }
        }
        catch
        {
            // Fallback line scan
            var lines = testCode.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("public void ") || trimmed.StartsWith("public async Task "))
                {
                    scenarios.Add(trimmed);
                }
            }
        }

        return scenarios;
    }

    private static bool IsBranchCoveredByTests(CodeBranchDescriptor branch, List<string> testScenarios, string fullTestCode)
    {
        var condition = branch.ConditionSnippet ?? string.Empty;
        var desc = branch.Description ?? string.Empty;

        // Check if any test method specifically mentions this condition or scenario
        foreach (var scenario in testScenarios)
        {
            var sLower = scenario.ToLowerInvariant();

            switch (branch.BranchType)
            {
                case CodeBranchType.GuardClause:
                case CodeBranchType.ThrowException:
                    if (sLower.Contains("throw") || sLower.Contains("null") || sLower.Contains("invalid") ||
                        sLower.Contains("empty") || sLower.Contains("exception") || sLower.Contains("fail"))
                    {
                        return true;
                    }
                    break;

                case CodeBranchType.NullCoalescing:
                    if (sLower.Contains("null") || sLower.Contains("default") || sLower.Contains("fallback"))
                    {
                        return true;
                    }
                    break;

                case CodeBranchType.SwitchCase:
                    var rawCondition = condition.Replace("case", "").Replace(":", "").Replace("\"", "").Trim().ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(rawCondition) && (sLower.Contains(rawCondition) || fullTestCode.ToLowerInvariant().Contains(rawCondition)))
                    {
                        return true;
                    }
                    break;

                case CodeBranchType.ElseBranch:
                    if (sLower.Contains("false") || sLower.Contains("else") || sLower.Contains("other") || sLower.Contains("not"))
                    {
                        return true;
                    }
                    break;

                case CodeBranchType.IfBranch:
                case CodeBranchType.Ternary:
                    if (sLower.Contains("valid") || sLower.Contains("success") || sLower.Contains("true") ||
                        sLower.Contains("returns") || sLower.Contains("when"))
                    {
                        return true;
                    }
                    break;

                case CodeBranchType.CatchBlock:
                    if (sLower.Contains("exception") || sLower.Contains("error") || sLower.Contains("catch"))
                    {
                        return true;
                    }
                    break;
            }
        }

        // Direct token matching in test method bodies
        if (!string.IsNullOrWhiteSpace(condition) && fullTestCode.IndexOf(condition, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }
}
