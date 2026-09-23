using CodeJanitor.Helpers;
using CodeJanitor.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Coordinates iterative AI unit test generation aiming for a target Code Coverage percentage (SRP & DIP).
/// </summary>
public sealed class AiCoverageTargetLogic
{
    private readonly IAiChatClient _aiClient;
    private readonly ICodeBranchAnalyzer _branchAnalyzer;
    private readonly ICoverageEvaluator _coverageEvaluator;

    public AiCoverageTargetLogic(
        IAiChatClient aiClient,
        ICodeBranchAnalyzer branchAnalyzer,
        ICoverageEvaluator coverageEvaluator)
    {
        _aiClient = aiClient;
        _branchAnalyzer = branchAnalyzer ?? new RoslynCodeBranchAnalyzer();
        _coverageEvaluator = coverageEvaluator ?? new BranchCoverageEvaluator();
    }

    /// <summary>
    /// Creates an instance using default application settings.
    /// </summary>
    public static AiCoverageTargetLogic CreateFromSettings()
    {
        var settings = Settings.Default;
        var client = CreateClientFromSettings(settings);

        return new AiCoverageTargetLogic(client, new RoslynCodeBranchAnalyzer(), new BranchCoverageEvaluator());
    }

    /// <summary>
    /// Checks if AI endpoint is configured in settings.
    /// </summary>
    public static bool IsConfigurationPresent()
    {
        var settings = Settings.Default;

        return OpenAiCompatibleClient.IsEndpointConfigured(settings.Cleaning_AiXmlDocumentationEndpointUrl);
    }

    /// <summary>
    /// Iteratively generates unit tests until the target coverage percentage is reached or max iterations is exhausted.
    /// </summary>
    public async Task<AiCoverageResult> GenerateTestsToTargetCoverageAsync(
        string targetName,
        string codeSnippet,
        int targetCoveragePct,
        int maxIterations,
        string testFramework,
        string mockingLib,
        IProgress<AiCoverageProgressReport> progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(codeSnippet))
        {
            return new AiCoverageResult
            {
                Success = false,
                ErrorMessage = "No code provided for test generation."
            };
        }

        if (_aiClient is null || !_aiClient.IsConfigured)
        {
            return new AiCoverageResult
            {
                Success = false,
                ErrorMessage = "AI endpoint is not configured. Please configure your endpoint in Tools > Options > Code Janitor > XML Documentation."
            };
        }

        var safeTargetCoverage = Math.Max(10, Math.Min(100, targetCoveragePct));
        var safeMaxIterations = Math.Max(1, Math.Min(10, maxIterations));
        var framework = string.IsNullOrWhiteSpace(testFramework) ? "xUnit" : testFramework;
        var mocking = string.IsNullOrWhiteSpace(mockingLib) ? "Moq" : mockingLib;

        // Step 1: Static Branch Analysis
        progress?.Report(new AiCoverageProgressReport
        {
            CurrentIteration = 0,
            MaxIterations = safeMaxIterations,
            TargetCoveragePercentage = safeTargetCoverage,
            CurrentCoveragePercentage = 0,
            StatusMessage = "Analyzing code branches, conditions, and exception paths via Roslyn AST..."
        });

        var branches = _branchAnalyzer.AnalyzeBranches(codeSnippet);
        var totalBranches = branches.Count;

        progress?.Report(new AiCoverageProgressReport
        {
            CurrentIteration = 0,
            MaxIterations = safeMaxIterations,
            TargetCoveragePercentage = safeTargetCoverage,
            CurrentCoveragePercentage = 0,
            TotalBranchesCount = totalBranches,
            CoveredBranchesCount = 0,
            StatusMessage = $"Discovered {totalBranches} decision points/branches. Starting test generation..."
        });

        string accumulatedTestCode = string.Empty;
        var currentCoverageResult = new CoverageEvaluationResult
        {
            TotalBranches = totalBranches,
            CoveredBranchesCount = 0,
            EstimatedCoveragePercentage = 0,
            UncoveredBranches = branches,
            CoveredBranches = Array.Empty<CodeBranchDescriptor>()
        };

        int iteration = 0;
        while (iteration < safeMaxIterations && currentCoverageResult.EstimatedCoveragePercentage < safeTargetCoverage)
        {
            cancellationToken.ThrowIfCancellationRequested();
            iteration++;

            progress?.Report(new AiCoverageProgressReport
            {
                CurrentIteration = iteration,
                MaxIterations = safeMaxIterations,
                TargetCoveragePercentage = safeTargetCoverage,
                CurrentCoveragePercentage = currentCoverageResult.EstimatedCoveragePercentage,
                TotalBranchesCount = totalBranches,
                CoveredBranchesCount = currentCoverageResult.CoveredBranchesCount,
                StatusMessage = iteration == 1
                    ? $"Iteration {iteration}/{safeMaxIterations}: Generating base test suite (Happy paths & primary flows)..."
                    : $"Iteration {iteration}/{safeMaxIterations}: Generating targeted tests for remaining {currentCoverageResult.UncoveredBranches.Count} uncovered branches..."
            });

            var prompt = BuildIterativePrompt(targetName, codeSnippet, framework, mocking, iteration, accumulatedTestCode, currentCoverageResult.UncoveredBranches);
            var systemPrompt = $"You are an expert C# unit testing specialist targeting 100% branch and line coverage using {framework} and {mocking}. Provide complete, compiling C# test code with Arrange-Act-Assert. Output ONLY the test code inside a ```csharp ``` block.";

            try
            {
                var aiResponse = await _aiClient.GetChatCompletionContentAsync(systemPrompt, prompt, cancellationToken).ConfigureAwait(false);
                var extractedCode = ExtractCodeSnippet(aiResponse);

                accumulatedTestCode = iteration == 1
                    ? extractedCode
                    : MergeTestCode(accumulatedTestCode, extractedCode);

                currentCoverageResult = _coverageEvaluator.Evaluate(branches, accumulatedTestCode);

                progress?.Report(new AiCoverageProgressReport
                {
                    CurrentIteration = iteration,
                    MaxIterations = safeMaxIterations,
                    TargetCoveragePercentage = safeTargetCoverage,
                    CurrentCoveragePercentage = currentCoverageResult.EstimatedCoveragePercentage,
                    TotalBranchesCount = totalBranches,
                    CoveredBranchesCount = currentCoverageResult.CoveredBranchesCount,
                    StatusMessage = $"Iteration {iteration}/{safeMaxIterations} complete. Current estimated coverage: {currentCoverageResult.EstimatedCoveragePercentage}% (Target: {safeTargetCoverage}%)"
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new AiCoverageResult
                {
                    Success = false,
                    ErrorMessage = $"Error during iteration {iteration}: {ex.Message}"
                };
            }
        }

        var targetMet = currentCoverageResult.EstimatedCoveragePercentage >= safeTargetCoverage;
        var summaryReport = BuildSummaryReport(targetName, safeTargetCoverage, currentCoverageResult, iteration, safeMaxIterations, branches);

        return new AiCoverageResult
        {
            Success = true,
            TargetMet = targetMet,
            AchievedCoveragePercentage = currentCoverageResult.EstimatedCoveragePercentage,
            TargetCoveragePercentage = safeTargetCoverage,
            IterationsUsed = iteration,
            GeneratedTestCode = accumulatedTestCode,
            CoverageSummaryReport = summaryReport
        };
    }

    /// <summary>
    /// This static method returns an LLM prompt that requests a full comprehensive unit-test suite (with the given framework and mocking) for the supplied C# snippet on the first iteration or when existing tests are empty, otherwise listing uncovered branches and requesting additional methods that update the existing suite, allocating only a temporary StringBuilder and producing no other side effects.
    /// </summary>
    /// <param name="targetName">The target name.</param>
    /// <param name="codeSnippet">The code snippet.</param>
    /// <param name="framework">The framework.</param>
    /// <param name="mocking">The mocking.</param>
    /// <param name="iteration">The iteration.</param>
    /// <param name="existingTests">The existing tests.</param>
    /// <param name="uncoveredBranches">The uncovered branches.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string BuildIterativePrompt(
        string targetName,
        string codeSnippet,
        string framework,
        string mocking,
        int iteration,
        string existingTests,
        IReadOnlyList<CodeBranchDescriptor> uncoveredBranches)
    {
        if (iteration == 1 || string.IsNullOrWhiteSpace(existingTests))
        {
            return $@"Generate a comprehensive {framework} unit test suite with {mocking} for the following C# code ('{targetName}'):

```csharp
{codeSnippet}
```

Requirements:
- Target maximum branch and line coverage.
- Include happy path tests, null input handling, and boundary assertions.
- Use naming convention: `MethodName_Condition_ExpectedResult`.
- Return the full test class enclosed in ```csharp ... ```.";
        }

        var missingBranchesText = new StringBuilder();
        foreach (var ub in uncoveredBranches)
        {
            missingBranchesText.AppendLine($"- {ub.Description} (Condition: `{ub.ConditionSnippet}`)");
        }

        return $@"We need additional test methods to achieve our target Code Coverage for '{targetName}'.

Target Code:
```csharp
{codeSnippet}
```

Currently uncovered decision paths:
{missingBranchesText}

Existing Test Suite:
```csharp
{existingTests}
```

Please generate the additional unit test methods (using {framework} and {mocking}) specifically targeting these uncovered paths. Return the complete updated test class.";
    }

    /// <summary>
    /// Merges two test-code strings by returning whichever is non-whitespace when the other is empty, preferring newTests when it contains a complete class with [Fact] or any [Test] attribute, otherwise appending newTests to originalTests after a comment separator, with no side effects.
    /// </summary>
    /// <param name="originalTests">The original tests.</param>
    /// <param name="newTests">The new tests.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string MergeTestCode(string originalTests, string newTests)
    {
        if (string.IsNullOrWhiteSpace(originalTests)) return newTests;
        if (string.IsNullOrWhiteSpace(newTests)) return originalTests;

        // If newTests is a complete class, use it; otherwise return newTests
        if (newTests.Contains("public class") && newTests.Contains("[Fact]") || newTests.Contains("[Test]"))
        {
            return newTests;
        }

        return originalTests + Environment.NewLine + Environment.NewLine + "// Additional coverage tests:" + Environment.NewLine + newTests;
    }

    /// <summary>
    /// Builds and returns a markdown coverage report string with target/achieved metrics, iteration counts, status, and per-branch covered/uncovered lines, producing no side effects.
    /// </summary>
    /// <param name="targetName">The target name.</param>
    /// <param name="targetCoverage">The target coverage.</param>
    /// <param name="evalResult">The eval result.</param>
    /// <param name="iterations">The iterations.</param>
    /// <param name="maxIterations">The max iterations.</param>
    /// <param name="allBranches">The all branches.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string BuildSummaryReport(
        string targetName,
        int targetCoverage,
        CoverageEvaluationResult evalResult,
        int iterations,
        int maxIterations,
        IReadOnlyList<CodeBranchDescriptor> allBranches)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# 📊 Code Coverage Test Report: {targetName}");
        sb.AppendLine();
        sb.AppendLine($"- **Target Coverage:** {targetCoverage}%");
        sb.AppendLine($"- **Achieved Coverage:** {evalResult.EstimatedCoveragePercentage}%");
        sb.AppendLine($"- **Covered Branches:** {evalResult.CoveredBranchesCount} / {evalResult.TotalBranches}");
        sb.AppendLine($"- **Iterations Used:** {iterations} / {maxIterations}");
        sb.AppendLine($"- **Status:** {(evalResult.EstimatedCoveragePercentage >= targetCoverage ? "✅ Target Reached" : "⚠️ Max Iterations Reached")}");
        sb.AppendLine();
        sb.AppendLine("### 🌳 Branch Analysis Breakdown");
        foreach (var b in allBranches)
        {
            var isCovered = evalResult.CoveredBranches is not null && evalResult.CoveredBranches.Any(cb => cb.Id == b.Id);
            sb.AppendLine($"- {(isCovered ? "✅" : "❌")} **{b.Description}** (Line {b.LineNumber})");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extracts a trimmed C# code snippet from markdown-fenced blocks in the AI response (preferring ```csharp then generic ``` while skipping short language identifiers), returning empty for null/whitespace input or the trimmed original if none found, with no side effects.
    /// </summary>
    /// <param name="aiResponse">The ai response.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string ExtractCodeSnippet(string aiResponse)
    {
        if (string.IsNullOrWhiteSpace(aiResponse)) return string.Empty;

        var startIndex = aiResponse.IndexOf("```csharp", StringComparison.OrdinalIgnoreCase);
        if (startIndex >= 0)
        {
            var codeStart = startIndex + "```csharp".Length;
            var endIndex = aiResponse.IndexOf("```", codeStart, StringComparison.Ordinal);
            if (endIndex > codeStart)
            {
                return aiResponse.Substring(codeStart, endIndex - codeStart).Trim();
            }
        }

        startIndex = aiResponse.IndexOf("```", StringComparison.Ordinal);
        if (startIndex >= 0)
        {
            var codeStart = startIndex + 3;
            var newlineIndex = aiResponse.IndexOf('\n', codeStart);
            if (newlineIndex > 0 && newlineIndex - codeStart < 15)
            {
                codeStart = newlineIndex + 1;
            }
            var endIndex = aiResponse.IndexOf("```", codeStart, StringComparison.Ordinal);
            if (endIndex > codeStart)
            {
                return aiResponse.Substring(codeStart, endIndex - codeStart).Trim();
            }
        }

        return aiResponse.Trim();
    }

    /// <summary>
    /// Creates an OpenAiCompatibleClient from the given Settings (or returns null if the endpoint is unconfigured) by unprotecting the encrypted API key with a plaintext fallback, defaulting the header to Authorization, timeout to 45 seconds and context window to 131072 tokens.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>A OpenAiCompatibleClient value produced by this method.</returns>
    private static OpenAiCompatibleClient CreateClientFromSettings(Settings settings)
    {
        var endpointUrl = settings.Cleaning_AiXmlDocumentationEndpointUrl;
        if (!OpenAiCompatibleClient.IsEndpointConfigured(endpointUrl))
        {
            return null;
        }

        var apiKey = SecretProtectionHelper.UnprotectForCurrentUser(settings.Cleaning_AiXmlDocumentationApiKeyEncrypted);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = settings.Cleaning_AiXmlDocumentationApiKey;
        }

        var header = string.IsNullOrWhiteSpace(settings.Cleaning_AiXmlDocumentationApiKeyHeader)
            ? "Authorization"
            : settings.Cleaning_AiXmlDocumentationApiKeyHeader;

        var model = settings.Cleaning_AiXmlDocumentationModel;
        var timeoutSeconds = settings.Cleaning_AiXmlDocumentationTimeoutSeconds > 0
            ? settings.Cleaning_AiXmlDocumentationTimeoutSeconds
            : 45;

        var contextWindowTokens = settings.Cleaning_AiXmlDocumentationContextWindowTokens > 0
            ? settings.Cleaning_AiXmlDocumentationContextWindowTokens
            : 131072;

        return new OpenAiCompatibleClient(endpointUrl, apiKey, header, model, timeoutSeconds, contextWindowTokens);
    }
}
