using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Ai;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.UnitTests.Ai;

[TestClass]
public sealed class AiFeaturesLogicTests
{
    [TestMethod]
    public async Task AiExplainLogic_ExplainCodeAsync_WhenCodeIsEmpty_ReturnsNoCodeMessage()
    {
        // Arrange
        var logic = CreateInstance<AiExplainLogic>();

        // Act
        var result = await logic.ExplainCodeAsync("TestMethod", string.Empty, CancellationToken.None);

        // Assert
        Assert.IsTrue(result.Contains("No code provided"), $"Expected 'No code provided' message but got: {result}");
    }

    [TestMethod]
    public async Task AiTestGeneratorLogic_GenerateUnitTestsAsync_WhenCodeIsEmpty_ReturnsNoCodeComment()
    {
        // Arrange
        var logic = CreateInstance<AiTestGeneratorLogic>();

        // Act
        var result = await logic.GenerateUnitTestsAsync("TestMethod", "   ", CancellationToken.None);

        // Assert
        Assert.IsTrue(result.Contains("No code provided"), $"Expected 'No code provided' comment but got: {result}");
    }

    [TestMethod]
    public void AiTestGeneratorLogic_ExtractCodeSnippet_ExtractsFromMarkdownBlocks()
    {
        // Arrange
        var responseWithMarkdown = "Here is the unit test class:\n```csharp\nusing Xunit;\npublic class SampleTests { }\n```\nHope this helps!";

        var method = typeof(AiTestGeneratorLogic).GetMethod("ExtractCodeSnippet", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method, "ExtractCodeSnippet method should exist on AiTestGeneratorLogic");

        // Act
        var extracted = (string)method.Invoke(null, new object[] { responseWithMarkdown });

        // Assert
        Assert.AreEqual("using Xunit;\npublic class SampleTests { }", extracted);
    }

    [TestMethod]
    public async Task AiCleanRefactorLogic_RefactorCodeAsync_WhenCodeIsEmpty_ReturnsFailure()
    {
        // Arrange
        var logic = CreateInstance<AiCleanRefactorLogic>();

        // Act
        var result = await logic.RefactorCodeAsync("TestMethod", null, CancellationToken.None);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.ErrorMessage.Contains("No code provided"));
    }

    [TestMethod]
    public void AiCleanRefactorLogic_ExtractCodeSnippet_ExtractsCleanCode()
    {
        // Arrange
        var response = "Key improvements:\n- Guard clauses\n- Flattened nesting\n```csharp\npublic void CleanMethod() { return; }\n```";

        var method = typeof(AiCleanRefactorLogic).GetMethod("ExtractCodeSnippet", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method, "ExtractCodeSnippet method should exist on AiCleanRefactorLogic");

        // Act
        var extracted = (string)method.Invoke(null, new object[] { response });

        // Assert
        Assert.AreEqual("public void CleanMethod() { return; }", extracted);
    }

    [TestMethod]
    public async Task AiCodeReviewLogic_ReviewCodeAsync_WhenCodeIsEmpty_ReturnsNoCodeMessage()
    {
        // Arrange
        var logic = CreateInstance<AiCodeReviewLogic>();

        // Act
        var result = await logic.ReviewCodeAsync("TestMethod", "", CancellationToken.None);

        // Assert
        Assert.IsTrue(result.Contains("No code provided"));
    }

    [TestMethod]
    public void AiExplainLogic_BuildExplainPrompt_IncludesMemberNameAndCode()
    {
        // Arrange
        var method = typeof(AiExplainLogic).GetMethod("BuildExplainPrompt", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method, "BuildExplainPrompt method should exist");

        // Act
        var prompt = (string)method.Invoke(null, new object[] { "CalculateDiscount", "public int CalculateDiscount() => 10;" });

        // Assert
        Assert.IsTrue(prompt.Contains("CalculateDiscount"));
        Assert.IsTrue(prompt.Contains("public int CalculateDiscount() => 10;"));
        Assert.IsTrue(prompt.Contains("Complexity & Risk Assessment"));
    }

    [TestMethod]
    public void AiTestGeneratorLogic_ExtractCodeSnippet_HandlesGenericCodeBlockAndPlainText()
    {
        // Arrange
        var method = typeof(AiTestGeneratorLogic).GetMethod("ExtractCodeSnippet", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        // Act & Assert - generic code block ```
        var genericBlock = "Summary:\n```\npublic class GenericTest {}\n```";
        var extractedGeneric = (string)method.Invoke(null, new object[] { genericBlock });
        Assert.AreEqual("public class GenericTest {}", extractedGeneric);

        // Act & Assert - plain text
        var plain = "public class PlainTest {}";
        var extractedPlain = (string)method.Invoke(null, new object[] { plain });
        Assert.AreEqual("public class PlainTest {}", extractedPlain);

        // Act & Assert - null/empty
        var extractedEmpty = (string)method.Invoke(null, new object[] { "   " });
        Assert.AreEqual(string.Empty, extractedEmpty);
    }

    [TestMethod]
    public void AiCleanRefactorLogic_ExtractCodeSnippet_HandlesGenericMarkdownAndEmptyInput()
    {
        // Arrange
        var method = typeof(AiCleanRefactorLogic).GetMethod("ExtractCodeSnippet", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        // Act & Assert - generic block
        var genericBlock = "Code:\n```\npublic void Simple() {}\n```";
        var extracted = (string)method.Invoke(null, new object[] { genericBlock });
        Assert.AreEqual("public void Simple() {}", extracted);

        // Act & Assert - null/empty
        var extractedEmpty = (string)method.Invoke(null, new object[] { null });
        Assert.AreEqual(string.Empty, extractedEmpty);
    }

    [TestMethod]
    public void AiCodeReviewLogic_BuildReviewPrompt_IncludesTargetAndSections()
    {
        var method = typeof(AiCodeReviewLogic).GetMethod("BuildReviewPrompt", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        var prompt = (string)method.Invoke(null, new object[] { "OrderService", "public void PlaceOrder() { }" });
        Assert.IsTrue(prompt.Contains("OrderService"));
        Assert.IsTrue(prompt.Contains("public void PlaceOrder() { }"));
        Assert.IsTrue(prompt.Contains("Critical Issues & Bugs"));
        Assert.IsTrue(prompt.Contains("Clean Code & Maintainability Tips"));
    }

    [TestMethod]
    public void AiCleanRefactorLogic_BuildRefactorPrompt_IncludesMemberNameAndGoals()
    {
        var method = typeof(AiCleanRefactorLogic).GetMethod("BuildRefactorPrompt", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        var prompt = (string)method.Invoke(null, new object[] { "ProcessPayment", "public bool ProcessPayment() { return true; }" });
        Assert.IsTrue(prompt.Contains("ProcessPayment"));
        Assert.IsTrue(prompt.Contains("public bool ProcessPayment() { return true; }"));
        Assert.IsTrue(prompt.Contains("Guard Clauses"));
        Assert.IsTrue(prompt.Contains("Refactoring Goals"));
    }

    [TestMethod]
    public void AiTestGeneratorLogic_BuildTestPrompt_IncludesFrameworkAndMocking()
    {
        var method = typeof(AiTestGeneratorLogic).GetMethod("BuildTestPrompt", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        var prompt = (string)method.Invoke(null, new object[] { "Calculator", "public int Add(int a, int b) => a + b;", "MSTest", "NSubstitute" });
        Assert.IsTrue(prompt.Contains("Calculator"));
        Assert.IsTrue(prompt.Contains("MSTest"));
        Assert.IsTrue(prompt.Contains("NSubstitute"));
        Assert.IsTrue(prompt.Contains("Arrange-Act-Assert"));
    }

    [TestMethod]
    public async Task AiFeatures_WhenEndpointNotConfigured_ReturnConfigurationNotice()
    {
        CodeJanitor.Properties.Settings.Default.Cleaning_AiXmlDocumentationEndpointUrl = string.Empty;

        var explain = CreateInstance<AiExplainLogic>();
        var testGen = CreateInstance<AiTestGeneratorLogic>();
        var refactor = CreateInstance<AiCleanRefactorLogic>();
        var review = CreateInstance<AiCodeReviewLogic>();

        var explainResult = await explain.ExplainCodeAsync("M", "void M() {}", CancellationToken.None);
        Assert.IsTrue(explainResult.Contains("AI endpoint is not configured"));

        var testGenResult = await testGen.GenerateUnitTestsAsync("M", "void M() {}", CancellationToken.None);
        Assert.IsTrue(testGenResult.Contains("AI endpoint is not configured"));

        var refactorResult = await refactor.RefactorCodeAsync("M", "void M() {}", CancellationToken.None);
        Assert.IsFalse(refactorResult.Success);
        Assert.IsTrue(refactorResult.ErrorMessage.Contains("AI endpoint is not configured"));

        var reviewResult = await review.ReviewCodeAsync("M", "void M() {}", CancellationToken.None);
        Assert.IsTrue(reviewResult.Contains("AI endpoint is not configured"));

        Assert.IsFalse(AiExplainLogic.IsConfigurationPresent());
        Assert.IsFalse(AiTestGeneratorLogic.IsConfigurationPresent());
        Assert.IsFalse(AiCleanRefactorLogic.IsConfigurationPresent());
        Assert.IsFalse(AiCodeReviewLogic.IsConfigurationPresent());
    }

    [TestMethod]
    public void OpenAiCompatibleClient_IsLocalEndpoint_IdentifiesLocalAndRemoteAddresses()
    {
        Assert.IsTrue(OpenAiCompatibleClient.IsLocalEndpoint("http://localhost:11434/v1"));
        Assert.IsTrue(OpenAiCompatibleClient.IsLocalEndpoint("http://127.0.0.1:8080/v1"));
        Assert.IsTrue(OpenAiCompatibleClient.IsLocalEndpoint("http://10.0.1.5:1234"));
        Assert.IsTrue(OpenAiCompatibleClient.IsLocalEndpoint("http://172.20.0.1:8000"));
        Assert.IsTrue(OpenAiCompatibleClient.IsLocalEndpoint("http://192.168.1.100:5000"));

        Assert.IsFalse(OpenAiCompatibleClient.IsLocalEndpoint("https://api.openai.com/v1"));
        Assert.IsFalse(OpenAiCompatibleClient.IsLocalEndpoint("https://api.anthropic.com/v1"));
        Assert.IsFalse(OpenAiCompatibleClient.IsLocalEndpoint("https://8.8.8.8:8080"));
        Assert.IsFalse(OpenAiCompatibleClient.IsLocalEndpoint("not a valid uri"));
        Assert.IsFalse(OpenAiCompatibleClient.IsLocalEndpoint(null));
    }

    [TestMethod]
    public void OpenAiCompatibleClient_EndpointNormalization_PreservesKnownEndpoints()
    {
        Assert.AreEqual("http://localhost:11434/api/generate", OpenAiCompatibleClient.GetNormalizedEndpointUrl("http://localhost:11434/api/generate"));
        Assert.AreEqual("https://api.anthropic.com/v1/messages", OpenAiCompatibleClient.GetNormalizedEndpointUrl("https://api.anthropic.com/v1/messages"));
        Assert.AreEqual("https://api.openai.com/v1/completions", OpenAiCompatibleClient.GetNormalizedEndpointUrl("https://api.openai.com/v1/completions"));
        Assert.AreEqual("http://localhost:1234/v1/chat/completions", OpenAiCompatibleClient.GetNormalizedEndpointUrl("http://localhost:1234/v1"));
        Assert.AreEqual(string.Empty, OpenAiCompatibleClient.GetNormalizedEndpointUrl(string.Empty));
        Assert.IsNull(OpenAiCompatibleClient.GetNormalizedEndpointUrl(null));
    }

    [TestMethod]
    public void OpenAiCompatibleClient_ConstructorsAndProperties_InitializeCorrectly()
    {
        var client = new OpenAiCompatibleClient("http://localhost:11434", "secret-key", "X-Api-Key", "llama3", 45, 8192);

        Assert.AreEqual("http://localhost:11434/chat/completions", client.EndpointUrl);
        Assert.AreEqual("secret-key", client.ApiKey);
        Assert.AreEqual("X-Api-Key", client.ApiKeyHeader);
        Assert.AreEqual("llama3", client.Model);
        Assert.AreEqual(45, client.TimeoutSeconds);
        Assert.AreEqual(8192, client.ContextWindowTokens);
        Assert.IsTrue(client.IsConfigured);
    }

    [TestMethod]
    public void OpenAiCompatibleClient_TryExtractContent_ExtractsVariedProviderPayloads()
    {
        // 1. Choices with text
        var textPayload = "{\"choices\":[{\"text\":\"Extracted text\"}]}";
        Assert.AreEqual("Extracted text", OpenAiCompatibleClient.TryExtractContentFromChatResponse(textPayload));

        // 2. Candidates with content parts
        var geminiPayload = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Gemini answer\"}]}}]}";
        Assert.AreEqual("Gemini answer", OpenAiCompatibleClient.TryExtractContentFromChatResponse(geminiPayload));

        // 3. Direct response or output
        var directOutput = "{\"output\":\"Direct model output\"}";
        Assert.AreEqual("Direct model output", OpenAiCompatibleClient.TryExtractContentFromChatResponse(directOutput));

        // 4. Fallback reasoning fields
        var reasoningPayload = "{\"reasoning_content\":\"DeepSeek reasoning output\"}";
        Assert.AreEqual("DeepSeek reasoning output", OpenAiCompatibleClient.TryExtractContentFromChatResponse(reasoningPayload));
    }

    [TestMethod]
    public void OpenAiCompatibleClient_TryGenerateDocumentation_WithUnconfiguredEndpoint_ReturnsFalse()
    {
        var client = new OpenAiCompatibleClient(string.Empty, null, null, null, 10);
        var success = client.TryGenerateDocumentation("Document this method", out var completion, out var error);

        Assert.IsFalse(success);
        Assert.IsNull(completion);
        Assert.IsTrue(error.Contains("not configured"));
    }

    [TestMethod]
    public void AiRefactorResult_Properties_SetAndGetCorrectly()
    {
        var result = new AiRefactorResult
        {
            Success = true,
            RefactoredCode = "public void Clean() {}",
            Explanation = "Simplified",
            ErrorMessage = null
        };

        Assert.IsTrue(result.Success);
        Assert.AreEqual("public void Clean() {}", result.RefactoredCode);
        Assert.AreEqual("Simplified", result.Explanation);
        Assert.IsNull(result.ErrorMessage);
    }

    private static T CreateInstance<T>() where T : class
    {
        var ctor = typeof(T).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(CodeJanitorPackage) }, null);

        return (T)ctor?.Invoke(new object[] { null });
    }
}
