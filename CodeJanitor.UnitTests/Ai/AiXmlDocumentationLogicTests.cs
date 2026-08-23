using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.UnitTests.Ai;

[TestClass]
public sealed class AiXmlDocumentationLogicTests
{
    [TestMethod]
    public void GenerateXmlDocumentationForSource_InsertsSummaryParamReturnsAndException()
    {
        var source = @"
namespace Demo;

public class Sample
{
public string BuildName(string firstName, string lastName)
{
    if (string.IsNullOrWhiteSpace(firstName))
    {
        throw new ArgumentException(nameof(firstName));
    }

    return firstName + "" "" + lastName;
}
}
";

        var updated = InvokeGenerateXmlDocumentation(source, _ => "Builds a combined display name.", 10);

        StringAssert.Contains(updated, "/// <summary>");
        StringAssert.Contains(updated, "/// Builds a combined display name.");
        StringAssert.Contains(updated, "<param name=\"firstName\">The first name.</param>");
        StringAssert.Contains(updated, "<param name=\"lastName\">The last name.</param>");
        StringAssert.Contains(updated, "<returns>The string result.</returns>");
        StringAssert.Contains(updated, "<exception cref=\"ArgumentException\">");
    }

    [TestMethod]
    public void GenerateXmlDocumentationForSource_DocumentsPositionalRecordWithParamTags()
    {
        var source = @"namespace RecipeVault.Application.Abstractions.CQRS;

public sealed record CreateRecipeCommand(
    string Title,
    string Description,
    Guid AuthorId,
    List<CreateIngredientDto> Ingredients,
    List<CreateStepDto> Steps
) : ICommand<CreateRecipeResponse>;
";

        var updated = InvokeGenerateXmlDocumentation(source, _ => "Represents a command to create a new recipe with the specified details.", 10);

        StringAssert.Contains(updated, "/// <summary>");
        StringAssert.Contains(updated, "/// Represents a command to create a new recipe with the specified details.");
        StringAssert.Contains(updated, "<param name=\"Title\">The title.</param>");
        StringAssert.Contains(updated, "<param name=\"Description\">The description.</param>");
        StringAssert.Contains(updated, "<param name=\"AuthorId\">The unique identifier of the author.</param>");
        StringAssert.Contains(updated, "<param name=\"Ingredients\">The collection of ingredients.</param>");
        StringAssert.Contains(updated, "<param name=\"Steps\">The collection of steps.</param>");
    }

    [TestMethod]
    public void GenerateXmlDocumentationForSource_PlacesBlockDirectlyAboveMemberKeepingIndent()
    {
        var source = "namespace Demo;\r\n\r\npublic class Sample\r\n{\r\n    public int Get(int x)\r\n    {\r\n        return x;\r\n    }\r\n}\r\n";

        var updated = InvokeGenerateXmlDocumentation(source, _ => "Gets a value.", 10);

        var lines = updated.Replace("\r\n", "\n").Split('\n');
        var memberIndex = Array.FindIndex(lines, x => x.Contains("public int Get"));

        Assert.IsTrue(memberIndex > 0, "Method declaration not found.");
        StringAssert.Contains(lines[memberIndex - 1], "///", "A blank line separates the documentation from the member.");
        Assert.AreEqual("    public int Get(int x)", lines[memberIndex], "The member lost its original indentation.");
        StringAssert.StartsWith(lines[memberIndex - 1], "    ///", "The documentation block is not aligned with the member.");
    }

    [TestMethod]
    public void NormalizeSentence_StripsThinkingProcessAndDraftsFromReasoningModels()
    {
        var rawThinking = "Thinking Process: 1. **Analyze the Request:** * Input: C# type information. 2. **Determine Meaning:** Interface for CQRS. 3. **Drafting:** * Draft 1: Represents a command. * Draft 2: Defines a command contract.";
        var result = InvokeNormalizeSentence(rawThinking);
        Assert.AreEqual("Defines a command contract.", result);

        var rawXmlThink = "<think>\nLet's analyze this method.\nIt calculates the sum.\n</think>\nCalculates the sum of two integers.";
        var result2 = InvokeNormalizeSentence(rawXmlThink);
        Assert.AreEqual("Calculates the sum of two integers.", result2);

        var rawPreamble = "Here is the summary sentence: Performs the validation of the given request.";
        var result3 = InvokeNormalizeSentence(rawPreamble);
        Assert.AreEqual("Performs the validation of the given request.", result3);
    }

    private static string InvokeNormalizeSentence(string text)
    {
        var type = Type.GetType("CodeJanitor.Logic.Ai.AiXmlDocumentationLogic, CodeJanitor.VS2026")
                   ?? Type.GetType("CodeJanitor.Logic.Ai.AiXmlDocumentationLogic, CodeJanitor");
        var method = type.GetMethod("NormalizeSentence", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

        return (string)method.Invoke(null, new object[] { text });
    }

    [TestMethod]
    public void GenerateXmlDocumentationForSource_RespectsMethodLimit()
    {
        var source = @"
namespace Demo;

public class Sample
{
public int First(int x)
{
    return x + 1;
}

public int Second(int y)
{
    return y + 2;
}
}
";

        var updated = InvokeGenerateXmlDocumentation(source, m => "Summary for " + GetMemberName(m) + ".", 1);

        var summaryCount = CountOccurrences(updated, "/// <summary>");
        Assert.AreEqual(1, summaryCount, "Only one member should be documented when the limit is 1.");
        StringAssert.Contains(updated, "Summary for Sample.");
        Assert.IsFalse(updated.Contains("Summary for Second."));
    }

    [TestMethod]
    public void GenerateXmlDocumentationForSource_DocumentsTypesAndPropertiesWithoutMethods()
    {
        var source = "namespace Demo;\r\n\r\npublic class WriteRelationsRequest\r\n{\r\n    public string Name { get; set; }\r\n\r\n    public int Count { get; }\r\n}\r\n";

        var updated = InvokeGenerateXmlDocumentation(source, m => "Summary for " + GetMemberName(m) + ".", 10);

        Assert.AreEqual(3, CountOccurrences(updated, "/// <summary>"), "The type and both properties should be documented.");
        StringAssert.Contains(updated, "Summary for WriteRelationsRequest.");
        StringAssert.Contains(updated, "Summary for Name.");
        StringAssert.Contains(updated, "Summary for Count.");
    }

    [TestMethod]
    public void GenerateXmlDocumentationForSource_DocumentsPublicConstAndPublicStaticMembers()
    {
        var source = @"namespace Demo;

public class ConfigClass
{
    public const string Version = ""1.0"";

    public static readonly string DefaultName = ""Test"";

    public static int StaticCounter { get; set; }

    public static string StaticExpressionProp => ""Hello"";
}
";

        var updated = InvokeGenerateXmlDocumentation(source, m => "Summary for " + GetMemberName(m) + ".", 10);

        Assert.AreEqual(5, CountOccurrences(updated, "/// <summary>"), "Class, const field, static readonly field, static property, and static expression property should all be documented.");
        StringAssert.Contains(updated, "Summary for ConfigClass.");
        StringAssert.Contains(updated, "Summary for Version.");
        StringAssert.Contains(updated, "Summary for DefaultName.");
        StringAssert.Contains(updated, "Summary for StaticCounter.");
        StringAssert.Contains(updated, "Summary for StaticExpressionProp.");
    }

    [TestMethod]
    public void GenerateXmlDocumentationForSource_DocumentsPublicFieldsAndSkipsPrivateFields()
    {
        var source = @"namespace Demo;

public class FieldSample
{
    public string PublicField;

    public int PublicNumber = 42;

    private string _privateField;

    int _unspecifiedPrivate;
}
";

        var updated = InvokeGenerateXmlDocumentation(source, m => "Summary for " + GetMemberName(m) + ".", 10);

        Assert.AreEqual(3, CountOccurrences(updated, "/// <summary>"), "Should document class and 2 public fields, but skip 2 private fields.");
        StringAssert.Contains(updated, "Summary for FieldSample.");
        StringAssert.Contains(updated, "Summary for PublicField.");
        StringAssert.Contains(updated, "Summary for PublicNumber.");
        Assert.IsFalse(updated.Contains("Summary for _privateField."));
        Assert.IsFalse(updated.Contains("Summary for _unspecifiedPrivate."));
    }

    [TestMethod]
    public void GenerateXmlDocumentationForSource_DocumentsAllConstFieldsInSinglePassRegardlessOfMethodLimit()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("namespace Demo;");
        sb.AppendLine("public static class LargeConstants {");
        for (int i = 0; i < 50; i++)
        {
            sb.AppendLine($"    public const int Field{i} = {i};");
        }
        sb.AppendLine("}");

        var source = sb.ToString();

        // Max methods per file set to 2, but all 50 constants plus class should be documented
        var updated = InvokeGenerateXmlDocumentation(source, m => "Summary for " + GetMemberName(m) + ".", 2);

        Assert.AreEqual(51, CountOccurrences(updated, "/// <summary>"), "All 50 const fields + class should be documented in a single pass without batching truncation.");
    }

    [TestMethod]
    public void GenerateXmlDocumentationForSource_SkipsMethodsThatAlreadyHaveDocComments()
    {
        var source = @"
namespace Demo;

public class Sample
{
/// <summary>
/// Existing docs.
/// </summary>
public int Existing(int x)
{
    return x;
}

public int Missing(int y)
{
    return y;
}
}
";

        var updated = InvokeGenerateXmlDocumentation(source, _ => "Generated docs.", 10);

        Assert.AreEqual(3, CountOccurrences(updated, "/// <summary>"));
        StringAssert.Contains(updated, "Existing docs.");
        StringAssert.Contains(updated, "Generated docs.");
    }

    [TestMethod]
    public void GenerateXmlDocumentationForSourceInternal_IgnoresObsoleteMethodsWhenEnabled()
    {
        var source = @"
namespace Demo;

public class Sample
{
[Obsolete]
public int Legacy(int x)
{
    return x;
}

public int Active(int y)
{
    return y;
}
}
";

        var updated = InvokeGenerateXmlDocumentationInternal(
            source,
            _ => "Generated docs.",
            (options, optionsType) => SetProperty(optionsType, options, "IgnoreObsolete", true));

        Assert.AreEqual(2, CountOccurrences(updated, "/// <summary>"));
        StringAssert.Contains(updated, "public int Active");
        StringAssert.Contains(updated, "Generated docs.");
    }

    [TestMethod]
    public void GenerateXmlDocumentationForSourceInternal_IgnoresLikelyTestMethodsWhenEnabled()
    {
        var source = @"
namespace Demo;

public class SampleTests
{
[Fact]
public void UsesFact()
{
}

public void AlsoATestByTypeName()
{
}

public void ProductionLike()
{
}
}

public class RealService
{
public void DoWork()
{
}
}
";

        var updated = InvokeGenerateXmlDocumentationInternal(
            source,
            _ => "Generated docs.",
            (options, optionsType) => SetProperty(optionsType, options, "IgnoreTestMethods", true));

        Assert.AreEqual(2, CountOccurrences(updated, "/// <summary>"));
        StringAssert.Contains(updated, "public void DoWork()");
    }

    [TestMethod]
    public void GenerateXmlDocumentationForSourceInternal_IgnoresMethodsMatchingRegex()
    {
        var source = @"
namespace Demo;

public class Sample
{
public void KeepThis()
{
}

public void SkipThisOne()
{
}
}
";

        var updated = InvokeGenerateXmlDocumentationInternal(
            source,
            _ => "Generated docs.",
            (options, optionsType) => SetProperty(optionsType, options, "IgnorePattern", "SkipThisOne$"));

        Assert.AreEqual(2, CountOccurrences(updated, "/// <summary>"));
        Assert.IsTrue(updated.Contains("public void KeepThis()"));
        Assert.IsTrue(updated.Contains("public void SkipThisOne()"));
    }

    [TestMethod]
    public void GenerateXmlDocumentationForSourceInternal_SkipsMethodsWhenSummaryProviderReturnsNull()
    {
        var source = @"
namespace Demo;

public class Sample
{
public int First(int x)
{
    return x + 1;
}

public int Second(int y)
{
    return y + 2;
}
}
";

        var updated = InvokeGenerateXmlDocumentationInternal(
            source,
            _ => null,
            (options, optionsType) => { });

        Assert.AreEqual(0, CountOccurrences(updated, "/// <summary>"));
    }

    private static string InvokeGenerateXmlDocumentation(string source, Func<MemberDeclarationSyntax, string> summaryProvider, int maxMethodsPerFile)
    {
        var assembly = typeof(CodeJanitor.Properties.Settings).Assembly;
        var type = assembly.GetType("CodeJanitor.Logic.Ai.AiXmlDocumentationLogic", throwOnError: true);
        var method = type.GetMethod("GenerateXmlDocumentationForSource", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method, "Could not locate GenerateXmlDocumentationForSource via reflection.");

        var result = method.Invoke(null, new object[] { source, summaryProvider, maxMethodsPerFile });

        return result as string;
    }

    private static string InvokeGenerateXmlDocumentationInternal(string source, Func<MemberDeclarationSyntax, string> summaryProvider, Action<object, Type> configureOptions)
    {
        var assembly = typeof(CodeJanitor.Properties.Settings).Assembly;
        var logicType = assembly.GetType("CodeJanitor.Logic.Ai.AiXmlDocumentationLogic", throwOnError: true);
        var optionsType = assembly.GetType("CodeJanitor.Logic.Ai.AiXmlDocumentationLogic+AiXmlDocumentationRunOptions", throwOnError: true);
        var statsType = assembly.GetType("CodeJanitor.Logic.Ai.AiXmlDocumentationLogic+AiXmlDocumentationRunStats", throwOnError: true);
        var method = logicType.GetMethod("GenerateXmlDocumentationForSourceInternal", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method, "Could not locate GenerateXmlDocumentationForSourceInternal via reflection.");

        var options = Activator.CreateInstance(optionsType);
        SetProperty(optionsType, options, "MaxMethodsPerFile", 25);
        SetProperty(optionsType, options, "MaxRequestsPerCleanup", 25);
        SetProperty(optionsType, options, "MaxInputCharsPerMethod", 2500);
        SetProperty(optionsType, options, "MaxTokensPerRequest", 256);
        SetProperty(optionsType, options, "MaxEstimatedTokensPerCleanup", int.MaxValue);
        SetProperty(optionsType, options, "GlobalTimeoutSeconds", 60);
        SetProperty(optionsType, options, "AllowDeterministicFallback", true);
        SetProperty(optionsType, options, "IgnoreGeneratedCode", false);
        SetProperty(optionsType, options, "IgnoreObsolete", false);
        SetProperty(optionsType, options, "IgnoreTestMethods", false);
        SetProperty(optionsType, options, "IgnorePattern", string.Empty);
        SetProperty(optionsType, options, "PreviewChanges", false);

        configureOptions?.Invoke(options, optionsType);

        var stats = Activator.CreateInstance(statsType);
        var result = method.Invoke(null, new object[] { source, summaryProvider, options, stats });

        return result as string;
    }

    private static void SetProperty(Type type, object instance, string name, object value)
    {
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(property, "Missing property on options object: " + name);
        property.SetValue(instance, value);
    }

    [TestMethod]
    public void OpenAiCompatibleClient_IsEndpointConfigured_ValidatesUrlAndKey()
    {
        var assembly = typeof(CodeJanitor.Properties.Settings).Assembly;
        var clientType = assembly.GetType("CodeJanitor.Logic.Ai.OpenAiCompatibleClient", throwOnError: true);
        var method = clientType.GetMethod("IsEndpointConfigured", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method);

        Assert.IsTrue((bool)method.Invoke(null, new object[] { "https://api.openai.com/v1", "sk-12345" }));
        Assert.IsTrue((bool)method.Invoke(null, new object[] { "http://localhost:11434/v1", "\"sk-quoted-key\"" }));
        Assert.IsTrue((bool)method.Invoke(null, new object[] { "https://api.openai.com/v1", "" }));
        Assert.IsTrue((bool)method.Invoke(null, new object[] { "http://192.168.1.52:20128/v1", null }));
        Assert.IsFalse((bool)method.Invoke(null, new object[] { "", "sk-12345" }));
        Assert.IsFalse((bool)method.Invoke(null, new object[] { "invalid-url", "sk-12345" }));
    }

    [TestMethod]
    public void OpenAiCompatibleClient_GetNormalizedEndpointUrl_AppendsChatCompletionsWhenNeeded()
    {
        var assembly = typeof(CodeJanitor.Properties.Settings).Assembly;
        var clientType = assembly.GetType("CodeJanitor.Logic.Ai.OpenAiCompatibleClient", throwOnError: true);
        var method = clientType.GetMethod("GetNormalizedEndpointUrl", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method);

        Assert.AreEqual("https://api.openai.com/v1/chat/completions", method.Invoke(null, new object[] { "https://api.openai.com/v1" }));
        Assert.AreEqual("https://api.openai.com/v1/chat/completions", method.Invoke(null, new object[] { "https://api.openai.com/v1/" }));
        Assert.AreEqual("https://api.openai.com/v1/chat/completions", method.Invoke(null, new object[] { "https://api.openai.com/v1/chat/completions" }));
        Assert.AreEqual("http://localhost:11434/v1/chat/completions", method.Invoke(null, new object[] { "http://localhost:11434/v1" }));
    }

    [TestMethod]
    public async Task OpenAiCompatibleClient_TestConnectionAsync_TimesOutWithoutBlocking()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverCancellation = new CancellationTokenSource();
        var serverTask = Task.Run(async () =>
        {
            try
            {
                using (var connection = await listener.AcceptTcpClientAsync())
                using (var stream = connection.GetStream())
                {
                    var buffer = new byte[4096];
                    await stream.ReadAsync(buffer, 0, buffer.Length);
                    await Task.Delay(TimeSpan.FromSeconds(10), serverCancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        try
        {
            var assembly = typeof(CodeJanitor.Properties.Settings).Assembly;
            var clientType = assembly.GetType("CodeJanitor.Logic.Ai.OpenAiCompatibleClient", throwOnError: true);
            var client = Activator.CreateInstance(
                clientType,
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: new object[] { $"http://127.0.0.1:{port}/v1", "test-key", "Authorization", "test-model", 1 },
                culture: null);
            var method = clientType.GetMethod("TestConnectionAsync", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(method);

            var stopwatch = Stopwatch.StartNew();
            var testTask = (Task)method.Invoke(client, null);
            await testTask;
            stopwatch.Stop();

            var result = testTask.GetType().GetProperty("Result").GetValue(testTask);
            var resultType = result.GetType();
            var succeeded = (bool)resultType.GetProperty("Succeeded", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(result);
            var errorMessage = (string)resultType.GetProperty("ErrorMessage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(result);

            Assert.IsFalse(succeeded);
            StringAssert.Contains(errorMessage, "timed out after 1 seconds");
            Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Connection test took {stopwatch.Elapsed}.");
        }
        finally
        {
            serverCancellation.Cancel();
            listener.Stop();
            await serverTask;
            serverCancellation.Dispose();
        }
    }

    [TestMethod]
    public void OpenAiCompatibleClient_TryExtractContentFromChatResponse_HandlesOpenAiAndDeepSeekFormats()
    {
        var assembly = typeof(CodeJanitor.Properties.Settings).Assembly;
        var clientType = assembly.GetType("CodeJanitor.Logic.Ai.OpenAiCompatibleClient", throwOnError: true);
        var method = clientType.GetMethod("TryExtractContentFromChatResponse", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method);

        // Standard OpenAI message
        var openAiJson = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"OK\"}}]}";
        Assert.AreEqual("OK", method.Invoke(null, new object[] { openAiJson }));

        // OpenAI with content as array of blocks
        var openAiBlocksJson = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"Block 1 \"},{\"text\":\"Block 2\"}]}}]}";
        Assert.AreEqual("Block 1 Block 2", method.Invoke(null, new object[] { openAiBlocksJson }));

        // DeepSeek Reasoner with content
        var deepSeekJson = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Result text\",\"reasoning_content\":\"Thinking...\"}}]}";
        Assert.AreEqual("Result text", method.Invoke(null, new object[] { deepSeekJson }));

        // DeepSeek Reasoner with empty content (tokens exhausted by reasoning)
        var deepSeekReasoningOnly = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"reasoning_content\":\"Summary from reasoning\"}}]}";
        Assert.AreEqual("Summary from reasoning", method.Invoke(null, new object[] { deepSeekReasoningOnly }));

        // DeepSeek with 'reasoning' or 'thought' field
        var deepSeekAltReasoning = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"reasoning\":\"Alternative reasoning text\"}}]}";
        Assert.AreEqual("Alternative reasoning text", method.Invoke(null, new object[] { deepSeekAltReasoning }));

        // Legacy / completions endpoint format
        var textJson = "{\"choices\":[{\"text\":\"Direct text\"}]}";
        Assert.AreEqual("Direct text", method.Invoke(null, new object[] { textJson }));

        // Invalid or empty JSON
        Assert.IsNull(method.Invoke(null, new object[] { "{}" }));
        Assert.IsNull(method.Invoke(null, new object[] { "{\"choices\":[]}" }));
        Assert.IsNull(method.Invoke(null, new object[] { (string)null }));
    }

    [TestMethod]
    public void OpenAiCompatibleClient_TryExtractContentFromChatResponse_HandlesClaudeGeminiOllamaAndGateways()
    {
        var assembly = typeof(CodeJanitor.Properties.Settings).Assembly;
        var clientType = assembly.GetType("CodeJanitor.Logic.Ai.OpenAiCompatibleClient", throwOnError: true);
        var method = clientType.GetMethod("TryExtractContentFromChatResponse", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method);

        // Anthropic Claude native messages format (content array of blocks)
        var claudeNativeJson = "{\"id\":\"msg_123\",\"type\":\"message\",\"content\":[{\"type\":\"text\",\"text\":\"Claude message text\"}]}";
        Assert.AreEqual("Claude message text", method.Invoke(null, new object[] { claudeNativeJson }));

        // Anthropic Claude legacy format
        var claudeLegacyJson = "{\"completion\":\"Claude legacy completion\"}";
        Assert.AreEqual("Claude legacy completion", method.Invoke(null, new object[] { claudeLegacyJson }));

        // Google Gemini native format (candidates -> content -> parts)
        var geminiJson = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Gemini documentation text\"}]}}]}";
        Assert.AreEqual("Gemini documentation text", method.Invoke(null, new object[] { geminiJson }));

        // Ollama native generate format
        var ollamaGenerateJson = "{\"response\":\"Ollama generated text\"}";
        Assert.AreEqual("Ollama generated text", method.Invoke(null, new object[] { ollamaGenerateJson }));

        // Ollama native chat format
        var ollamaChatJson = "{\"message\":{\"role\":\"assistant\",\"content\":\"Ollama chat text\"}}";
        Assert.AreEqual("Ollama chat text", method.Invoke(null, new object[] { ollamaChatJson }));

        // Gateway / OmniRoute wrapping response in "data" array
        var dataArrayChoicesJson = "{\"data\":[{\"choices\":[{\"message\":{\"content\":\"Data array choices content\"}}]}]}";
        Assert.AreEqual("Data array choices content", method.Invoke(null, new object[] { dataArrayChoicesJson }));

        var dataArrayMessageJson = "{\"data\":[{\"message\":{\"content\":\"Data array message content\"}}]}";
        Assert.AreEqual("Data array message content", method.Invoke(null, new object[] { dataArrayMessageJson }));

        var dataArrayDirectContentJson = "{\"data\":[{\"content\":\"Data array direct content\"}]}";
        Assert.AreEqual("Data array direct content", method.Invoke(null, new object[] { dataArrayDirectContentJson }));

        var dataArrayDirectStringJson = "{\"data\":[\"Data array direct string\"]}";
        Assert.AreEqual("Data array direct string", method.Invoke(null, new object[] { dataArrayDirectStringJson }));

        // Gateway / OpenAI-compatible endpoint with "data" as single object
        var dataObjectChoicesJson = "{\"data\":{\"choices\":[{\"message\":{\"content\":\"Data object choices content\"}}]}}";
        Assert.AreEqual("Data object choices content", method.Invoke(null, new object[] { dataObjectChoicesJson }));

        var dataObjectDirectJson = "{\"data\":{\"content\":\"Data object direct content\"}}";
        Assert.AreEqual("Data object direct content", method.Invoke(null, new object[] { dataObjectDirectJson }));

        var dataStringJson = "{\"data\":\"Direct string in data property\"}";
        Assert.AreEqual("Direct string in data property", method.Invoke(null, new object[] { dataStringJson }));

        // Root JSON is an array of objects
        var rootArrayJson = "[{\"choices\":[{\"message\":{\"content\":\"Root array content\"}}]}]";
        Assert.AreEqual("Root array content", method.Invoke(null, new object[] { rootArrayJson }));

        // HuggingFace / TGI generated_text format
        var huggingFaceJson = "[{\"generated_text\":\"HuggingFace generated text\"}]";
        Assert.AreEqual("HuggingFace generated text", method.Invoke(null, new object[] { huggingFaceJson }));

        // Result / Output wrappers
        var resultWrapperJson = "{\"result\":{\"choices\":[{\"message\":{\"content\":\"Result wrapper content\"}}]}}";
        Assert.AreEqual("Result wrapper content", method.Invoke(null, new object[] { resultWrapperJson }));

        var outputWrapperJson = "{\"output\":\"Output string content\"}";
        Assert.AreEqual("Output string content", method.Invoke(null, new object[] { outputWrapperJson }));
    }

    [TestMethod]
    public void OpenAiCompatibleClient_TryExtractContentFromChatResponse_ReassemblesServerSentEventStream()
    {
        var assembly = typeof(CodeJanitor.Properties.Settings).Assembly;
        var clientType = assembly.GetType("CodeJanitor.Logic.Ai.OpenAiCompatibleClient", throwOnError: true);
        var method = clientType.GetMethod("TryExtractContentFromChatResponse", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method);

        // OpenAI SSE
        var stream = "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\",\"content\":\"Builds \"}}]}\n"
            + "\n"
            + "data: {\"choices\":[{\"delta\":{\"content\":\"a name.\"}}]}\n"
            + "\n"
            + "data: [DONE]\n";

        Assert.AreEqual("Builds a name.", method.Invoke(null, new object[] { stream }));

        // Claude SSE
        var claudeStream = "event: content_block_delta\n"
            + "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Claude \"}}\n"
            + "event: content_block_delta\n"
            + "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"stream.\"}}\n"
            + "event: message_stop\n"
            + "data: {\"type\":\"message_stop\"}\n";

        Assert.AreEqual("Claude stream.", method.Invoke(null, new object[] { claudeStream }));

        // Gemini SSE
        var geminiStream = "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Gemini \"}]}}]}\n"
            + "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"stream.\"}]}}]}\n";

        Assert.AreEqual("Gemini stream.", method.Invoke(null, new object[] { geminiStream }));

        // Data array in SSE
        var dataArrayStream = "data: [{\"choices\":[{\"delta\":{\"content\":\"Data array stream.\"}}]}]\n";
        Assert.AreEqual("Data array stream.", method.Invoke(null, new object[] { dataArrayStream }));
    }

    [TestMethod]
    public void OpenAiCompatibleClient_TryExtractContentFromChatResponse_ReturnsNullForUnparseableText()
    {
        var assembly = typeof(CodeJanitor.Properties.Settings).Assembly;
        var clientType = assembly.GetType("CodeJanitor.Logic.Ai.OpenAiCompatibleClient", throwOnError: true);
        var method = clientType.GetMethod("TryExtractContentFromChatResponse", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method);

        Assert.IsNull(method.Invoke(null, new object[] { "data: not-json" }));
        Assert.IsNull(method.Invoke(null, new object[] { "<html>gateway error</html>" }));
    }

    [TestMethod]
    public void OpenAiCompatibleClient_BuildRequestJson_RequestsNonStreamingResponse()
    {
        var assembly = typeof(CodeJanitor.Properties.Settings).Assembly;
        var clientType = assembly.GetType("CodeJanitor.Logic.Ai.OpenAiCompatibleClient", throwOnError: true);
        var client = Activator.CreateInstance(
            clientType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { "http://127.0.0.1:1234/v1", "test-key", "Authorization", "test-model", 30 },
            culture: null);
        var method = clientType.GetMethod("BuildRequestJson", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(int) }, null);

        Assert.IsNotNull(method);

        var json = (string)method.Invoke(client, new object[] { "prompt", 128 });

        StringAssert.Contains(json, "\"stream\":false");
    }

    [TestMethod]
    public void AiXmlDocumentationLogic_CancelRun_AbortsGenerationImmediately()
    {
        var assembly = typeof(CodeJanitor.Properties.Settings).Assembly;
        var logicType = assembly.GetType("CodeJanitor.Logic.Ai.AiXmlDocumentationLogic", throwOnError: true);
        var beginRunMethod = logicType.GetMethod("BeginRun", BindingFlags.NonPublic | BindingFlags.Static);
        var cancelRunMethod = logicType.GetMethod("CancelRun", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(beginRunMethod);
        Assert.IsNotNull(cancelRunMethod);

        beginRunMethod.Invoke(null, null);

        var source = @"
namespace Demo;

public class Sample
{
    public int MethodOne() => 1;
    public int MethodTwo() => 2;
}
";
        var callCount = 0;
        var updated = InvokeGenerateXmlDocumentation(source, m =>
        {
            callCount++;
            cancelRunMethod.Invoke(null, null);

            return "Summary";
        }, 10);

        Assert.AreEqual(1, callCount, "Should abort immediately after cancellation without continuing to other methods.");
    }

    private static string GetMemberName(MemberDeclarationSyntax member)
    {
        if (member is BaseTypeDeclarationSyntax type) return type.Identifier.ValueText;
        if (member is MethodDeclarationSyntax method) return method.Identifier.ValueText;
        if (member is PropertyDeclarationSyntax property) return property.Identifier.ValueText;
        if (member is FieldDeclarationSyntax field) return field.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText ?? "Field";

        return member.Kind().ToString();
    }

    private static int CountOccurrences(string text, string value)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
        {
            return 0;
        }

        return text.Split(new[] { value }, StringSplitOptions.None).Length - 1;
    }
}
