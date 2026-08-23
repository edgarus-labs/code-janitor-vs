using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Ai;
using System.Linq;

namespace CodeJanitor.UnitTests.Ai;

[TestClass]
public sealed class GitHubCopilotDetectorTests
{
    [TestMethod]
    public void IsCopilotEndpoint_IdentifiesCopilotUrlsCorrectly()
    {
        Assert.IsTrue(GitHubCopilotDetector.IsCopilotEndpoint("https://api.githubcopilot.com/chat/completions"));
        Assert.IsTrue(GitHubCopilotDetector.IsCopilotEndpoint("https://api.individual.githubcopilot.com/chat/completions"));
        Assert.IsTrue(GitHubCopilotDetector.IsCopilotEndpoint("https://api.business.githubcopilot.com/v1/chat/completions"));

        Assert.IsFalse(GitHubCopilotDetector.IsCopilotEndpoint("http://localhost:11434/v1"));
        Assert.IsFalse(GitHubCopilotDetector.IsCopilotEndpoint("https://api.openai.com/v1"));
        Assert.IsFalse(GitHubCopilotDetector.IsCopilotEndpoint(null));
        Assert.IsFalse(GitHubCopilotDetector.IsCopilotEndpoint(string.Empty));
    }

    [TestMethod]
    public void SupportedCopilotModels_ContainsGpt4oAndClaude()
    {
        var models = GitHubCopilotDetector.SupportedCopilotModels;
        Assert.IsNotNull(models);
        Assert.IsTrue(models.Contains("gpt-4o"));
        Assert.IsTrue(models.Contains("claude-3.5-sonnet"));
        Assert.IsTrue(models.Contains("o1-mini"));
        Assert.IsTrue(models.Contains("gpt-4o-mini"));
    }

    [TestMethod]
    public void DetectCopilotStatus_DoesNotThrowAndReturnsValidResult()
    {
        // Act
        var result = GitHubCopilotDetector.DetectCopilotStatus();

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.StatusDescription);
        Assert.AreEqual(GitHubCopilotDetector.DefaultCopilotEndpoint, result.RecommendedEndpoint);
        Assert.AreEqual(GitHubCopilotDetector.DefaultCopilotModel, result.RecommendedModel);
    }

    [TestMethod]
    public void FetchCopilotModelsAsync_ReturnsValidModelList()
    {
        var task = GitHubCopilotDetector.FetchCopilotModelsAsync(null);
        task.Wait();
        var models = task.Result;

        Assert.IsNotNull(models);
        Assert.IsTrue(models.Count > 0);
        Assert.IsTrue(models.Contains("gpt-4o"));
    }

    [TestMethod]
    public void ExchangeGitHubTokenForCopilotTokenAsync_HandlesNullOrExistingTokenGracefully()
    {
        var task1 = GitHubCopilotDetector.ExchangeGitHubTokenForCopilotTokenAsync(null);
        task1.Wait();
        // May return auto-extracted session token if local VS GitHub token exists, or null
        if (task1.Result is not null)
        {
            Assert.IsTrue(task1.Result.StartsWith("tid=") || task1.Result.StartsWith("gh"));
        }

        var task2 = GitHubCopilotDetector.ExchangeGitHubTokenForCopilotTokenAsync("tid=12345;exp=999");
        task2.Wait();
        Assert.AreEqual("tid=12345;exp=999", task2.Result);
    }
}
