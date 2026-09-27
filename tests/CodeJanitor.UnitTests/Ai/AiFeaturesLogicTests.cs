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
        AiExplainLogic logic = CreateInstance<AiExplainLogic>();

        // Act
        string result = await logic.ExplainCodeAsync("TestMethod", string.Empty, CancellationToken.None);

        // Assert
        Assert.Contains("No code provided", result, $"Expected 'No code provided' message but got: {result}");
    }

    [TestMethod]
    public async Task AiTestGeneratorLogic_GenerateUnitTestsAsync_WhenCodeIsEmpty_ReturnsNoCodeComment()
    {
        // Arrange
        AiTestGeneratorLogic logic = CreateInstance<AiTestGeneratorLogic>();

        // Act
        string result = await logic.GenerateUnitTestsAsync("TestMethod", "   ", CancellationToken.None);

        // Assert
        Assert.Contains("No code provided", result, $"Expected 'No code provided' comment but got: {result}");
    }

    [TestMethod]
    public void AiTestGeneratorLogic_ExtractCodeSnippet_ExtractsFromMarkdownBlocks()
    {
        // Arrange
        string responseWithMarkdown = "Here is the unit test class:\n```csharp\nusing Xunit;\npublic class SampleTests { }\n```\nHope this helps!";

        MethodInfo method = typeof(AiTestGeneratorLogic).GetMethod("ExtractCodeSnippet", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method, "ExtractCodeSnippet method should exist on AiTestGeneratorLogic");

        // Act
        string extracted = (string)method.Invoke(null, new object[] { responseWithMarkdown });

        // Assert
        Assert.AreEqual("using Xunit;\npublic class SampleTests { }", extracted);
    }

    [TestMethod]
    public async Task AiCleanRefactorLogic_RefactorCodeAsync_WhenCodeIsEmpty_ReturnsFailure()
    {
        // Arrange
        AiCleanRefactorLogic logic = CreateInstance<AiCleanRefactorLogic>();

        // Act
        AiRefactorResult result = await logic.RefactorCodeAsync("TestMethod", null, CancellationToken.None);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.Contains("No code provided", result.ErrorMessage);
    }

    [TestMethod]
    public void AiCleanRefactorLogic_ExtractCodeSnippet_ExtractsCleanCode()
    {
        // Arrange
        string response = "Key improvements:\n- Guard clauses\n- Flattened nesting\n```csharp\npublic void CleanMethod() { return; }\n```";

        MethodInfo method = typeof(AiCleanRefactorLogic).GetMethod("ExtractCodeSnippet", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method, "ExtractCodeSnippet method should exist on AiCleanRefactorLogic");

        // Act
        string extracted = (string)method.Invoke(null, new object[] { response });

        // Assert
        Assert.AreEqual("public void CleanMethod() { return; }", extracted);
    }

    [TestMethod]
    public async Task AiCodeReviewLogic_ReviewCodeAsync_WhenCodeIsEmpty_ReturnsNoCodeMessage()
    {
        // Arrange
        AiCodeReviewLogic logic = CreateInstance<AiCodeReviewLogic>();

        // Act
        string result = await logic.ReviewCodeAsync("TestMethod", "", CancellationToken.None);

        // Assert
        Assert.Contains("No code provided", result);
    }

    [TestMethod]
    public void AiExplainLogic_BuildExplainPrompt_IncludesMemberNameAndCode()
    {
        // Arrange
        MethodInfo method = typeof(AiExplainLogic).GetMethod("BuildExplainPrompt", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method, "BuildExplainPrompt method should exist");

        // Act
        string prompt = (string)method.Invoke(null, new object[] { "CalculateDiscount", "public int CalculateDiscount() => 10;" });

        // Assert
        Assert.Contains("CalculateDiscount", prompt);
        Assert.Contains("public int CalculateDiscount() => 10;", prompt);
        Assert.Contains("Complexity & Risk Assessment", prompt);
    }

    [TestMethod]
    public void AiTestGeneratorLogic_ExtractCodeSnippet_HandlesGenericCodeBlockAndPlainText()
    {
        // Arrange
        MethodInfo method = typeof(AiTestGeneratorLogic).GetMethod("ExtractCodeSnippet", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        // Act & Assert - generic code block ```
        string genericBlock = "Summary:\n```\npublic class GenericTest {}\n```";
        string extractedGeneric = (string)method.Invoke(null, new object[] { genericBlock });
        Assert.AreEqual("public class GenericTest {}", extractedGeneric);

        // Act & Assert - plain text
        string plain = "public class PlainTest {}";
        string extractedPlain = (string)method.Invoke(null, new object[] { plain });
        Assert.AreEqual("public class PlainTest {}", extractedPlain);

        // Act & Assert - null/empty
        string extractedEmpty = (string)method.Invoke(null, new object[] { "   " });
        Assert.AreEqual(string.Empty, extractedEmpty);
    }

    [TestMethod]
    public void AiCleanRefactorLogic_ExtractCodeSnippet_HandlesGenericMarkdownAndEmptyInput()
    {
        // Arrange
        MethodInfo method = typeof(AiCleanRefactorLogic).GetMethod("ExtractCodeSnippet", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        // Act & Assert - generic block
        string genericBlock = "Code:\n```\npublic void Simple() {}\n```";
        string extracted = (string)method.Invoke(null, new object[] { genericBlock });
        Assert.AreEqual("public void Simple() {}", extracted);

        // Act & Assert - null/empty
        string extractedEmpty = (string)method.Invoke(null, new object[] { null });
        Assert.AreEqual(string.Empty, extractedEmpty);
    }

    [TestMethod]
    public void AiCodeReviewLogic_BuildReviewPrompt_IncludesTargetAndSections()
    {
        MethodInfo method = typeof(AiCodeReviewLogic).GetMethod("BuildReviewPrompt", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        string prompt = (string)method.Invoke(null, new object[] { "OrderService", "public void PlaceOrder() { }" });
        Assert.Contains("OrderService", prompt);
        Assert.Contains("public void PlaceOrder() { }", prompt);
        Assert.Contains("Critical Issues & Bugs", prompt);
        Assert.Contains("Clean Code & Maintainability Tips", prompt);
    }

    [TestMethod]
    public void AiCleanRefactorLogic_BuildRefactorPrompt_IncludesMemberNameAndGoals()
    {
        MethodInfo method = typeof(AiCleanRefactorLogic).GetMethod("BuildRefactorPrompt", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        string prompt = (string)method.Invoke(null, new object[] { "ProcessPayment", "public bool ProcessPayment() { return true; }" });
        Assert.Contains("ProcessPayment", prompt);
        Assert.Contains("public bool ProcessPayment() { return true; }", prompt);
        Assert.Contains("Guard Clauses", prompt);
        Assert.Contains("Refactoring Goals", prompt);
    }

    [TestMethod]
    public void AiTestGeneratorLogic_BuildTestPrompt_IncludesFrameworkAndMocking()
    {
        MethodInfo method = typeof(AiTestGeneratorLogic).GetMethod("BuildTestPrompt", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        string prompt = (string)method.Invoke(null, new object[] { "Calculator", "public int Add(int a, int b) => a + b;", "MSTest", "NSubstitute" });
        Assert.Contains("Calculator", prompt);
        Assert.Contains("MSTest", prompt);
        Assert.Contains("NSubstitute", prompt);
        Assert.Contains("Arrange-Act-Assert", prompt);
    }

    [TestMethod]
    public async Task AiFeatures_WhenEndpointNotConfigured_ReturnConfigurationNotice()
    {
        CodeJanitor.Properties.Settings.Default.Cleaning_AiXmlDocumentationEndpointUrl = string.Empty;

        AiExplainLogic explain = CreateInstance<AiExplainLogic>();
        AiTestGeneratorLogic testGen = CreateInstance<AiTestGeneratorLogic>();
        AiCleanRefactorLogic refactor = CreateInstance<AiCleanRefactorLogic>();
        AiCodeReviewLogic review = CreateInstance<AiCodeReviewLogic>();

        string explainResult = await explain.ExplainCodeAsync("M", "void M() {}", CancellationToken.None);
        Assert.Contains("AI endpoint is not configured", explainResult);

        string testGenResult = await testGen.GenerateUnitTestsAsync("M", "void M() {}", CancellationToken.None);
        Assert.Contains("AI endpoint is not configured", testGenResult);

        AiRefactorResult refactorResult = await refactor.RefactorCodeAsync("M", "void M() {}", CancellationToken.None);
        Assert.IsFalse(refactorResult.Success);
        Assert.Contains("AI endpoint is not configured", refactorResult.ErrorMessage);

        string reviewResult = await review.ReviewCodeAsync("M", "void M() {}", CancellationToken.None);
        Assert.Contains("AI endpoint is not configured", reviewResult);

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
        OpenAiCompatibleClient client = new OpenAiCompatibleClient("http://localhost:11434", "secret-key", "X-Api-Key", "llama3", 45, 8192);

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
        string textPayload = "{\"choices\":[{\"text\":\"Extracted text\"}]}";
        Assert.AreEqual("Extracted text", OpenAiCompatibleClient.TryExtractContentFromChatResponse(textPayload));

        // 2. Candidates with content parts
        string geminiPayload = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Gemini answer\"}]}}]}";
        Assert.AreEqual("Gemini answer", OpenAiCompatibleClient.TryExtractContentFromChatResponse(geminiPayload));

        // 3. Direct response or output
        string directOutput = "{\"output\":\"Direct model output\"}";
        Assert.AreEqual("Direct model output", OpenAiCompatibleClient.TryExtractContentFromChatResponse(directOutput));

        // 4. Fallback reasoning fields
        string reasoningPayload = "{\"reasoning_content\":\"DeepSeek reasoning output\"}";
        Assert.AreEqual("DeepSeek reasoning output", OpenAiCompatibleClient.TryExtractContentFromChatResponse(reasoningPayload));
    }

    [TestMethod]
    public void OpenAiCompatibleClient_TryGenerateDocumentation_WithUnconfiguredEndpoint_ReturnsFalse()
    {
        OpenAiCompatibleClient client = new OpenAiCompatibleClient(string.Empty, null, null, null, 10);
        bool success = client.TryGenerateDocumentation("Document this method", out string completion, out string error, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(success);
        Assert.IsNull(completion);
        Assert.Contains("not configured", error);
    }

    [TestMethod]
    public void AiRefactorResult_Properties_SetAndGetCorrectly()
    {
        AiRefactorResult result = new AiRefactorResult
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
        ConstructorInfo ctor = typeof(T).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(CodeJanitorPackage) }, null);

        return (T)ctor?.Invoke(new object[] { null });
    }

    public TestContext TestContext { get; set; }
}
