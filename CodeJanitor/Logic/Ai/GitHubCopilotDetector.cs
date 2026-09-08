using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

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
    /// List of standard models supported by GitHub Copilot Chat.
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

    /// <summary>
    /// Exchanges a GitHub OAuth/Personal Access Token for a GitHub Copilot session token if needed.
    /// </summary>
    public static async Task<string> ExchangeGitHubTokenForCopilotTokenAsync(string token)
    {
        var raw = token;
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = TryExtractVsGitHubToken();
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        var cleaned = raw.Trim().Trim('"');
        if (cleaned.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(7).Trim();
        }

        // If it is already a Copilot session token (contains tid=...)
        if (cleaned.Contains("tid="))
        {
            return cleaned;
        }

        try
        {
            using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) })
            using (var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://api.github.com/copilot_internal/v2/token"))
            {
                var authHeaderValue = cleaned.StartsWith("gh", StringComparison.OrdinalIgnoreCase) || cleaned.StartsWith("token ", StringComparison.OrdinalIgnoreCase)
                    ? (cleaned.StartsWith("token ", StringComparison.OrdinalIgnoreCase) ? cleaned : "token " + cleaned)
                    : "token " + cleaned;

                request.Headers.TryAddWithoutValidation("Authorization", authHeaderValue);
                request.Headers.TryAddWithoutValidation("User-Agent", "GitHubCopilotChat/18.9");
                request.Headers.TryAddWithoutValidation("Editor-Version", "VisualStudio/18.0");
                request.Headers.TryAddWithoutValidation("Copilot-Integration-Id", "vscode-chat");

                var response = await client.SendAsync(request).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                    var dict = serializer.Deserialize<Dictionary<string, object>>(json);
                    if (dict is not null && dict.TryGetValue("token", out var tokenObj) && tokenObj is not null)
                    {
                        return tokenObj.ToString();
                    }
                }
            }
        }
        catch
        {
            // Fallback to original token on exchange error
        }

        return cleaned;
    }

    /// <summary>
    /// Fetches the dynamically available models from the GitHub Copilot API or returns the fallback list.
    /// </summary>
    public static async Task<List<string>> FetchCopilotModelsAsync(string token)
    {
        var result = new List<string>(SupportedCopilotModels);

        try
        {
            var sessionToken = await ExchangeGitHubTokenForCopilotTokenAsync(token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(sessionToken))
            {
                return result;
            }

            var endpoints = new[]
            {
                "https://api.individual.githubcopilot.com/models",
                "https://api.githubcopilot.com/models"
            };

            foreach (var url in endpoints)
            {
                try
                {
                    using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) })
                    using (var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url))
                    {
                        var auth = sessionToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                            ? sessionToken
                            : "Bearer " + sessionToken;
                        request.Headers.TryAddWithoutValidation("Authorization", auth);
                        request.Headers.TryAddWithoutValidation("User-Agent", "GitHubCopilotChat/18.9");
                        request.Headers.TryAddWithoutValidation("Editor-Version", "VisualStudio/18.0");
                        request.Headers.TryAddWithoutValidation("Editor-Plugin-Version", "copilot-chat/0.24.1");
                        request.Headers.TryAddWithoutValidation("Copilot-Integration-Id", "vscode-chat");
                        request.Headers.TryAddWithoutValidation("Openai-Intent", "conversation-panel");

                        var response = await client.SendAsync(request).ConfigureAwait(false);
                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                            var dict = serializer.Deserialize<Dictionary<string, object>>(json);
                            if (dict is not null && dict.TryGetValue("data", out var dataObj) && dataObj is System.Collections.IList dataList)
                            {
                                var fetched = new List<string>();
                                foreach (var item in dataList)
                                {
                                    if (item is Dictionary<string, object> modelDict &&
                                        modelDict.TryGetValue("id", out var idObj) && idObj is not null)
                                    {
                                        var id = idObj.ToString();
                                        if (!string.IsNullOrWhiteSpace(id) && !fetched.Contains(id))
                                        {
                                            fetched.Add(id);
                                        }
                                    }
                                }

                                if (fetched.Count > 0)
                                {
                                    return fetched;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Try next endpoint
                }
            }
        }
        catch
        {
            // Fallback to defaults
        }

        return result;
    }

    /// <summary>
    /// Detects the presence and active state of GitHub Copilot in the current environment.
    /// </summary>
    public static CopilotDetectionResult DetectCopilotStatus()
    {
        var result = new CopilotDetectionResult();

        try
        {
            // 1. Check Windows Credential Manager for Visual Studio GitHub Account and Git credentials
            var vsToken = TryExtractVsGitHubToken();
            if (!string.IsNullOrEmpty(vsToken))
            {
                result.IsInstalled = true;
                result.IsActive = true;
                result.DetectedToken = vsToken;
                result.StatusDescription = "Visual Studio GitHub account detected & connected.";

                return result;
            }

            // 2. Check Visual Studio GitHub Copilot log files
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
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

            // 3. Check ~/.config/github-copilot/hosts.json or %LOCALAPPDATA%/github-copilot/hosts.json
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configPaths = new[]
            {
                Path.Combine(userProfile, ".config", "github-copilot", "hosts.json"),
                Path.Combine(userProfile, ".config", "github-copilot", "apps.json"),
                Path.Combine(localAppData, "github-copilot", "hosts.json")
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

            if (result.IsInstalled)
            {
                result.StatusDescription = "GitHub Copilot extension is installed in Visual Studio.";
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
    /// Reads a file&apos;s text and returns the OAuth token value found after the `&quot;oauth_token&quot;:&quot;` marker, or null if absent or on any I/O/parse error.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string TryExtractOAuthToken(string filePath)
    {
        try
        {
            var text = File.ReadAllText(filePath);
            var tokenMarker = "\"oauth_token\":\"";
            var index = text.IndexOf(tokenMarker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var start = index + tokenMarker.Length;
                var end = text.IndexOf('"', start);
                if (end > start)
                {
                    return text.Substring(start, end - start);
                }
            }
        }
        catch
        {
            // Ignore file read/parse errors
        }

        return null;
    }
}
