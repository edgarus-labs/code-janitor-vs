using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// A small OpenAI-compatible chat completion client used by AI-assisted XML documentation.
/// </summary>

internal sealed class OpenAiCompatibleClient : IAiChatClient
{
    /// <summary>
    /// The max attempts.
    /// </summary>
    private const int MaxAttempts = 3;

    /// <summary>
    /// Gets a value indicating whether the client endpoint is configured.
    /// </summary>
    public bool IsConfigured => IsEndpointConfigured(EndpointUrl);

    /// <summary>
    /// ConnectionTestResult represents the outcome of a connection test, indicating success status along with an associated error message when the connection fails.
    /// </summary>
    internal sealed class ConnectionTestResult
    {
        /// <summary>
        /// Gets or sets the succeeded.
        /// </summary>
        internal bool Succeeded { get; set; }

        /// <summary>
        /// Gets or sets the error message.
        /// </summary>
        internal string ErrorMessage { get; set; }

        /// <summary>
        /// Gets the list of available models discovered during the connection test.
        /// </summary>
        internal List<string> AvailableModels { get; set; } = new List<string>();
    }

    internal OpenAiCompatibleClient(string endpointUrl, string apiKey, string apiKeyHeader, string model, int timeoutSeconds)
    {
        EndpointUrl = GetNormalizedEndpointUrl(endpointUrl);
        ApiKey = apiKey?.Trim().Trim('"');
        ApiKeyHeader = string.IsNullOrWhiteSpace(apiKeyHeader) ? "Authorization" : apiKeyHeader.Trim();
        Model = model?.Trim();
        TimeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 30;
    }

    internal OpenAiCompatibleClient(string endpointUrl, string apiKey, string apiKeyHeader, string model, int timeoutSeconds, int contextWindowTokens)
        : this(endpointUrl, apiKey, apiKeyHeader, model, timeoutSeconds)
    {
        ContextWindowTokens = contextWindowTokens > 0 ? contextWindowTokens : 0;
    }

    internal OpenAiCompatibleClient(string endpointUrl, string apiKey, string apiKeyHeader, string model, int timeoutSeconds, int contextWindowTokens, HttpMessageHandler httpMessageHandler)
        : this(endpointUrl, apiKey, apiKeyHeader, model, timeoutSeconds, contextWindowTokens)
    {
        _httpMessageHandler = httpMessageHandler;
    }

    private readonly HttpMessageHandler _httpMessageHandler;

    private GitHubCopilotDetector.CopilotSession _copilotSession;

    private HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var client = _httpMessageHandler is null ? new HttpClient() : new HttpClient(_httpMessageHandler, disposeHandler: false);
        client.Timeout = timeout;

        return client;
    }

    /// <summary>
    /// Gets the endpoint url.
    /// </summary>
    internal string EndpointUrl { get; }

    /// <summary>
    /// Gets the api key.
    /// </summary>
    internal string ApiKey { get; }

    /// <summary>
    /// Gets the api key header.
    /// </summary>
    internal string ApiKeyHeader { get; }

    /// <summary>
    /// Gets the model.
    /// </summary>
    internal string Model { get; }

    /// <summary>
    /// Gets the timeout seconds.
    /// </summary>
    internal int TimeoutSeconds { get; }

    /// <summary>
    /// Gets the configured minimum context window (in tokens), or 0 if not configured. This is
    /// only ever sent to the endpoint (as a best-effort "num_ctx" field) when the endpoint looks
    /// like a local server, since hosted OpenAI-compatible APIs commonly reject unrecognized
    /// request fields with a validation error.
    /// </summary>
    internal int ContextWindowTokens { get; }

    /// <summary>
    /// Determines whether the given endpoint URL refers to a local resource by returning true for valid absolute URIs that are loopback, have a host of &quot;localhost&quot;, or resolve to an IPv4 address within the private ranges 10.0.0.0/8, 172.16.0.0/12, or 192.168.0.0/16, and false otherwise.
    /// </summary>
    /// <param name="endpointUrl">The endpoint url.</param>
    /// <returns>A bool value produced by this method.</returns>
    internal static bool IsLocalEndpoint(string endpointUrl)
    {
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.IsLoopback)
        {
            return true;
        }

        var host = uri.Host;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (System.Net.IPAddress.TryParse(host, out var ipAddress))
        {
            var bytes = ipAddress.GetAddressBytes();
            if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && bytes.Length == 4)
            {
                // 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16
                if (bytes[0] == 10) return true;
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                if (bytes[0] == 192 && bytes[1] == 168) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Normalizes an endpoint URL by trimming whitespace, returning the input unchanged if it is null/whitespace or not a valid absolute URI, preserving it if it already ends with a recognized completion path (`/chat/completions`, `/completions`, `/messages`, or `/generate`), and otherwise appending `/chat/completions` after stripping any trailing slashes.
    /// </summary>
    /// <param name="endpointUrl">The endpoint url.</param>
    /// <returns>A string value produced by this method.</returns>
    internal static string GetNormalizedEndpointUrl(string endpointUrl)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            return endpointUrl;
        }

        var url = endpointUrl.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ||
            url.EndsWith("/completions", StringComparison.OrdinalIgnoreCase) ||
            url.EndsWith("/messages", StringComparison.OrdinalIgnoreCase) ||
            url.EndsWith("/generate", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        url = url.TrimEnd('/');

        return url + "/chat/completions";
    }

    /// <summary>
    /// Validates that the specified endpoint URL is a non-empty, well-formed absolute URI with an HTTP or HTTPS scheme, returning false otherwise without throwing exceptions.
    /// </summary>
    /// <param name="endpointUrl">The endpoint url.</param>
    /// <param name="apiKey">The api key.</param>
    /// <returns>A bool value produced by this method.</returns>
    internal static bool IsEndpointConfigured(string endpointUrl, string apiKey = null)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(endpointUrl.Trim(), UriKind.Absolute, out var endpoint))
        {
            return false;
        }

        return endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps;
    }

    /// <summary>
    /// Computes the models endpoint URL from the given endpoint URL.
    /// Strips known completion suffixes and appends /models.
    /// </summary>
    /// <param name="endpointUrl">The endpoint url.</param>
    /// <returns>A string value representing the models endpoint URL.</returns>
    internal static string GetModelsEndpointUrl(string endpointUrl)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            return endpointUrl;
        }

        var url = endpointUrl.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return url;
        }

        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return url.Substring(0, url.Length - "/chat/completions".Length).TrimEnd('/') + "/models";
        }

        if (url.EndsWith("/completions", StringComparison.OrdinalIgnoreCase))
        {
            return url.Substring(0, url.Length - "/completions".Length).TrimEnd('/') + "/models";
        }

        if (url.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
        {
            return url.Substring(0, url.Length - "/messages".Length).TrimEnd('/') + "/models";
        }

        if (url.EndsWith("/generate", StringComparison.OrdinalIgnoreCase))
        {
            return url.Substring(0, url.Length - "/generate".Length).TrimEnd('/') + "/models";
        }

        url = url.TrimEnd('/');
        return url + "/models";
    }

    /// <summary>
    /// Parses available model identifiers from an OpenAI-compatible /models JSON response.
    /// Supports standard OpenAI format (data[].id) and Ollama/alternative format (models[].name / id).
    /// </summary>
    /// <param name="json">The response JSON string.</param>
    /// <returns>A list of model names or IDs sorted alphabetically.</returns>
    internal static List<string> ParseModelIds(string json)
    {
        var models = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return models;
        }

        try
        {
            var serializer = new JavaScriptSerializer();
            var dict = serializer.Deserialize<Dictionary<string, object>>(json);
            if (dict is null)
            {
                return models;
            }

            if (dict.TryGetValue("data", out var dataObj) && dataObj is IList dataList)
            {
                foreach (var item in dataList)
                {
                    if (item is Dictionary<string, object> modelDict &&
                        modelDict.TryGetValue("id", out var idObj) && idObj is not null)
                    {
                        var id = idObj.ToString().Trim();
                        if (!string.IsNullOrEmpty(id) && !models.Contains(id))
                        {
                            models.Add(id);
                        }
                    }
                }
            }
            else if (dict.TryGetValue("models", out var modelsObj) && modelsObj is IList modelsList)
            {
                foreach (var item in modelsList)
                {
                    if (item is Dictionary<string, object> modelDict)
                    {
                        object nameObj = null;
                        if (modelDict.TryGetValue("name", out nameObj) ||
                            modelDict.TryGetValue("id", out nameObj) ||
                            modelDict.TryGetValue("model", out nameObj))
                        {
                            if (nameObj is not null)
                            {
                                var id = nameObj.ToString().Trim();
                                if (!string.IsNullOrEmpty(id) && !models.Contains(id))
                                {
                                    models.Add(id);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Best effort parsing: return any models collected
        }

        models.Sort(StringComparer.OrdinalIgnoreCase);
        return models;
    }

    /// <summary>
    /// Sends an authenticated GET request to the models endpoint (e.g. /v1/models) to verify endpoint
    /// reachability and authentication without invoking or loading any specific model into memory.
    /// If successful, returns the list of available models.
    /// </summary>
    /// <returns>A Task&lt;ConnectionTestResult&gt; value produced by this method.</returns>
    internal async Task<ConnectionTestResult> TestApiConnectionAsync()
    {
        if (!IsEndpointConfigured(EndpointUrl))
        {
            return new ConnectionTestResult { ErrorMessage = "AI endpoint is not configured." };
        }

        var authError = await PrepareAuthenticationAsync().ConfigureAwait(false);
        if (authError is not null)
        {
            return new ConnectionTestResult { ErrorMessage = authError };
        }

        var modelsUrl = GetModelsEndpointUrl(EndpointUrl);
        var timeoutSeconds = TimeoutSeconds > 0 ? TimeoutSeconds : 30;

        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
        using (var httpClient = CreateHttpClient(System.Threading.Timeout.InfiniteTimeSpan))
        {
            var result = await QueryModelsEndpointAsync(httpClient, modelsUrl, timeout.Token).ConfigureAwait(false);
            if (result.Succeeded || result.IsAuthError)
            {
                return result.ToConnectionTestResult();
            }

            // Fallback: if modelsUrl returned 404 and didn't include "/v1", try with "/v1/models"
            if (result.StatusCode == System.Net.HttpStatusCode.NotFound &&
                !modelsUrl.Contains("/v1/") &&
                !modelsUrl.EndsWith("/v1/models", StringComparison.OrdinalIgnoreCase))
            {
                var v1Url = modelsUrl.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
                    ? modelsUrl.Substring(0, modelsUrl.Length - "/models".Length).TrimEnd('/') + "/v1/models"
                    : null;

                if (!string.IsNullOrEmpty(v1Url))
                {
                    var fallbackResult = await QueryModelsEndpointAsync(httpClient, v1Url, timeout.Token).ConfigureAwait(false);
                    if (fallbackResult.Succeeded || fallbackResult.IsAuthError)
                    {
                        return fallbackResult.ToConnectionTestResult();
                    }
                }
            }

            return result.ToConnectionTestResult();
        }
    }

    /// <summary>
    /// Fetches the available models from the endpoint via GET /models.
    /// </summary>
    /// <returns>List of model IDs or names.</returns>
    internal async Task<List<string>> FetchAvailableModelsAsync()
    {
        var result = await TestApiConnectionAsync().ConfigureAwait(false);
        return result.AvailableModels;
    }

    private sealed class ModelsFetchResult
    {
        internal bool Succeeded { get; set; }
        internal bool IsAuthError { get; set; }
        internal System.Net.HttpStatusCode? StatusCode { get; set; }
        internal string ErrorMessage { get; set; }
        internal List<string> AvailableModels { get; set; } = new List<string>();

        internal ConnectionTestResult ToConnectionTestResult()
        {
            return new ConnectionTestResult
            {
                Succeeded = Succeeded,
                ErrorMessage = ErrorMessage,
                AvailableModels = AvailableModels
            };
        }
    }

    private async Task<ModelsFetchResult> QueryModelsEndpointAsync(HttpClient httpClient, string url, CancellationToken cancellationToken)
    {
        using (var message = new HttpRequestMessage(HttpMethod.Get, url))
        {
            ApplyAuthentication(message);

            try
            {
                using (var response = await httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false))
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                        response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        return new ModelsFetchResult
                        {
                            IsAuthError = true,
                            StatusCode = response.StatusCode,
                            ErrorMessage = $"AI endpoint rejected the request ({(int)response.StatusCode} {response.ReasonPhrase}). Check the API key. {Truncate(responseText, 512)}"
                        };
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var models = ParseModelIds(responseText);

                        return new ModelsFetchResult
                        {
                            Succeeded = true,
                            StatusCode = response.StatusCode,
                            AvailableModels = models
                        };
                    }

                    var errText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    return new ModelsFetchResult
                    {
                        StatusCode = response.StatusCode,
                        ErrorMessage = $"AI endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(errText, 512)}"
                    };
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new ModelsFetchResult
                {
                    ErrorMessage = $"Connection test timed out after {TimeoutSeconds} seconds."
                };
            }
            catch (Exception ex)
            {
                return new ModelsFetchResult { ErrorMessage = ex.Message };
            }
        }
    }

    /// <summary>
    /// Sends an authenticated POST request with a probe prompt to the configured AI endpoint using
    /// the configured model, then returns a `ConnectionTestResult` indicating success, an
    /// HTTP/status error, a missing/malformed response body, a timeout, or any thrown exception.
    /// Use this to verify the configured model name actually works, as opposed to
    /// <see cref="TestApiConnectionAsync" /> which only checks endpoint reachability/authentication.
    /// </summary>
    /// <returns>A Task&lt;ConnectionTestResult&gt; value produced by this method.</returns>
    internal async Task<ConnectionTestResult> TestModelAsync()
    {
        if (!IsEndpointConfigured(EndpointUrl))
        {
            return new ConnectionTestResult { ErrorMessage = "AI endpoint is not configured." };
        }

        var authError = await PrepareAuthenticationAsync().ConfigureAwait(false);
        if (authError is not null)
        {
            return new ConnectionTestResult { ErrorMessage = authError };
        }

        var requestJson = BuildRequestJson("Reply with exactly: OK", 512);
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds)))
        using (var httpClient = CreateHttpClient(System.Threading.Timeout.InfiniteTimeSpan))
        using (var message = new HttpRequestMessage(HttpMethod.Post, EndpointUrl))
        {
            ApplyAuthentication(message);
            message.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            try
            {
                using (var response = await httpClient.SendAsync(message, timeout.Token).ConfigureAwait(false))
                {
                    var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        return new ConnectionTestResult
                        {
                            ErrorMessage = $"Model '{Model}' request failed with {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(responseText, 512)}"
                        };
                    }

                    var content = TryExtractContentFromChatResponse(responseText);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        return new ConnectionTestResult
                        {
                            ErrorMessage = $"Model '{Model}' response did not contain message content. Response: {Truncate(responseText, 512)}"
                        };
                    }

                    return new ConnectionTestResult { Succeeded = true };
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return new ConnectionTestResult
                {
                    ErrorMessage = $"Model test timed out after {TimeoutSeconds} seconds."
                };
            }
            catch (Exception ex)
            {
                return new ConnectionTestResult { ErrorMessage = ex.Message };
            }
        }
    }

    /// <summary>
    /// Asynchronously requests a chat completion from the AI endpoint using the specified system and user prompts.
    /// </summary>
    public async Task<string> GetChatCompletionContentAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default(CancellationToken), int maxTokens = 2048)
    {
        if (!IsEndpointConfigured(EndpointUrl))
        {
            throw new InvalidOperationException("AI endpoint is not configured.");
        }

        var authError = await PrepareAuthenticationAsync().ConfigureAwait(false);
        if (authError is not null)
        {
            throw new InvalidOperationException(authError);
        }

        var requestJson = BuildRequestJson(userPrompt, maxTokens, systemPrompt);
        string lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using (var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds)))
                using (var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token))
                using (var httpClient = CreateHttpClient(TimeSpan.FromSeconds(TimeoutSeconds)))
                using (var message = new HttpRequestMessage(HttpMethod.Post, EndpointUrl))
                {
                    ApplyAuthentication(message);
                    message.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                    using (var response = await httpClient.SendAsync(message, linkedSource.Token).ConfigureAwait(false))
                    {
                        var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (!response.IsSuccessStatusCode)
                        {
                            lastError = $"AI endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(responseText, 512)}";
                            if (attempt < MaxAttempts && IsTransientStatusCode((int)response.StatusCode))
                            {
                                continue;
                            }

                            throw new InvalidOperationException(lastError);
                        }

                        var content = TryExtractContentFromChatResponse(responseText);
                        if (string.IsNullOrWhiteSpace(content))
                        {
                            throw new InvalidOperationException($"AI endpoint response did not contain message content. Response: {Truncate(responseText, 512)}");
                        }

                        return content.Trim();
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"AI request timed out after {TimeoutSeconds} seconds.");
            }
            catch (Exception ex) when (attempt < MaxAttempts && !(ex is OperationCanceledException))
            {
                lastError = ex.Message;
            }
        }

        throw new InvalidOperationException(lastError ?? "Unknown AI endpoint error.");
    }

    /// <summary>
    /// TryGenerateDocumentation normalizes maxTokens to either MaxTokens or a 256-token default and delegates to TrySendChatCompletion, returning a bool with completionText and errorMessage populated as out parameters.
    /// </summary>
    /// <param name="prompt">The prompt.</param>
    /// <param name="completionText">The completion text.</param>
    /// <param name="errorMessage">The error message.</param>
    /// <param name="maxTokens">The max tokens.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A bool value produced by this method.</returns>
    internal bool TryGenerateDocumentation(string prompt, out string completionText, out string errorMessage, int maxTokens = 256, CancellationToken cancellationToken = default(CancellationToken), string systemPrompt = null)
    {
        var safeMaxTokens = maxTokens > 0 ? maxTokens : 256;

        return TrySendChatCompletion(prompt, safeMaxTokens, out completionText, out errorMessage, MaxAttempts, cancellationToken, systemPrompt);
    }

    /// <summary>
    /// TrySendChatCompletion sends an authenticated POST request with a JSON payload to a configured AI chat endpoint, retrying up to maxAttempts on transient status codes, and returns true with the extracted and trimmed message content via completionText (or false with a diagnostic message in errorMessage) while respecting the supplied CancellationToken.
    /// </summary>
    /// <param name="userPrompt">The user prompt.</param>
    /// <param name="maxTokens">The max tokens.</param>
    /// <param name="completionText">The completion text.</param>
    /// <param name="errorMessage">The error message.</param>
    /// <param name="maxAttempts">The max attempts.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="systemPrompt">The system prompt.</param>
    /// <returns>A bool value produced by this method.</returns>
    private bool TrySendChatCompletion(string userPrompt, int maxTokens, out string completionText, out string errorMessage, int maxAttempts = MaxAttempts, CancellationToken cancellationToken = default(CancellationToken), string systemPrompt = null)
    {
        completionText = null;
        errorMessage = null;

        if (!IsEndpointConfigured(EndpointUrl))
        {
            errorMessage = "AI XML documentation endpoint is not configured.";

            return false;
        }

        var authError = PrepareAuthenticationAsync().GetAwaiter().GetResult();
        if (authError is not null)
        {
            errorMessage = authError;

            return false;
        }

        var requestJson = BuildRequestJson(userPrompt, maxTokens, systemPrompt);
        string lastError = null;
        Exception lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                errorMessage = "AI request canceled.";

                return false;
            }

            try
            {
                using (var httpClient = CreateHttpClient(TimeSpan.FromSeconds(TimeoutSeconds)))
                using (var message = new HttpRequestMessage(HttpMethod.Post, EndpointUrl))
                {
                    ApplyAuthentication(message);

                    message.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                    var response = httpClient.SendAsync(message, cancellationToken).GetAwaiter().GetResult();
                    var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (!response.IsSuccessStatusCode)
                    {
                        lastError = $"AI endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(responseText, 512)}";
                        if (attempt < maxAttempts && IsTransientStatusCode((int)response.StatusCode))
                        {
                            continue;
                        }

                        errorMessage = lastError;

                        return false;
                    }

                    var content = TryExtractContentFromChatResponse(responseText);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        errorMessage = $"AI endpoint response did not contain message content. Response: {Truncate(responseText, 512)}";

                        return false;
                    }

                    completionText = content.Trim();

                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                errorMessage = "AI request canceled.";

                return false;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt >= maxAttempts)
                {
                    break;
                }
            }
        }

        errorMessage = lastError ?? lastException?.Message ?? "Unknown AI endpoint error.";

        return false;
    }

    /// <summary>
    /// Determines whether an HTTP status code represents a transient failure (408 Request Timeout, 429 Too Many Requests, or any 5xx server error), returning true if the code should be retried, without throwing exceptions.
    /// </summary>
    /// <param name="statusCode">The status code.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool IsTransientStatusCode(int statusCode)
    {
        return statusCode == 408 || statusCode == 429 || (statusCode >= 500 && statusCode <= 599);
    }

    private async Task<string> PrepareAuthenticationAsync()
    {
        if (!GitHubCopilotDetector.IsCopilotEndpoint(EndpointUrl))
        {
            return null;
        }

        _copilotSession = await GitHubCopilotDetector.ExchangeForCopilotSessionAsync(ApiKey, _httpMessageHandler).ConfigureAwait(false);

        return _copilotSession.ErrorMessage;
    }

    private void ApplyAuthentication(HttpRequestMessage message)
    {
        var rawKey = ApiKey;

        if (GitHubCopilotDetector.IsCopilotEndpoint(EndpointUrl))
        {
            rawKey = _copilotSession.Token;
            message.RequestUri = GitHubCopilotDetector.ResolveCopilotApiUri(message.RequestUri, _copilotSession.ApiBaseUrl);
            GitHubCopilotDetector.ApplyCopilotHeaders(message.Headers);
        }

        if (string.Equals(ApiKeyHeader, "Authorization", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(rawKey))
            {
                message.Headers.TryAddWithoutValidation("Authorization", rawKey.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? rawKey : "Bearer " + rawKey);
            }
        }
        else if (!string.IsNullOrEmpty(rawKey))
        {
            message.Headers.TryAddWithoutValidation(ApiKeyHeader, rawKey);
        }
    }

    /// <summary>
    /// Builds and returns an OpenAI-compatible chat completion JSON request payload from a system prompt, user prompt, temperature, and max tokens, conditionally appending model and num_ctx fields based on endpoint configuration.
    /// </summary>
    /// <param name="userPrompt">The user prompt.</param>
    /// <param name="maxTokens">The max tokens.</param>
    /// <returns>A string value produced by this method.</returns>
    private string BuildRequestJson(string userPrompt, int maxTokens)
    {
        return BuildRequestJson(userPrompt, maxTokens, null);
    }

    /// <summary>
    /// Builds and returns a serialized OpenAI-compatible chat completion request JSON string from the supplied prompt, token limit, and optional system prompt, applying a fallback default system message, a fixed low temperature of 0.2, non-streaming mode, and conditionally appending a `model` field and a `num_ctx` context-window hint when targeting a local endpoint.
    /// </summary>
    /// <param name="userPrompt">The user prompt.</param>
    /// <param name="maxTokens">The max tokens.</param>
    /// <param name="systemPrompt">The system prompt.</param>
    /// <returns>A string value produced by this method.</returns>
    private string BuildRequestJson(string userPrompt, int maxTokens, string systemPrompt)
    {
        var effectiveSystemPrompt = !string.IsNullOrWhiteSpace(systemPrompt)
            ? systemPrompt
            : "You are a C# documentation assistant. Output concise and accurate technical text.";

        var request = new Dictionary<string, object>
        {
            ["messages"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["role"] = "system",
                    ["content"] = effectiveSystemPrompt
                },
                new Dictionary<string, object>
                {
                    ["role"] = "user",
                    ["content"] = userPrompt ?? string.Empty
                }
            },
            ["temperature"] = 0.2,
            ["max_tokens"] = maxTokens,
            ["stream"] = false
        };

        if (!string.IsNullOrWhiteSpace(Model))
        {
            request["model"] = Model;
        }

        // Best-effort context window hint for local OpenAI-compatible servers (e.g. llama.cpp,
        // text-generation-webui, LM Studio) that accept a "num_ctx" field. Real Ollama servers
        // ignore this field on the OpenAI-compatible endpoint (context size must be configured
        // server-side via a Modelfile), and hosted/cloud OpenAI-compatible APIs often reject
        // unrecognized fields outright, so this is only ever sent for endpoints that look local.
        if (ContextWindowTokens > 0 && IsLocalEndpoint(EndpointUrl))
        {
            request["num_ctx"] = ContextWindowTokens;
        }

        var serializer = new JavaScriptSerializer();

        return serializer.Serialize(request);
    }

    /// <summary>
    /// ries to extract and return trimmed message content from a chat response by first attempting JSON deserialization and falling back to SSE event stream parsing, returning null when the input is blank or no content is found.
    /// </summary>
    /// <param name="responseText">The response text.</param>
    /// <returns>A string value produced by this method.</returns>
    internal static string TryExtractContentFromChatResponse(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        var payload = TryDeserializeAny(responseText);
        if (payload is not null)
        {
            var content = ExtractContentFromNode(payload);
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content.Trim();
            }
        }

        var sseContent = TryExtractContentFromEventStream(responseText);

        return string.IsNullOrWhiteSpace(sseContent) ? null : sseContent.Trim();
    }

    /// <summary>
    /// Attempts to deserialize a JSON string into an object using JavaScriptSerializer, returning null for null/whitespace input or when ArgumentException or InvalidOperationException occurs.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A object value produced by this method.</returns>
    private static object TryDeserializeAny(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return new JavaScriptSerializer().DeserializeObject(text);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// ExtractContentFromNode returns null for null or unrecognized nodes, the trimmed string itself for string nodes (treating empty strings as null), and otherwise delegates to ExtractContentFromDictionary or ExtractContentFromList to produce a string representation.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string ExtractContentFromNode(object node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is string text)
        {
            return string.IsNullOrEmpty(text) ? null : text;
        }

        if (node is IDictionary dict)
        {
            return ExtractContentFromDictionary(dict);
        }

        if (node is IList list)
        {
            return ExtractContentFromList(list);
        }

        return null;
    }

    /// <summary>
    /// Extracts the first non-empty content string from an AI response dictionary, checking OpenAI/DeepSeek/OpenRouter-style &quot;choices&quot; arrays (handling message, delta, text, and content fields) and Google Gemini-style &quot;candidates&quot; arrays, returning null if the dictionary is empty or none of the expected fields contain usable text.
    /// </summary>
    /// <param name="dict">The dict.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string ExtractContentFromDictionary(IDictionary dict)
    {
        if (dict is null)
        {
            return null;
        }

        // 1. Standard OpenAI / DeepSeek / OpenRouter choices array
        if (dict["choices"] is IList choices && choices.Count > 0)
        {
            foreach (var choiceObj in choices)
            {
                if (choiceObj is IDictionary choice)
                {
                    if (choice["message"] is IDictionary message)
                    {
                        var content = ReadContentField(message);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            return content;
                        }
                    }

                    if (choice["delta"] is IDictionary delta)
                    {
                        var content = ReadContentField(delta);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            return content;
                        }
                    }

                    if (choice["text"] is not null)
                    {
                        var text = ExtractStringOrBlocks(choice["text"]);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
                        }
                    }

                    if (choice["content"] is not null)
                    {
                        var content = ExtractStringOrBlocks(choice["content"]);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            return content;
                        }
                    }
                }
                else if (choiceObj is string strChoice)
                {
                    if (!string.IsNullOrEmpty(strChoice))
                    {
                        return strChoice;
                    }
                }
            }
        }

        // 2. Google Gemini candidates format
        if (dict["candidates"] is IList candidates && candidates.Count > 0)
        {
            foreach (var candObj in candidates)
            {
                if (candObj is IDictionary cand)
                {
                    if (cand["content"] is not null)
                    {
                        var content = ExtractStringOrBlocks(cand["content"]);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            return content;
                        }
                    }

                    if (cand["message"] is IDictionary candMsg)
                    {
                        var content = ReadContentField(candMsg);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            return content;
                        }
                    }

                    if (cand["text"] is not null)
                    {
                        var text = ExtractStringOrBlocks(cand["text"]);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
                        }
                    }
                }
            }
        }

        // 3. Wrapped in "data" property (array, object, or string) - common in gateways, OmniRoute, etc.
        if (dict["data"] is not null)
        {
            var dataContent = ExtractContentFromNode(dict["data"]);
            if (!string.IsNullOrWhiteSpace(dataContent))
            {
                return dataContent;
            }
        }

        // 4. Wrapped in "result" property
        if (dict["result"] is not null)
        {
            var resultContent = ExtractContentFromNode(dict["result"]);
            if (!string.IsNullOrWhiteSpace(resultContent))
            {
                return resultContent;
            }
        }

        // 5. Direct "message" or "delta" object (e.g. Ollama /api/chat or gateway wrapper)
        if (dict["message"] is IDictionary messageDict)
        {
            var content = ReadContentField(messageDict);
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        if (dict["delta"] is IDictionary deltaDict)
        {
            var content = ReadContentField(deltaDict);
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        // 6. Direct response/content/output fields (Claude / Ollama / HuggingFace / TGI)
        foreach (var key in new[] { "content", "response", "output", "completion", "generated_text", "text" })
        {
            if (dict[key] is not null)
            {
                var text = ExtractStringOrBlocks(dict[key]);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        // 7. Fallback reasoning fields
        foreach (var key in new[] { "reasoning_content", "reasoning", "thought", "thinking" })
        {
            if (dict[key] is not null)
            {
                var text = ExtractStringOrBlocks(dict[key]);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the first non-whitespace content extracted from each item in the list via ExtractContentFromNode, or null if the list is empty or no item yields content.
    /// </summary>
    /// <param name="list">The list.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string ExtractContentFromList(IList list)
    {
        if (list is null || list.Count == 0)
        {
            return null;
        }

        foreach (var item in list)
        {
            var content = ExtractContentFromNode(item);
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the assistant text from a message or delta object, falling back to the reasoning
    /// fields used by DeepSeek-R1 style models when the content is empty.
    /// </summary>
    private static string ReadContentField(IDictionary source)
    {
        if (source is null)
        {
            return null;
        }

        // 1. Primary content fields
        foreach (var key in new[] { "content", "text", "value" })
        {
            if (source[key] is not null)
            {
                var val = ExtractStringOrBlocks(source[key]);
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val;
                }
            }
        }

        // 2. Reasoning / thinking fallback fields (e.g. DeepSeek-R1, Qwen, Claude thinking)
        foreach (var key in new[] { "reasoning_content", "reasoning", "thought", "thinking" })
        {
            if (source[key] is not null)
            {
                var val = ExtractStringOrBlocks(source[key]);
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Recursively extracts and concatenates string content from nested object structures, handling strings, lists of strings/dictionaries, and dictionaries by inspecting known keys like text, content, parts, value, and various reasoning fields, returning null if no content is found.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string ExtractStringOrBlocks(object value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string str)
        {
            return string.IsNullOrEmpty(str) ? null : str;
        }

        if (value is IList list)
        {
            var builder = new StringBuilder();
            foreach (var item in list)
            {
                if (item is string itemStr)
                {
                    builder.Append(itemStr);
                }
                else if (item is IDictionary itemDict)
                {
                    string piece = null;
                    if (itemDict["text"] is not null)
                    {
                        piece = Convert.ToString(itemDict["text"]);
                    }
                    else if (itemDict["content"] is not null)
                    {
                        piece = ExtractStringOrBlocks(itemDict["content"]);
                    }
                    else if (itemDict["parts"] is not null)
                    {
                        piece = ExtractStringOrBlocks(itemDict["parts"]);
                    }
                    else if (itemDict["value"] is not null)
                    {
                        piece = Convert.ToString(itemDict["value"]);
                    }
                    else if (itemDict["thought"] is not null || itemDict["thinking"] is not null || itemDict["reasoning_content"] is not null || itemDict["reasoning"] is not null)
                    {
                        piece = Convert.ToString(itemDict["thought"] ?? itemDict["thinking"] ?? itemDict["reasoning_content"] ?? itemDict["reasoning"]);
                    }

                    if (!string.IsNullOrEmpty(piece))
                    {
                        builder.Append(piece);
                    }
                }
            }

            var result = builder.ToString();

            return string.IsNullOrEmpty(result) ? null : result;
        }

        if (value is IDictionary dict)
        {
            if (dict["parts"] is not null)
            {
                var partsText = ExtractStringOrBlocks(dict["parts"]);
                if (!string.IsNullOrWhiteSpace(partsText))
                {
                    return partsText;
                }
            }

            if (dict["text"] is not null)
            {
                var text = Convert.ToString(dict["text"]);
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }

            if (dict["value"] is not null)
            {
                var val = Convert.ToString(dict["value"]);
                if (!string.IsNullOrEmpty(val))
                {
                    return val;
                }
            }

            if (dict["content"] is not null)
            {
                var cont = ExtractStringOrBlocks(dict["content"]);
                if (!string.IsNullOrWhiteSpace(cont))
                {
                    return cont;
                }
            }
        }

        var fallback = Convert.ToString(value);

        return string.IsNullOrEmpty(fallback) ? null : fallback;
    }

    /// <summary>
    /// Reassembles a server-sent event response, which local servers may return even when the
    /// request asked for a non-streaming completion.
    /// </summary>
    private static string TryExtractContentFromEventStream(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        var contentBuilder = new StringBuilder();
        var reasoningBuilder = new StringBuilder();
        var generalBuilder = new StringBuilder();
        var hasSseData = false;

        foreach (var rawLine in responseText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            hasSseData = true;
            var chunkText = line.Substring("data:".Length).Trim();
            if (chunkText.Length == 0 || string.Equals(chunkText, "[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var chunkNode = TryDeserializeAny(chunkText);
            if (chunkNode is null)
            {
                continue;
            }

            ExtractStreamChunkPiece(chunkNode, contentBuilder, reasoningBuilder, generalBuilder);
        }

        if (!hasSseData)
        {
            return null;
        }

        if (contentBuilder.Length > 0)
        {
            var res = contentBuilder.ToString();

            return string.IsNullOrWhiteSpace(res) ? null : res;
        }

        if (generalBuilder.Length > 0)
        {
            var res = generalBuilder.ToString();

            return string.IsNullOrWhiteSpace(res) ? null : res;
        }

        if (reasoningBuilder.Length > 0)
        {
            var res = reasoningBuilder.ToString();

            return string.IsNullOrWhiteSpace(res) ? null : res;
        }

        return null;
    }

    /// <summary>
    /// ursively traverses a streaming response node (string, IList, or IDictionary), extracting and appending message content and reasoning/thinking text into separate StringBuilder accumulators based on OpenAI-style delta/message structures and Claude-style delta fields.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="contentBuilder">The content builder.</param>
    /// <param name="reasoningBuilder">The reasoning builder.</param>
    /// <param name="generalBuilder">The general builder.</param>
    private static void ExtractStreamChunkPiece(object node, StringBuilder contentBuilder, StringBuilder reasoningBuilder, StringBuilder generalBuilder)
    {
        if (node is null) return;

        if (node is string s)
        {
            generalBuilder.Append(s);

            return;
        }

        if (node is IList list)
        {
            foreach (var item in list)
            {
                ExtractStreamChunkPiece(item, contentBuilder, reasoningBuilder, generalBuilder);
            }

            return;
        }

        if (node is IDictionary dict)
        {
            if (dict["data"] is not null)
            {
                ExtractStreamChunkPiece(dict["data"], contentBuilder, reasoningBuilder, generalBuilder);

                return;
            }

            if (dict["choices"] is IList choices && choices.Count > 0)
            {
                foreach (var choiceObj in choices)
                {
                    if (choiceObj is IDictionary choice)
                    {
                        var delta = choice["delta"] as IDictionary;
                        var message = choice["message"] as IDictionary;
                        var target = delta ?? message;

                        if (target is not null)
                        {
                            var content = ExtractStringOrBlocks(target["content"] ?? target["text"]);
                            if (!string.IsNullOrEmpty(content))
                            {
                                contentBuilder.Append(content);
                            }

                            var reasoning = ExtractStringOrBlocks(target["reasoning_content"] ?? target["reasoning"] ?? target["thought"] ?? target["thinking"]);
                            if (!string.IsNullOrEmpty(reasoning))
                            {
                                reasoningBuilder.Append(reasoning);
                            }
                        }
                        else if (choice["text"] is not null)
                        {
                            var text = ExtractStringOrBlocks(choice["text"]);
                            if (!string.IsNullOrEmpty(text))
                            {
                                contentBuilder.Append(text);
                            }
                        }
                    }
                }

                return;
            }

            if (dict["delta"] is IDictionary claudeDelta)
            {
                var text = ExtractStringOrBlocks(claudeDelta["text"] ?? claudeDelta["content"]);
                if (!string.IsNullOrEmpty(text))
                {
                    contentBuilder.Append(text);

                    return;
                }

                var thought = ExtractStringOrBlocks(claudeDelta["thought"] ?? claudeDelta["thinking"]);
                if (!string.IsNullOrEmpty(thought))
                {
                    reasoningBuilder.Append(thought);

                    return;
                }
            }

            if (dict["candidates"] is IList candidates && candidates.Count > 0)
            {
                foreach (var candObj in candidates)
                {
                    if (candObj is IDictionary cand)
                    {
                        var text = ExtractStringOrBlocks(cand["content"] ?? cand["text"]);
                        if (!string.IsNullOrEmpty(text))
                        {
                            contentBuilder.Append(text);
                        }
                    }
                }

                return;
            }

            var genericText = ExtractContentFromDictionary(dict);
            if (!string.IsNullOrEmpty(genericText))
            {
                generalBuilder.Append(genericText);
            }
        }
    }

    /// <summary>
    /// Truncates the given string to the specified length, appending a period, or returns the original string unchanged if it is null, empty, or already within the length limit.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="length">The length.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string Truncate(string text, int length)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= length)
        {
            return text;
        }

        return text.Substring(0, length) + "...";
    }
}
