using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Web.Script.Serialization;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A small OpenAI-compatible chat completion client used by AI-assisted XML documentation.
/// </summary>

internal sealed class OpenAiCompatibleClient
{
    private const int MaxAttempts = 3;

    internal OpenAiCompatibleClient(string endpointUrl, string apiKey, string apiKeyHeader, string model, int timeoutSeconds)
    {
        EndpointUrl = GetNormalizedEndpointUrl(endpointUrl);
        ApiKey = apiKey?.Trim().Trim('"');
        ApiKeyHeader = string.IsNullOrWhiteSpace(apiKeyHeader) ? "Authorization" : apiKeyHeader.Trim();
        Model = model?.Trim();
        TimeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 30;
    }

    internal string EndpointUrl { get; }

    internal string ApiKey { get; }

    internal string ApiKeyHeader { get; }

    internal string Model { get; }

    internal int TimeoutSeconds { get; }

    /// <summary>
    /// Returns the trimmed endpoint URL unchanged if it is null/whitespace, not an absolute URI, or already ends with &quot;/chat/completions&quot; or &quot;/completions&quot;; otherwise trims trailing slashes and appends &quot;/chat/completions&quot;.
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

    /// <summary>
    /// Attempts a chat completion requesting &quot;OK&quot; to test connectivity, returning success and an error message via out parameter while discarding the response content.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>A bool value produced by this method.</returns>

    internal bool TryTestConnection(out string errorMessage)
    {
        string content;

        return TrySendChatCompletion(
            "Reply with exactly: OK",
            512,
            out content,
            out errorMessage);
    }

    /// <summary>
    /// Attempts to generate documentation by delegating to TrySendChatCompletion with a sanitized positive token limit (defaulting to 256 for non-positive values) and writes results to the output parameters.
    /// </summary>
    /// <param name="prompt">The prompt.</param>
    /// <param name="completionText">The completion text.</param>
    /// <param name="errorMessage">The error message.</param>
    /// <param name="maxTokens">The max tokens.</param>
    /// <returns>A bool value produced by this method.</returns>

    internal bool TryGenerateDocumentation(string prompt, out string completionText, out string errorMessage, int maxTokens = 256)
    {
        var safeMaxTokens = maxTokens > 0 ? maxTokens : 256;

        return TrySendChatCompletion(prompt, safeMaxTokens, out completionText, out errorMessage);
    }

    /// <summary>
    /// Attempts to send a chat completion request with retries, setting completionText on success or errorMessage on failure, and returns false if the endpoint is unconfigured or all attempts fail after transient HTTP statuses and exceptions.
    /// </summary>
    /// <param name="userPrompt">The user prompt.</param>
    /// <param name="maxTokens">The max tokens.</param>
    /// <param name="completionText">The completion text.</param>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>A bool value produced by this method.</returns>

    private bool TrySendChatCompletion(string userPrompt, int maxTokens, out string completionText, out string errorMessage)
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

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using (var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) })
                using (var message = new HttpRequestMessage(HttpMethod.Post, EndpointUrl))
                {
                    ApplyAuthentication(message.Headers);

                    message.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                    var response = httpClient.SendAsync(message).GetAwaiter().GetResult();
                    var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (!response.IsSuccessStatusCode)
                    {
                        lastError = $"AI endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(responseText, 512)}";
                        if (attempt < MaxAttempts && IsTransientStatusCode((int)response.StatusCode))
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
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt >= MaxAttempts)
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
            ["max_tokens"] = maxTokens
        };

        if (!string.IsNullOrWhiteSpace(Model))
        {
            request["model"] = Model;
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

        var serializer = new JavaScriptSerializer();
        var payload = serializer.DeserializeObject(responseText) as IDictionary;
        if (payload == null)
        {
            return null;
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
                    if (message["content"] != null)
                    {
                        var content = Convert.ToString(message["content"]);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            return content;
                        }
                    }

                    // Fallback for reasoning models (e.g. DeepSeek-R1 / deepseek-reasoner) when content is empty or omitted
                    if (message["reasoning_content"] != null)
                    {
                        var reasoning = Convert.ToString(message["reasoning_content"]);
                        if (!string.IsNullOrWhiteSpace(reasoning))
                        {
                            return reasoning;
                        }
                    }

                    if (message["reasoning"] != null)
                    {
                        var reasoning = Convert.ToString(message["reasoning"]);
                        if (!string.IsNullOrWhiteSpace(reasoning))
                        {
                            return reasoning;
                        }
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

    /// <summary>
    /// Returns the original text if it is null, empty, or within the specified length; otherwise, it returns a substring of the first length characters followed by &quot;...&quot;, with no side effects or exceptions thrown.
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
