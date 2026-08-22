using CodeJanitor.Helpers;
using CodeJanitor.Properties;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Provides AI-assisted explanation, complexity analysis, and decomposition suggestions for C# methods and classes.
/// </summary>
internal sealed class AiExplainLogic
{
    private readonly CodeJanitorPackage _package;
    private static AiExplainLogic _instance;

    internal static AiExplainLogic GetInstance(CodeJanitorPackage package) =>
        _instance ?? (_instance = new AiExplainLogic(package));

    private AiExplainLogic(CodeJanitorPackage package)
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
    /// Analyzes the provided code and returns a structured explanation with complexity analysis and refactoring suggestions.
    /// </summary>
    internal async Task<string> ExplainCodeAsync(string memberName, string codeSnippet, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(codeSnippet))
        {
            return "No code provided for analysis.";
        }

        var client = CreateClientFromSettings();
        if (client == null)
        {
            return "AI endpoint is not configured. Please configure your endpoint in Tools > Options > Code Janitor > XML Documentation.";
        }

        var prompt = BuildExplainPrompt(memberName, codeSnippet);
        var systemPrompt = "You are an expert C# software architect and clean code mentor. Provide clear, well-structured, constructive explanations that help both junior and senior developers understand code functionality, risks, and clean decomposition opportunities.";

        try
        {
            var response = await client.GetChatCompletionContentAsync(systemPrompt, prompt, cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(response)
                ? "The AI model returned an empty response. Please check your model settings."
                : response.Trim();
        }
        catch (Exception ex)
        {
            return $"Error during AI explanation: {ex.Message}";
        }
    }

    private static string BuildExplainPrompt(string memberName, string codeSnippet)
    {
        return $@"Analyze the following C# code for member '{memberName}':

```csharp
{codeSnippet}
```

Please structure your response in clear markdown format using the following sections:

### 1. What This Code Does
A concise summary explaining the business purpose, inputs, and outputs in plain language.

### 2. Complexity & Risk Assessment
- Cyclomatic complexity and branching depth analysis.
- Potential edge case vulnerabilities or hidden risks (e.g. nulls, unhandled exceptions, async traps).

### 3. Step-by-step Refactoring & Decomposition
- Concrete recommendations on how to simplify and decompose this code (e.g. Extract Method, Guard Clauses, Pattern Matching).
- A clean, modern C# code example demonstrating the decomposed/refactored version.
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
            : 30;

        var contextWindowTokens = settings.Cleaning_AiXmlDocumentationContextWindowTokens > 0
            ? settings.Cleaning_AiXmlDocumentationContextWindowTokens
            : 131072;

        return new OpenAiCompatibleClient(endpointUrl, apiKey, header, model, timeoutSeconds, contextWindowTokens);
    }
}
