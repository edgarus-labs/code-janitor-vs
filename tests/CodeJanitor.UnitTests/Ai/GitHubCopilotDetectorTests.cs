using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Ai;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.UnitTests.Ai;

[TestClass]
public sealed class GitHubCopilotDetectorTests
{
    private const string ExchangeUrl = "https://api.github.com/copilot_internal/v2/token";
    private const string SessionToken = "tid=abc;exp=9999999999";
    private const string ExchangeResponse = "{\"token\":\"tid=abc;exp=9999999999\",\"endpoints\":{\"api\":\"https://api.business.githubcopilot.com\"}}";
    private const string ChatResponse = "{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}";

    [TestInitialize]
    public void TestInitialize()
    {
        GitHubCopilotDetector.ResetCopilotSessionCache();
    }

    private static string ExchangeResponseExpiringIn(TimeSpan lifetime)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds();
        return "{\"token\":\"tid=abc;exp=" + expiresAt + "\",\"expires_at\":" + expiresAt + ",\"endpoints\":{\"api\":\"https://api.business.githubcopilot.com\"}}";
    }

    [TestMethod]
    public async Task ExchangeForCopilotSessionAsync_ReusesUnexpiredSessionAcrossCalls()
    {
        var handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponseExpiringIn(TimeSpan.FromMinutes(30)));

        var first = await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("ghu_cached", handler);
        var second = await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("ghu_cached", handler);

        Assert.IsNull(second.ErrorMessage);
        Assert.AreEqual(first.Token, second.Token);
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public async Task ExchangeForCopilotSessionAsync_ExchangesAgainWhenSessionIsAboutToExpire()
    {
        var handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponseExpiringIn(TimeSpan.FromSeconds(30)));

        await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("ghu_expiring", handler);
        await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("ghu_expiring", handler);

        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestMethod]
    public async Task ExchangeForCopilotSessionAsync_DoesNotReuseFailedExchange()
    {
        var handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.ServiceUnavailable, "upstream down");

        await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("ghu_transient", handler);
        await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("ghu_transient", handler);

        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestMethod]
    public async Task ExchangeForCopilotSessionAsync_BlamesTokenOnlyForAuthorizationFailures()
    {
        var unauthorized = await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("ghu_a", new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.Unauthorized, "{}"));
        var unavailable = await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("ghu_b", new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.ServiceUnavailable, new string('x', 2000)));

        StringAssert.Contains(unauthorized.ErrorMessage, "not authorized");
        StringAssert.Contains(unavailable.ErrorMessage, "503");
        Assert.IsFalse(unavailable.ErrorMessage.Contains("not authorized"));
        Assert.IsTrue(unavailable.ErrorMessage.Length < 1000);
    }

    [TestMethod]
    public async Task FetchCopilotModelsAsync_ReportsWhyFallbackModelsAreShown()
    {
        var handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.Unauthorized, "{}");

        var result = await GitHubCopilotDetector.FetchCopilotModelsAsync("ghu_bad", handler);

        CollectionAssert.AreEqual(GitHubCopilotDetector.SupportedCopilotModels, result.Models);
        StringAssert.Contains(result.ErrorMessage, "token exchange failed");
    }

    [TestMethod]
    public async Task FetchCopilotModelsAsync_FallsBackWhenTokenExchangeReturnsNonJson()
    {
        var handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.OK, "<html>captive portal</html>");

        var result = await GitHubCopilotDetector.FetchCopilotModelsAsync("ghu_proxy", handler);

        CollectionAssert.AreEqual(GitHubCopilotDetector.SupportedCopilotModels, result.Models);
        StringAssert.Contains(result.ErrorMessage, "invalid response");
    }

    [TestMethod]
    public async Task CopilotClient_GetChatCompletionContentAsync_DropsCachedSessionRejectedWith401()
    {
        var handler = new FakeHttpHandler()
            .On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponseExpiringIn(TimeSpan.FromMinutes(30)))
            .On("https://api.business.githubcopilot.com/chat/completions", HttpStatusCode.Unauthorized, "{}");
        var client = new OpenAiCompatibleClient(GitHubCopilotDetector.DefaultCopilotEndpoint, "ghu_revoked", "Authorization", "gpt-5", 5, 0, handler);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.GetChatCompletionContentAsync("system", "user"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.GetChatCompletionContentAsync("system", "user"));

        Assert.AreEqual(2, handler.Requests.Count(r => r.Url == ExchangeUrl));
    }

    [TestMethod]
    public async Task CopilotClient_TestModelAsync_DropsCachedSessionRejectedWith401()
    {
        var handler = new FakeHttpHandler()
            .On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponseExpiringIn(TimeSpan.FromMinutes(30)))
            .On("https://api.business.githubcopilot.com/chat/completions", HttpStatusCode.Unauthorized, "{}");
        var client = new OpenAiCompatibleClient(GitHubCopilotDetector.DefaultCopilotEndpoint, "ghu_revoked_test", "Authorization", "gpt-5", 5, 0, handler);

        await client.TestModelAsync();
        await client.TestModelAsync();

        Assert.AreEqual(2, handler.Requests.Count(r => r.Url == ExchangeUrl));
    }

    [TestMethod]
    public async Task CopilotClient_GetChatCompletionContentAsync_UsesSessionEndpointAndToken()
    {
        var handler = new FakeHttpHandler()
            .On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponse)
            .On("https://api.business.githubcopilot.com/chat/completions", HttpStatusCode.OK, ChatResponse);
        var client = new OpenAiCompatibleClient(GitHubCopilotDetector.DefaultCopilotEndpoint, "ghu_user", "Authorization", "gpt-5", 5, 0, handler);

        var content = await client.GetChatCompletionContentAsync("system", "user");

        Assert.AreEqual("OK", content);
        Assert.AreEqual("Bearer " + SessionToken, handler.Requests[1].Authorization);
    }

    [TestMethod]
    public async Task CopilotClient_GetChatCompletionContentAsync_DoesNotRetryTokenExchangeFailure()
    {
        var handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.Unauthorized, "{}");
        var client = new OpenAiCompatibleClient(GitHubCopilotDetector.DefaultCopilotEndpoint, "gho_bad", "Authorization", "gpt-5", 5, 0, handler);

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.GetChatCompletionContentAsync("system", "user"));

        StringAssert.Contains(error.Message, "token exchange failed");
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public async Task CopilotClient_TestModelAsync_TimeoutCoversOnlyTheModelRequest()
    {
        var handler = new FakeHttpHandler()
            .On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponse, delayMilliseconds: 1500)
            .On("https://api.business.githubcopilot.com/chat/completions", HttpStatusCode.OK, ChatResponse);
        var client = new OpenAiCompatibleClient(GitHubCopilotDetector.DefaultCopilotEndpoint, "ghu_slow_exchange", "Authorization", "gpt-5", 1, 0, handler);

        var result = await client.TestModelAsync();

        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
    }

    [TestMethod]
    public void DetectCopilotStatus_ReadsTokenFromHostsJson()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var localAppData = Path.Combine(root, "local");
            var configDir = Path.Combine(localAppData, "github-copilot");
            Directory.CreateDirectory(configDir);
            File.WriteAllText(Path.Combine(configDir, "hosts.json"), "{\"github.com\":{\"user\":\"octo\",\"oauth_token\":\"ghu_hosts\"}}");

            var result = GitHubCopilotDetector.DetectCopilotStatus(Path.Combine(root, "profile"), localAppData, () => null);

            Assert.IsTrue(result.IsActive);
            Assert.AreEqual("ghu_hosts", result.DetectedToken);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
    public async Task FetchCopilotModelsAsync_FallsBackToDefaultModelsWhenModelsEndpointFails()
    {
        var handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponse);

        var result = await GitHubCopilotDetector.FetchCopilotModelsAsync("ghu_user", handler);

        CollectionAssert.AreEqual(GitHubCopilotDetector.SupportedCopilotModels, result.Models);
        StringAssert.Contains(result.ErrorMessage, "404");
    }

    [TestMethod]
    public async Task ExchangeForCopilotSessionAsync_ReturnsSessionTokenAndApiEndpoint()
    {
        var handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponse);

        var session = await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("\"Bearer ghu_user\"", handler);

        Assert.IsNull(session.ErrorMessage);
        Assert.AreEqual(SessionToken, session.Token);
        Assert.AreEqual("https://api.business.githubcopilot.com", session.ApiBaseUrl);
        Assert.AreEqual("token ghu_user", handler.Requests.Single().Authorization);
    }

    [TestMethod]
    public async Task ExchangeForCopilotSessionAsync_KeepsExistingSessionTokenWithoutExchange()
    {
        var handler = new FakeHttpHandler();

        var session = await GitHubCopilotDetector.ExchangeForCopilotSessionAsync(SessionToken, handler);

        Assert.IsNull(session.ErrorMessage);
        Assert.AreEqual(SessionToken, session.Token);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task ExchangeForCopilotSessionAsync_ReportsRejectedGitHubToken()
    {
        var handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}");

        var session = await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("gho_notcopilot", handler);

        Assert.IsNull(session.Token);
        StringAssert.Contains(session.ErrorMessage, "token exchange failed");
        StringAssert.Contains(session.ErrorMessage, "404");
    }

    [TestMethod]
    public async Task FetchCopilotModelsAsync_ReturnsAllChatModelsFromSessionApiEndpoint()
    {
        var handler = new FakeHttpHandler()
            .On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponse)
            .On("https://api.business.githubcopilot.com/models", HttpStatusCode.OK,
                "{\"data\":[" +
                "{\"id\":\"gpt-5\",\"capabilities\":{\"type\":\"chat\"}}," +
                "{\"id\":\"claude-sonnet-4\",\"capabilities\":{\"type\":\"chat\"}}," +
                "{\"id\":\"gemini-2.5-pro\",\"capabilities\":{\"type\":\"chat\"}}," +
                "{\"id\":\"kimi-k3\",\"capabilities\":{\"type\":\"chat\"},\"policy\":{\"state\":\"enabled\"},\"supported_endpoints\":[\"/chat/completions\"]}," +
                "{\"id\":\"claude-disabled\",\"capabilities\":{\"type\":\"chat\"},\"policy\":{\"state\":\"disabled\"},\"supported_endpoints\":[\"/chat/completions\"]}," +
                "{\"id\":\"responses-only\",\"capabilities\":{\"type\":\"chat\"},\"policy\":{\"state\":\"enabled\"},\"supported_endpoints\":[\"/responses\"]}," +
                "{\"id\":\"text-embedding-3-small\",\"capabilities\":{\"type\":\"embeddings\"}}]}");

        var result = await GitHubCopilotDetector.FetchCopilotModelsAsync("ghu_user", handler);

        CollectionAssert.AreEquivalent(new[] { "gpt-5", "claude-sonnet-4", "gemini-2.5-pro", "kimi-k3" }, result.Models);
        Assert.IsNull(result.ErrorMessage);
        Assert.AreEqual("Bearer " + SessionToken, handler.Requests[1].Authorization);
        Assert.AreEqual("visualstudio-chat", handler.Requests[1].Header("Copilot-Integration-Id"));
    }

    [TestMethod]
    public async Task CopilotClient_TestApiConnectionAsync_UsesSessionTokenAndSessionEndpoint()
    {
        var handler = new FakeHttpHandler()
            .On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponse)
            .On("https://api.business.githubcopilot.com/models", HttpStatusCode.OK, "{\"data\":[{\"id\":\"gpt-5\"}]}");
        var client = new OpenAiCompatibleClient(GitHubCopilotDetector.DefaultCopilotEndpoint, "ghu_user", "Authorization", null, 5, 0, handler);

        var result = await client.TestApiConnectionAsync();

        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        Assert.AreEqual("gpt-5", result.AvailableModels.Single());
        Assert.AreEqual("Bearer " + SessionToken, handler.Requests[1].Authorization);
    }

    [TestMethod]
    public async Task CopilotClient_TestModelAsync_PostsToSessionEndpointWithSessionToken()
    {
        var handler = new FakeHttpHandler()
            .On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponse)
            .On("https://api.business.githubcopilot.com/chat/completions", HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}");
        var client = new OpenAiCompatibleClient(GitHubCopilotDetector.DefaultCopilotEndpoint, "ghu_user", "Authorization", "gpt-5", 5, 0, handler);

        var result = await client.TestModelAsync();

        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[1].Method);
        Assert.AreEqual("Bearer " + SessionToken, handler.Requests[1].Authorization);
        Assert.AreEqual("visualstudio-chat", handler.Requests[1].Header("Copilot-Integration-Id"));
    }

    [TestMethod]
    public async Task CopilotClient_TestApiConnectionAsync_ReportsTokenExchangeFailureInsteadOfSendingGitHubToken()
    {
        var handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.Unauthorized, "{\"message\":\"Bad credentials\"}");
        var client = new OpenAiCompatibleClient(GitHubCopilotDetector.DefaultCopilotEndpoint, "gho_bad", "Authorization", null, 5, 0, handler);

        var result = await client.TestApiConnectionAsync();

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.ErrorMessage, "token exchange failed");
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public void DetectCopilotStatus_PrefersCopilotConfigTokenOverLogsAndCredentialManager()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var userProfile = Path.Combine(root, "profile");
            var localAppData = Path.Combine(root, "local");
            var configDir = Path.Combine(userProfile, ".config", "github-copilot");
            var logsDir = Path.Combine(localAppData, "Temp", "VSGitHubCopilotLogs");
            Directory.CreateDirectory(configDir);
            Directory.CreateDirectory(logsDir);
            File.WriteAllText(Path.Combine(configDir, "apps.json"), "{\n  \"github.com:Iv1.app\": {\n    \"user\": \"octo\",\n    \"oauth_token\": \"ghu_copilot\"\n  }\n}");
            File.WriteAllText(Path.Combine(logsDir, "a.chat.log"), "HasToken: True");

            var result = GitHubCopilotDetector.DetectCopilotStatus(userProfile, localAppData, () => "gho_credential_manager");

            Assert.IsTrue(result.IsActive);
            Assert.AreEqual("ghu_copilot", result.DetectedToken);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Tuple<HttpStatusCode, string, int>> _responses = new Dictionary<string, Tuple<HttpStatusCode, string, int>>(StringComparer.OrdinalIgnoreCase);

        public List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();

        public FakeHttpHandler On(string url, HttpStatusCode status, string body, int delayMilliseconds = 0)
        {
            _responses[url] = Tuple.Create(status, body, delayMilliseconds);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(request));
            if (!_responses.TryGetValue(request.RequestUri.AbsoluteUri, out var configured))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(string.Empty) };
            }

            await Task.Delay(configured.Item3, cancellationToken);

            return new HttpResponseMessage(configured.Item1) { Content = new StringContent(configured.Item2, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class RecordedRequest
    {
        public RecordedRequest(HttpRequestMessage request)
        {
            Method = request.Method;
            Url = request.RequestUri.AbsoluteUri;
            Headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.FirstOrDefault(), StringComparer.OrdinalIgnoreCase);
        }

        public HttpMethod Method { get; }

        public string Url { get; }

        public string Authorization => Header("Authorization");

        private Dictionary<string, string> Headers { get; }

        public string Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
    }
}
