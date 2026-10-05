using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Ai;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using CodeJanitor.UI.Dialogs.CleanupProgress;
using EnvDTE80;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Threading;
using NSubstitute;

namespace CodeJanitor.UnitTests.Ai;

[TestClass]
[TestCategory("Ai UnitTests")]
public sealed class AiXmlDocumentationLimitsTests
{
    private static readonly Type LogicType = typeof(AiXmlDocumentationLogic);

    private FakeChatServer _server;

    [TestInitialize]
    public void TestInitialize()
    {
        Settings.Default.Reset();
        AiXmlDocumentationLogic.BeginRun();
        _server = new FakeChatServer();
        Settings.Default.Cleaning_AiXmlDocumentationEnabled = true;
        Settings.Default.Cleaning_AiXmlDocumentationEndpointUrl = _server.EndpointUrl;
    }

    [TestCleanup]
    public void TestCleanup()
    {
        _server.Dispose();
        Settings.Default.Reset();
        AiXmlDocumentationLogic.BeginRun();
    }

    [TestMethod]
    public void Enabled_On_DocumentsEveryUndocumentedMethodThroughTheChatCompletionsEndpoint()
    {
        string output = Apply(Source("Alpha", "Bravo", "Charlie"));

        Assert.HasCount(3, _server.Requests);
        Assert.IsTrue(_server.Requests.All(request => request.Method == "POST" && request.Path == "/v1/chat/completions"));
        Assert.Contains("/// Summary for Alpha.", output);
        Assert.Contains("/// Summary for Bravo.", output);
        Assert.Contains("/// Summary for Charlie.", output);
        Assert.AreEqual(1, CountOccurrences(output, "Existing."), "The documentation of the class must stay untouched.");
    }

    [TestMethod]
    public void Enabled_Off_SendsNoRequestAndLeavesTheSourceUnchanged()
    {
        Settings.Default.Cleaning_AiXmlDocumentationEnabled = false;
        string source = Source("Alpha", "Bravo");

        string output = Apply(source);

        Assert.AreEqual(source, output);
        Assert.IsEmpty(_server.Requests);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("not a url")]
    [DataRow("ftp://127.0.0.1/v1")]
    public void EndpointUrl_MissingOrNotHttp_SendsNoRequestAndLeavesTheSourceUnchanged(string endpointUrl)
    {
        Settings.Default.Cleaning_AiXmlDocumentationEndpointUrl = endpointUrl;
        string source = Source("Alpha", "Bravo");

        string output = Apply(source);

        Assert.IsFalse(AiXmlDocumentationLogic.IsConfigurationPresent());
        Assert.AreEqual(source, output);
        Assert.IsNull(CreateClientFromSettings());
        Assert.IsEmpty(_server.Requests);
    }

    [TestMethod]
    public void EndpointUrl_FullChatCompletionsUrl_IsUsedAsConfigured()
    {
        Settings.Default.Cleaning_AiXmlDocumentationEndpointUrl = _server.EndpointUrl + "/chat/completions";

        Apply(Source("Alpha"));

        Assert.HasCount(1, _server.Requests);
        Assert.AreEqual("/v1/chat/completions", _server.Requests[0].Path);
    }

    [TestMethod]
    public void PreviewChanges_On_PlainApplyWritesNothingAndSendsNoRequest()
    {
        Settings.Default.Cleaning_AiXmlDocumentationPreviewChanges = true;
        string source = Source("Alpha", "Bravo");

        string output = Apply(source);

        Assert.AreEqual(source, output, "With the preview on, the documentation is applied only after the user confirmed it in the editor.");
        Assert.IsEmpty(_server.Requests);
    }

    [TestMethod]
    public void PreviewChanges_On_ApplyIgnoringPreviewStillDocumentsTheSource()
    {
        Settings.Default.Cleaning_AiXmlDocumentationPreviewChanges = true;

        string output = AiXmlDocumentationLogic.GetInstance(null).ApplyXmlDocumentationToSourceIgnoringPreview(Source("Alpha", "Bravo"));

        Assert.HasCount(2, _server.Requests);
        Assert.Contains("/// Summary for Alpha.", output);
        Assert.Contains("/// Summary for Bravo.", output);
    }

    [TestMethod]
    [DataRow(true, true, true, true)]
    [DataRow(true, true, false, false)]
    [DataRow(true, false, true, false)]
    [DataRow(false, true, true, false)]
    public void RequiresEditorCleanupForCSharp_OnlyForPreviewedXmlDocumentationDuringCleanup(bool enabled, bool runDuringCleanup, bool preview, bool expected)
    {
        Settings.Default.Cleaning_AiXmlDocumentationEnabled = enabled;
        Settings.Default.Cleaning_AiXmlDocumentationRunDuringCleanup = runDuringCleanup;
        Settings.Default.Cleaning_AiXmlDocumentationPreviewChanges = preview;

        Assert.AreEqual(expected, CodeCleanupManager.RequiresEditorCleanupForCSharp());
    }

    [TestMethod]
    [DataRow(true, true, true)]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    public void RunDuringCleanup_DocumentsTheClosedFileOnlyWhenEnabledAndRunDuringCleanup(bool enabled, bool runDuringCleanup, bool expectedDocumented)
    {
        string directory = CreateTempDirectory();
        try
        {
            string filePath = Path.Combine(directory, "Sample.cs");
            string source = Source("Alpha", "Bravo");
            File.WriteAllText(filePath, source);
            Settings.Default.Cleaning_AiXmlDocumentationEnabled = enabled;
            Settings.Default.Cleaning_AiXmlDocumentationRunDuringCleanup = runDuringCleanup;

            bool changed = false;
            RunOnVisualStudioUIThread(() =>
            {
                CodeCleanupManager manager = (CodeCleanupManager)FormatterServices.GetUninitializedObject(typeof(CodeCleanupManager));
                typeof(CodeCleanupManager).GetField("_aiXmlDocumentationLogic", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(manager, AiXmlDocumentationLogic.GetInstance(null));
                EnvDTE.ProjectItem item = CreateClosedFileItem(filePath);

                changed = ThreadHelper.JoinableTaskFactory.Run(() => manager.RunXmlDocumentationDuringCleanupAsync(item));
            });

            string written = File.ReadAllText(filePath);
            Assert.AreEqual(expectedDocumented, changed);
            Assert.AreEqual(expectedDocumented, written.Contains("/// Summary for Alpha.") && written.Contains("/// Summary for Bravo."));
            Assert.AreEqual(expectedDocumented ? 2 : 0, _server.Requests.Count);
            if (!expectedDocumented)
            {
                Assert.AreEqual(source, written);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(2, 2)]
    public void MaxParallelFiles_LimitsTheFilesDocumentedConcurrently(int maxParallelFiles, int expectedMaxConcurrentRequests)
    {
        Settings.Default.Cleaning_AiXmlDocumentationMaxParallelFiles = maxParallelFiles;
        _server.Respond = request => Ok("Summary for " + request.MethodName + ".", delayMs: 300);
        string directory = CreateTempDirectory();
        try
        {
            string[] filePaths = Enumerable.Range(1, 4).Select(index => Path.Combine(directory, "File" + index + ".cs")).ToArray();
            foreach (string filePath in filePaths)
            {
                File.WriteAllText(filePath, Source("Alpha"));
            }

            int changedCount = -1;
            int failedCount = -1;
            List<string> errors = [];
            RunOnVisualStudioUIThread(() =>
            {
                CodeJanitorPackage package = CreatePackage(Substitute.For<DTE2>());
                List<EnvDTE.ProjectItem> items = filePaths.Select(CreateClosedFileItem).ToList();
                XmlDocProgressViewModel viewModel = new XmlDocProgressViewModel(package, items, errors.Add);
                WaitForBatch(viewModel);

                Assert.IsTrue(viewModel.DialogResult, "The batch must complete.");
                changedCount = viewModel.ChangedCount;
                failedCount = viewModel.FailedCount;
            });

            Assert.IsEmpty(errors);
            Assert.AreEqual(0, failedCount);
            Assert.AreEqual(4, changedCount);
            Assert.HasCount(4, _server.Requests);
            Assert.AreEqual(expectedMaxConcurrentRequests, _server.MaxInFlight);
            Assert.IsTrue(filePaths.All(filePath => File.ReadAllText(filePath).Contains("/// Summary for Alpha.")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ApiKey_Plain_IsSentAsBearerTokenOnlyInTheAuthorizationHeader()
    {
        Settings.Default.Cleaning_AiXmlDocumentationApiKey = "plain-secret-123";

        Apply(Source("Alpha"));

        FakeRequest request = _server.Requests.Single();
        Assert.AreEqual("Bearer plain-secret-123", request.Headers["Authorization"]);
        AssertSecretOnlyIn(request, "plain-secret-123", "Authorization");
    }

    [TestMethod]
    public void ApiKey_AlreadyPrefixedWithBearer_IsNotPrefixedTwice()
    {
        Settings.Default.Cleaning_AiXmlDocumentationApiKey = "Bearer prefixed-secret";

        Apply(Source("Alpha"));

        Assert.AreEqual("Bearer prefixed-secret", _server.Requests.Single().Headers["Authorization"]);
    }

    [TestMethod]
    public void ApiKey_Encrypted_WinsOverThePlainLegacyKey()
    {
        Settings.Default.Cleaning_AiXmlDocumentationApiKey = "legacy-plain-key";
        Settings.Default.Cleaning_AiXmlDocumentationApiKeyEncrypted = SecretProtectionHelper.ProtectForCurrentUser("encrypted-secret-456");

        Apply(Source("Alpha"));

        FakeRequest request = _server.Requests.Single();
        Assert.AreEqual("Bearer encrypted-secret-456", request.Headers["Authorization"]);
        AssertSecretOnlyIn(request, "encrypted-secret-456", "Authorization");
        Assert.IsFalse(request.Headers.Values.Any(value => value.Contains("legacy-plain-key")));
        Assert.DoesNotContain("legacy-plain-key", request.Body);
    }

    [TestMethod]
    public void ApiKey_EncryptedThatCannotBeDecrypted_FallsBackToThePlainLegacyKey()
    {
        Settings.Default.Cleaning_AiXmlDocumentationApiKey = "legacy-plain-key";
        Settings.Default.Cleaning_AiXmlDocumentationApiKeyEncrypted = "enc:AAAA";

        Apply(Source("Alpha"));

        Assert.AreEqual("Bearer legacy-plain-key", _server.Requests.Single().Headers["Authorization"]);
    }

    [TestMethod]
    public void ApiKey_NoneConfigured_SendsNoAuthorizationHeader()
    {
        Apply(Source("Alpha"));

        Assert.IsFalse(_server.Requests.Single().Headers.ContainsKey("Authorization"));
    }

    [TestMethod]
    public void ApiKeyHeader_Custom_CarriesTheRawKeyAndNoAuthorizationHeaderIsSent()
    {
        Settings.Default.Cleaning_AiXmlDocumentationApiKey = "header-secret-789";
        Settings.Default.Cleaning_AiXmlDocumentationApiKeyHeader = "X-Api-Key";

        Apply(Source("Alpha"));

        FakeRequest request = _server.Requests.Single();
        Assert.AreEqual("header-secret-789", request.Headers["X-Api-Key"]);
        Assert.IsFalse(request.Headers.ContainsKey("Authorization"));
        AssertSecretOnlyIn(request, "header-secret-789", "X-Api-Key");
    }

    [TestMethod]
    public void ApiKeyHeader_Blank_FallsBackToTheAuthorizationHeader()
    {
        Settings.Default.Cleaning_AiXmlDocumentationApiKey = "blank-header-secret";
        Settings.Default.Cleaning_AiXmlDocumentationApiKeyHeader = "  ";

        Apply(Source("Alpha"));

        Assert.AreEqual("Bearer blank-header-secret", _server.Requests.Single().Headers["Authorization"]);
    }

    [TestMethod]
    public void Model_Configured_IsSentInTheRequestBody()
    {
        Settings.Default.Cleaning_AiXmlDocumentationModel = "my-model-1";

        Apply(Source("Alpha"));

        Assert.Contains("\"model\":\"my-model-1\"", _server.Requests.Single().Body);
    }

    [TestMethod]
    public void Model_Empty_IsOmittedFromTheRequestBody()
    {
        Settings.Default.Cleaning_AiXmlDocumentationModel = string.Empty;

        Apply(Source("Alpha"));

        Assert.DoesNotContain("\"model\"", _server.Requests.Single().Body);
    }

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(100, 100)]
    [DataRow(0, 256)]
    [DataRow(-5, 256)]
    public void MaxTokensPerRequest_IsSentAsMaxTokens_WithADefaultWhenNotPositive(int configured, int expectedSent)
    {
        Settings.Default.Cleaning_AiXmlDocumentationMaxTokensPerRequest = configured;

        Apply(Source("Alpha"));

        Assert.Contains("\"max_tokens\":" + expectedSent + ",", _server.Requests.Single().Body + ",");
    }

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(4096, 4096)]
    [DataRow(0, 131072)]
    public void ContextWindowTokens_IsSentAsNumCtxToALocalEndpoint_WithADefaultWhenNotPositive(int configured, int expectedSent)
    {
        Settings.Default.Cleaning_AiXmlDocumentationContextWindowTokens = configured;

        Apply(Source("Alpha"));

        Assert.Contains("\"num_ctx\":" + expectedSent + ",", _server.Requests.Single().Body.Replace("}", ",}"));
    }

    [TestMethod]
    [DataRow(2, 2)]
    [DataRow(3, 3)]
    [DataRow(4, 3)]
    public void MaxMethodsPerFile_DocumentsOnlyTheFirstMethodsOfTheFile(int limit, int expectedDocumented)
    {
        Settings.Default.Cleaning_AiXmlDocumentationMaxMethodsPerFile = limit;
        string[] names = ["Alpha", "Bravo", "Charlie"];

        PipelineRun run = RunPipeline(Source(names));

        Assert.HasCount(expectedDocumented, _server.Requests);
        for (int index = 0; index < names.Length; index++)
        {
            Assert.AreEqual(index < expectedDocumented, run.Output.Contains("Summary for " + names[index] + "."), names[index]);
        }

        Assert.AreEqual(names.Length - expectedDocumented, run.Stats.SkippedByBudget);
    }

    [TestMethod]
    [DataRow(2, 2)]
    [DataRow(3, 3)]
    [DataRow(4, 3)]
    public void MaxRequestsPerCleanup_StopsSendingRequestsOnceTheLimitIsReached(int limit, int expectedRequests)
    {
        Settings.Default.Cleaning_AiXmlDocumentationMaxRequestsPerCleanup = limit;
        string[] names = ["Alpha", "Bravo", "Charlie"];

        PipelineRun run = RunPipeline(Source(names));

        Assert.HasCount(expectedRequests, _server.Requests);
        Assert.AreEqual(expectedRequests, run.Stats.AttemptedMethods);

        // The methods are documented from the end of the file, so the first ones are the methods left without a request.
        for (int index = 0; index < names.Length; index++)
        {
            bool expectedDocumented = index >= names.Length - expectedRequests;
            Assert.AreEqual(expectedDocumented, run.Output.Contains("Summary for " + names[index] + "."), names[index]);
        }
    }

    [TestMethod]
    [DataRow(12, true)]
    [DataRow(13, false)]
    [DataRow(14, false)]
    public void MaxInputCharsPerMethod_TruncatesTheMethodBodySentToTheModel(int limit, bool expectedTruncated)
    {
        Settings.Default.Cleaning_AiXmlDocumentationMaxInputCharsPerMethod = limit;

        Apply(Source("Alpha"));

        string prompt = _server.Requests.Single().UserPrompt;
        if (expectedTruncated)
        {
            Assert.Contains("Method body:\n{ return 1; ...\nDetected", prompt);
        }
        else
        {
            Assert.Contains("Method body:\n{ return 1; }\nDetected", prompt);
        }
    }

    [TestMethod]
    [DataRow(2, 2)]
    [DataRow(3, 3)]
    [DataRow(4, 3)]
    public void MaxEstimatedTokensPerCleanup_StopsSendingRequestsOnceTheBudgetIsSpent(int budgetInRequests, int expectedRequests)
    {
        string[] names = ["Alpha", "Bravo", "Delta"];
        int tokensPerRequest = MeasureEstimatedTokensPerRequest(names);

        // One token less than the budget of two requests lets only one through; the exact budget lets both through.
        _server.Clear();
        Settings.Default.Cleaning_AiXmlDocumentationMaxEstimatedTokensPerCleanup = budgetInRequests * tokensPerRequest;
        PipelineRun run = RunPipeline(Source(names));

        Assert.HasCount(expectedRequests, _server.Requests);
        Assert.AreEqual(expectedRequests * tokensPerRequest, run.Stats.EstimatedTokensUsed);
        for (int index = 0; index < names.Length; index++)
        {
            bool expectedDocumented = index >= names.Length - expectedRequests;
            Assert.AreEqual(expectedDocumented, run.Output.Contains("Summary for " + names[index] + "."), names[index]);
        }
    }

    [TestMethod]
    public void MaxEstimatedTokensPerCleanup_OneTokenBelowTheCostOfARequest_SendsNoRequest()
    {
        string[] names = ["Alpha"];
        int tokensPerRequest = MeasureEstimatedTokensPerRequest(names);
        _server.Clear();
        Settings.Default.Cleaning_AiXmlDocumentationMaxEstimatedTokensPerCleanup = tokensPerRequest - 1;

        PipelineRun run = RunPipeline(Source(names));

        Assert.IsEmpty(_server.Requests);
        Assert.DoesNotContain("Summary for Alpha.", run.Output);
        Assert.AreEqual(0, run.Stats.EstimatedTokensUsed);
    }

    [TestMethod]
    [DataRow(255, 1)]
    [DataRow(256, 1)]
    [DataRow(257, 0)]
    public void MaxTokensPerRequest_CountsAgainstTheEstimatedTokenBudget(int maxTokensPerRequest, int expectedRequests)
    {
        string[] names = ["Delta"];
        int tokensPerRequest = MeasureEstimatedTokensPerRequest(names);
        _server.Clear();

        // The budget is exactly the cost of a request that may produce 256 tokens.
        Settings.Default.Cleaning_AiXmlDocumentationMaxEstimatedTokensPerCleanup = tokensPerRequest;
        Settings.Default.Cleaning_AiXmlDocumentationMaxTokensPerRequest = maxTokensPerRequest;
        PipelineRun run = RunPipeline(Source(names));

        Assert.HasCount(expectedRequests, _server.Requests);
        Assert.AreEqual(expectedRequests == 1, run.Output.Contains("Summary for Delta."));
    }

    [TestMethod]
    public void GlobalTimeoutSeconds_ExpiredDuringARequest_SkipsTheRemainingMethods()
    {
        Settings.Default.Cleaning_AiXmlDocumentationGlobalTimeoutSeconds = 1;
        _server.Respond = request => Ok("Summary for " + request.MethodName + ".", delayMs: 1300);

        PipelineRun run = RunPipeline(Source("Alpha", "Bravo", "Charlie"));

        Assert.HasCount(1, _server.Requests);
        Assert.Contains("Summary for Charlie.", run.Output);
        Assert.DoesNotContain("Summary for Bravo.", run.Output);
        Assert.DoesNotContain("Summary for Alpha.", run.Output);
        Assert.AreEqual(2, run.Stats.SkippedByBudget);
    }

    [TestMethod]
    public void GlobalTimeoutSeconds_NotExpired_DocumentsEveryMethod()
    {
        Settings.Default.Cleaning_AiXmlDocumentationGlobalTimeoutSeconds = 1;

        PipelineRun run = RunPipeline(Source("Alpha", "Bravo", "Charlie"));

        Assert.HasCount(3, _server.Requests);
        Assert.AreEqual(0, run.Stats.SkippedByBudget);
    }

    [TestMethod]
    public void TimeoutSeconds_ResponseWithinTheTimeout_IsUsed()
    {
        Settings.Default.Cleaning_AiXmlDocumentationTimeoutSeconds = 1;
        _server.Respond = request => Ok("Summary for " + request.MethodName + ".", delayMs: 300);

        PipelineRun run = RunPipeline(Source("Alpha"));

        Assert.Contains("/// Summary for Alpha.", run.Output);
        Assert.AreEqual(0, run.Stats.AiFailures);
    }

    [TestMethod]
    public void TimeoutSeconds_NoResponseWithinTheTimeout_FailsOnceWithoutRetryAndUsesTheFallback()
    {
        Settings.Default.Cleaning_AiXmlDocumentationTimeoutSeconds = 1;
        _server.Respond = _ => new FakeReply { Hang = true };
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        PipelineRun run = RunPipeline(Source("Alpha"));

        stopwatch.Stop();
        Assert.IsLessThan(TimeSpan.FromSeconds(5), stopwatch.Elapsed, "The request must give up after its own timeout, not wait for the endpoint.");
        Assert.HasCount(1, _server.Requests);
        Assert.Contains("/// Executes alpha.", run.Output);
        Assert.AreEqual(1, run.Stats.AiFailures);
        Assert.AreEqual(1, run.Stats.FallbacksUsed);
    }

    [TestMethod]
    public void TimeoutSeconds_NoResponseWithinTheTimeout_WithoutFallbackLeavesTheMethodUndocumented()
    {
        Settings.Default.Cleaning_AiXmlDocumentationTimeoutSeconds = 1;
        Settings.Default.Cleaning_AiXmlDocumentationAllowDeterministicFallback = false;
        _server.Respond = _ => new FakeReply { Hang = true };
        string source = Source("Alpha");

        PipelineRun run = RunPipeline(source);

        Assert.AreEqual(source, run.Output);
        Assert.AreEqual(1, run.Stats.AiFailures);
        Assert.AreEqual(0, run.Stats.FallbacksUsed);
    }

    [TestMethod]
    [DataRow(401, 1)]
    [DataRow(404, 1)]
    [DataRow(503, 3)]
    [DataRow(429, 3)]
    public void AllowDeterministicFallback_On_FailedRequestGetsTheFallbackDocumentation(int status, int expectedAttempts)
    {
        _server.Respond = _ => new FakeReply { Status = status, Body = "{\"error\":\"nope\"}" };

        PipelineRun run = RunPipeline(Source("Alpha"));

        Assert.HasCount(expectedAttempts, _server.Requests);
        Assert.Contains("/// Executes alpha.", run.Output);
        Assert.AreEqual(1, run.Stats.AiFailures);
        Assert.AreEqual(1, run.Stats.FallbacksUsed);
        Assert.AreEqual(1, run.Stats.DocumentedMethods);
    }

    [TestMethod]
    [DataRow(401)]
    [DataRow(503)]
    public void AllowDeterministicFallback_Off_FailedRequestLeavesTheMethodUndocumentedAndIsReported(int status)
    {
        Settings.Default.Cleaning_AiXmlDocumentationAllowDeterministicFallback = false;
        _server.Respond = _ => new FakeReply { Status = status, Body = "{\"error\":\"nope\"}" };
        string source = Source("Alpha");

        PipelineRun run = RunPipeline(source);

        Assert.AreEqual(source, run.Output);
        Assert.AreEqual(1, run.Stats.AiFailures);
        Assert.AreEqual(0, run.Stats.FallbacksUsed);
        Assert.AreEqual(0, run.Stats.DocumentedMethods);
    }

    [TestMethod]
    public void TransientFailureFollowedBySuccess_IsRetriedAndDocumentedByTheModel()
    {
        int calls = 0;
        _server.Respond = request => Interlocked.Increment(ref calls) == 1
            ? new FakeReply { Status = 503, Body = "busy" }
            : Ok("Summary for " + request.MethodName + ".");

        PipelineRun run = RunPipeline(Source("Alpha"));

        Assert.HasCount(2, _server.Requests);
        Assert.Contains("/// Summary for Alpha.", run.Output);
        Assert.AreEqual(0, run.Stats.AiFailures);
    }

    [TestMethod]
    [DataRow("this is not json at all")]
    [DataRow("{\"choices\":[]}")]
    [DataRow("{\"choices\":[{\"message\":{\"content\":\"   \"}}]}")]
    public void MalformedResponse_WithFallback_IsReportedAsFailureAndUsesTheFallback(string responseBody)
    {
        _server.Respond = _ => new FakeReply { Body = responseBody };

        PipelineRun run = RunPipeline(Source("Alpha"));

        Assert.HasCount(1, _server.Requests);
        Assert.Contains("/// Executes alpha.", run.Output);
        Assert.AreEqual(1, run.Stats.AiFailures);
        Assert.AreEqual(1, run.Stats.FallbacksUsed);
    }

    [TestMethod]
    [DataRow("this is not json at all")]
    [DataRow("{\"choices\":[]}")]
    [DataRow("{\"choices\":[{\"message\":{\"content\":\"   \"}}]}")]
    public void MalformedResponse_WithoutFallback_LeavesTheMethodUndocumented(string responseBody)
    {
        Settings.Default.Cleaning_AiXmlDocumentationAllowDeterministicFallback = false;
        _server.Respond = _ => new FakeReply { Body = responseBody };
        string source = Source("Alpha");

        PipelineRun run = RunPipeline(source);

        Assert.AreEqual(source, run.Output);
        Assert.AreEqual(1, run.Stats.AiFailures);
    }

    [TestMethod]
    public void AllowDeterministicFallback_OnlyTheFailedMethodsGetTheFallback()
    {
        _server.Respond = request => request.MethodName == "Bravo"
            ? new FakeReply { Status = 401, Body = "denied" }
            : Ok("Summary for " + request.MethodName + ".");

        PipelineRun run = RunPipeline(Source("Alpha", "Bravo", "Charlie"));

        Assert.Contains("/// Summary for Alpha.", run.Output);
        Assert.Contains("/// Executes bravo.", run.Output);
        Assert.Contains("/// Summary for Charlie.", run.Output);
        Assert.AreEqual(1, run.Stats.FallbacksUsed);
    }

    [TestMethod]
    public void ExistingDocumentation_IsNeverReplacedAndNeverRequested()
    {
        string source = "namespace Demo;\r\n\r\n/// <summary>Existing.</summary>\r\npublic class Sample\r\n{\r\n    public int Alpha() { return 1; }\r\n\r\n    /// <summary>\r\n    /// Hand written.\r\n    /// </summary>\r\n    public int Bravo() { return 1; }\r\n}\r\n";

        string output = Apply(source);

        Assert.HasCount(1, _server.Requests);
        Assert.AreEqual("Alpha", _server.Requests[0].MethodName);
        Assert.AreEqual(1, CountOccurrences(output, "Hand written."));
        Assert.DoesNotContain("Summary for Bravo.", output);
        Assert.Contains("/// Summary for Alpha.", output);
    }

    [TestMethod]
    public void Apply_RunTwice_SecondRunSendsNoRequestAndChangesNothing()
    {
        string first = Apply(Source("Alpha", "Bravo", "Charlie"));
        int requestsAfterFirstRun = _server.Requests.Count;

        string second = Apply(first);

        Assert.AreEqual(3, requestsAfterFirstRun);
        Assert.AreEqual(first, second);
        Assert.HasCount(requestsAfterFirstRun, _server.Requests);
    }

    [TestMethod]
    public void Apply_PreservesTheLineEndingsOfTheSource()
    {
        string output = Apply(Source("Alpha", "Bravo").Replace("\r\n", "\n"));

        Assert.DoesNotContain("\r", output);
        Assert.Contains("/// Summary for Alpha.", output);
    }

    [TestMethod]
    public void IgnoreObsolete_On_ObsoleteMethodsAreNotSentToTheModel()
    {
        string output = Apply(SourceWith("Sample", string.Empty, "[Obsolete(\"old\")]"));

        Assert.HasCount(1, _server.Requests);
        Assert.DoesNotContain("Summary for Alpha.", output);
        Assert.Contains("Summary for Bravo.", output);
    }

    [TestMethod]
    public void IgnoreObsolete_Off_ObsoleteMethodsAreDocumented()
    {
        Settings.Default.Cleaning_AiXmlDocumentationIgnoreObsolete = false;

        string output = Apply(SourceWith("Sample", string.Empty, "[Obsolete(\"old\")]"));

        Assert.HasCount(2, _server.Requests);
        Assert.Contains("Summary for Alpha.", output);
    }

    [TestMethod]
    [DataRow("", "[GeneratedCode(\"tool\", \"1.0\")]")]
    [DataRow("", "[CompilerGenerated]")]
    [DataRow("[GeneratedCode(\"tool\", \"1.0\")]", "")]
    [DataRow("[System.Runtime.CompilerServices.CompilerGenerated]", "")]
    public void IgnoreGeneratedCode_On_GeneratedMethodsAndMembersOfGeneratedTypesAreNotSentToTheModel(string classAttribute, string alphaAttribute)
    {
        string output = Apply(SourceWith("Sample", classAttribute, alphaAttribute));

        if (classAttribute.Length > 0)
        {
            Assert.IsEmpty(_server.Requests);
            Assert.DoesNotContain("Summary for", output);
        }
        else
        {
            Assert.HasCount(1, _server.Requests);
            Assert.DoesNotContain("Summary for Alpha.", output);
            Assert.Contains("Summary for Bravo.", output);
        }
    }

    [TestMethod]
    public void IgnoreGeneratedCode_Off_GeneratedCodeIsDocumented()
    {
        Settings.Default.Cleaning_AiXmlDocumentationIgnoreGeneratedCode = false;

        string output = Apply(SourceWith("Sample", "[GeneratedCode(\"tool\", \"1.0\")]", "[CompilerGenerated]"));

        Assert.HasCount(2, _server.Requests);
        Assert.Contains("Summary for Alpha.", output);
        Assert.Contains("Summary for Bravo.", output);
    }

    [TestMethod]
    [DataRow("[TestMethod]")]
    [DataRow("[Fact]")]
    [DataRow("[Theory]")]
    [DataRow("[Test]")]
    public void IgnoreTestMethods_On_TestMethodsAreNotSentToTheModel(string alphaAttribute)
    {
        string output = Apply(SourceWith("Sample", string.Empty, alphaAttribute));

        Assert.HasCount(1, _server.Requests);
        Assert.DoesNotContain("Summary for Alpha.", output);
        Assert.Contains("Summary for Bravo.", output);
    }

    [TestMethod]
    [DataRow("SampleTests")]
    [DataRow("SampleTest")]
    public void IgnoreTestMethods_On_MethodsOfTestTypesAreNotSentToTheModel(string className)
    {
        string output = Apply(SourceWith(className, string.Empty, string.Empty));

        Assert.IsEmpty(_server.Requests);
        Assert.DoesNotContain("Summary for", output);
    }

    [TestMethod]
    public void IgnoreTestMethods_Off_TestMethodsAndMethodsOfTestTypesAreDocumented()
    {
        Settings.Default.Cleaning_AiXmlDocumentationIgnoreTestMethods = false;

        string output = Apply(SourceWith("SampleTests", string.Empty, "[TestMethod]"));

        Assert.HasCount(2, _server.Requests);
        Assert.Contains("Summary for Alpha.", output);
        Assert.Contains("Summary for Bravo.", output);
    }

    [TestMethod]
    [DataRow("^Demo\\.Sample\\.Bravo$", "Bravo")]
    [DataRow("sample\\.bravo", "Bravo")]
    [DataRow("\\.Alpha$", "Alpha")]
    public void IgnorePattern_MatchingMethods_AreNotSentToTheModel_IgnoringCase(string pattern, string ignored)
    {
        Settings.Default.Cleaning_AiXmlDocumentationIgnorePattern = pattern;
        string[] names = ["Alpha", "Bravo", "Charlie"];

        string output = Apply(Source(names));

        Assert.HasCount(2, _server.Requests);
        foreach (string name in names)
        {
            Assert.AreEqual(name != ignored, output.Contains("Summary for " + name + "."), name);
        }
    }

    [TestMethod]
    public void IgnorePattern_NotMatchingAnyMethod_DocumentsEverything()
    {
        Settings.Default.Cleaning_AiXmlDocumentationIgnorePattern = "^Other\\.";

        Apply(Source("Alpha", "Bravo"));

        Assert.HasCount(2, _server.Requests);
    }

    [TestMethod]
    [DataRow("([unclosed")]
    [DataRow("*invalid")]
    public void IgnorePattern_InvalidRegex_IsIgnoredAndEveryMethodIsDocumented(string pattern)
    {
        Settings.Default.Cleaning_AiXmlDocumentationIgnorePattern = pattern;

        string output = Apply(Source("Alpha", "Bravo"));

        Assert.HasCount(2, _server.Requests);
        Assert.Contains("Summary for Alpha.", output);
        Assert.Contains("Summary for Bravo.", output);
    }

    [TestMethod]
    public void IgnorePattern_Blank_DocumentsEverything()
    {
        Settings.Default.Cleaning_AiXmlDocumentationIgnorePattern = "   ";

        Apply(Source("Alpha", "Bravo"));

        Assert.HasCount(2, _server.Requests);
    }

    private static string Apply(string source) => AiXmlDocumentationLogic.GetInstance(null).ApplyXmlDocumentationToSource(source);

    private static string Source(params string[] methodNames)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("namespace Demo;\r\n\r\n/// <summary>Existing.</summary>\r\npublic class Sample\r\n{\r\n");
        for (int index = 0; index < methodNames.Length; index++)
        {
            if (index > 0)
            {
                builder.Append("\r\n");
            }

            builder.Append("    public int ").Append(methodNames[index]).Append("() { return 1; }\r\n");
        }

        builder.Append("}\r\n");

        return builder.ToString();
    }

    private static string SourceWith(string className, string classAttribute, string alphaAttribute)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("using System;\r\n\r\nnamespace Demo;\r\n\r\n/// <summary>Existing.</summary>\r\n");
        if (classAttribute.Length > 0)
        {
            builder.Append(classAttribute).Append("\r\n");
        }

        builder.Append("public class ").Append(className).Append("\r\n{\r\n");
        if (alphaAttribute.Length > 0)
        {
            builder.Append("    ").Append(alphaAttribute).Append("\r\n");
        }

        builder.Append("    public int Alpha() { return 1; }\r\n\r\n    public int Bravo() { return 1; }\r\n}\r\n");

        return builder.ToString();
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static void AssertSecretOnlyIn(FakeRequest request, string secret, string allowedHeader)
    {
        Assert.DoesNotContain(secret, request.Body, "The key must not be part of the request body.");
        Assert.DoesNotContain(secret, request.Path, "The key must not be part of the URL.");
        foreach (KeyValuePair<string, string> header in request.Headers)
        {
            if (!string.Equals(header.Key, allowedHeader, StringComparison.OrdinalIgnoreCase))
            {
                Assert.DoesNotContain(secret, header.Value, "The key must not be sent in the header " + header.Key + ".");
            }
        }
    }

    /// <summary>
    /// Sends one request per method with an unlimited budget and returns the estimated cost of a request, which is
    /// the same for all methods because their prompts have the same length.
    /// </summary>
    private int MeasureEstimatedTokensPerRequest(string[] names)
    {
        Settings.Default.Cleaning_AiXmlDocumentationMaxEstimatedTokensPerCleanup = int.MaxValue;
        PipelineRun run = RunPipeline(Source(names));

        Assert.HasCount(names.Length, _server.Requests);
        int[] promptLengths = _server.Requests.Select(request => request.UserPrompt.Length).Distinct().ToArray();
        Assert.HasCount(1, promptLengths, "The measured methods must have equally long prompts.");
        int tokensPerRequest = (promptLengths[0] / 4) + 1 + Settings.Default.Cleaning_AiXmlDocumentationMaxTokensPerRequest;
        Assert.AreEqual(names.Length * tokensPerRequest, run.Stats.EstimatedTokensUsed);

        return tokensPerRequest;
    }

    private static OpenAiCompatibleClient CreateClientFromSettings() =>
        (OpenAiCompatibleClient)LogicMethod("CreateClientFromSettings").Invoke(null, null);

    private static MethodInfo LogicMethod(string name)
    {
        MethodInfo method = LogicType.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method, "Could not locate " + name + " via reflection.");

        return method;
    }

    /// <summary>
    /// Runs the documentation exactly as the cleanup does (client, options and per-member summaries from the
    /// settings) and keeps the run statistics, which the cleanup only writes to the diagnostics output.
    /// </summary>
    private static PipelineRun RunPipeline(string source)
    {
        OpenAiCompatibleClient client = CreateClientFromSettings();
        Assert.IsNotNull(client, "The endpoint must be configured.");
        object options = LogicMethod("LoadRunOptionsFromSettings").Invoke(null, null);
        AiXmlDocumentationLogic.AiXmlDocumentationRunStats stats = new AiXmlDocumentationLogic.AiXmlDocumentationRunStats();
        MethodInfo createSummary = LogicMethod("CreateSummary");
        Func<MemberDeclarationSyntax, string> summaryProvider = member => (string)createSummary.Invoke(null, new[] { client, member, options, stats });

        string output = (string)LogicMethod("GenerateXmlDocumentationForSourceInternal").Invoke(null, new[] { source, summaryProvider, options, stats });

        return new PipelineRun(output, stats);
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        return directory;
    }

    private static EnvDTE.ProjectItem CreateClosedFileItem(string filePath)
    {
#pragma warning disable VSTHRD010 // The substitute stands in for a Visual Studio COM object, which the test calls from its own thread.
        EnvDTE.ProjectItem item = Substitute.For<EnvDTE.ProjectItem>();
        item.Kind.Returns(EnvDTE.Constants.vsProjectItemKindPhysicalFile);
        item.Name.Returns(Path.GetFileName(filePath));
        item.FileNames[1].Returns(filePath);
        item.Document.Returns((EnvDTE.Document)null);
#pragma warning restore VSTHRD010

        return item;
    }

    private static FakeReply Ok(string content, int delayMs = 0) =>
        new FakeReply { Body = "{\"choices\":[{\"message\":{\"content\":\"" + content + "\"}}]}", DelayMs = delayMs };

    /// <summary>
    /// Runs the test on an STA thread that the Visual Studio <see cref="ThreadHelper" /> treats as its UI thread, and
    /// restores the <see cref="ThreadHelper" /> state afterwards.
    /// </summary>
    private static void RunOnVisualStudioUIThread(Action test)
    {
        Exception failure = null;
        FieldInfo uiThreadDispatcherField = typeof(ThreadHelper).GetField("uiThreadDispatcher", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo joinableTaskContextField = typeof(ThreadHelper).GetField("_joinableTaskContextCache", BindingFlags.Static | BindingFlags.NonPublic);
        object previousUIThreadDispatcher = uiThreadDispatcherField.GetValue(null);
        object previousJoinableTaskContext = joinableTaskContextField.GetValue(null);
        Thread uiThread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
#pragma warning disable VSSDK005 // The test stands in for Visual Studio, which owns the ThreadHelper singleton.
            using JoinableTaskContext joinableTaskContext = new JoinableTaskContext();
#pragma warning restore VSSDK005
            try
            {
                typeof(ThreadHelper).GetMethod("SetUIThread", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                joinableTaskContextField.SetValue(null, joinableTaskContext);

                test();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                uiThreadDispatcherField.SetValue(null, previousUIThreadDispatcher);
                joinableTaskContextField.SetValue(null, previousJoinableTaskContext);
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        uiThread.IsBackground = true;
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        if (!uiThread.Join(TimeSpan.FromSeconds(60)))
        {
            Assert.Fail("The test did not complete.");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>
    /// Creates a package without its constructor, which needs a running Visual Studio.
    /// </summary>
    private static CodeJanitorPackage CreatePackage(DTE2 ide)
    {
        CodeJanitorPackage package = (CodeJanitorPackage)FormatterServices.GetUninitializedObject(typeof(CodeJanitorPackage));
        typeof(CodeJanitorPackage).GetField("_ide", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(package, ide);

        return package;
    }

    /// <summary>
    /// Pumps the dispatcher of this thread, where the batch completes, until the dialog result is set.
    /// </summary>
    private static void WaitForBatch(BaseProgressViewModel viewModel)
    {
        DispatcherFrame frame = new DispatcherFrame();
        DispatcherTimer poll = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Background, (_, _) => frame.Continue = viewModel.DialogResult is null, Dispatcher.CurrentDispatcher);
        DispatcherTimer timeout = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Normal, (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        Dispatcher.PushFrame(frame);
        poll.Stop();
        timeout.Stop();
    }

    private sealed class PipelineRun
    {
        public PipelineRun(string output, AiXmlDocumentationLogic.AiXmlDocumentationRunStats stats)
        {
            Output = output;
            Stats = stats;
        }

        public string Output { get; }

        public AiXmlDocumentationLogic.AiXmlDocumentationRunStats Stats { get; }
    }

    private sealed class FakeReply
    {
        public int Status { get; set; } = 200;

        public string Body { get; set; }

        public int DelayMs { get; set; }

        public bool Hang { get; set; }
    }

    private sealed class FakeRequest
    {
        private static readonly Regex UserMessage = new Regex("\"role\":\"user\",\"content\":\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

        private static readonly Regex SignatureName = new Regex(@"Signature: .*?(\w+)\([^)]*\)\s*\nMethod body", RegexOptions.Compiled | RegexOptions.Singleline);

        public FakeRequest(string method, string path, Dictionary<string, string> headers, string body)
        {
            Method = method;
            Path = path;
            Headers = headers;
            Body = body;
            Match message = UserMessage.Match(body);
            UserPrompt = message.Success ? Regex.Unescape(message.Groups[1].Value) : string.Empty;
            Match signature = SignatureName.Match(UserPrompt);
            MethodName = signature.Success ? signature.Groups[1].Value : string.Empty;
        }

        public string Method { get; }

        public string Path { get; }

        public Dictionary<string, string> Headers { get; }

        public string Body { get; }

        public string UserPrompt { get; }

        public string MethodName { get; }
    }

    /// <summary>
    /// A minimal OpenAI-compatible endpoint on the loopback interface that records every request.
    /// </summary>
    private sealed class FakeChatServer : IDisposable
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly object _gate = new object();
        private readonly List<FakeRequest> _requests = new List<FakeRequest>();
        private readonly Task _acceptLoop;
        private int _inFlight;
        private int _maxInFlight;

        public FakeChatServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Respond = request => Ok("Summary for " + request.MethodName + ".");
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public int Port { get; }

        public string EndpointUrl => "http://127.0.0.1:" + Port + "/v1";

        public Func<FakeRequest, FakeReply> Respond { get; set; }

        public IReadOnlyList<FakeRequest> Requests
        {
            get
            {
                lock (_gate)
                {
                    return _requests.ToArray();
                }
            }
        }

        public int MaxInFlight
        {
            get
            {
                lock (_gate)
                {
                    return _maxInFlight;
                }
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _requests.Clear();
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
#pragma warning disable VSTHRD002 // Disposal of a test server has no async context to await in.
            _acceptLoop.Wait(TimeSpan.FromSeconds(2));
#pragma warning restore VSTHRD002
            _stop.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient connection;
                try
                {
                    connection = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(connection));
            }
        }

        private async Task HandleAsync(TcpClient connection)
        {
            using (connection)
            {
                try
                {
                    NetworkStream stream = connection.GetStream();
                    FakeRequest request = await ReadRequestAsync(stream).ConfigureAwait(false);
                    if (request is null)
                    {
                        return;
                    }

                    lock (_gate)
                    {
                        _requests.Add(request);
                        _inFlight++;
                        _maxInFlight = Math.Max(_maxInFlight, _inFlight);
                    }

                    try
                    {
                        FakeReply reply = Respond(request);
                        if (reply.Hang)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(30), _stop.Token).ConfigureAwait(false);
                        }
                        else if (reply.DelayMs > 0)
                        {
                            await Task.Delay(reply.DelayMs, _stop.Token).ConfigureAwait(false);
                        }

                        byte[] body = Encoding.UTF8.GetBytes(reply.Body ?? string.Empty);
                        string head = "HTTP/1.1 " + reply.Status + " " + ReasonPhrase(reply.Status) + "\r\nContent-Type: application/json\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n";
                        byte[] headBytes = Encoding.ASCII.GetBytes(head);
                        await stream.WriteAsync(headBytes, 0, headBytes.Length, _stop.Token).ConfigureAwait(false);
                        await stream.WriteAsync(body, 0, body.Length, _stop.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        lock (_gate)
                        {
                            _inFlight--;
                        }
                    }
                }
                catch (Exception)
                {
                    // The client gave up (timeout) or the server was disposed while the reply was pending.
                }
            }
        }

        private async Task<FakeRequest> ReadRequestAsync(NetworkStream stream)
        {
            MemoryStream received = new MemoryStream();
            byte[] buffer = new byte[8192];
            int headerEnd = -1;
            while (headerEnd < 0)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, _stop.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    return null;
                }

                await received.WriteAsync(buffer, 0, read, _stop.Token).ConfigureAwait(false);
                headerEnd = Latin1.GetString(received.ToArray()).IndexOf("\r\n\r\n", StringComparison.Ordinal);
            }

            string[] headLines = Latin1.GetString(received.ToArray(), 0, headerEnd).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] requestLine = headLines[0].Split(' ');
            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in headLines.Skip(1))
            {
                int separator = line.IndexOf(':');
                if (separator > 0)
                {
                    headers[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
                }
            }

            int contentLength = headers.TryGetValue("Content-Length", out string length) ? int.Parse(length) : 0;
            int bodyStart = headerEnd + 4;
            if (received.Length < bodyStart + contentLength && headers.TryGetValue("Expect", out string expect) && expect.StartsWith("100", StringComparison.Ordinal))
            {
                byte[] continueBytes = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
                await stream.WriteAsync(continueBytes, 0, continueBytes.Length, _stop.Token).ConfigureAwait(false);
            }

            while (received.Length < bodyStart + contentLength)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, _stop.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    return null;
                }

                await received.WriteAsync(buffer, 0, read, _stop.Token).ConfigureAwait(false);
            }

            string bodyText = Encoding.UTF8.GetString(received.ToArray(), bodyStart, contentLength);

            return new FakeRequest(requestLine[0], requestLine[1], headers, bodyText);
        }

        private static string ReasonPhrase(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 401: return "Unauthorized";
                case 404: return "Not Found";
                case 429: return "Too Many Requests";
                case 503: return "Service Unavailable";
                default: return "Error";
            }
        }
    }
}
