using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Ai;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.UnitTests.Ai;

[TestClass]
public class AiCoverageTargetLogicTests
{
    private sealed class FakeAiChatClient : IAiChatClient
    {
        public bool IsConfigured => true;
        public int CallCount { get; private set; }
        public Func<string, string, string> ResponseProvider { get; set; }

        public Task<string> GetChatCompletionContentAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default, int maxTokens = 2048)
        {
            CallCount++;
            var response = ResponseProvider?.Invoke(systemPrompt, userPrompt)
                ?? "```csharp\npublic class Tests { [Fact] public void Test1() {} }\n```";
            return Task.FromResult(response);
        }
    }

    [TestMethod]
    public void RoslynCodeBranchAnalyzer_DiscoversIfElseAndGuardClauses()
    {
        // Arrange
        var analyzer = new RoslynCodeBranchAnalyzer();
        var code = @"
public string FormatName(string firstName, string lastName)
{
    if (firstName == null) throw new ArgumentNullException(nameof(firstName));
    if (lastName == null) return firstName;
    else return firstName + "" "" + lastName;
}";

        // Act
        var branches = analyzer.AnalyzeBranches(code);

        // Assert
        Assert.IsTrue(branches.Count >= 3, $"Expected at least 3 branches, found {branches.Count}");
        Assert.IsTrue(branches.Any(b => b.BranchType == CodeBranchType.GuardClause || b.BranchType == CodeBranchType.ThrowException), "Should detect guard/throw branch");
        Assert.IsTrue(branches.Any(b => b.BranchType == CodeBranchType.ElseBranch), "Should detect else branch");
    }

    [TestMethod]
    public void RoslynCodeBranchAnalyzer_DiscoversSwitchAndTernaryAndNullCoalesce()
    {
        // Arrange
        var analyzer = new RoslynCodeBranchAnalyzer();
        var code = @"
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
        var branches = analyzer.AnalyzeBranches(code);

        // Assert
        Assert.IsTrue(branches.Any(b => b.BranchType == CodeBranchType.NullCoalescing), "Should find null-coalescing ??");
        Assert.IsTrue(branches.Any(b => b.BranchType == CodeBranchType.Ternary), "Should find ternary ? :");
        Assert.IsTrue(branches.Any(b => b.BranchType == CodeBranchType.SwitchCase), "Should find switch cases");
    }

    [TestMethod]
    public void BranchCoverageEvaluator_EvaluatesCoveragePercentagesAccurately()
    {
        // Arrange
        var evaluator = new BranchCoverageEvaluator();
        var branches = new List<CodeBranchDescriptor>
        {
            new CodeBranchDescriptor { Id = "B1", BranchType = CodeBranchType.IfBranch, Description = "Happy path", ConditionSnippet = "valid" },
            new CodeBranchDescriptor { Id = "B2", BranchType = CodeBranchType.GuardClause, Description = "Null check", ConditionSnippet = "arg is null" }
        };

        // Act - Empty test code
        var emptyResult = evaluator.Evaluate(branches, "");
        Assert.AreEqual(0, emptyResult.EstimatedCoveragePercentage);
        Assert.AreEqual(2, emptyResult.UncoveredBranches.Count);

        // Act - Tests covering null check and happy path
        var testCode = @"
public class SampleTests
{
    [Fact]
    public void Method_ValidInput_ReturnsSuccess() { }

    [Fact]
    public void Method_WhenNull_ThrowsArgumentNullException() { }
}";
        var coveredResult = evaluator.Evaluate(branches, testCode);

        // Assert
        Assert.AreEqual(100, coveredResult.EstimatedCoveragePercentage);
        Assert.AreEqual(2, coveredResult.CoveredBranchesCount);
        Assert.AreEqual(0, coveredResult.UncoveredBranches.Count);
    }

    [TestMethod]
    public async Task AiCoverageTargetLogic_IterativelyGeneratesUntilTargetCoverageIsReached()
    {
        // Arrange
        var analyzer = new RoslynCodeBranchAnalyzer();
        var evaluator = new BranchCoverageEvaluator();
        var fakeClient = new FakeAiChatClient();

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

        var logic = new AiCoverageTargetLogic(fakeClient, analyzer, evaluator);
        var reports = new List<AiCoverageProgressReport>();
        var progress = new Progress<AiCoverageProgressReport>(r => reports.Add(r));

        var methodCode = @"
public int Calculate(string input)
{
    if (input == null) throw new ArgumentNullException(nameof(input));
    return input.Length;
}";

        // Act
        var result = await logic.GenerateTestsToTargetCoverageAsync(
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
        Assert.IsTrue(result.AchievedCoveragePercentage >= 90);
        Assert.IsTrue(fakeClient.CallCount >= 2, $"Expected at least 2 AI iterations, took {fakeClient.CallCount}");
        Assert.IsTrue(result.GeneratedTestCode.Contains("Calculate_WhenNull_ThrowsException"));
        Assert.IsTrue(result.CoverageSummaryReport.Contains("Code Coverage Test Report"));
    }

    [TestMethod]
    public async Task AiCoverageTargetLogic_WhenCanceled_ThrowsOperationCanceledExceptionInstantly()
    {
        // Arrange
        var fakeClient = new FakeAiChatClient
        {
            ResponseProvider = (sys, user) =>
            {
                Thread.Sleep(50);
                return "```csharp\n[Fact] public void Test() {}\n```";
            }
        };

        var logic = new AiCoverageTargetLogic(fakeClient, new RoslynCodeBranchAnalyzer(), new BranchCoverageEvaluator());
        using var cts = new CancellationTokenSource();
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
        var analyzer = new RoslynCodeBranchAnalyzer();
        var code = @"
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
        var branches = analyzer.AnalyzeBranches(code);

        // Assert
        Assert.IsTrue(branches.Any(b => b.BranchType == CodeBranchType.SwitchCase), "Should find switch expression arms");
        Assert.IsTrue(branches.Any(b => b.BranchType == CodeBranchType.CatchBlock), "Should find catch clause");
    }

    [TestMethod]
    public void RoslynCodeBranchAnalyzer_EmptyOrNoBranches_ReturnsSensibleDefaults()
    {
        // Arrange
        var analyzer = new RoslynCodeBranchAnalyzer();

        // Act & Assert - Empty string
        var emptyBranches = analyzer.AnalyzeBranches("");
        Assert.AreEqual(0, emptyBranches.Count);

        // Act & Assert - Straight linear method
        var linearBranches = analyzer.AnalyzeBranches("public void Log() { Console.WriteLine(\"hi\"); }");
        Assert.AreEqual(1, linearBranches.Count);
        Assert.AreEqual("B1", linearBranches[0].Id);
    }

    [TestMethod]
    public void BranchCoverageEvaluator_EdgeCases_HandlesVariousScenarios()
    {
        // Arrange
        var evaluator = new BranchCoverageEvaluator();

        // Null branches
        var nullResult = evaluator.Evaluate(null, "public void Test() {}");
        Assert.AreEqual(100, nullResult.EstimatedCoveragePercentage);

        // Branches with Switch, Else, Catch
        var branches = new List<CodeBranchDescriptor>
        {
            new CodeBranchDescriptor { Id = "B1", BranchType = CodeBranchType.SwitchCase, Description = "case \"Active\"", ConditionSnippet = "case \"Active\":" },
            new CodeBranchDescriptor { Id = "B2", BranchType = CodeBranchType.ElseBranch, Description = "Else branch", ConditionSnippet = "!isActive" },
            new CodeBranchDescriptor { Id = "B3", BranchType = CodeBranchType.CatchBlock, Description = "catch (IOException)", ConditionSnippet = "IOException" }
        };

        var tests = @"
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
        var result = evaluator.Evaluate(branches, tests);

        // Assert
        Assert.AreEqual(3, result.CoveredBranchesCount);
        Assert.AreEqual(100, result.EstimatedCoveragePercentage);
    }

    [TestMethod]
    public async Task AiCoverageTargetLogic_WhenMaxIterationsReached_ReportsTargetNotMet()
    {
        // Arrange
        var analyzer = new RoslynCodeBranchAnalyzer();
        var evaluator = new BranchCoverageEvaluator();
        var fakeClient = new FakeAiChatClient
        {
            ResponseProvider = (sys, user) => "```csharp\npublic class Tests { [Fact] public void TestOnlyOnePath() {} }\n```"
        };

        var logic = new AiCoverageTargetLogic(fakeClient, analyzer, evaluator);
        var code = @"
public int Check(int a, int b)
{
    if (a < 0) throw new ArgumentOutOfRangeException();
    if (b < 0) throw new ArgumentOutOfRangeException();
    if (a == b) return 0;
    return a + b;
}";

        // Act
        var result = await logic.GenerateTestsToTargetCoverageAsync(
            "Check",
            code,
            targetCoveragePct: 100,
            maxIterations: 2,
            testFramework: "xUnit",
            mockingLib: "Moq");

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsFalse(result.TargetMet, "Should not meet 100% target coverage when test only covers one path");
        Assert.AreEqual(2, result.IterationsUsed);
        Assert.IsTrue(result.CoverageSummaryReport.Contains("Max Iterations Reached"));
    }

    [TestMethod]
    public async Task AiCoverageTargetLogic_Validation_HandlesNullCodeAndUnconfiguredClient()
    {
        // Arrange
        var analyzer = new RoslynCodeBranchAnalyzer();
        var evaluator = new BranchCoverageEvaluator();
        var fakeClient = new FakeAiChatClient();
        var logic = new AiCoverageTargetLogic(fakeClient, analyzer, evaluator);

        // Act - Null code
        var nullCodeResult = await logic.GenerateTestsToTargetCoverageAsync("Test", null, 90, 3, "xUnit", "Moq");
        Assert.IsFalse(nullCodeResult.Success);
        Assert.IsTrue(nullCodeResult.ErrorMessage.Contains("No code provided"));

        // Act - Unconfigured client
        var unconfiguredLogic = new AiCoverageTargetLogic(null, analyzer, evaluator);
        var unconfiguredResult = await unconfiguredLogic.GenerateTestsToTargetCoverageAsync("Test", "public void Foo() {}", 90, 3, "xUnit", "Moq");
        Assert.IsFalse(unconfiguredResult.Success);
        Assert.IsTrue(unconfiguredResult.ErrorMessage.Contains("not configured"));
    }
}
