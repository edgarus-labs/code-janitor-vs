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

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public class AiXmlDocumentationLogicTests
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
        StringAssert.Contains(updated, "<returns>A string value produced by this method.</returns>");
        StringAssert.Contains(updated, "<exception cref=\"ArgumentException\">");
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

        var updated = InvokeGenerateXmlDocumentation(source, m => "Summary for " + m.Identifier.ValueText + ".", 1);

        var summaryCount = CountOccurrences(updated, "/// <summary>");
        Assert.AreEqual(1, summaryCount, "Only one method should be documented when maxMethodsPerFile=1.");
        StringAssert.Contains(updated, "Summary for First.");
        Assert.IsFalse(updated.Contains("Summary for Second."));
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

        Assert.AreEqual(2, CountOccurrences(updated, "/// <summary>"));
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

        Assert.AreEqual(1, CountOccurrences(updated, "/// <summary>"));
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

        Assert.AreEqual(1, CountOccurrences(updated, "/// <summary>"));
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

        Assert.AreEqual(1, CountOccurrences(updated, "/// <summary>"));
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

    private static string InvokeGenerateXmlDocumentation(string source, Func<MethodDeclarationSyntax, string> summaryProvider, int maxMethodsPerFile)
    {
        var assembly = typeof(CodeJanitor.Properties.Settings).Assembly;
        var type = assembly.GetType("CodeJanitor.Logic.Cleaning.AiXmlDocumentationLogic", throwOnError: true);
        var method = type.GetMethod("GenerateXmlDocumentationForSource", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method, "Could not locate GenerateXmlDocumentationForSource via reflection.");

        var result = method.Invoke(null, new object[] { source, summaryProvider, maxMethodsPerFile });

        return result as string;
    }

    private static string InvokeGenerateXmlDocumentationInternal(string source, Func<MethodDeclarationSyntax, string> summaryProvider, Action<object, Type> configureOptions)
    {
        var assembly = typeof(CodeJanitor.Properties.Settings).Assembly;
        var logicType = assembly.GetType("CodeJanitor.Logic.Cleaning.AiXmlDocumentationLogic", throwOnError: true);
        var optionsType = assembly.GetType("CodeJanitor.Logic.Cleaning.AiXmlDocumentationLogic+AiXmlDocumentationRunOptions", throwOnError: true);
        var statsType = assembly.GetType("CodeJanitor.Logic.Cleaning.AiXmlDocumentationLogic+AiXmlDocumentationRunStats", throwOnError: true);
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
        var clientType = assembly.GetType("CodeJanitor.Logic.Cleaning.OpenAiCompatibleClient", throwOnError: true);
        var method = clientType.GetMethod("IsEndpointConfigured", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method);

        Assert.IsTrue((bool)method.Invoke(null, new object[] { "https://api.openai.com/v1", "sk-12345" }));
        Assert.IsTrue((bool)method.Invoke(null, new object[] { "http://localhost:11434/v1", "\"sk-quoted-key\"" }));
        Assert.IsFalse((bool)method.Invoke(null, new object[] { "", "sk-12345" }));
        Assert.IsFalse((bool)method.Invoke(null, new object[] { "https://api.openai.com/v1", "" }));
        Assert.IsFalse((bool)method.Invoke(null, new object[] { "invalid-url", "sk-12345" }));
    }

    [TestMethod]
    public void OpenAiCompatibleClient_GetNormalizedEndpointUrl_AppendsChatCompletionsWhenNeeded()
    {
        var assembly = typeof(CodeJanitor.Properties.Settings).Assembly;
        var clientType = assembly.GetType("CodeJanitor.Logic.Cleaning.OpenAiCompatibleClient", throwOnError: true);
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
            var clientType = assembly.GetType("CodeJanitor.Logic.Cleaning.OpenAiCompatibleClient", throwOnError: true);
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
        var clientType = assembly.GetType("CodeJanitor.Logic.Cleaning.OpenAiCompatibleClient", throwOnError: true);
        var method = clientType.GetMethod("TryExtractContentFromChatResponse", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method);

        // Standard OpenAI message
        var openAiJson = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"OK\"}}]}";
        Assert.AreEqual("OK", method.Invoke(null, new object[] { openAiJson }));

        // DeepSeek Reasoner with content
        var deepSeekJson = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Result text\",\"reasoning_content\":\"Thinking...\"}}]}";
        Assert.AreEqual("Result text", method.Invoke(null, new object[] { deepSeekJson }));

        // DeepSeek Reasoner with empty content (tokens exhausted by reasoning)
        var deepSeekReasoningOnly = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"reasoning_content\":\"Summary from reasoning\"}}]}";
        Assert.AreEqual("Summary from reasoning", method.Invoke(null, new object[] { deepSeekReasoningOnly }));

        // Legacy / completions endpoint format
        var textJson = "{\"choices\":[{\"text\":\"Direct text\"}]}";
        Assert.AreEqual("Direct text", method.Invoke(null, new object[] { textJson }));

        // Invalid or empty JSON
        Assert.IsNull(method.Invoke(null, new object[] { "{}" }));
        Assert.IsNull(method.Invoke(null, new object[] { "{\"choices\":[]}" }));
        Assert.IsNull(method.Invoke(null, new object[] { (string)null }));
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