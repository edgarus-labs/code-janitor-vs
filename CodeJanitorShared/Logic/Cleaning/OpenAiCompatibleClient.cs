using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A small OpenAI-compatible chat completion client used by AI-assisted XML documentation.
/// </summary>

internal sealed class OpenAiCompatibleClient
{
    private const int MaxAttempts = 3;

    internal sealed class ConnectionTestResult
    {
        internal bool Succeeded { get; set; }

        internal string ErrorMessage { get; set; }
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

    internal string EndpointUrl { get; }

    internal string ApiKey { get; }

    internal string ApiKeyHeader { get; }

    internal string Model { get; }

    internal int TimeoutSeconds { get; }

    /// <summary>
<<<<<<< HEAD
    /// Returns the trimmed endpoint URL unchanged if it is null/whitespace, not an absolute URI, or already ends with &quot;/chat/completions&quot; or &quot;/completions&quot;; otherwise trims trailing slashes and appends &quot;/chat/completions&quot;.
    /// </summary>
    /// <param name="endpointUrl">The endpoint url.</param>
    /// <returns>A string value produced by this method.</returns>
=======
    /// Gets the configured minimum context window (in tokens), or 0 if not configured. This is
    /// only ever sent to the endpoint (as a best-effort "num_ctx" field) when the endpoint looks
    /// like a local server, since hosted OpenAI-compatible APIs commonly reject unrecognized
    /// request fields with a validation error.
    /// </summary>
    internal int ContextWindowTokens { get; }

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
>>>>>>> b9e78414af282a58c367e7d5c92e87b209aead52

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
            url.EndsWith("/completions", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        url = url.TrimEnd('/');

        return url + "/chat/completions";
    }

    /// <summary>
    /// Returns true only if a non-blank API key (after trimming quotes) and a non-blank endpoint URL are provided, the URL parses as an absolute http/https URI, and the scheme is HTTP or HTTPS; otherwise returns false with no side effects.
    /// </summary>
    /// <param name="endpointUrl">The endpoint url.</param>
    /// <param name="apiKey">The api key.</param>
    /// <returns>A bool value produced by this method.</returns>

    internal static bool IsEndpointConfigured(string endpointUrl, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        var cleanKey = apiKey.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(cleanKey))
        {
            return false;
        }

        Uri endpoint;
        if (!Uri.TryCreate(endpointUrl?.Trim(), UriKind.Absolute, out endpoint))
        {
            return false;
        }

        return endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps;
    }

<<<<<<< HEAD
    /// <summary>
    /// Attempts a chat completion requesting &quot;OK&quot; to test connectivity, returning success and an error message via out parameter while discarding the response content.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>A bool value produced by this method.</returns>

    internal bool TryTestConnection(out string errorMessage)
=======
    internal async Task<ConnectionTestResult> TestConnectionAsync()
>>>>>>> b9e78414af282a58c367e7d5c92e87b209aead52
    {
        if (!IsEndpointConfigured(EndpointUrl, ApiKey))
        {
            return new ConnectionTestResult { ErrorMessage = "AI XML documentation endpoint is not configured." };
        }

        var requestJson = BuildRequestJson("Reply with exactly: OK", 512);
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds)))
        using (var httpClient = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan })
        using (var message = new HttpRequestMessage(HttpMethod.Post, EndpointUrl))
        {
            ApplyAuthentication(message.Headers);
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
                            ErrorMessage = $"AI endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(responseText, 512)}"
                        };
                    }

                    var content = TryExtractContentFromChatResponse(responseText);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        return new ConnectionTestResult
                        {
                            ErrorMessage = $"AI endpoint response did not contain message content. Response: {Truncate(responseText, 512)}"
                        };
                    }

                    return new ConnectionTestResult { Succeeded = true };
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return new ConnectionTestResult
                {
                    ErrorMessage = $"Connection test timed out after {TimeoutSeconds} seconds."
                };
            }
            catch (Exception ex)
            {
                return new ConnectionTestResult { ErrorMessage = ex.Message };
            }
        }
    }

<<<<<<< HEAD
    /// <summary>
    /// Attempts to generate documentation by delegating to TrySendChatCompletion with a sanitized positive token limit (defaulting to 256 for non-positive values) and writes results to the output parameters.
    /// </summary>
    /// <param name="prompt">The prompt.</param>
    /// <param name="completionText">The completion text.</param>
    /// <param name="errorMessage">The error message.</param>
    /// <param name="maxTokens">The max tokens.</param>
    /// <returns>A bool value produced by this method.</returns>

    internal bool TryGenerateDocumentation(string prompt, out string completionText, out string errorMessage, int maxTokens = 256)
=======
    internal bool TryGenerateDocumentation(string prompt, out string completionText, out string errorMessage, int maxTokens = 256, CancellationToken cancellationToken = default(CancellationToken))
>>>>>>> b9e78414af282a58c367e7d5c92e87b209aead52
    {
        var safeMaxTokens = maxTokens > 0 ? maxTokens : 256;

        return TrySendChatCompletion(prompt, safeMaxTokens, out completionText, out errorMessage, MaxAttempts, cancellationToken);
    }

<<<<<<< HEAD
    /// <summary>
    /// Attempts to send a chat completion request with retries, setting completionText on success or errorMessage on failure, and returns false if the endpoint is unconfigured or all attempts fail after transient HTTP statuses and exceptions.
    /// </summary>
    /// <param name="userPrompt">The user prompt.</param>
    /// <param name="maxTokens">The max tokens.</param>
    /// <param name="completionText">The completion text.</param>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>A bool value produced by this method.</returns>

    private bool TrySendChatCompletion(string userPrompt, int maxTokens, out string completionText, out string errorMessage)
=======
    private bool TrySendChatCompletion(string userPrompt, int maxTokens, out string completionText, out string errorMessage, int maxAttempts = MaxAttempts, CancellationToken cancellationToken = default(CancellationToken))
>>>>>>> b9e78414af282a58c367e7d5c92e87b209aead52
    {
        completionText = null;
        errorMessage = null;

        if (!IsEndpointConfigured(EndpointUrl, ApiKey))
        {
            errorMessage = "AI XML documentation endpoint is not configured.";

            return false;
        }

        var requestJson = BuildRequestJson(userPrompt, maxTokens);
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
                using (var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) })
                using (var message = new HttpRequestMessage(HttpMethod.Post, EndpointUrl))
                {
                    ApplyAuthentication(message.Headers);

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
    /// Returns true for transient HTTP status codes (408, 429, or any 5xx) and has no side effects.
    /// </summary>
    /// <param name="statusCode">The status code.</param>
    /// <returns>A bool value produced by this method.</returns>

    private static bool IsTransientStatusCode(int statusCode)
    {
        return statusCode == 408 || statusCode == 429 || (statusCode >= 500 && statusCode <= 599);
    }

    /// <summary>
    /// If the configured API key header is &quot;Authorization&quot;, this method adds an Authorization header with the API key prefixed by &quot;Bearer &quot; unless already present; otherwise it adds the API key under the configured header name, mutating the provided headers collection without validation.
    /// </summary>
    /// <param name="headers">The headers.</param>

    private void ApplyAuthentication(HttpRequestHeaders headers)
    {
        if (string.Equals(ApiKeyHeader, "Authorization", StringComparison.OrdinalIgnoreCase))
        {
            var tokenValue = ApiKey.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? ApiKey
                : "Bearer " + ApiKey;

            headers.TryAddWithoutValidation("Authorization", tokenValue);

            return;
        }

        headers.TryAddWithoutValidation(ApiKeyHeader, ApiKey);
    }

    /// <summary>
    /// Builds a JSON request payload from a system prompt and user prompt (defaulting null to empty string) with fixed temperature and max tokens, conditionally includes the Model property if non-whitespace, then serializes to JSON with no side effects.
    /// </summary>
    /// <param name="userPrompt">The user prompt.</param>
    /// <param name="maxTokens">The max tokens.</param>
    /// <returns>A string value produced by this method.</returns>

    private string BuildRequestJson(string userPrompt, int maxTokens)
    {
        var request = new Dictionary<string, object>
        {
            ["messages"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["role"] = "system",
                    ["content"] = "You are a C# documentation assistant. Output concise and accurate technical text."
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
    /// Attempts to extract text content from a chat API JSON response by parsing it with JavaScriptSerializer and returning the first non-empty value from content, reasoning_content, reasoning, choice text, or response fields, or null if the input is blank or no extractable content exists.
    /// </summary>
    /// <param name="responseText">The response text.</param>
    /// <returns>A string value produced by this method.</returns>

    internal static string TryExtractContentFromChatResponse(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        var payload = TryDeserializeObject(responseText);
        if (payload == null)
        {
            return TryExtractContentFromEventStream(responseText);
        }

        var choices = payload["choices"] as IList;
        if (choices != null && choices.Count > 0)
        {
            var firstChoice = choices[0] as IDictionary;
            if (firstChoice != null)
            {
                var message = firstChoice["message"] as IDictionary;
                if (message != null)
                {
                    var content = ReadContentField(message);
                    if (content != null)
                    {
                        return content;
                    }
                }

                // Fallback for APIs returning text directly on the choice.
                if (firstChoice["text"] != null)
                {
                    var text = Convert.ToString(firstChoice["text"]);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
        }

        if (payload["response"] != null)
        {
            var response = Convert.ToString(payload["response"]);
            if (!string.IsNullOrWhiteSpace(response))
            {
                return response;
            }
        }

        if (payload["output"] != null)
        {
            var output = Convert.ToString(payload["output"]);
            if (!string.IsNullOrWhiteSpace(output))
            {
                return output;
            }
        }

        return null;
    }

<<<<<<< HEAD
    /// <summary>
    /// Returns the original text if it is null, empty, or within the specified length; otherwise, it returns a substring of the first length characters followed by &quot;...&quot;, with no side effects or exceptions thrown.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="length">The length.</param>
    /// <returns>A string value produced by this method.</returns>
=======
    private static IDictionary TryDeserializeObject(string text)
    {
        try
        {
            return new JavaScriptSerializer().DeserializeObject(text) as IDictionary;
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
    /// Reads the assistant text from a message or delta object, falling back to the reasoning
    /// fields used by DeepSeek-R1 style models when the content is empty.
    /// </summary>

    private static string ReadContentField(IDictionary source)
    {
        foreach (var key in new[] { "content", "reasoning_content", "reasoning" })
        {
            if (source[key] == null)
            {
                continue;
            }

            var value = Convert.ToString(source[key]);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Reassembles a server-sent event response, which local servers may return even when the
    /// request asked for a non-streaming completion.
    /// </summary>

    private static string TryExtractContentFromEventStream(string responseText)
    {
        var builder = new StringBuilder();

        foreach (var rawLine in responseText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var chunkText = line.Substring("data:".Length).Trim();
            if (chunkText.Length == 0 || string.Equals(chunkText, "[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var chunk = TryDeserializeObject(chunkText);
            var choices = chunk?["choices"] as IList;
            if (choices == null || choices.Count == 0)
            {
                continue;
            }

            if (!(choices[0] is IDictionary firstChoice))
            {
                continue;
            }

            var piece = firstChoice["delta"] is IDictionary delta ? ReadContentField(delta) : null;

            if (piece == null && firstChoice["message"] is IDictionary message)
            {
                piece = ReadContentField(message);
            }

            if (piece == null && firstChoice["text"] != null)
            {
                piece = Convert.ToString(firstChoice["text"]);
            }

            builder.Append(piece);
        }

        var result = builder.ToString();

        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
>>>>>>> b9e78414af282a58c367e7d5c92e87b209aead52

    private static string Truncate(string text, int length)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= length)
        {
            return text;
        }

        return text.Substring(0, length) + "...";
    }
}
