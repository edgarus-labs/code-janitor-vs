using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Helper class for detecting and configuring GitHub Copilot integration in Visual Studio.
/// </summary>
public static class GitHubCopilotDetector
{
    /// <summary>
    /// Default GitHub Copilot OpenAI-compatible chat completions endpoint.
    /// </summary>
    public const string DefaultCopilotEndpoint = "https://api.githubcopilot.com/chat/completions";

    /// <summary>
    /// Default recommended GitHub Copilot model.
    /// </summary>
    public const string DefaultCopilotModel = "gpt-4o";

    /// <summary>
    /// Fallback models shown when live GitHub Copilot model discovery fails.
    /// </summary>
    public static readonly string[] SupportedCopilotModels = ["gpt-4o", "claude-3.5-sonnet", "o1-mini", "gpt-4o-mini", "gpt-4-turbo"];

    /// <summary>
    /// Checks whether the given endpoint URL is a GitHub Copilot endpoint.
    /// </summary>
    public static bool IsCopilotEndpoint(string endpointUrl)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            return false;
        }

        return endpointUrl.IndexOf("api.githubcopilot.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
               endpointUrl.IndexOf("api.individual.githubcopilot.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
               endpointUrl.IndexOf("api.business.githubcopilot.com", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal const string CopilotTokenExchangeUrl = "https://api.github.com/copilot_internal/v2/token";

    internal const string DefaultCopilotApiBaseUrl = "https://api.githubcopilot.com";

    private static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(8);

    internal sealed class CopilotSession
    {
        internal string Token { get; set; }

        internal string ApiBaseUrl { get; set; }

        internal string ErrorMessage { get; set; }

        internal DateTimeOffset? ExpiresAt { get; set; }
    }

    private static readonly TimeSpan SessionRefreshMargin = TimeSpan.FromSeconds(60);

    private static readonly ConcurrentDictionary<string, CopilotSession> SessionCache = new ConcurrentDictionary<string, CopilotSession>(StringComparer.Ordinal);

    public sealed class CopilotModelsResult
    {
        public List<string> Models { get; set; }

        public string ErrorMessage { get; set; }
    }

    internal static void ResetCopilotSessionCache()
    {
        SessionCache.Clear();
    }

    internal static void ApplyCopilotHeaders(HttpRequestHeaders headers)
    {
        headers.TryAddWithoutValidation("User-Agent", "GitHubCopilotChat/18.9");
        headers.TryAddWithoutValidation("Copilot-Integration-Id", "visualstudio-chat");
        headers.TryAddWithoutValidation("Editor-Version", "VisualStudio/18.0");
        headers.TryAddWithoutValidation("Editor-Plugin-Version", "copilot-chat/0.24.1");
        headers.TryAddWithoutValidation("Openai-Intent", "conversation-panel");
    }

    internal static Uri ResolveCopilotApiUri(Uri requestUri, string apiBaseUrl)
    {
        if (requestUri is null || string.IsNullOrWhiteSpace(apiBaseUrl) || !Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiBase))
        {
            return requestUri;
        }

        return new UriBuilder(requestUri) { Scheme = apiBase.Scheme, Host = apiBase.Host, Port = apiBase.Port }.Uri;
    }

    internal static async Task<CopilotSession> ExchangeForCopilotSessionAsync(string token, HttpMessageHandler httpMessageHandler = null)
    {
        var cleaned = NormalizeGitHubToken(token);
        if (cleaned is null)
        {
            return new CopilotSession { ErrorMessage = "No GitHub token is available for GitHub Copilot. Sign in to GitHub Copilot or enter a GitHub token." };
        }

        if (cleaned.Contains("tid="))
        {
            return new CopilotSession { Token = cleaned };
        }

        if (SessionCache.TryGetValue(cleaned, out var cached) && cached.ExpiresAt - DateTimeOffset.UtcNow > SessionRefreshMargin)
        {
            return cached;
        }

        var session = await RequestCopilotSessionAsync(cleaned, httpMessageHandler).ConfigureAwait(false);
        if (session.ErrorMessage is null && session.ExpiresAt.HasValue)
        {
            SessionCache[cleaned] = session;
        }

        return session;
    }

    /// <summary>
    /// Drops the cached Copilot session for the GitHub token so the next request exchanges a fresh one.
    /// Call this when a Copilot endpoint rejects the session token before its reported expiry.
    /// </summary>
    internal static void InvalidateCopilotSession(string token)
    {
        var cleaned = NormalizeGitHubToken(token);
        if (cleaned is not null)
        {
            SessionCache.TryRemove(cleaned, out _);
        }
    }

    private static string NormalizeGitHubToken(string token)
    {
        var raw = string.IsNullOrWhiteSpace(token) ? DetectCopilotStatus().DetectedToken : token;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = raw.Trim().Trim('"');
        if (cleaned.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(7).Trim();
        }

        if (cleaned.StartsWith("token ", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(6).Trim();
        }

        return cleaned;
    }

    private static async Task<CopilotSession> RequestCopilotSessionAsync(string cleaned, HttpMessageHandler httpMessageHandler)
    {
        try
        {
            using (var client = CreateHttpClient(httpMessageHandler))
            using (var request = new HttpRequestMessage(HttpMethod.Get, CopilotTokenExchangeUrl))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "token " + cleaned);
                ApplyCopilotHeaders(request.Headers);

                using (var response = await client.SendAsync(request).ConfigureAwait(false))
                {
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        var isAuthorizationFailure = response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.NotFound;
                        var reason = isAuthorizationFailure
                            ? "The GitHub token is invalid or not authorized for GitHub Copilot."
                            : "GitHub could not issue a Copilot session; try again later.";

                        return new CopilotSession
                        {
                            ErrorMessage = $"GitHub Copilot token exchange failed ({(int)response.StatusCode} {response.ReasonPhrase}). {reason} {Truncate(json, 300)}".Trim()
                        };
                    }

                    var dict = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                    if (dict is null || !dict.TryGetValue("token", out var tokenObj) || string.IsNullOrWhiteSpace(tokenObj as string))
                    {
                        return new CopilotSession { ErrorMessage = "GitHub Copilot token exchange failed: the response did not contain a Copilot token." };
                    }

                    var apiBaseUrl = dict.TryGetValue("endpoints", out var endpointsObj) &&
                                     endpointsObj is Dictionary<string, object> endpoints &&
                                     endpoints.TryGetValue("api", out var apiObj)
                        ? apiObj as string
                        : null;

                    var expiresAt = dict.TryGetValue("expires_at", out var expiresObj) && long.TryParse(Convert.ToString(expiresObj, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiresSeconds)
                        ? DateTimeOffset.FromUnixTimeSeconds(expiresSeconds)
                        : (DateTimeOffset?)null;

                    return new CopilotSession { Token = (string)tokenObj, ApiBaseUrl = apiBaseUrl, ExpiresAt = expiresAt };
                }
            }
        }
        catch (TaskCanceledException)
        {
            return new CopilotSession { ErrorMessage = $"GitHub Copilot token exchange timed out after {ExchangeTimeout.TotalSeconds:0} seconds contacting api.github.com." };
        }
        catch (HttpRequestException ex)
        {
            return new CopilotSession { ErrorMessage = "GitHub Copilot token exchange failed: " + (ex.InnerException?.Message ?? ex.Message) };
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            return new CopilotSession { ErrorMessage = "GitHub Copilot token exchange returned an invalid response." };
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text.Substring(0, maxLength) + "...";
    }

    public static Task<CopilotModelsResult> FetchCopilotModelsAsync(string token)
    {
        return FetchCopilotModelsAsync(token, null);
    }

    internal static async Task<CopilotModelsResult> FetchCopilotModelsAsync(string token, HttpMessageHandler httpMessageHandler)
    {
        var fallback = new List<string>(SupportedCopilotModels);
        var session = await ExchangeForCopilotSessionAsync(token, httpMessageHandler).ConfigureAwait(false);
        if (session.ErrorMessage is not null)
        {
            return new CopilotModelsResult { Models = fallback, ErrorMessage = session.ErrorMessage };
        }

        try
        {
            var baseUrl = string.IsNullOrWhiteSpace(session.ApiBaseUrl) ? DefaultCopilotApiBaseUrl : session.ApiBaseUrl.TrimEnd('/');
            using (var client = CreateHttpClient(httpMessageHandler))
            using (var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/models"))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + session.Token);
                ApplyCopilotHeaders(request.Headers);

                using (var response = await client.SendAsync(request).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return new CopilotModelsResult { Models = fallback, ErrorMessage = $"GitHub Copilot models request failed ({(int)response.StatusCode} {response.ReasonPhrase})." };
                    }

                    var models = ParseCopilotChatModelIds(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

                    return models.Count > 0
                        ? new CopilotModelsResult { Models = models }
                        : new CopilotModelsResult { Models = fallback, ErrorMessage = "GitHub Copilot returned no chat models for this account." };
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is ArgumentException || ex is InvalidOperationException)
        {
            return new CopilotModelsResult { Models = fallback, ErrorMessage = "GitHub Copilot models request failed: " + ex.Message };
        }
    }

    internal static List<string> ParseCopilotChatModelIds(string json)
    {
        var models = new List<string>();
        var dict = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
        if (dict is null || !dict.TryGetValue("data", out var dataObj) || dataObj is not IList dataList)
        {
            return models;
        }

        foreach (var item in dataList.OfType<Dictionary<string, object>>())
        {
            var id = item.TryGetValue("id", out var idObj) ? (idObj as string)?.Trim() : null;
            var type = item.TryGetValue("capabilities", out var capabilitiesObj) &&
                       capabilitiesObj is Dictionary<string, object> capabilities &&
                       capabilities.TryGetValue("type", out var typeObj)
                ? typeObj as string
                : null;

            var policyDisabled = item.TryGetValue("policy", out var policyObj) &&
                                 policyObj is Dictionary<string, object> policy &&
                                 policy.TryGetValue("state", out var stateObj) &&
                                 string.Equals(stateObj as string, "disabled", StringComparison.OrdinalIgnoreCase);
            var supportsChatCompletions = !item.TryGetValue("supported_endpoints", out var endpointsObj) ||
                                          endpointsObj is not IList endpoints ||
                                          endpoints.OfType<string>().Any(e => string.Equals(e, "/chat/completions", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(id) && (type is null || string.Equals(type, "chat", StringComparison.OrdinalIgnoreCase)) && !policyDisabled && supportsChatCompletions && !models.Contains(id))
            {
                models.Add(id);
            }
        }

        models.Sort(StringComparer.OrdinalIgnoreCase);

        return models;
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler httpMessageHandler)
    {
        var client = httpMessageHandler is null ? new HttpClient() : new HttpClient(httpMessageHandler, disposeHandler: false);
        client.Timeout = ExchangeTimeout;

        return client;
    }

    /// <summary>
    /// Detects the presence and active state of GitHub Copilot in the current environment.
    /// </summary>
    public static CopilotDetectionResult DetectCopilotStatus()
    {
        return DetectCopilotStatus(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            TryExtractVsGitHubToken);
    }

    internal static CopilotDetectionResult DetectCopilotStatus(string userProfile, string localAppData, Func<string> readCredentialManagerToken)
    {
        var result = new CopilotDetectionResult();

        try
        {
            var configPaths = new[]
            {
                Path.Combine(userProfile ?? string.Empty, ".config", "github-copilot", "apps.json"),
                Path.Combine(userProfile ?? string.Empty, ".config", "github-copilot", "hosts.json"),
                Path.Combine(localAppData ?? string.Empty, "github-copilot", "apps.json"),
                Path.Combine(localAppData ?? string.Empty, "github-copilot", "hosts.json")
            };

            foreach (var path in configPaths)
            {
                if (File.Exists(path))
                {
                    result.IsInstalled = true;
                    var token = TryExtractOAuthToken(path);
                    if (!string.IsNullOrEmpty(token))
                    {
                        result.IsActive = true;
                        result.DetectedToken = token;
                        result.StatusDescription = "GitHub Copilot credentials found in user configuration.";

                        return result;
                    }
                }
            }

            var vsToken = readCredentialManagerToken();
            if (!string.IsNullOrEmpty(vsToken))
            {
                result.IsInstalled = true;
                result.IsActive = true;
                result.DetectedToken = vsToken;
                result.StatusDescription = "Visual Studio GitHub account detected & connected.";

                return result;
            }

            if (!string.IsNullOrEmpty(localAppData))
            {
                var vsCopilotLogsDir = Path.Combine(localAppData, "Temp", "VSGitHubCopilotLogs");
                if (Directory.Exists(vsCopilotLogsDir))
                {
                    result.IsInstalled = true;
                    var latestLog = Directory.GetFiles(vsCopilotLogsDir, "*.chat.log")
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(fi => fi.LastWriteTime)
                        .FirstOrDefault();

                    if (latestLog is not null)
                    {
                        var content = ReadSafeHeaderLines(latestLog.FullName, 50);
                        if (content.IndexOf("HasToken: True", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            content.IndexOf("ChatEnabled: True", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            content.IndexOf("Copilot token changed", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            result.IsActive = true;
                            result.StatusDescription = "Active GitHub Copilot session detected in Visual Studio.";

                            return result;
                        }

                        result.StatusDescription = "Visual Studio GitHub Copilot logs found (session idle or unauthenticated).";
                    }
                }
            }

            if (result.IsInstalled)
            {
                result.StatusDescription ??= "GitHub Copilot extension is installed in Visual Studio.";
            }
            else
            {
                result.StatusDescription = "GitHub Copilot not detected. Enter your GitHub Token or configure a custom endpoint.";
            }
        }
        catch (Exception ex)
        {
            result.StatusDescription = "Copilot detection note: " + ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Reads GitHub OAuth token from Windows Credential Manager stored by Visual Studio or Git Credential Manager.
    /// </summary>
    public static string TryExtractVsGitHubToken()
    {
        try
        {
            if (CredEnumerate(null, 0, out var count, out var pCredentials))
            {
                try
                {
                    var candidates = new List<Tuple<int, string>>(); // priority, token
                    for (var i = 0; i < count; i++)
                    {
                        var credPtr = Marshal.ReadIntPtr(pCredentials, i * IntPtr.Size);
                        var cred = (NativeCredential)Marshal.PtrToStructure(credPtr, typeof(NativeCredential));
                        if (string.IsNullOrEmpty(cred.TargetName))
                        {
                            continue;
                        }

                        var target = cred.TargetName;
                        var isVsGithub = target.IndexOf("Visual Studio", StringComparison.OrdinalIgnoreCase) >= 0 && target.IndexOf("github", StringComparison.OrdinalIgnoreCase) >= 0;
                        var isGitGithub = target.IndexOf("git:https://", StringComparison.OrdinalIgnoreCase) >= 0 && target.IndexOf("github.com", StringComparison.OrdinalIgnoreCase) >= 0;
                        var isGhCli = target.IndexOf("gh:github.com", StringComparison.OrdinalIgnoreCase) >= 0;
                        var isCopilotLs = target.IndexOf("copilot-language-server", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (isVsGithub || isGitGithub || isGhCli || isCopilotLs)
                        {
                            var token = ExtractTokenFromCredentialBlob(cred);
                            if (!string.IsNullOrEmpty(token))
                            {
                                int priority = isVsGithub ? 1 : (isGitGithub ? 2 : (isGhCli ? 3 : 4));
                                candidates.Add(Tuple.Create(priority, token));
                            }
                        }
                    }

                    if (candidates.Count > 0)
                    {
                        return candidates.OrderBy(c => c.Item1).First().Item2;
                    }
                }
                finally
                {
                    CredFree(pCredentials);
                }
            }
        }
        catch
        {
            // Ignore native credential extraction errors
        }

        return null;
    }

    /// <summary>
    /// Extracts a token string from a native credential blob by copying its bytes and attempting Unicode and UTF-8 decoding, returning the first decoded value that starts with &quot;gh&quot; or &quot;tid=&quot; or is at least 20 characters without null bytes, or null if no match is found.
    /// </summary>
    /// <param name="cred">The cred.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string ExtractTokenFromCredentialBlob(NativeCredential cred)
    {
        if (cred.CredentialBlobSize <= 0 || cred.CredentialBlob == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, cred.CredentialBlobSize);

            var sUni = Encoding.Unicode.GetString(bytes).Trim('\0', ' ', '\r', '\n');
            if (sUni.StartsWith("gh", StringComparison.OrdinalIgnoreCase) || sUni.StartsWith("tid=", StringComparison.OrdinalIgnoreCase))
            {
                return sUni;
            }

            var sUtf8 = Encoding.UTF8.GetString(bytes).Trim('\0', ' ', '\r', '\n');
            if (sUtf8.StartsWith("gh", StringComparison.OrdinalIgnoreCase) || sUtf8.StartsWith("tid=", StringComparison.OrdinalIgnoreCase))
            {
                return sUtf8;
            }

            if (sUni.Length >= 20 && !sUni.Contains("\0"))
            {
                return sUni;
            }

            if (sUtf8.Length >= 20 && !sUtf8.Contains("\0"))
            {
                return sUtf8;
            }
        }
        catch
        {
            // Ignore decoding failure
        }

        return null;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredEnumerate(string filter, int flags, out int count, out IntPtr pCredentials);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree(IntPtr cred);

    /// <summary>
    /// NativeCredential is a P/Invoke interop structure that mirrors the Windows CREDENTIAL native type, representing a single stored credential entry as used by the Credential Management API.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        /// <summary>
        /// The flags.
        /// </summary>
        public int Flags;

        /// <summary>
        /// The type.
        /// </summary>
        public int Type;

        /// <summary>
        /// The target name.
        /// </summary>
        public string TargetName;

        /// <summary>
        /// The comment.
        /// </summary>
        public string Comment;

        /// <summary>
        /// The last written.
        /// </summary>
        public long LastWritten;

        /// <summary>
        /// The credential blob size.
        /// </summary>
        public int CredentialBlobSize;

        /// <summary>
        /// The credential blob.
        /// </summary>
        public IntPtr CredentialBlob;

        /// <summary>
        /// The persist.
        /// </summary>
        public int Persist;

        /// <summary>
        /// The attribute count.
        /// </summary>
        public int AttributeCount;

        /// <summary>
        /// The attributes.
        /// </summary>
        public IntPtr Attributes;

        /// <summary>
        /// The target alias.
        /// </summary>
        public string TargetAlias;

        /// <summary>
        /// The user name.
        /// </summary>
        public string UserName;
    }

    /// <summary>
    /// Safely reads up to a specified number of lines from the beginning of a file with shared read access, concatenating them into a single string and returning an empty string if any exception occurs.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="lineCount">The line count.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string ReadSafeHeaderLines(string filePath, int lineCount)
    {
        try
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                var lines = new StringBuilder();
                for (var i = 0; i < lineCount && !reader.EndOfStream; i++)
                {
                    lines.AppendLine(reader.ReadLine());
                }

                return lines.ToString();
            }
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Parses a GitHub Copilot apps.json or hosts.json file and returns the first non-empty oauth_token value, or null if absent or on any I/O/parse error.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string TryExtractOAuthToken(string filePath)
    {
        try
        {
            var dict = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(filePath));

            return dict?.Values
                .OfType<Dictionary<string, object>>()
                .Select(entry => entry.TryGetValue("oauth_token", out var tokenObj) ? tokenObj as string : null)
                .FirstOrDefault(token => !string.IsNullOrWhiteSpace(token));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
