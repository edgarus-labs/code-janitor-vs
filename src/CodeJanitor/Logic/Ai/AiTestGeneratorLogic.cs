using CodeJanitor.Helpers;
using CodeJanitor.Properties;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Provides AI-assisted unit test scaffolding and edge case generation for C# methods and classes.
/// </summary>
internal sealed class AiTestGeneratorLogic
{
    private readonly CodeJanitorPackage _package;
    private static AiTestGeneratorLogic _instance;

    /// <summary>
    /// Returns the lazily initialized singleton AiTestGeneratorLogic instance, creating and assigning a new one from the given package to the static field if it is currently null.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>A AiTestGeneratorLogic value produced by this method.</returns>
    internal static AiTestGeneratorLogic GetInstance(CodeJanitorPackage package) =>
        _instance ?? (_instance = new AiTestGeneratorLogic(package));

    private AiTestGeneratorLogic(CodeJanitorPackage package)
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
    /// Generates unit tests for the supplied code snippet using configured test framework and mocking library.
    /// </summary>
    internal async Task<string> GenerateUnitTestsAsync(string memberOrClassName, string codeSnippet, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(codeSnippet))
        {
            return "// No code provided to generate unit tests for.";
        }

        var client = CreateClientFromSettings();
        if (client is null)
        {
            return "// AI endpoint is not configured. Please configure your endpoint in Tools > Options > Code Janitor > XML Documentation.";
        }

        var settings = Settings.Default;
        var testFramework = string.IsNullOrWhiteSpace(settings.Ai_TestFramework) ? "xUnit" : settings.Ai_TestFramework;
        var mockingLib = string.IsNullOrWhiteSpace(settings.Ai_MockingLibrary) ? "Moq" : settings.Ai_MockingLibrary;

        var prompt = BuildTestPrompt(memberOrClassName, codeSnippet, testFramework, mockingLib);
        var systemPrompt = $"You are an expert C# unit testing specialist using {testFramework} and {mockingLib}. Generate complete, high quality, production-ready unit test classes with exhaustive edge-case coverage and clean naming conventions (MethodName_Scenario_ExpectedResult). Output ONLY the test code inside a markdown csharp code block, without conversational filler.";

        try
        {
            var response = await client.GetChatCompletionContentAsync(systemPrompt, prompt, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(response))
            {
                return "// The AI model returned an empty response. Please check your model settings.";
            }

            return ExtractCodeSnippet(response.Trim());
        }
        catch (Exception ex)
        {
            return $"// Error during AI unit test generation: {ex.Message}";
        }
    }

    /// <summary>
    /// Constructs and returns a formatted multi-line prompt string that requests generation of a complete unit-test class for the supplied C# snippet using the given test framework and mocking library, covering happy paths, argument validations, edge cases, and AAA-style methods.
    /// </summary>
    /// <param name="memberOrClassName">The member or class name.</param>
    /// <param name="codeSnippet">The code snippet.</param>
    /// <param name="testFramework">The test framework.</param>
    /// <param name="mockingLib">The mocking lib.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string BuildTestPrompt(string memberOrClassName, string codeSnippet, string testFramework, string mockingLib)
    {
        return $@"Generate a comprehensive suite of unit tests for the following C# code ('{memberOrClassName}') using {testFramework} and {mockingLib}:

```csharp
{codeSnippet}
```

Requirements:
1. Target Framework: {testFramework}
2. Mocking Library (if needed): {mockingLib}
3. Include test methods for:
   - Happy paths with expected valid outputs
   - Null or empty argument validations (asserting ArgumentNullException / ArgumentException)
   - Boundary/edge conditions and special values
   - Asynchronous execution / exception throwing paths if applicable
4. Use clean Arrange-Act-Assert structure and readable method names following: `MethodName_Condition_ExpectedResult`.
5. Return the complete test class file with necessary using statements.";
    }

    /// <summary>
    /// Extracts the trimmed interior of a markdown code fence from the AI response, preferring a case-insensitive ```csharp block then a generic ``` block (skipping a short language identifier after the opening fence), falling back to the trimmed original string or empty for null/whitespace input, with no side effects.
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
    /// an `OpenAiCompatibleClient` from user settings, returning null when the endpoint URL is not configured, attempting to unprotect the encrypted API key (falling back to the plaintext value via the `ApiKey` field assignment if that yields empty), and applying defaults of 45 seconds for timeout and 131072 tokens for context window when not explicitly set.
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
            : 45;

        var contextWindowTokens = settings.Cleaning_AiXmlDocumentationContextWindowTokens > 0
            ? settings.Cleaning_AiXmlDocumentationContextWindowTokens
            : 131072;

        return new OpenAiCompatibleClient(endpointUrl, apiKey, header, model, timeoutSeconds, contextWindowTokens);
    }
}
