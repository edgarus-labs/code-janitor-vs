using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Web.Script.Serialization;

namespace CodeJanitor.Logic.Cleaning
{
    /// <summary>
    /// A small OpenAI-compatible chat completion client used by AI-assisted XML documentation.
    /// </summary>
    internal sealed class OpenAiCompatibleClient
    {
        private const int MaxAttempts = 3;

        #region Constructors

        internal OpenAiCompatibleClient(string endpointUrl, string apiKey, string apiKeyHeader, string model, int timeoutSeconds)
        {
            EndpointUrl = endpointUrl?.Trim();
            ApiKey = apiKey?.Trim();
            ApiKeyHeader = string.IsNullOrWhiteSpace(apiKeyHeader) ? "Authorization" : apiKeyHeader.Trim();
            Model = model?.Trim();
            TimeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 30;
        }

        #endregion Constructors

        #region Properties

        internal string EndpointUrl { get; }

        internal string ApiKey { get; }

        internal string ApiKeyHeader { get; }

        internal string Model { get; }

        internal int TimeoutSeconds { get; }

        #endregion Properties

        #region Methods

        internal static bool IsEndpointConfigured(string endpointUrl, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(endpointUrl) || string.IsNullOrWhiteSpace(apiKey))
            {
                return false;
            }

            Uri endpoint;
            if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out endpoint))
            {
                return false;
            }

            return endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps;
        }

        internal bool TryTestConnection(out string errorMessage)
        {
            string content;
            return TrySendChatCompletion(
                "Reply with exactly: OK",
                16,
                out content,
                out errorMessage);
        }

        internal bool TryGenerateDocumentation(string prompt, out string completionText, out string errorMessage)
        {
            return TrySendChatCompletion(prompt, 256, out completionText, out errorMessage);
        }

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
                            errorMessage = "AI endpoint response does not contain choices[0].message.content.";
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

            var serializer = new JavaScriptSerializer();
            return serializer.Serialize(request);
        }

        private static string TryExtractContentFromChatResponse(string responseText)
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
            if (choices == null || choices.Count == 0)
            {
                return null;
            }

            var firstChoice = choices[0] as IDictionary;
            if (firstChoice == null)
            {
                return null;
            }

            var message = firstChoice["message"] as IDictionary;
            if (message != null && message["content"] != null)
            {
                return Convert.ToString(message["content"]);
            }

            // Fallback for APIs returning text directly on the choice.
            return firstChoice["text"] == null ? null : Convert.ToString(firstChoice["text"]);
        }

        private static string Truncate(string text, int length)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= length)
            {
                return text;
            }

            return text.Substring(0, length) + "...";
        }

        #endregion Methods
    }
}