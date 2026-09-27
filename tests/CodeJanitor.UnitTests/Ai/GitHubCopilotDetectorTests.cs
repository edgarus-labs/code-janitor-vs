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
        long expiresAt = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds();

        return "{\"token\":\"tid=abc;exp=" + expiresAt + "\",\"expires_at\":" + expiresAt + ",\"endpoints\":{\"api\":\"https://api.business.githubcopilot.com\"}}";
    }

    [TestMethod]
    public async Task ExchangeForCopilotSessionAsync_ReusesUnexpiredSessionAcrossCalls()
    {
        FakeHttpHandler handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponseExpiringIn(TimeSpan.FromMinutes(30)));

        GitHubCopilotDetector.CopilotSession first = await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("ghu_cached", handler);
        GitHubCopilotDetector.CopilotSession second = await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("ghu_cached", handler);

        Assert.IsNull(second.ErrorMessage);
        Assert.AreEqual(first.Token, second.Token);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task ExchangeForCopilotSessionAsync_ExchangesAgainWhenSessionIsAboutToExpire()
    {
        FakeHttpHandler handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponseExpiringIn(TimeSpan.FromSeconds(30)));

        await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("ghu_expiring", handler);
        await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("ghu_expiring", handler);

        Assert.HasCount(2, handler.Requests);
    }

    [TestMethod]
    public async Task ExchangeForCopilotSessionAsync_DoesNotReuseFailedExchange()
    {
        FakeHttpHandler handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.ServiceUnavailable, "upstream down");

        await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("ghu_transient", handler);
        await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("ghu_transient", handler);

        Assert.HasCount(2, handler.Requests);
    }

    [TestMethod]
    public async Task ExchangeForCopilotSessionAsync_BlamesTokenOnlyForAuthorizationFailures()
    {
        GitHubCopilotDetector.CopilotSession unauthorized = await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("ghu_a", new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.Unauthorized, "{}"));
        GitHubCopilotDetector.CopilotSession unavailable = await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("ghu_b", new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.ServiceUnavailable, new string('x', 2000)));

        Assert.Contains("not authorized", unauthorized.ErrorMessage);
        Assert.Contains("503", unavailable.ErrorMessage);
        Assert.DoesNotContain("not authorized", unavailable.ErrorMessage);
        Assert.IsLessThan(1000, unavailable.ErrorMessage.Length);
    }

    [TestMethod]
    public async Task FetchCopilotModelsAsync_ReportsWhyFallbackModelsAreShown()
    {
        FakeHttpHandler handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.Unauthorized, "{}");

        GitHubCopilotDetector.CopilotModelsResult result = await GitHubCopilotDetector.FetchCopilotModelsAsync("ghu_bad", handler);

        Assert.AreSequenceEqual(GitHubCopilotDetector.SupportedCopilotModels, result.Models);
        Assert.Contains("token exchange failed", result.ErrorMessage);
    }

    [TestMethod]
    public async Task FetchCopilotModelsAsync_FallsBackWhenTokenExchangeReturnsNonJson()
    {
        FakeHttpHandler handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.OK, "<html>captive portal</html>");

        GitHubCopilotDetector.CopilotModelsResult result = await GitHubCopilotDetector.FetchCopilotModelsAsync("ghu_proxy", handler);

        Assert.AreSequenceEqual(GitHubCopilotDetector.SupportedCopilotModels, result.Models);
        Assert.Contains("invalid response", result.ErrorMessage);
    }

    [TestMethod]
    public async Task CopilotClient_GetChatCompletionContentAsync_DropsCachedSessionRejectedWith401()
    {
        FakeHttpHandler handler = new FakeHttpHandler()
            .On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponseExpiringIn(TimeSpan.FromMinutes(30)))
            .On("https://api.business.githubcopilot.com/chat/completions", HttpStatusCode.Unauthorized, "{}");
        OpenAiCompatibleClient client = new OpenAiCompatibleClient(GitHubCopilotDetector.DefaultCopilotEndpoint, "ghu_revoked", "Authorization", "gpt-5", 5, 0, handler);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.GetChatCompletionContentAsync("system", "user", TestContext.CancellationToken));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.GetChatCompletionContentAsync("system", "user", TestContext.CancellationToken));

        Assert.AreEqual(2, handler.Requests.Count(r => r.Url == ExchangeUrl));
    }

    [TestMethod]
    public async Task CopilotClient_TestModelAsync_DropsCachedSessionRejectedWith401()
    {
        FakeHttpHandler handler = new FakeHttpHandler()
            .On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponseExpiringIn(TimeSpan.FromMinutes(30)))
            .On("https://api.business.githubcopilot.com/chat/completions", HttpStatusCode.Unauthorized, "{}");
        OpenAiCompatibleClient client = new OpenAiCompatibleClient(GitHubCopilotDetector.DefaultCopilotEndpoint, "ghu_revoked_test", "Authorization", "gpt-5", 5, 0, handler);

        await client.TestModelAsync();
        await client.TestModelAsync();

        Assert.AreEqual(2, handler.Requests.Count(r => r.Url == ExchangeUrl));
    }

    [TestMethod]
    public async Task CopilotClient_GetChatCompletionContentAsync_UsesSessionEndpointAndToken()
    {
        FakeHttpHandler handler = new FakeHttpHandler()
            .On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponse)
            .On("https://api.business.githubcopilot.com/chat/completions", HttpStatusCode.OK, ChatResponse);
        OpenAiCompatibleClient client = new OpenAiCompatibleClient(GitHubCopilotDetector.DefaultCopilotEndpoint, "ghu_user", "Authorization", "gpt-5", 5, 0, handler);

        string content = await client.GetChatCompletionContentAsync("system", "user", TestContext.CancellationToken);

        Assert.AreEqual("OK", content);
        Assert.AreEqual("Bearer " + SessionToken, handler.Requests[1].Authorization);
    }

    [TestMethod]
    public async Task CopilotClient_GetChatCompletionContentAsync_DoesNotRetryTokenExchangeFailure()
    {
        FakeHttpHandler handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.Unauthorized, "{}");
        OpenAiCompatibleClient client = new OpenAiCompatibleClient(GitHubCopilotDetector.DefaultCopilotEndpoint, "gho_bad", "Authorization", "gpt-5", 5, 0, handler);

        InvalidOperationException error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.GetChatCompletionContentAsync("system", "user", TestContext.CancellationToken));

        Assert.Contains("token exchange failed", error.Message);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task CopilotClient_TestModelAsync_TimeoutCoversOnlyTheModelRequest()
    {
        FakeHttpHandler handler = new FakeHttpHandler()
            .On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponse, delayMilliseconds: 1500)
            .On("https://api.business.githubcopilot.com/chat/completions", HttpStatusCode.OK, ChatResponse);
        OpenAiCompatibleClient client = new OpenAiCompatibleClient(GitHubCopilotDetector.DefaultCopilotEndpoint, "ghu_slow_exchange", "Authorization", "gpt-5", 1, 0, handler);

        OpenAiCompatibleClient.ConnectionTestResult result = await client.TestModelAsync();

        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
    }

    [TestMethod]
    public void DetectCopilotStatus_ReadsTokenFromHostsJson()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            string localAppData = Path.Combine(root, "local");
            string configDir = Path.Combine(localAppData, "github-copilot");
            Directory.CreateDirectory(configDir);
            File.WriteAllText(Path.Combine(configDir, "hosts.json"), "{\"github.com\":{\"user\":\"octo\",\"oauth_token\":\"ghu_hosts\"}}");

            CopilotDetectionResult result = GitHubCopilotDetector.DetectCopilotStatus(Path.Combine(root, "profile"), localAppData, () => null);

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
        string[] models = GitHubCopilotDetector.SupportedCopilotModels;
        Assert.IsNotNull(models);
        Assert.Contains("gpt-4o", models);
        Assert.Contains("claude-3.5-sonnet", models);
        Assert.Contains("o1-mini", models);
        Assert.Contains("gpt-4o-mini", models);
    }

    [TestMethod]
    public void DetectCopilotStatus_DoesNotThrowAndReturnsValidResult()
    {
        // Act
        CopilotDetectionResult result = GitHubCopilotDetector.DetectCopilotStatus();

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.StatusDescription);
        Assert.AreEqual(GitHubCopilotDetector.DefaultCopilotEndpoint, result.RecommendedEndpoint);
        Assert.AreEqual(GitHubCopilotDetector.DefaultCopilotModel, result.RecommendedModel);
    }

    [TestMethod]
    public async Task FetchCopilotModelsAsync_FallsBackToDefaultModelsWhenModelsEndpointFails()
    {
        FakeHttpHandler handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponse);

        GitHubCopilotDetector.CopilotModelsResult result = await GitHubCopilotDetector.FetchCopilotModelsAsync("ghu_user", handler);

        Assert.AreSequenceEqual(GitHubCopilotDetector.SupportedCopilotModels, result.Models);
        Assert.Contains("404", result.ErrorMessage);
    }

    [TestMethod]
    public async Task ExchangeForCopilotSessionAsync_ReturnsSessionTokenAndApiEndpoint()
    {
        FakeHttpHandler handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponse);

        GitHubCopilotDetector.CopilotSession session = await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("\"Bearer ghu_user\"", handler);

        Assert.IsNull(session.ErrorMessage);
        Assert.AreEqual(SessionToken, session.Token);
        Assert.AreEqual("https://api.business.githubcopilot.com", session.ApiBaseUrl);
        Assert.AreEqual("token ghu_user", handler.Requests.Single().Authorization);
    }

    [TestMethod]
    public async Task ExchangeForCopilotSessionAsync_KeepsExistingSessionTokenWithoutExchange()
    {
        FakeHttpHandler handler = new FakeHttpHandler();

        GitHubCopilotDetector.CopilotSession session = await GitHubCopilotDetector.ExchangeForCopilotSessionAsync(SessionToken, handler);

        Assert.IsNull(session.ErrorMessage);
        Assert.AreEqual(SessionToken, session.Token);
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task ExchangeForCopilotSessionAsync_ReportsRejectedGitHubToken()
    {
        FakeHttpHandler handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}");

        GitHubCopilotDetector.CopilotSession session = await GitHubCopilotDetector.ExchangeForCopilotSessionAsync("gho_notcopilot", handler);

        Assert.IsNull(session.Token);
        Assert.Contains("token exchange failed", session.ErrorMessage);
        Assert.Contains("404", session.ErrorMessage);
    }

    [TestMethod]
    public async Task FetchCopilotModelsAsync_ReturnsAllChatModelsFromSessionApiEndpoint()
    {
        FakeHttpHandler handler = new FakeHttpHandler()
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

        GitHubCopilotDetector.CopilotModelsResult result = await GitHubCopilotDetector.FetchCopilotModelsAsync("ghu_user", handler);

        Assert.AreSequenceEqual(new[] { "gpt-5", "claude-sonnet-4", "gemini-2.5-pro", "kimi-k3" }, result.Models, Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
        Assert.IsNull(result.ErrorMessage);
        Assert.AreEqual("Bearer " + SessionToken, handler.Requests[1].Authorization);
        Assert.AreEqual("visualstudio-chat", handler.Requests[1].Header("Copilot-Integration-Id"));
    }

    [TestMethod]
    public async Task CopilotClient_TestApiConnectionAsync_UsesSessionTokenAndSessionEndpoint()
    {
        FakeHttpHandler handler = new FakeHttpHandler()
            .On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponse)
            .On("https://api.business.githubcopilot.com/models", HttpStatusCode.OK, "{\"data\":[{\"id\":\"gpt-5\"}]}");
        OpenAiCompatibleClient client = new OpenAiCompatibleClient(GitHubCopilotDetector.DefaultCopilotEndpoint, "ghu_user", "Authorization", null, 5, 0, handler);

        OpenAiCompatibleClient.ConnectionTestResult result = await client.TestApiConnectionAsync();

        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        Assert.AreEqual("gpt-5", result.AvailableModels.Single());
        Assert.AreEqual("Bearer " + SessionToken, handler.Requests[1].Authorization);
    }

    [TestMethod]
    public async Task CopilotClient_TestModelAsync_PostsToSessionEndpointWithSessionToken()
    {
        FakeHttpHandler handler = new FakeHttpHandler()
            .On(ExchangeUrl, HttpStatusCode.OK, ExchangeResponse)
            .On("https://api.business.githubcopilot.com/chat/completions", HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}");
        OpenAiCompatibleClient client = new OpenAiCompatibleClient(GitHubCopilotDetector.DefaultCopilotEndpoint, "ghu_user", "Authorization", "gpt-5", 5, 0, handler);

        OpenAiCompatibleClient.ConnectionTestResult result = await client.TestModelAsync();

        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[1].Method);
        Assert.AreEqual("Bearer " + SessionToken, handler.Requests[1].Authorization);
        Assert.AreEqual("visualstudio-chat", handler.Requests[1].Header("Copilot-Integration-Id"));
    }

    [TestMethod]
    public async Task CopilotClient_TestApiConnectionAsync_ReportsTokenExchangeFailureInsteadOfSendingGitHubToken()
    {
        FakeHttpHandler handler = new FakeHttpHandler().On(ExchangeUrl, HttpStatusCode.Unauthorized, "{\"message\":\"Bad credentials\"}");
        OpenAiCompatibleClient client = new OpenAiCompatibleClient(GitHubCopilotDetector.DefaultCopilotEndpoint, "gho_bad", "Authorization", null, 5, 0, handler);

        OpenAiCompatibleClient.ConnectionTestResult result = await client.TestApiConnectionAsync();

        Assert.IsFalse(result.Succeeded);
        Assert.Contains("token exchange failed", result.ErrorMessage);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public void DetectCopilotStatus_PrefersCopilotConfigTokenOverLogsAndCredentialManager()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            string userProfile = Path.Combine(root, "profile");
            string localAppData = Path.Combine(root, "local");
            string configDir = Path.Combine(userProfile, ".config", "github-copilot");
            string logsDir = Path.Combine(localAppData, "Temp", "VSGitHubCopilotLogs");
            Directory.CreateDirectory(configDir);
            Directory.CreateDirectory(logsDir);
            File.WriteAllText(Path.Combine(configDir, "apps.json"), "{\n  \"github.com:Iv1.app\": {\n    \"user\": \"octo\",\n    \"oauth_token\": \"ghu_copilot\"\n  }\n}");
            File.WriteAllText(Path.Combine(logsDir, "a.chat.log"), "HasToken: True");

            CopilotDetectionResult result = GitHubCopilotDetector.DetectCopilotStatus(userProfile, localAppData, () => "gho_credential_manager");

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

        public List<RecordedRequest> Requests { get; } = [];

        public FakeHttpHandler On(string url, HttpStatusCode status, string body, int delayMilliseconds = 0)
        {
            _responses[url] = Tuple.Create(status, body, delayMilliseconds);

            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(request));
            if (!_responses.TryGetValue(request.RequestUri.AbsoluteUri, out Tuple<HttpStatusCode, string, int> configured))
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

        public string Header(string name) => Headers.TryGetValue(name, out string value) ? value : null;
    }

    public TestContext TestContext { get; set; }
}
