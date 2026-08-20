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

    internal async Task<ConnectionTestResult> TestConnectionAsync()
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

    internal bool TryGenerateDocumentation(string prompt, out string completionText, out string errorMessage, int maxTokens = 256)
    {
        var safeMaxTokens = maxTokens > 0 ? maxTokens : 256;

        return TrySendChatCompletion(prompt, safeMaxTokens, out completionText, out errorMessage);
    }

    private bool TrySendChatCompletion(string userPrompt, int maxTokens, out string completionText, out string errorMessage, int maxAttempts = MaxAttempts)
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

    private static bool IsTransientStatusCode(int statusCode)
    {
        return statusCode == 408 || statusCode == 429 || (statusCode >= 500 && statusCode <= 599);
    }

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

    private static string Truncate(string text, int length)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= length)
        {
            return text;
        }

        return text.Substring(0, length) + "...";
    }
}