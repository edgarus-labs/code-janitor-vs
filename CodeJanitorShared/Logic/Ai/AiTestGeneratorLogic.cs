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
        if (client == null)
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
