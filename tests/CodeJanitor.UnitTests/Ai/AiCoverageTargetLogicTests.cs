using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Ai;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.UnitTests.Ai;

[TestClass]
public sealed class AiCoverageTargetLogicTests
{
    /// <summary>
    /// FakeAiChatClient is a test double for an AI chat client that tracks usage and provides configurable responses for chat completion requests.
    /// </summary>
    private sealed class FakeAiChatClient : IAiChatClient
    {
        /// <summary>
        /// Gets the is configured.
        /// </summary>
        public bool IsConfigured => true;

        /// <summary>
        /// Gets or sets the call count.
        /// </summary>
        public int CallCount { get; private set; }

        /// <summary>
        /// Gets or sets the response provider.
        /// </summary>
        public Func<string, string, string> ResponseProvider { get; set; }

        /// <summary>
        /// Increments CallCount, ignores the ResponseProvider&apos;s actual result, and always returns a hardcoded C# code block string, ignoring the cancellation token and maxTokens parameters.
        /// </summary>
        /// <param name="systemPrompt">The system prompt.</param>
        /// <param name="userPrompt">The user prompt.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="maxTokens">The max tokens.</param>
        /// <returns>A Task&lt;string&gt; value produced by this method.</returns>
        public Task<string> GetChatCompletionContentAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default, int maxTokens = 2048)
        {
            CallCount++;
            string response = ResponseProvider?.Invoke(systemPrompt, userPrompt)
                ?? "```csharp\npublic class Tests { [Fact] public void Test1() {} }\n```";

            return Task.FromResult(response);
        }
    }

    [TestMethod]
    public void RoslynCodeBranchAnalyzer_DiscoversIfElseAndGuardClauses()
    {
        // Arrange
        RoslynCodeBranchAnalyzer analyzer = new RoslynCodeBranchAnalyzer();
        string code = @"
public string FormatName(string firstName, string lastName)
{
    if (firstName == null) throw new ArgumentNullException(nameof(firstName));
    if (lastName == null) return firstName;
    else return firstName + "" "" + lastName;
}";

        // Act
        IReadOnlyList<CodeBranchDescriptor> branches = analyzer.AnalyzeBranches(code);

        // Assert
        Assert.IsGreaterThanOrEqualTo(3, branches.Count, $"Expected at least 3 branches, found {branches.Count}");
        Assert.Contains(b => b.BranchType is CodeBranchType.GuardClause or CodeBranchType.ThrowException, branches, "Should detect guard/throw branch");
        Assert.Contains(b => b.BranchType == CodeBranchType.ElseBranch, branches, "Should detect else branch");
    }

    [TestMethod]
    public void RoslynCodeBranchAnalyzer_DiscoversSwitchAndTernaryAndNullCoalesce()
    {
        // Arrange
        RoslynCodeBranchAnalyzer analyzer = new RoslynCodeBranchAnalyzer();
        string code = @"
public int Calculate(int? value, string mode)
{
    var val = value ?? 0;
    var factor = mode == ""Double"" ? 2 : 1;
    switch (mode)
    {
        case ""A"": return val * 10;
        case ""B"": return val * 20;
        default: return val * factor;
    }
}";

        // Act
        IReadOnlyList<CodeBranchDescriptor> branches = analyzer.AnalyzeBranches(code);

        // Assert
        Assert.Contains(b => b.BranchType == CodeBranchType.NullCoalescing, branches, "Should find null-coalescing ??");
        Assert.Contains(b => b.BranchType == CodeBranchType.Ternary, branches, "Should find ternary ? :");
        Assert.Contains(b => b.BranchType == CodeBranchType.SwitchCase, branches, "Should find switch cases");
    }

    [TestMethod]
    public void BranchCoverageEvaluator_EvaluatesCoveragePercentagesAccurately()
    {
        // Arrange
        BranchCoverageEvaluator evaluator = new BranchCoverageEvaluator();
        List<CodeBranchDescriptor> branches = [new CodeBranchDescriptor { Id = "B1", BranchType = CodeBranchType.IfBranch, Description = "Happy path", ConditionSnippet = "valid" }, new CodeBranchDescriptor { Id = "B2", BranchType = CodeBranchType.GuardClause, Description = "Null check", ConditionSnippet = "arg is null" }];

        // Act - Empty test code
        CoverageEvaluationResult emptyResult = evaluator.Evaluate(branches, "");
        Assert.AreEqual(0, emptyResult.EstimatedCoveragePercentage);
        Assert.HasCount(2, emptyResult.UncoveredBranches);

        // Act - Tests covering null check and happy path
        string testCode = @"
public class SampleTests
{
    [Fact]
    public void Method_ValidInput_ReturnsSuccess() { }

    [Fact]
    public void Method_WhenNull_ThrowsArgumentNullException() { }
}";
        CoverageEvaluationResult coveredResult = evaluator.Evaluate(branches, testCode);

        // Assert
        Assert.AreEqual(100, coveredResult.EstimatedCoveragePercentage);
        Assert.AreEqual(2, coveredResult.CoveredBranchesCount);
        Assert.IsEmpty(coveredResult.UncoveredBranches);
    }

    [TestMethod]
    public async Task AiCoverageTargetLogic_IterativelyGeneratesUntilTargetCoverageIsReached()
    {
        // Arrange
        RoslynCodeBranchAnalyzer analyzer = new RoslynCodeBranchAnalyzer();
        BranchCoverageEvaluator evaluator = new BranchCoverageEvaluator();
        FakeAiChatClient fakeClient = new FakeAiChatClient();

        int callNum = 0;
        fakeClient.ResponseProvider = (sys, user) =>
        {
            callNum++;
            if (callNum == 1)
            {
                // Round 1: only happy path
                return "```csharp\npublic class Tests { [Fact] public void Calculate_Valid_ReturnsValue() {} }\n```";
            }
            // Round 2: covers null branch as well

            return @"```csharp
public class Tests
{
    [Fact] public void Calculate_Valid_ReturnsValue() {}
    [Fact] public void Calculate_WhenNull_ThrowsException() {}
}
```";
        };

        AiCoverageTargetLogic logic = new AiCoverageTargetLogic(fakeClient, analyzer, evaluator);
        List<AiCoverageProgressReport> reports = [];
        Progress<AiCoverageProgressReport> progress = new Progress<AiCoverageProgressReport>(reports.Add);

        string methodCode = @"
public int Calculate(string input)
{
    if (input == null) throw new ArgumentNullException(nameof(input));
    return input.Length;
}";

        // Act
        AiCoverageResult result = await logic.GenerateTestsToTargetCoverageAsync(
            targetName: "Calculate",
            codeSnippet: methodCode,
            targetCoveragePct: 90,
            maxIterations: 3,
            testFramework: "xUnit",
            mockingLib: "Moq",
            progress: progress,
            cancellationToken: CancellationToken.None);

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.TargetMet);
        Assert.IsGreaterThanOrEqualTo(90, result.AchievedCoveragePercentage);
        Assert.IsGreaterThanOrEqualTo(2, fakeClient.CallCount, $"Expected at least 2 AI iterations, took {fakeClient.CallCount}");
        Assert.Contains("Calculate_WhenNull_ThrowsException", result.GeneratedTestCode);
        Assert.Contains("Code Coverage Test Report", result.CoverageSummaryReport);
    }

    [TestMethod]
    public async Task AiCoverageTargetLogic_WhenCanceled_ThrowsOperationCanceledExceptionInstantly()
    {
        // Arrange
        FakeAiChatClient fakeClient = new FakeAiChatClient
        {
            ResponseProvider = (sys, user) =>
            {
                Thread.Sleep(50);

                return "```csharp\n[Fact] public void Test() {}\n```";
            }
        };

        AiCoverageTargetLogic logic = new AiCoverageTargetLogic(fakeClient, new RoslynCodeBranchAnalyzer(), new BranchCoverageEvaluator());
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel

        // Act & Assert
        try
        {
            await logic.GenerateTestsToTargetCoverageAsync(
                "Method",
                "public void DoWork() {}",
                targetCoveragePct: 100,
                maxIterations: 5,
                testFramework: "xUnit",
                mockingLib: "Moq",
                cancellationToken: cts.Token);

            Assert.Fail("Expected OperationCanceledException was not thrown.");
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }

    [TestMethod]
    public void RoslynCodeBranchAnalyzer_SwitchExpressionAndCatchClauses_DiscoversAllBranches()
    {
        // Arrange
        RoslynCodeBranchAnalyzer analyzer = new RoslynCodeBranchAnalyzer();
        string code = @"
public string Process(int state)
{
    try
    {
        return state switch
        {
            1 => ""One"",
            2 => ""Two"",
            _ => ""Other""
        };
    }
    catch (InvalidOperationException)
    {
        return ""Error"";
    }
}";

        // Act
        IReadOnlyList<CodeBranchDescriptor> branches = analyzer.AnalyzeBranches(code);

        // Assert
        Assert.Contains(b => b.BranchType == CodeBranchType.SwitchCase, branches, "Should find switch expression arms");
        Assert.Contains(b => b.BranchType == CodeBranchType.CatchBlock, branches, "Should find catch clause");
    }

    [TestMethod]
    public void RoslynCodeBranchAnalyzer_EmptyOrNoBranches_ReturnsSensibleDefaults()
    {
        // Arrange
        RoslynCodeBranchAnalyzer analyzer = new RoslynCodeBranchAnalyzer();

        // Act & Assert - Empty string
        IReadOnlyList<CodeBranchDescriptor> emptyBranches = analyzer.AnalyzeBranches("");
        Assert.IsEmpty(emptyBranches);

        // Act & Assert - Straight linear method
        IReadOnlyList<CodeBranchDescriptor> linearBranches = analyzer.AnalyzeBranches("public void Log() { Console.WriteLine(\"hi\"); }");
        Assert.HasCount(1, linearBranches);
        Assert.AreEqual("B1", linearBranches[0].Id);
    }

    [TestMethod]
    public void BranchCoverageEvaluator_EdgeCases_HandlesVariousScenarios()
    {
        // Arrange
        BranchCoverageEvaluator evaluator = new BranchCoverageEvaluator();

        // Null branches
        CoverageEvaluationResult nullResult = evaluator.Evaluate(null, "public void Test() {}");
        Assert.AreEqual(100, nullResult.EstimatedCoveragePercentage);

        // Branches with Switch, Else, Catch
        List<CodeBranchDescriptor> branches = [new CodeBranchDescriptor { Id = "B1", BranchType = CodeBranchType.SwitchCase, Description = "case \"Active\"", ConditionSnippet = "case \"Active\":" }, new CodeBranchDescriptor { Id = "B2", BranchType = CodeBranchType.ElseBranch, Description = "Else branch", ConditionSnippet = "!isActive" }, new CodeBranchDescriptor { Id = "B3", BranchType = CodeBranchType.CatchBlock, Description = "catch (IOException)", ConditionSnippet = "IOException" }];

        string tests = @"
public class ServiceTests
{
    [Fact]
    public void WhenActive_ReturnsTrue() { var status = ""Active""; }

    [Fact]
    public void WhenNotActive_ReturnsFalse() { }

    [Fact]
    public void WhenExceptionThrown_CatchesError() { }
}";

        // Act
        CoverageEvaluationResult result = evaluator.Evaluate(branches, tests);

        // Assert
        Assert.AreEqual(3, result.CoveredBranchesCount);
        Assert.AreEqual(100, result.EstimatedCoveragePercentage);
    }

    [TestMethod]
    public async Task AiCoverageTargetLogic_WhenMaxIterationsReached_ReportsTargetNotMet()
    {
        // Arrange
        RoslynCodeBranchAnalyzer analyzer = new RoslynCodeBranchAnalyzer();
        BranchCoverageEvaluator evaluator = new BranchCoverageEvaluator();
        FakeAiChatClient fakeClient = new FakeAiChatClient
        {
            ResponseProvider = (sys, user) => "```csharp\npublic class Tests { [Fact] public void TestOnlyOnePath() {} }\n```"
        };

        AiCoverageTargetLogic logic = new AiCoverageTargetLogic(fakeClient, analyzer, evaluator);
        string code = @"
public int Check(int a, int b)
{
    if (a < 0) throw new ArgumentOutOfRangeException();
    if (b < 0) throw new ArgumentOutOfRangeException();
    if (a == b) return 0;
    return a + b;
}";

        // Act
        AiCoverageResult result = await logic.GenerateTestsToTargetCoverageAsync(
            "Check",
            code,
            targetCoveragePct: 100,
            maxIterations: 2,
            testFramework: "xUnit",
            mockingLib: "Moq", cancellationToken: TestContext.CancellationToken);

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsFalse(result.TargetMet, "Should not meet 100% target coverage when test only covers one path");
        Assert.AreEqual(2, result.IterationsUsed);
        Assert.Contains("Max Iterations Reached", result.CoverageSummaryReport);
    }

    [TestMethod]
    public async Task AiCoverageTargetLogic_Validation_HandlesNullCodeAndUnconfiguredClient()
    {
        // Arrange
        RoslynCodeBranchAnalyzer analyzer = new RoslynCodeBranchAnalyzer();
        BranchCoverageEvaluator evaluator = new BranchCoverageEvaluator();
        FakeAiChatClient fakeClient = new FakeAiChatClient();
        AiCoverageTargetLogic logic = new AiCoverageTargetLogic(fakeClient, analyzer, evaluator);

        // Act - Null code
        AiCoverageResult nullCodeResult = await logic.GenerateTestsToTargetCoverageAsync("Test", null, 90, 3, "xUnit", "Moq", cancellationToken: TestContext.CancellationToken);
        Assert.IsFalse(nullCodeResult.Success);
        Assert.Contains("No code provided", nullCodeResult.ErrorMessage);

        // Act - Unconfigured client
        AiCoverageTargetLogic unconfiguredLogic = new AiCoverageTargetLogic(null, analyzer, evaluator);
        AiCoverageResult unconfiguredResult = await unconfiguredLogic.GenerateTestsToTargetCoverageAsync("Test", "public void Foo() {}", 90, 3, "xUnit", "Moq", cancellationToken: TestContext.CancellationToken);
        Assert.IsFalse(unconfiguredResult.Success);
        Assert.Contains("not configured", unconfiguredResult.ErrorMessage);
    }

    public TestContext TestContext { get; set; }
}
