using CodeJanitor.Helpers;
using CodeJanitor.Properties;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Provides AI-assisted Code Review, antipattern detection, and security/reliability inspections for C# code.
/// </summary>
internal sealed class AiCodeReviewLogic
{
    private readonly CodeJanitorPackage _package;
    private static AiCodeReviewLogic _instance;

    internal static AiCodeReviewLogic GetInstance(CodeJanitorPackage package) =>
        _instance ?? (_instance = new AiCodeReviewLogic(package));

    private AiCodeReviewLogic(CodeJanitorPackage package)
    {
        _package = package;
    }

    /// <summary>
    /// Checks if AI endpoint is configured in settings.
    /// </summary>
    internal static bool IsConfigurationPresent()
    {
        var settings = Settings.Default;
        return OpenAiCompatibleClient.IsEndpointConfigured(settings.Cleaning_AiXmlDocumentationEndpointUrl);
    }

    /// <summary>
    /// Performs an AI-assisted code review on the provided C# code.
    /// </summary>
    internal async Task<string> ReviewCodeAsync(string targetName, string codeSnippet, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(codeSnippet))
        {
            return "No code provided for code review.";
        }

        var client = CreateClientFromSettings();
        if (client == null)
        {
            return "AI endpoint is not configured. Please configure your endpoint in Tools > Options > Code Janitor > XML Documentation.";
        }

        var prompt = BuildReviewPrompt(targetName, codeSnippet);
        var systemPrompt = "You are a senior .NET code reviewer and security auditor. Inspect the code thoroughly for bugs, async-await deadlocks (.Result/.Wait()), resource leaks, exception safety, thread-safety, and clean code principles. Provide actionable, concise, and constructive feedback.";

        try
        {
            var response = await client.GetChatCompletionContentAsync(systemPrompt, prompt, cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(response)
                ? "The AI model returned an empty response. Please check your model settings."
                : response.Trim();
        }
        catch (Exception ex)
        {
            return $"Error during AI code review: {ex.Message}";
        }
    }

    private static string BuildReviewPrompt(string targetName, string codeSnippet)
    {
        return $@"Perform a comprehensive, professional code review on the following C# code for '{targetName}':

```csharp
{codeSnippet}
```

Please structure your review as follows:

### 🎯 Summary
A 1-2 sentence overall verdict on code health and quality.

### 🚨 Critical Issues & Bugs (if any)
- Potential runtime exceptions, null dereferences, or race conditions.
- Async/await anti-patterns (e.g. sync-over-async, unobserved Task faults, blocking on `.Result`).

### ⚠️ Warnings & Improvements
- Resource management (IDisposable / using statements).
- Performance & memory efficiency (unnecessary allocations, inefficient collections/LINQ).
- Security considerations (input validation, SQL/command execution).

### 💡 Clean Code & Maintainability Tips
- Naming, method decomposition, and modern C# idioms.
- Concrete fix recommendations.
";
    }

    private static OpenAiCompatibleClient CreateClientFromSettings()
    {
        var settings = Settings.Default;
        var endpointUrl = settings.Cleaning_AiXmlDocumentationEndpointUrl;
        if (!OpenAiCompatibleClient.IsEndpointConfigured(endpointUrl))
        {
            return null;
        }

        var apiKey = SecretProtectionHelper.UnprotectForCurrentUser(settings.Cleaning_AiXmlDocumentationApiKeyEncrypted);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = settings.Cleaning_AiXmlDocumentationApiKey;
        }

        var header = string.IsNullOrWhiteSpace(settings.Cleaning_AiXmlDocumentationApiKeyHeader)
            ? "Authorization"
            : settings.Cleaning_AiXmlDocumentationApiKeyHeader;

        var model = settings.Cleaning_AiXmlDocumentationModel;
        var timeoutSeconds = settings.Cleaning_AiXmlDocumentationTimeoutSeconds > 0
            ? settings.Cleaning_AiXmlDocumentationTimeoutSeconds
            : 45;

        var contextWindowTokens = settings.Cleaning_AiXmlDocumentationContextWindowTokens > 0
            ? settings.Cleaning_AiXmlDocumentationContextWindowTokens
            : 131072;

        return new OpenAiCompatibleClient(endpointUrl, apiKey, header, model, timeoutSeconds, contextWindowTokens);
    }
}
