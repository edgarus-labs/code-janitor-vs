using CodeJanitor.Helpers;
using CodeJanitor.Properties;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Provides AI-assisted Clean Code refactoring, Guard Clause insertion, and modern C# pattern adoption.
/// </summary>
internal sealed class AiCleanRefactorLogic
{
    private readonly CodeJanitorPackage _package;
    private static AiCleanRefactorLogic _instance;

    /// <summary>
    /// Internal static GetInstance lazily returns the singleton AiCleanRefactorLogic, creating and assigning a new instance from the given CodeJanitorPackage to the static _instance field as a side effect when it is null.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>A AiCleanRefactorLogic value produced by this method.</returns>
    internal static AiCleanRefactorLogic GetInstance(CodeJanitorPackage package) =>
        _instance ?? (_instance = new AiCleanRefactorLogic(package));

    private AiCleanRefactorLogic(CodeJanitorPackage package)
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
    /// Refactors the provided C# code snippet into idiomatic, clean C# with early exits/guard clauses and flattened nesting.
    /// </summary>
    internal async Task<AiRefactorResult> RefactorCodeAsync(string memberName, string codeSnippet, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(codeSnippet))
        {
            return new AiRefactorResult
            {
                Success = false,
                ErrorMessage = "No code provided for refactoring."
            };
        }

        var client = CreateClientFromSettings();
        if (client is null)
        {
            return new AiRefactorResult
            {
                Success = false,
                ErrorMessage = "AI endpoint is not configured. Please configure your endpoint in Tools > Options > Code Janitor > XML Documentation."
            };
        }

        var prompt = BuildRefactorPrompt(memberName, codeSnippet);
        var systemPrompt = "You are a master C# refactoring expert adhering to Clean Code, SOLID principles, and modern C# idioms. Your goal is to simplify nested logic using Guard Clauses, simplify LINQ, improve readability, and preserve exact semantics. Provide your response with a concise bullet list of improvements, followed by the complete refactored C# code inside a ```csharp ``` block.";

        try
        {
            var response = await client.GetChatCompletionContentAsync(systemPrompt, prompt, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(response))
            {
                return new AiRefactorResult
                {
                    Success = false,
                    ErrorMessage = "The AI model returned an empty response. Please check your model settings."
                };
            }

            var refactoredCode = ExtractCodeSnippet(response);

            return new AiRefactorResult
            {
                Success = true,
                RefactoredCode = refactoredCode,
                Explanation = response.Trim()
            };
        }
        catch (Exception ex)
        {
            return new AiRefactorResult
            {
                Success = false,
                ErrorMessage = $"Error during AI refactoring: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// BuildRefactorPrompt interpolates memberName and codeSnippet into a verbatim string template and returns a refactoring prompt listing five Clean Code goals with no side effects or exceptions.
    /// </summary>
    /// <param name="memberName">The member name.</param>
    /// <param name="codeSnippet">The code snippet.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string BuildRefactorPrompt(string memberName, string codeSnippet)
    {
        return $@"Refactor the following C# code for '{memberName}' to follow modern Clean Code standards:

```csharp
{codeSnippet}
```

Refactoring Goals:
1. Flatten deep nesting using Guard Clauses (early returns / inverted `if` checks).
2. Simplify complex boolean conditions and LINQ queries.
3. Apply modern C# pattern matching, null checks, and idiomatic constructs.
4. Keep exact business semantics and error handling intact.
5. Provide a summary of key changes made, followed by the complete refactored C# method.";
    }

    /// <summary>
    /// Extracts a trimmed C# code snippet from an AI response by locating a ```csharp fenced block or a generic ``` block (skipping a short language identifier after the opening fence), returning the inner content or the trimmed original string if none is found, with no side effects.
    /// </summary>
    /// <param name="aiResponse">The ai response.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string ExtractCodeSnippet(string aiResponse)
    {
        if (string.IsNullOrWhiteSpace(aiResponse))
        {
            return string.Empty;
        }

        var startIndex = aiResponse.IndexOf("```csharp", StringComparison.OrdinalIgnoreCase);
        if (startIndex >= 0)
        {
            var codeStart = startIndex + "```csharp".Length;
            var endIndex = aiResponse.IndexOf("```", codeStart, StringComparison.Ordinal);
            if (endIndex > codeStart)
            {
                return aiResponse.Substring(codeStart, endIndex - codeStart).Trim();
            }
        }

        startIndex = aiResponse.IndexOf("```", StringComparison.Ordinal);
        if (startIndex >= 0)
        {
            var codeStart = startIndex + 3;
            var newlineIndex = aiResponse.IndexOf('\n', codeStart);
            if (newlineIndex >= codeStart && newlineIndex - codeStart < 15)
            {
                codeStart = newlineIndex + 1;
            }
            var endIndex = aiResponse.IndexOf("```", codeStart, StringComparison.Ordinal);
            if (endIndex > codeStart)
            {
                return aiResponse.Substring(codeStart, endIndex - codeStart).Trim();
            }
        }

        return aiResponse.Trim();
    }

    /// <summary>
    /// Creates an OpenAiCompatibleClient from Settings.Default (unprotecting the encrypted API key with plaintext fallback, defaulting header to Authorization, timeout to 40s and context window to 131072) or returns null when the endpoint is unconfigured.
    /// </summary>
    /// <returns>A OpenAiCompatibleClient value produced by this method.</returns>
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
            : 40;

        var contextWindowTokens = settings.Cleaning_AiXmlDocumentationContextWindowTokens > 0
            ? settings.Cleaning_AiXmlDocumentationContextWindowTokens
            : 131072;

        return new OpenAiCompatibleClient(endpointUrl, apiKey, header, model, timeoutSeconds, contextWindowTokens);
    }
}
