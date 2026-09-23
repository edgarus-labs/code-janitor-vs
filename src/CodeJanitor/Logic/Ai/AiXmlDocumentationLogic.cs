using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CodeJanitor.Helpers;
using CodeJanitor.Properties;
using EnvDTE;
using TextDocument = EnvDTE.TextDocument;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.Shell;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Adds XML documentation comments for C# methods that do not already have them,
/// using an OpenAI-compatible endpoint for concise summaries and deterministic
/// tags for parameters, returns and detected exceptions.
/// </summary>

internal sealed class AiXmlDocumentationLogic
{
    private readonly CodeJanitorPackage _package;

    private static AiXmlDocumentationLogic _instance;

    private static CancellationTokenSource _runCancellation = new CancellationTokenSource();

    /// <summary>
    /// Cancels the AI work of the current cleanup batch. Without this a single file can hold the
    /// batch for minutes, because one request may run until its own timeout.
    /// </summary>

    internal static CancellationToken RunToken => _runCancellation.Token;

    /// <summary>
    /// Atomically replaces the shared cancellation token source with a new one and disposes the previous instance.
    /// </summary>
    internal static void BeginRun()
    {
        var previous = Interlocked.Exchange(ref _runCancellation, new CancellationTokenSource());
        previous?.Dispose();
    }

    /// <summary>
    /// Cancels the ongoing run by signaling the internal CancellationTokenSource, which propagates cancellation to any awaiting or executing operations.
    /// </summary>
    internal static void CancelRun()
    {
        _runCancellation?.Cancel();
    }

    /// <summary>
    /// Returns the existing cached `AiXmlDocumentationLogic` singleton or lazily creates and stores a new instance initialized with the supplied `CodeJanitorPackage` on first call.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>A AiXmlDocumentationLogic value produced by this method.</returns>
    internal static AiXmlDocumentationLogic GetInstance(CodeJanitorPackage package)
    {
        return _instance ?? (_instance = new AiXmlDocumentationLogic(package));
    }

    private AiXmlDocumentationLogic(CodeJanitorPackage package)
    {
        _package = package;
    }

    /// <summary>
    /// Configuration options that govern an AI-based XML documentation generation run, controlling limits on methods, requests, tokens, input size, and cleanup behavior along with inclusion and filtering rules.
    /// </summary>
    internal sealed class AiXmlDocumentationRunOptions
    {
        /// <summary>
        /// Gets or sets the max methods per file.
        /// </summary>
        public int MaxMethodsPerFile { get; set; }

        /// <summary>
        /// Gets or sets the max requests per cleanup.
        /// </summary>
        public int MaxRequestsPerCleanup { get; set; }

        /// <summary>
        /// Gets or sets the max input chars per method.
        /// </summary>
        public int MaxInputCharsPerMethod { get; set; }

        /// <summary>
        /// Gets or sets the max tokens per request.
        /// </summary>
        public int MaxTokensPerRequest { get; set; }

        /// <summary>
        /// Gets or sets the context window tokens.
        /// </summary>
        public int ContextWindowTokens { get; set; }

        /// <summary>
        /// Gets or sets the max estimated tokens per cleanup.
        /// </summary>
        public int MaxEstimatedTokensPerCleanup { get; set; }

        /// <summary>
        /// Gets or sets the global timeout seconds.
        /// </summary>
        public int GlobalTimeoutSeconds { get; set; }

        /// <summary>
        /// Gets or sets the allow deterministic fallback.
        /// </summary>
        public bool AllowDeterministicFallback { get; set; }

        /// <summary>
        /// Gets or sets the ignore generated code.
        /// </summary>
        public bool IgnoreGeneratedCode { get; set; }

        /// <summary>
        /// Gets or sets the ignore obsolete.
        /// </summary>
        public bool IgnoreObsolete { get; set; }

        /// <summary>
        /// Gets or sets the ignore test methods.
        /// </summary>
        public bool IgnoreTestMethods { get; set; }

        /// <summary>
        /// Gets or sets the ignore pattern.
        /// </summary>
        public string IgnorePattern { get; set; }

        /// <summary>
        /// Gets or sets the preview changes.
        /// </summary>
        public bool PreviewChanges { get; set; }
    }

    /// <summary>
    /// Represents the aggregated statistics and outcome metrics of a single AI-driven XML documentation generation run, tracking counts of eligible, attempted, documented, and skipped methods, AI failures, fallbacks, and resource usage such as tokens and elapsed time.
    /// </summary>
    internal sealed class AiXmlDocumentationRunStats
    {
        /// <summary>
        /// Gets or sets the eligible methods.
        /// </summary>
        public int EligibleMethods { get; set; }

        /// <summary>
        /// Gets or sets the attempted methods.
        /// </summary>
        public int AttemptedMethods { get; set; }

        /// <summary>
        /// Gets or sets the documented methods.
        /// </summary>
        public int DocumentedMethods { get; set; }

        /// <summary>
        /// Gets or sets the skipped by filters.
        /// </summary>
        public int SkippedByFilters { get; set; }

        /// <summary>
        /// Gets or sets the skipped by budget.
        /// </summary>
        public int SkippedByBudget { get; set; }

        /// <summary>
        /// Gets or sets the ai failures.
        /// </summary>
        public int AiFailures { get; set; }

        /// <summary>
        /// Gets or sets the fallbacks used.
        /// </summary>
        public int FallbacksUsed { get; set; }

        /// <summary>
        /// Gets or sets the estimated tokens used.
        /// </summary>
        public int EstimatedTokensUsed { get; set; }

        /// <summary>
        /// Gets or sets the elapsed milliseconds.
        /// </summary>
        public long ElapsedMilliseconds { get; set; }
    }

    /// <summary>
    /// whether the AI XML documentation cleaning endpoint URL is configured in the application settings.
    /// </summary>
    /// <returns>true if the condition is met; otherwise, false.</returns>
    internal static bool IsConfigurationPresent()
    {
        return OpenAiCompatibleClient.IsEndpointConfigured(
            Settings.Default.Cleaning_AiXmlDocumentationEndpointUrl);
    }

    /// <summary>
    /// Asynchronously tests reachability and authentication of the specified OpenAI-compatible AI
    /// endpoint, independent of whether the configured model name is valid.
    /// </summary>
    /// <param name="endpointUrl">The endpoint url.</param>
    /// <param name="apiKey">The api key.</param>
    /// <param name="apiKeyHeader">The api key header.</param>
    /// <param name="model">The model.</param>
    /// <param name="timeoutSeconds">The timeout seconds.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the open ai compatible client.connection test result.</returns>
    internal static async Task<OpenAiCompatibleClient.ConnectionTestResult> ValidateApiConnectionAsync(
        string endpointUrl,
        string apiKey,
        string apiKeyHeader,
        string model = null,
        int timeoutSeconds = 30)
    {
        var client = CreateClient(endpointUrl, apiKey, apiKeyHeader, model, timeoutSeconds);
        if (client is null)
        {
            return new OpenAiCompatibleClient.ConnectionTestResult
            {
                ErrorMessage = "AI XML documentation endpoint URL is missing or invalid."
            };
        }

        return await client.TestApiConnectionAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously fetches available models from the specified AI endpoint.
    /// </summary>
    /// <param name="endpointUrl">The endpoint url.</param>
    /// <param name="apiKey">The api key.</param>
    /// <param name="apiKeyHeader">The api key header.</param>
    /// <param name="timeoutSeconds">The timeout seconds.</param>
    /// <returns>A list of available model names/IDs.</returns>
    internal static async Task<List<string>> FetchAvailableModelsAsync(
        string endpointUrl,
        string apiKey,
        string apiKeyHeader,
        int timeoutSeconds = 30)
    {
        var client = CreateClient(endpointUrl, apiKey, apiKeyHeader, null, timeoutSeconds);
        if (client is null)
        {
            return new List<string>();
        }

        return await client.FetchAvailableModelsAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously verifies that the configured model produces a usable response from the
    /// specified OpenAI-compatible AI endpoint.
    /// </summary>
    /// <param name="endpointUrl">The endpoint url.</param>
    /// <param name="apiKey">The api key.</param>
    /// <param name="apiKeyHeader">The api key header.</param>
    /// <param name="model">The model.</param>
    /// <param name="timeoutSeconds">The timeout seconds.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the open ai compatible client.connection test result.</returns>
    internal static async Task<OpenAiCompatibleClient.ConnectionTestResult> ValidateModelAsync(
        string endpointUrl,
        string apiKey,
        string apiKeyHeader,
        string model,
        int timeoutSeconds)
    {
        var client = CreateClient(endpointUrl, apiKey, apiKeyHeader, model, timeoutSeconds);
        if (client is null)
        {
            return new OpenAiCompatibleClient.ConnectionTestResult
            {
                ErrorMessage = "AI XML documentation endpoint URL is missing or invalid."
            };
        }

        return await client.TestModelAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Determines whether the specified project item is an eligible C# file for AI XML documentation.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>True if eligible, otherwise false.</returns>
    internal bool CanDocumentProjectItem(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (projectItem is null || !projectItem.IsPhysicalFile())
        {
            return false;
        }

        if (!string.Equals(Path.GetExtension(projectItem.Name), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var filePath = projectItem.GetFileName();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        if (NamespacePathHelper.IsInExcludedDirectory(filePath))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Applies AI XML documentation to a project item asynchronously, offloading file I/O, Roslyn transforms, and AI network calls to background threads.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>True if the file was modified, otherwise false.</returns>
    internal async Task<bool> ApplyXmlDocumentationAsync(ProjectItem projectItem)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (RunToken.IsCancellationRequested || !CanDocumentProjectItem(projectItem))
        {
            return false;
        }

        var client = CreateClientFromSettings();
        if (client is null)
        {
            return false;
        }

        var document = projectItem.Document;
        if (document is not null)
        {
            var textDocument = document.GetTextDocument();
            if (textDocument is not null)
            {
                if (Settings.Default.Cleaning_AiXmlDocumentationPreviewChanges)
                {
                    var startPt = textDocument.StartPoint.CreateEditPoint();
                    var before = startPt.GetText(textDocument.EndPoint);
                    ApplyXmlDocumentationWithPreview(textDocument, client);
                    var after = textDocument.StartPoint.CreateEditPoint().GetText(textDocument.EndPoint);

                    return before != after;
                }

                var startPoint = textDocument.StartPoint.CreateEditPoint();
                var originalText = startPoint.GetText(textDocument.EndPoint);

                var updatedText = await Task.Run(() => ApplyXmlDocumentationToSourceInternal(originalText, client));

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (RunToken.IsCancellationRequested || updatedText == originalText)
                {
                    return false;
                }

                var endPoint = textDocument.EndPoint.CreateEditPoint();
                startPoint.ReplaceText(endPoint, updatedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);

                return true;
            }
        }

        var filePath = projectItem.GetFileName();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        return await Task.Run(() =>
        {
            if (RunToken.IsCancellationRequested)
            {
                return false;
            }

            string originalFileText;
            Encoding encoding;

            using (var reader = new StreamReader(filePath, true))
            {
                originalFileText = reader.ReadToEnd();
                encoding = reader.CurrentEncoding;
            }

            var updatedFileText = ApplyXmlDocumentationToSourceInternal(originalFileText, client);
            if (RunToken.IsCancellationRequested || updatedFileText == originalFileText)
            {
                return false;
            }

            File.WriteAllText(filePath, updatedFileText, encoding);

            return true;
        });
    }

    /// <summary>
    /// Applies AI XML documentation to a project item (either an open document in editor or a file on disk).
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>True if the file was modified, otherwise false.</returns>
    internal bool ApplyXmlDocumentation(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!CanDocumentProjectItem(projectItem))
        {
            return false;
        }

        var client = CreateClientFromSettings();
        if (client is null)
        {
            return false;
        }

        var document = projectItem.Document;
        if (document is not null)
        {
            var textDocument = document.GetTextDocument();
            if (textDocument is not null)
            {
                if (Settings.Default.Cleaning_AiXmlDocumentationPreviewChanges)
                {
                    var startPt = textDocument.StartPoint.CreateEditPoint();
                    var before = startPt.GetText(textDocument.EndPoint);
                    ApplyXmlDocumentationWithPreview(textDocument, client);
                    var after = textDocument.StartPoint.CreateEditPoint().GetText(textDocument.EndPoint);

                    return before != after;
                }

                var startPoint = textDocument.StartPoint.CreateEditPoint();
                var originalText = startPoint.GetText(textDocument.EndPoint);
                var updatedText = ApplyXmlDocumentationToSourceInternal(originalText, client);

                if (updatedText == originalText)
                {
                    return false;
                }

                var endPoint = textDocument.EndPoint.CreateEditPoint();
                startPoint.ReplaceText(endPoint, updatedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);

                return true;
            }
        }

        var filePath = projectItem.GetFileName();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        string originalFileText;
        Encoding encoding;

        using (var reader = new StreamReader(filePath, true))
        {
            originalFileText = reader.ReadToEnd();
            encoding = reader.CurrentEncoding;
        }

        var updatedFileText = ApplyXmlDocumentationToSourceInternal(originalFileText, client);
        if (updatedFileText == originalFileText)
        {
            return false;
        }

        File.WriteAllText(filePath, updatedFileText, encoding);

        return true;
    }

    /// <summary>
    /// Applies AI-generated XML documentation comments to the specified text document by invoking the configured AI client, optionally previewing changes before replacing the document content.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    internal void ApplyXmlDocumentation(TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!Settings.Default.Cleaning_AiXmlDocumentationEnabled)
        {
            return;
        }

        var client = CreateClientFromSettings();
        if (client is null)
        {
            return;
        }

        if (Settings.Default.Cleaning_AiXmlDocumentationPreviewChanges)
        {
            ApplyXmlDocumentationWithPreview(textDocument, client);

            return;
        }

        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var originalText = startPoint.GetText(textDocument.EndPoint);
        var updatedText = ApplyXmlDocumentationToSourceInternal(originalText, client);

        if (updatedText == originalText)
        {
            return;
        }

        var endPoint = textDocument.EndPoint.CreateEditPoint();
        startPoint.ReplaceText(endPoint, updatedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
    }

    /// <summary>
    /// Applies AI-generated XML documentation comments to the provided source string when AI cleaning is enabled and preview mode is disabled, delegating to an internal processor via the configured client, or returns the source unchanged otherwise.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>The string result.</returns>
    internal string ApplyXmlDocumentationToSource(string source)
    {
        if (!Settings.Default.Cleaning_AiXmlDocumentationEnabled ||
            Settings.Default.Cleaning_AiXmlDocumentationPreviewChanges)
        {
            return source;
        }

        var client = CreateClientFromSettings();

        return client is null ? source : ApplyXmlDocumentationToSourceInternal(source, client);
    }

    /// <summary>
    /// Applies documentation regardless of the preview setting, for sources that never reach the
    /// editor path where the preview prompt lives.
    /// </summary>

    internal string ApplyXmlDocumentationToSourceIgnoringPreview(string source)
    {
        if (!Settings.Default.Cleaning_AiXmlDocumentationEnabled)
        {
            return source;
        }

        var client = CreateClientFromSettings();

        return client is null ? source : ApplyXmlDocumentationToSourceInternal(source, client);
    }

    /// <summary>
    /// Generates AI-produced XML documentation comments for the specified text document and applies the changes to the document after displaying a preview and obtaining user confirmation.
    /// </summary>
    /// <param name="textDocument">The text document.</param>
    /// <param name="client">The client.</param>
    private void ApplyXmlDocumentationWithPreview(TextDocument textDocument, OpenAiCompatibleClient client)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var options = LoadRunOptionsFromSettings();
        var stats = new AiXmlDocumentationRunStats();
        var stopwatch = Stopwatch.StartNew();

        var startPoint = textDocument.StartPoint.CreateEditPoint();
        var originalText = startPoint.GetText(textDocument.EndPoint);
        var updatedText = GenerateXmlDocumentationForSourceInternal(
            originalText,
            method => CreateSummary(client, method, options, stats),
            options,
            stats);

        stopwatch.Stop();
        stats.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        OutputWindowHelper.DiagnosticWriteLine(
            "AI XMLDoc stats: " +
            $"eligible={stats.EligibleMethods}, attempted={stats.AttemptedMethods}, documented={stats.DocumentedMethods}, " +
            $"filtered={stats.SkippedByFilters}, budgetSkipped={stats.SkippedByBudget}, aiFailures={stats.AiFailures}, " +
            $"fallbacks={stats.FallbacksUsed}, estTokens={stats.EstimatedTokensUsed}, elapsedMs={stats.ElapsedMilliseconds}");

        if (updatedText == originalText)
        {
            return;
        }

        var preview = BuildChangesPreview(originalText, updatedText, 90);
        var previewText = preview.Length > 3800 ? preview.Substring(0, 3800) + "\n..." : preview;
        var shouldApply = MessageBox.Show(
            "Apply AI XMLDoc changes for this file?\n\n" + previewText,
            "Code Janitor - AI XMLDoc Preview",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

        if (!shouldApply)
        {
            return;
        }

        var endPoint = textDocument.EndPoint.CreateEditPoint();
        startPoint.ReplaceText(endPoint, updatedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
    }

    /// <summary>
    /// Applies AI-generated XML documentation to the provided source text using the supplied OpenAI-compatible client, records run statistics, writes a diagnostic summary to the output window, and returns the updated source.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="client">The client.</param>
    /// <returns>The string result.</returns>
    private static string ApplyXmlDocumentationToSourceInternal(string source, OpenAiCompatibleClient client)
    {
        var options = LoadRunOptionsFromSettings();
        var stats = new AiXmlDocumentationRunStats();
        var stopwatch = Stopwatch.StartNew();

        var updatedText = GenerateXmlDocumentationForSourceInternal(
            source,
            method => CreateSummary(client, method, options, stats),
            options,
            stats);

        stopwatch.Stop();
        stats.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        OutputWindowHelper.DiagnosticWriteLine(
            "AI XMLDoc stats: " +
            $"eligible={stats.EligibleMethods}, attempted={stats.AttemptedMethods}, documented={stats.DocumentedMethods}, " +
            $"filtered={stats.SkippedByFilters}, budgetSkipped={stats.SkippedByBudget}, aiFailures={stats.AiFailures}, " +
            $"fallbacks={stats.FallbacksUsed}, estTokens={stats.EstimatedTokensUsed}, elapsedMs={stats.ElapsedMilliseconds}");

        return updatedText;
    }

    /// <summary>
    /// Generates AI-based XML documentation for the members in the specified source file using the provided summary provider, applying default run options and returning the resulting documentation output.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="summaryProvider">The summary provider.</param>
    /// <param name="maxMethodsPerFile">The max methods per file.</param>
    /// <returns>The string result.</returns>
    internal static string GenerateXmlDocumentationForSource(string source, Func<MemberDeclarationSyntax, string> summaryProvider, int maxMethodsPerFile)
    {
        var options = new AiXmlDocumentationRunOptions
        {
            MaxMethodsPerFile = PositiveOrDefault(maxMethodsPerFile, 25),
            MaxRequestsPerCleanup = 25,
            MaxInputCharsPerMethod = 2500,
            MaxTokensPerRequest = 256,
            MaxEstimatedTokensPerCleanup = int.MaxValue,
            GlobalTimeoutSeconds = 60,
            AllowDeterministicFallback = true,
            IgnoreGeneratedCode = false,
            IgnoreObsolete = false,
            IgnoreTestMethods = false,
            IgnorePattern = string.Empty,
            PreviewChanges = false
        };

        return GenerateXmlDocumentationForSourceInternal(source, summaryProvider, options, new AiXmlDocumentationRunStats());
    }

    /// <summary>
    /// Parses the provided C# source, identifies documentable members according to the supplied options, and inserts their generated XML documentation summaries in reverse token order while enforcing method-count, timeout, and cancellation constraints.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="summaryProvider">The summary provider.</param>
    /// <param name="options">The options.</param>
    /// <param name="stats">The stats.</param>
    /// <returns>The string result.</returns>
    private static string GenerateXmlDocumentationForSourceInternal(string source, Func<MemberDeclarationSyntax, string> summaryProvider, AiXmlDocumentationRunOptions options, AiXmlDocumentationRunStats stats)
    {
        if (string.IsNullOrWhiteSpace(source) || summaryProvider is null)
        {
            return source;
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var skippedByFilter = 0;
        var eligibleMethods = root.DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(x => CanDocumentMember(x, options, ref skippedByFilter))
            .OrderBy(x => x.GetFirstToken().SpanStart)
            .ToList();

        stats.SkippedByFilters += skippedByFilter;

        stats.EligibleMethods = eligibleMethods.Count;

        if (!eligibleMethods.Any())
        {
            return source;
        }

        var methodLimit = PositiveOrDefault(options.MaxMethodsPerFile, 25);

        // Methods, constructors, and types that require AI requests are subject to MaxMethodsPerFile.
        // Deterministic members (fields, properties, indexers, events) don't consume AI requests and are fully documented in one pass.
        var aiCandidates = eligibleMethods.Where(x => x is MethodDeclarationSyntax || x is ConstructorDeclarationSyntax || x is BaseTypeDeclarationSyntax).ToList();
        var deterministicMembers = eligibleMethods.Where(x => !(x is MethodDeclarationSyntax || x is ConstructorDeclarationSyntax || x is BaseTypeDeclarationSyntax)).ToList();

        if (aiCandidates.Count > methodLimit)
        {
            var allowedAi = aiCandidates.Take(methodLimit).ToList();
            stats.SkippedByBudget += (aiCandidates.Count - allowedAi.Count);
            aiCandidates = allowedAi;
        }

        var eligibleToProcess = aiCandidates.Concat(deterministicMembers)
            .OrderBy(x => x.GetFirstToken().SpanStart)
            .ToList();

        var builder = new StringBuilder(source);
        var deadlineUtc = DateTime.UtcNow.AddSeconds(options.GlobalTimeoutSeconds > 0 ? options.GlobalTimeoutSeconds : 60);
        foreach (var method in eligibleToProcess.OrderByDescending(x => x.GetFirstToken().SpanStart))
        {
            if (RunToken.IsCancellationRequested)
            {
                break;
            }

            if (DateTime.UtcNow > deadlineUtc)
            {
                stats.SkippedByBudget++;
                continue;
            }

            var insertPosition = GetLineStart(builder.ToString(), method.GetFirstToken().SpanStart);
            var indent = GetLineIndent(builder.ToString(), insertPosition);

            var rawSummary = summaryProvider(method);
            if (RunToken.IsCancellationRequested)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(rawSummary))
            {
                stats.SkippedByBudget++;
                continue;
            }

            var summary = NormalizeSentence(rawSummary);

            var exceptions = method is BaseMethodDeclarationSyntax methodForExceptions
                ? DetectThrownExceptions(methodForExceptions).ToList()
                : new List<string>();
            var xmlBlock = BuildXmlCommentBlock(indent, method, summary, exceptions);

            builder.Insert(insertPosition, xmlBlock);
            stats.DocumentedMethods++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Loads and returns an AiXmlDocumentationRunOptions instance populated from the application settings, applying positive-value fallbacks to numeric limits and the configured toggles for deterministic fallback, filtering rules, and change previewing.
    /// </summary>
    /// <returns>The ai xml documentation run options result.</returns>
    private static AiXmlDocumentationRunOptions LoadRunOptionsFromSettings()
    {
        return new AiXmlDocumentationRunOptions
        {
            MaxMethodsPerFile = PositiveOrDefault(Settings.Default.Cleaning_AiXmlDocumentationMaxMethodsPerFile, 25),
            MaxRequestsPerCleanup = PositiveOrDefault(Settings.Default.Cleaning_AiXmlDocumentationMaxRequestsPerCleanup, 25),
            MaxInputCharsPerMethod = PositiveOrDefault(Settings.Default.Cleaning_AiXmlDocumentationMaxInputCharsPerMethod, 2500),
            MaxTokensPerRequest = PositiveOrDefault(Settings.Default.Cleaning_AiXmlDocumentationMaxTokensPerRequest, 256),
            ContextWindowTokens = PositiveOrDefault(Settings.Default.Cleaning_AiXmlDocumentationContextWindowTokens, 131072),
            MaxEstimatedTokensPerCleanup = PositiveOrDefault(Settings.Default.Cleaning_AiXmlDocumentationMaxEstimatedTokensPerCleanup, 8000),
            GlobalTimeoutSeconds = PositiveOrDefault(Settings.Default.Cleaning_AiXmlDocumentationGlobalTimeoutSeconds, 60),
            AllowDeterministicFallback = Settings.Default.Cleaning_AiXmlDocumentationAllowDeterministicFallback,
            IgnoreGeneratedCode = Settings.Default.Cleaning_AiXmlDocumentationIgnoreGeneratedCode,
            IgnoreObsolete = Settings.Default.Cleaning_AiXmlDocumentationIgnoreObsolete,
            IgnoreTestMethods = Settings.Default.Cleaning_AiXmlDocumentationIgnoreTestMethods,
            IgnorePattern = Settings.Default.Cleaning_AiXmlDocumentationIgnorePattern,
            PreviewChanges = Settings.Default.Cleaning_AiXmlDocumentationPreviewChanges
        };
    }

    /// <summary>
    /// Returns the specified value when it is positive, or the provided fallback when the value is zero or negative.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="fallback">The fallback.</param>
    /// <returns>The int result.</returns>
    private static int PositiveOrDefault(int value, int fallback)
    {
        return value > 0 ? value : fallback;
    }

    /// <summary>
    /// Creates and configures an OpenAI-compatible client using the AI XML documentation cleaning settings, including endpoint URL, API key, model, timeout, and context window size.
    /// </summary>
    /// <returns>The open ai compatible client result.</returns>
    private static OpenAiCompatibleClient CreateClientFromSettings()
    {
        return CreateClient(
            Settings.Default.Cleaning_AiXmlDocumentationEndpointUrl,
            GetConfiguredApiKey(),
            Settings.Default.Cleaning_AiXmlDocumentationApiKeyHeader,
            Settings.Default.Cleaning_AiXmlDocumentationModel,
                Settings.Default.Cleaning_AiXmlDocumentationTimeoutSeconds,
                PositiveOrDefault(Settings.Default.Cleaning_AiXmlDocumentationContextWindowTokens, 131072));
    }

    /// <summary>
    /// Creates and returns a new OpenAI-compatible client instance using the specified endpoint, credentials, model, and timeout settings, or returns null when the endpoint is not configured.
    /// </summary>
    /// <param name="endpointUrl">The endpoint url.</param>
    /// <param name="apiKey">The api key.</param>
    /// <param name="apiKeyHeader">The api key header.</param>
    /// <param name="model">The model.</param>
    /// <param name="timeoutSeconds">The timeout seconds.</param>
    /// <param name="contextWindowTokens">The context window tokens.</param>
    /// <returns>The open ai compatible client result.</returns>
    private static OpenAiCompatibleClient CreateClient(string endpointUrl, string apiKey, string apiKeyHeader, string model, int timeoutSeconds, int contextWindowTokens = 0)
    {
        if (!OpenAiCompatibleClient.IsEndpointConfigured(endpointUrl))
        {
            return null;
        }

        return new OpenAiCompatibleClient(
            endpointUrl,
            apiKey,
            apiKeyHeader,
            model,
                timeoutSeconds,
                contextWindowTokens);
    }

    /// <summary>
    /// es the configured API key for AI XML documentation cleaning by first attempting to unprotect the encrypted setting for the current user and falling back to the legacy plain-text setting when unavailable.
    /// </summary>
    /// <returns>The string result.</returns>
    private static string GetConfiguredApiKey()
    {
        var encrypted = Settings.Default.Cleaning_AiXmlDocumentationApiKeyEncrypted;
        var plain = SecretProtectionHelper.UnprotectForCurrentUser(encrypted);
        if (!string.IsNullOrWhiteSpace(plain))
        {
            return plain;
        }

        // Backward compatibility with the initial plain-text settings approach.

        return Settings.Default.Cleaning_AiXmlDocumentationApiKey;
    }

    /// <summary>
    /// Types, properties, fields, indexers, and events carry no parameters or exceptions, so they only need the shared
    /// attribute and existing-documentation filters.
    /// </summary>
    private static bool CanDocumentMember(MemberDeclarationSyntax member, AiXmlDocumentationRunOptions options, ref int filteredCounter)
    {
        if (member is null)
        {
            return false;
        }

        if (member is MethodDeclarationSyntax method)
        {
            return CanDocumentMethod(method, options, ref filteredCounter);
        }

        if (member is ConstructorDeclarationSyntax constructor)
        {
            return CanDocumentConstructor(constructor, options, ref filteredCounter);
        }

        if (!(member is BaseTypeDeclarationSyntax) &&
            !(member is PropertyDeclarationSyntax) &&
            !(member is FieldDeclarationSyntax) &&
            !(member is IndexerDeclarationSyntax) &&
            !(member is EventDeclarationSyntax) &&
            !(member is EventFieldDeclarationSyntax))
        {
            return false;
        }

        if ((member is PropertyDeclarationSyntax || member is FieldDeclarationSyntax || member is EventDeclarationSyntax || member is EventFieldDeclarationSyntax || member is IndexerDeclarationSyntax) &&
            member.Parent is InterfaceDeclarationSyntax)
        {
            return false;
        }

        if (member is FieldDeclarationSyntax field)
        {
            if (field.Declaration.Variables.Count == 0)
            {
                return false;
            }

            var isAccessibleOrConst = field.Modifiers.Any(m =>
                m.IsKind(SyntaxKind.PublicKeyword) ||
                m.IsKind(SyntaxKind.InternalKeyword) ||
                m.IsKind(SyntaxKind.ProtectedKeyword) ||
                m.IsKind(SyntaxKind.ConstKeyword));

            if (!isAccessibleOrConst)
            {
                return false;
            }
        }

        if (options.IgnoreTestMethods && member is BaseTypeDeclarationSyntax testType && IsLikelyTestType(testType))
        {
            filteredCounter++;

            return false;
        }

        if (HasDocumentationComment(member))
        {
            filteredCounter++;

            return false;
        }

        if (options.IgnoreObsolete && HasAnyAttribute(member, "Obsolete"))
        {
            filteredCounter++;

            return false;
        }

        if (options.IgnoreGeneratedCode && (HasAnyAttribute(member, "GeneratedCode", "CompilerGenerated") ||
                                            HasAnyAttribute(member.Parent as MemberDeclarationSyntax, "GeneratedCode", "CompilerGenerated")))
        {
            filteredCounter++;

            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns true only for non-interface, non-abstract, non-extern methods with a body that lack an existing documentation comment and do not match the configured ignore options, incrementing the `filteredCounter` for each option-based exclusion.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <param name="options">The options.</param>
    /// <param name="filteredCounter">The filtered counter.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool CanDocumentMethod(MethodDeclarationSyntax method, AiXmlDocumentationRunOptions options, ref int filteredCounter)
    {
        if (method is null)
        {
            return false;
        }

        if (method.Parent is InterfaceDeclarationSyntax)
        {
            return false;
        }

        if (method.Modifiers.Any(x => x.IsKind(SyntaxKind.AbstractKeyword) || x.IsKind(SyntaxKind.ExternKeyword)))
        {
            return false;
        }

        if (method.Body is null && method.ExpressionBody is null)
        {
            return false;
        }

        if (HasDocumentationComment(method))
        {
            filteredCounter++;

            return false;
        }

        if (options.IgnoreObsolete && HasAnyAttribute(method, "Obsolete"))
        {
            filteredCounter++;

            return false;
        }

        if (options.IgnoreGeneratedCode && (HasAnyAttribute(method, "GeneratedCode", "CompilerGenerated") ||
                                            HasAnyAttribute(method.Parent as MemberDeclarationSyntax, "GeneratedCode", "CompilerGenerated")))
        {
            filteredCounter++;

            return false;
        }

        if (options.IgnoreTestMethods && IsLikelyTestMethod(method))
        {
            filteredCounter++;

            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.IgnorePattern) && MatchesIgnorePattern(method, options.IgnorePattern))
        {
            filteredCounter++;

            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns true only for non-static, non-extern constructors with a body that lack an existing documentation comment and do not match the configured ignore options, incrementing the `filteredCounter` for each option-based exclusion.
    /// </summary>
    /// <param name="constructor">The constructor.</param>
    /// <param name="options">The options.</param>
    /// <param name="filteredCounter">The filtered counter.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool CanDocumentConstructor(ConstructorDeclarationSyntax constructor, AiXmlDocumentationRunOptions options, ref int filteredCounter)
    {
        if (constructor is null)
        {
            return false;
        }

        if (constructor.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword) || x.IsKind(SyntaxKind.ExternKeyword)))
        {
            return false;
        }

        if (constructor.Body is null && constructor.ExpressionBody is null)
        {
            return false;
        }

        if (HasDocumentationComment(constructor))
        {
            filteredCounter++;

            return false;
        }

        if (options.IgnoreObsolete && HasAnyAttribute(constructor, "Obsolete"))
        {
            filteredCounter++;

            return false;
        }

        if (options.IgnoreGeneratedCode && (HasAnyAttribute(constructor, "GeneratedCode", "CompilerGenerated") ||
                                            HasAnyAttribute(constructor.Parent as MemberDeclarationSyntax, "GeneratedCode", "CompilerGenerated")))
        {
            filteredCounter++;

            return false;
        }

        if (options.IgnoreTestMethods && IsLikelyTestType(constructor.Parent as TypeDeclarationSyntax))
        {
            filteredCounter++;

            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.IgnorePattern) && MatchesIgnorePattern(constructor, options.IgnorePattern))
        {
            filteredCounter++;

            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves the containing type's name and its kind ("record", "struct", or "class") for the specified constructor, defaulting the name to &quot;instance&quot; when the constructor has no containing type declaration.
    /// </summary>
    /// <param name="constructor">The constructor.</param>
    /// <returns>A tuple of the containing type name and its kind.</returns>
    private static (string ContainingTypeName, string TypeKind) GetContainingTypeInfo(ConstructorDeclarationSyntax constructor)
    {
        var containingTypeName = (constructor.Parent as TypeDeclarationSyntax)?.Identifier.ValueText ?? "instance";
        var typeKind = constructor.Parent is RecordDeclarationSyntax ? "record"
            : constructor.Parent is StructDeclarationSyntax ? "struct"
            : "class";

        return (containingTypeName, typeKind);
    }

    /// <summary>
    /// Determines whether a type's name ends with &quot;Tests&quot; or &quot;Test&quot; (case-insensitive), the naming convention used to identify test classes.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool IsLikelyTestType(BaseTypeDeclarationSyntax type)
    {
        var name = type?.Identifier.ValueText ?? string.Empty;

        return name.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Test", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if the member declaration contains any attribute whose name (ignoring namespace, case, and the &quot;Attribute&quot; suffix) matches one of the supplied target names; returns false when the declaration is null or no match is found.
    /// </summary>
    /// <param name="declaration">The declaration.</param>
    /// <param name="names">The names.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool HasAnyAttribute(MemberDeclarationSyntax declaration, params string[] names)
    {
        if (declaration is null)
        {
            return false;
        }

        var targetNames = new HashSet<string>(names ?? new string[0], StringComparer.OrdinalIgnoreCase);
        foreach (var list in declaration.AttributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                var raw = attribute.Name.ToString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var normalized = raw.Split('.').Last().Replace("Attribute", string.Empty);
                if (targetNames.Contains(normalized))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a method is likely a test method by checking for common test attributes (TestMethod, Fact, Theory, Test, TestCase, DataTestMethod) or by verifying that its containing type name ends with &quot;Tests&quot; or &quot;Test&quot; (case-insensitive).
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool IsLikelyTestMethod(MethodDeclarationSyntax method)
    {
        if (HasAnyAttribute(method, "TestMethod", "Fact", "Theory", "Test", "TestCase", "DataTestMethod"))
        {
            return true;
        }

        return IsLikelyTestType(method.Parent as TypeDeclarationSyntax);
    }

    /// <summary>
    /// Determines whether a member&apos;s fully qualified name (namespace.type.member) matches the given regex pattern case-insensitively, returning false for null/whitespace patterns or any exception.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <param name="ignorePattern">The ignore pattern.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool MatchesIgnorePattern(MemberDeclarationSyntax member, string ignorePattern)
    {
        if (string.IsNullOrWhiteSpace(ignorePattern))
        {
            return false;
        }

        try
        {
            var containingType = (member.Parent as TypeDeclarationSyntax)?.Identifier.ValueText ?? string.Empty;
            var containingNamespace = member.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? string.Empty;
            var memberName = member switch
            {
                MethodDeclarationSyntax m => m.Identifier.ValueText,
                ConstructorDeclarationSyntax c => c.Identifier.ValueText,
                _ => throw new NotSupportedException("Unsupported member kind for ignore-pattern matching: " + member.GetType().Name)
            };
            var fullName = string.IsNullOrWhiteSpace(containingNamespace)
                ? containingType + "." + memberName
                : containingNamespace + "." + containingType + "." + memberName;

            return Regex.IsMatch(fullName, ignorePattern, RegexOptions.IgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is RegexMatchTimeoutException)
        {
            OutputWindowHelper.DiagnosticWriteLine("AI XMLDoc ignore pattern is invalid: '" + ignorePattern + "'", ex);

            return false;
        }
    }

    /// <summary>
    /// Determines whether the provided SyntaxNode includes documentation comments within its leading trivia.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool HasDocumentationComment(SyntaxNode member)
    {
        if (member is null)
        {
            return false;
        }

        return member.GetLeadingTrivia().Any(trivia =>
        {
            if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
                return true;
            }

            var structure = trivia.GetStructure();

            return structure is not null &&
                   (structure.Kind() == SyntaxKind.SingleLineDocumentationCommentTrivia ||
                    structure.Kind() == SyntaxKind.MultiLineDocumentationCommentTrivia);
        });
    }

    /// <summary>
    /// Generates an AI-generated or fallback summary for a member, routing properties, fields, indexers, and events to deterministic builders, enforcing per-cleanup token/request limits and cancellation via RunToken, updating stats, and falling back to a deterministic summary on failure.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="member">The member.</param>
    /// <param name="options">The options.</param>
    /// <param name="stats">The stats.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string CreateSummary(OpenAiCompatibleClient client, MemberDeclarationSyntax member, AiXmlDocumentationRunOptions options, AiXmlDocumentationRunStats stats)
    {
        // Property, field, indexer, and event wording is formulaic, so spending an AI request on it buys nothing.
        if (member is PropertyDeclarationSyntax property)
        {
            return BuildPropertySummary(property);
        }

        if (member is FieldDeclarationSyntax field)
        {
            return BuildFieldSummary(field);
        }

        if (member is IndexerDeclarationSyntax indexer)
        {
            return BuildIndexerSummary(indexer);
        }

        if (member is EventDeclarationSyntax || member is EventFieldDeclarationSyntax)
        {
            return BuildEventSummary(member);
        }

        if (stats.AttemptedMethods >= options.MaxRequestsPerCleanup || RunToken.IsCancellationRequested)
        {
            return null;
        }

        string prompt;
        if (member is MethodDeclarationSyntax method)
        {
            prompt = BuildMethodPrompt(method, options);
        }
        else if (member is ConstructorDeclarationSyntax constructor)
        {
            prompt = BuildConstructorPrompt(constructor, options);
        }
        else
        {
            prompt = BuildTypePrompt((BaseTypeDeclarationSyntax)member, options);
        }

        var estimatedTokens = EstimateRequestTokens(prompt, options.MaxTokensPerRequest);
        if (stats.EstimatedTokensUsed + estimatedTokens > options.MaxEstimatedTokensPerCleanup)
        {
            return null;
        }

        stats.EstimatedTokensUsed += estimatedTokens;
        stats.AttemptedMethods++;

        const string systemPrompt =
            "You are a principal .NET software architect and technical writer authoring official Microsoft-standard XML documentation comments (<summary>).\n" +
            "Your task is to write a single-sentence, professional, production-grade summary for the given C# code element.\n" +
            "Rules:\n" +
            "1. Output EXACTLY ONE concise, high-quality summary sentence in plain text. No XML tags, no markdown quotes, no preambles, no reasoning, no thinking steps.\n" +
            "2. Never output generic or vague filler like 'Performs the operation.', 'Represents the class.', 'Executes the method.', or 'Contains data.'.\n" +
            "3. Use standard .NET conventions:\n" +
            "   - Commands (e.g., CreateRecipeCommand): 'Represents a command to create a new recipe with the specified details.'\n" +
            "   - Queries (e.g., GetRecipeByIdQuery): 'Represents a query to retrieve recipe details by identifier.'\n" +
            "   - DTOs / Records / Models: 'Represents the data structure containing {domain} details.'\n" +
            "   - Interfaces: 'Defines a contract for {domain} operations.'\n" +
            "   - Methods: start with third-person singular present tense active verbs ('Creates...', 'Calculates...', 'Asynchronously processes...', 'Handles...').\n" +
            "   - Enums: 'Specifies the available {category} options.'\n" +
            "4. Pay close attention to the element name, base types, interfaces, parameters, and properties to deduce the exact business domain semantics.";

        if (!client.TryGenerateDocumentation(prompt, out var completion, out var error, options.MaxTokensPerRequest, RunToken, systemPrompt) || string.IsNullOrWhiteSpace(completion))
        {
            if (RunToken.IsCancellationRequested)
            {
                return null;
            }

            stats.AiFailures++;
            if (!options.AllowDeterministicFallback)
            {
                return null;
            }

            stats.FallbacksUsed++;

            return BuildFallbackSummary(member);
        }

        return NormalizeSentence(completion);
    }

    /// <summary>
    /// BuildMethodPrompt is a private static helper that constructs an AI analysis prompt string from a MethodDeclarationSyntax by composing its stripped signature, optionally truncated body text, and a comma-separated list of detected thrown exceptions (defaulting to &quot;none detected&quot;).
    /// </summary>
    /// <param name="method">The method.</param>
    /// <param name="options">The options.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string BuildMethodPrompt(MethodDeclarationSyntax method, AiXmlDocumentationRunOptions options)
    {
        var signature = method.WithBody(null)
            .WithExpressionBody(null)
            .WithSemicolonToken(default(SyntaxToken))
            .NormalizeWhitespace()
            .ToFullString();

        var bodyText = method.Body is not null
            ? method.Body.ToFullString().Trim()
            : method.ExpressionBody?.ToFullString().Trim() ?? string.Empty;

        var exceptionList = string.Join(", ", DetectThrownExceptions(method));
        if (string.IsNullOrWhiteSpace(exceptionList))
        {
            exceptionList = "none detected";
        }

        return
            "Generate a professional C# XML documentation <summary> sentence for the following C# method:\n" +
            "Signature: " + signature + "\n" +
            "Method body:\n" + Truncate(bodyText, options.MaxInputCharsPerMethod) + "\n" +
            "Detected thrown exceptions: " + exceptionList + "\n\n" +
            "Guidelines:\n" +
            "- Start with a third-person singular active verb (e.g., 'Asynchronously retrieves...', 'Executes the...', 'Validates and parses...').\n" +
            "- Describe what the method does and any important outcome or side effect.\n" +
            "- Do NOT output vague generic filler like 'Performs the operation.' or 'Executes the action.'\n" +
            "- Output ONLY the single summary sentence (plain text, no XML, no quotes, no reasoning).";
    }

    /// <summary>
    /// Constructs an AI analysis prompt string for a ConstructorDeclarationSyntax by composing its stripped signature,
    /// optionally truncated body text, and a comma-separated list of detected thrown exceptions.
    /// </summary>
    /// <param name="constructor">The constructor.</param>
    /// <param name="options">The options.</param>
    /// <returns>A string prompt for the AI model.</returns>
    private static string BuildConstructorPrompt(ConstructorDeclarationSyntax constructor, AiXmlDocumentationRunOptions options)
    {
        var signature = constructor.WithBody(null)
            .WithExpressionBody(null)
            .WithSemicolonToken(default(SyntaxToken))
            .NormalizeWhitespace()
            .ToFullString();

        var bodyText = constructor.Body is not null
            ? constructor.Body.ToFullString().Trim()
            : constructor.ExpressionBody?.ToFullString().Trim() ?? string.Empty;

        var exceptionList = string.Join(", ", DetectThrownExceptions(constructor));
        if (string.IsNullOrWhiteSpace(exceptionList))
        {
            exceptionList = "none detected";
        }

        var (containingTypeName, typeKind) = GetContainingTypeInfo(constructor);

        return
            "Generate a professional C# XML documentation <summary> sentence for the following C# constructor of " + typeKind + " '" + containingTypeName + "':\n" +
            "Signature: " + signature + "\n" +
            "Constructor body:\n" + Truncate(bodyText, options.MaxInputCharsPerMethod) + "\n" +
            "Detected thrown exceptions: " + exceptionList + "\n\n" +
            "Guidelines:\n" +
            "- Standard Microsoft style for constructors: 'Initializes a new instance of the " + containingTypeName + " " + typeKind + ".' or 'Initializes a new instance of the " + containingTypeName + " " + typeKind + " with the specified parameters.'\n" +
            "- Describe any validation or initialization done by the constructor.\n" +
            "- Do NOT output vague generic filler.\n" +
            "- Output ONLY the single summary sentence (plain text, no XML, no quotes, no reasoning).";
    }

    /// <summary>
    /// Describes a type by its declaration header, base types/interfaces, constructor parameters (for records),
    /// and member signatures.
    /// </summary>
    private static string BuildTypePrompt(BaseTypeDeclarationSyntax type, AiXmlDocumentationRunOptions options)
    {
        var header = type.Identifier.ValueText;
        var kind = type is InterfaceDeclarationSyntax ? "interface"
            : type is EnumDeclarationSyntax ? "enum"
            : type is StructDeclarationSyntax ? "struct"
            : type is RecordDeclarationSyntax ? "record"
            : "class";

        var memberNames = new List<string>();

        // For positional records (e.g. record Foo(int A, string B)), extract parameters
        if (type is RecordDeclarationSyntax recordDeclaration && recordDeclaration.ParameterList is not null)
        {
            foreach (var parameter in recordDeclaration.ParameterList.Parameters)
            {
                var paramType = parameter.Type?.ToString() ?? "object";
                memberNames.Add(parameter.Identifier.ValueText + " (" + paramType + ")");
            }
        }

        if (type is TypeDeclarationSyntax typeDeclaration)
        {
            foreach (var member in typeDeclaration.Members)
            {
                if (member is PropertyDeclarationSyntax p) memberNames.Add(p.Identifier.ValueText + " (" + (p.Type?.ToString() ?? "property") + ")");
                else if (member is MethodDeclarationSyntax m) memberNames.Add(m.Identifier.ValueText + "()");
                else if (member is ConstructorDeclarationSyntax c) memberNames.Add(c.Identifier.ValueText + "()");
                else if (member is FieldDeclarationSyntax f) memberNames.AddRange(f.Declaration.Variables.Select(v => v.Identifier.ValueText));
            }
        }
        else if (type is EnumDeclarationSyntax enumDeclaration)
        {
            memberNames.AddRange(enumDeclaration.Members.Select(x => x.Identifier.ValueText));
        }

        var baseTypes = type.BaseList?.Types.Select(t => t.Type.ToString()).ToList();
        var baseListText = baseTypes is not null && baseTypes.Count > 0
            ? string.Join(", ", baseTypes)
            : "none";

        var members = memberNames.Count == 0 ? "none" : string.Join(", ", memberNames);

        var declarationSignature = type.Modifiers.ToString() + " " + kind + " " + header;
        if (type is RecordDeclarationSyntax rec && rec.ParameterList is not null)
        {
            declarationSignature += rec.ParameterList.ToString();
        }
        if (type.BaseList is not null)
        {
            declarationSignature += " " + type.BaseList.ToString();
        }

        return
            "Generate a professional C# XML documentation <summary> sentence for the following C# " + kind + ":\n" +
            "Declaration: " + declarationSignature + "\n" +
            "Kind: " + kind + "\n" +
            "Name: " + header + "\n" +
            "Implemented interfaces / base types: " + baseListText + "\n" +
            "Parameters / Properties / Members: " + Truncate(members, options.MaxInputCharsPerMethod) + "\n\n" +
            "Guidelines:\n" +
            "- If this is a Command (e.g. implements ICommand, IRequest, or ends with 'Command'): start with 'Represents a command to {action}...' or 'Defines the command for {action}...' describing what action will be initiated and what data it carries.\n" +
            "- If this is a Query (e.g. implements IQuery, IRequest, or ends with 'Query'): start with 'Represents a query to retrieve {noun}...'.\n" +
            "- If this is a DTO, response, or event (e.g. ends with 'Dto', 'Response', 'Event'): start with 'Represents {noun}...' describing the data it encapsulates.\n" +
            "- If this is an Interface: start with 'Defines a contract for...' or 'Provides an abstraction for...'.\n" +
            "- If this is a marker interface with no members: state that it serves as a marker/indicator contract for type checking or pipeline dispatch.\n" +
            "- If this is a Class/Struct/Record: start with 'Represents...' or 'Provides...' describing its primary responsibility.\n" +
            "- If this is an Enum: start with 'Specifies...' or 'Defines constants for...'.\n" +
            "- Do NOT output vague generic filler like 'Performs the operation.' or 'Represents the object.' Be specific to the domain name and properties.\n" +
            "- Output ONLY the single summary sentence (plain text, no XML, no quotes, no reasoning).";
    }

    /// <summary>
    /// Performs build field summary.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string BuildFieldSummary(FieldDeclarationSyntax field)
    {
        var firstVar = field.Declaration.Variables.FirstOrDefault();
        var name = firstVar is not null ? firstVar.Identifier.ValueText : "value";

        return "The " + SplitIdentifier(name).ToLowerInvariant() + ".";
    }

    /// <summary>
    /// the fixed documentation text &quot;Gets or sets element at specified index.&quot; for any given IndexerDeclarationSyntax without performing analysis or producing side effects.
    /// </summary>
    /// <param name="indexer">The indexer.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string BuildIndexerSummary(IndexerDeclarationSyntax indexer)
    {
        return "Gets or sets the element at the specified index.";
    }

    /// <summary>
    /// Builds a standardized &quot;Occurs when {name}.&quot; summary string for an event member, extracting the event name from either an EventDeclarationSyntax or EventFieldDeclarationSyntax and lowercasing the split identifier, with no side effects or thrown exceptions.
    /// </summary>
    /// <param name="eventMember">The event member.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string BuildEventSummary(MemberDeclarationSyntax eventMember)
    {
        var name = "event";
        if (eventMember is EventDeclarationSyntax ed)
        {
            name = ed.Identifier.ValueText;
        }
        else if (eventMember is EventFieldDeclarationSyntax efd)
        {
            var v = efd.Declaration.Variables.FirstOrDefault();
            if (v is not null)
            {
                name = v.Identifier.ValueText;
            }
        }

        return "Occurs when " + SplitIdentifier(name).ToLowerInvariant() + ".";
    }

    /// <summary>
    /// Estimates the total token count for a request by approximating prompt tokens as `(prompt.Length / 4) + 1` (or 0 if the prompt is null or whitespace) and adding `maxOutputTokens` (defaulted to 256 via `PositiveOrDefault` when non-positive).
    /// </summary>
    /// <param name="prompt">The prompt.</param>
    /// <param name="maxOutputTokens">The max output tokens.</param>
    /// <returns>A int value produced by this method.</returns>
    private static int EstimateRequestTokens(string prompt, int maxOutputTokens)
    {
        var estimatedPromptTokens = string.IsNullOrWhiteSpace(prompt) ? 0 : (prompt.Length / 4) + 1;

        return estimatedPromptTokens + PositiveOrDefault(maxOutputTokens, 256);
    }

    /// <summary>
    /// Detects exception types thrown by a method by collecting names from `throw` statements and expressions and inferring `ArgumentNullException`, `ArgumentException`, or `ArgumentOutOfRangeException` from `ThrowIf*` guard helper invocations, returning the unique results.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>A IEnumerable&lt;string&gt; value produced by this method.</returns>
    private static IEnumerable<string> DetectThrownExceptions(BaseMethodDeclarationSyntax method)
    {
        var exceptions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var throwStatement in method.DescendantNodes().OfType<ThrowStatementSyntax>())
        {
            var createdType = TryGetThrownTypeName(throwStatement.Expression);
            if (!string.IsNullOrWhiteSpace(createdType))
            {
                exceptions.Add(createdType);
            }
        }

        foreach (var throwExpression in method.DescendantNodes().OfType<ThrowExpressionSyntax>())
        {
            var createdType = TryGetThrownTypeName(throwExpression.Expression);
            if (!string.IsNullOrWhiteSpace(createdType))
            {
                exceptions.Add(createdType);
            }
        }

        // Guard helper patterns often used instead of explicit throw new statements.
        foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = invocation.Expression.ToString();
            if (name.IndexOf("ThrowIfNull", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                exceptions.Add("ArgumentNullException");
            }
            else if (name.IndexOf("ThrowIfNullOrEmpty", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf("ThrowIfNullOrWhiteSpace", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                exceptions.Add("ArgumentException");
            }
            else if (name.IndexOf("ThrowIfNegative", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                exceptions.Add("ArgumentOutOfRangeException");
            }
        }

        return exceptions;
    }

    /// <summary>
    /// Tries to return the type name string from an object creation expression, returning null if the expression is not an object creation or its type is null.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string TryGetThrownTypeName(ExpressionSyntax expression)
    {
        if (expression is ObjectCreationExpressionSyntax creation && creation.Type is not null)
        {
            return creation.Type.ToString();
        }

        return null;
    }

    /// <summary>
    /// The member's own line already carries its indentation, so the block is inserted at the
    /// start of that line rather than at the declaration token.
    /// </summary>

    /// <summary>
    /// Finds the start index of the line containing the character at the specified index.
    /// </summary>
    private static int GetLineStart(string text, int index)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, Math.Min(index, text.Length) - 1));

        return lineStart < 0 ? 0 : lineStart + 1;
    }

    /// <summary>
    /// Builds an XML documentation comment block string for a member by appending an indented &lt;summary&gt; element,
    /// and for methods, constructors, and positional records additionally appending &lt;param&gt; elements, a &lt;returns&gt; element,
    /// and ordered &lt;exception&gt; elements.
    /// </summary>
    private static string BuildXmlCommentBlock(string indent, MemberDeclarationSyntax member, string summary, IEnumerable<string> exceptionTypes)
    {
        var sb = new StringBuilder();

        sb.Append(indent).AppendLine("/// <summary>");
        sb.Append(indent).Append("/// ").AppendLine(XmlEscape(summary));
        sb.Append(indent).AppendLine("/// </summary>");

        // 1. Positional records or primary constructors parameters
        IEnumerable<ParameterSyntax> parameters = null;
        if (member is MethodDeclarationSyntax method)
        {
            parameters = method.ParameterList?.Parameters;
        }
        else if (member is ConstructorDeclarationSyntax constructor)
        {
            parameters = constructor.ParameterList?.Parameters;
        }
        else if (member is RecordDeclarationSyntax record)
        {
            parameters = record.ParameterList?.Parameters;
        }

        if (parameters is not null)
        {
            foreach (var parameter in parameters)
            {
                var parameterName = parameter.Identifier.ValueText;
                if (!string.IsNullOrWhiteSpace(parameterName))
                {
                    var paramType = parameter.Type?.ToString();
                    sb.Append(indent)
                        .Append("/// <param name=\"")
                        .Append(parameterName)
                        .Append("\">")
                        .Append(XmlEscape(BuildParameterDescription(parameterName, paramType)))
                        .AppendLine("</param>");
                }
            }
        }

        // 2. Return description for methods
        if (member is MethodDeclarationSyntax m)
        {
            var returnTypeStr = m.ReturnType?.ToString();
            var returnsVoid = string.Equals(returnTypeStr, "void", StringComparison.OrdinalIgnoreCase);

            if (!returnsVoid && !string.IsNullOrWhiteSpace(returnTypeStr))
            {
                sb.Append(indent)
                    .Append("/// <returns>")
                    .Append(XmlEscape(BuildReturnDescription(returnTypeStr, m.Identifier.ValueText)))
                    .AppendLine("</returns>");
            }
        }

        // 3. Exceptions
        if (exceptionTypes is not null)
        {
            foreach (var exceptionType in exceptionTypes.OrderBy(x => x, StringComparer.Ordinal))
            {
                sb.Append(indent)
                    .Append("/// <exception cref=\"")
                    .Append(XmlEscape(exceptionType))
                    .Append("\">")
                    .Append(XmlEscape("Thrown when an error occurs during execution."))
                    .AppendLine("</exception>");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds a professional, context-aware description for a parameter based on its name and type.
    /// </summary>
    private static string BuildParameterDescription(string parameterName, string parameterType = null)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return "The parameter value.";
        }

        if (string.Equals(parameterName, "cancellationToken", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parameterType, "CancellationToken", StringComparison.OrdinalIgnoreCase))
        {
            return "The cancellation token to monitor for cancellation requests.";
        }

        if (string.Equals(parameterName, "id", StringComparison.OrdinalIgnoreCase))
        {
            return "The unique identifier.";
        }

        if (parameterName.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && parameterName.Length > 2)
        {
            var entity = parameterName.Substring(0, parameterName.Length - 2);

            return "The unique identifier of the " + SplitIdentifier(entity).ToLowerInvariant() + ".";
        }

        if (parameterName.EndsWith("Dto", StringComparison.OrdinalIgnoreCase) ||
            parameterName.EndsWith("Request", StringComparison.OrdinalIgnoreCase) ||
            parameterName.EndsWith("Command", StringComparison.OrdinalIgnoreCase))
        {
            return "The " + SplitIdentifier(parameterName).ToLowerInvariant() + " containing the operation data.";
        }

        var split = SplitIdentifier(parameterName).ToLowerInvariant();

        if (parameterType is not null && (parameterType.StartsWith("List<") || parameterType.StartsWith("IList<") || parameterType.StartsWith("IEnumerable<") || parameterType.StartsWith("IReadOnlyList<") || parameterType.EndsWith("[]")))
        {
            return "The collection of " + split + ".";
        }

        return "The " + split + ".";
    }

    /// <summary>
    /// Builds a professional, context-aware description for a method return type.
    /// </summary>
    private static string BuildReturnDescription(string returnType, string methodName = null)
    {
        if (string.IsNullOrWhiteSpace(returnType))
        {
            return "The result of the operation.";
        }

        if (string.Equals(returnType, "bool", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(returnType, "Boolean", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(methodName) &&
                (methodName.StartsWith("Is", StringComparison.OrdinalIgnoreCase) ||
                 methodName.StartsWith("Has", StringComparison.OrdinalIgnoreCase) ||
                 methodName.StartsWith("Can", StringComparison.OrdinalIgnoreCase) ||
                 methodName.StartsWith("Try", StringComparison.OrdinalIgnoreCase)))
            {
                return "true if the condition is met; otherwise, false.";
            }

            return "true if the operation succeeded; otherwise, false.";
        }

        if (string.Equals(returnType, "Task", StringComparison.OrdinalIgnoreCase))
        {
            return "A task representing the asynchronous operation.";
        }

        if (string.Equals(returnType, "ValueTask", StringComparison.OrdinalIgnoreCase))
        {
            return "A value task representing the asynchronous operation.";
        }

        if (returnType.StartsWith("Task<", StringComparison.OrdinalIgnoreCase) && returnType.EndsWith(">"))
        {
            var inner = returnType.Substring(5, returnType.Length - 6).Trim();
            if (string.Equals(inner, "bool", StringComparison.OrdinalIgnoreCase))
            {
                return "A task representing the asynchronous operation. The task result is true if successful; otherwise, false.";
            }

            return "A task representing the asynchronous operation. The task result contains the " + CleanGenericTypeName(inner) + ".";
        }

        if (returnType.StartsWith("ValueTask<", StringComparison.OrdinalIgnoreCase) && returnType.EndsWith(">"))
        {
            var inner = returnType.Substring(10, returnType.Length - 11).Trim();

            return "A value task representing the asynchronous operation. The task result contains the " + CleanGenericTypeName(inner) + ".";
        }

        if (returnType.StartsWith("IEnumerable<") || returnType.StartsWith("IReadOnlyList<") || returnType.StartsWith("List<") || returnType.EndsWith("[]"))
        {
            return "A collection of " + CleanGenericTypeName(returnType) + " items.";
        }

        return "The " + CleanGenericTypeName(returnType) + " result.";
    }

    /// <summary>
    /// Converts a generic type name into a simplified, lowercase identifier by stripping generic arguments and array brackets, returning &quot;result&quot; when the input is null, empty, or whitespace.
    /// </summary>
    /// <param name="typeName">The type name.</param>
    /// <returns>The string result.</returns>
    private static string CleanGenericTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return "result";
        }

        var idx = typeName.IndexOf('<');
        var baseName = idx > 0 ? typeName.Substring(0, idx) : typeName;
        baseName = baseName.Replace("[]", string.Empty);

        return SplitIdentifier(baseName).ToLowerInvariant();
    }

    /// <summary>
    /// Builds a documentation summary string for a property following official .NET documentation conventions.
    /// </summary>
    private static string BuildPropertySummary(PropertyDeclarationSyntax property)
    {
        var accessors = property.AccessorList?.Accessors;
        var hasGet = accessors?.Any(x => x.IsKind(SyntaxKind.GetAccessorDeclaration)) ?? property.ExpressionBody is not null;
        var hasSet = accessors?.Any(x => x.IsKind(SyntaxKind.SetAccessorDeclaration) || x.IsKind(SyntaxKind.InitAccessorDeclaration)) ?? false;

        if (!hasGet && !hasSet && property.Initializer is not null)
        {
            hasGet = true;
        }

        var propType = property.Type?.ToString() ?? string.Empty;
        var isBool = string.Equals(propType, "bool", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(propType, "Boolean", StringComparison.OrdinalIgnoreCase);

        var propName = property.Identifier.ValueText;
        var split = SplitIdentifier(propName).ToLowerInvariant();

        if (isBool)
        {
            return hasGet && hasSet
                ? "Gets or sets a value indicating whether " + split + "."
                : hasSet
                    ? "Sets a value indicating whether " + split + "."
                    : "Gets a value indicating whether " + split + ".";
        }

        var verb = hasGet && hasSet ? "Gets or sets" : hasSet ? "Sets" : "Gets";

        if (propType.StartsWith("List<") || propType.StartsWith("IList<") || propType.StartsWith("IReadOnlyList<") ||
            propType.StartsWith("IEnumerable<") || propType.StartsWith("ICollection<") || propType.EndsWith("[]"))
        {
            return verb + " the collection of " + split + ".";
        }

        return verb + " the " + split + ".";
    }

    /// <summary>
    /// Builds a fallback XML documentation summary string for a given member declaration by dispatching to type-specific summary builders for properties, fields, indexers, and events, while producing generic &quot;Represents X.&quot; and &quot;Performs X.&quot; text for type and method declarations respectively.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string BuildFallbackSummary(MemberDeclarationSyntax member)
    {
        if (member is BaseTypeDeclarationSyntax type)
        {
            var typeName = type.Identifier.ValueText;
            if (type is InterfaceDeclarationSyntax)
            {
                return "Defines a contract for " + SplitIdentifier(typeName).ToLowerInvariant() + ".";
            }
            if (typeName.EndsWith("Command", StringComparison.OrdinalIgnoreCase))
            {
                var action = typeName.Substring(0, typeName.Length - "Command".Length);
                var split = SplitIdentifier(action).ToLowerInvariant();

                return "Represents a command to " + (string.IsNullOrWhiteSpace(split) ? "execute the operation" : split) + ".";
            }
            if (typeName.EndsWith("Query", StringComparison.OrdinalIgnoreCase))
            {
                var noun = typeName.Substring(0, typeName.Length - "Query".Length);
                var split = SplitIdentifier(noun).ToLowerInvariant();

                return "Represents a query to retrieve " + (string.IsNullOrWhiteSpace(split) ? "the requested data" : split) + ".";
            }
            if (typeName.EndsWith("Dto", StringComparison.OrdinalIgnoreCase) ||
                typeName.EndsWith("Response", StringComparison.OrdinalIgnoreCase) ||
                typeName.EndsWith("Request", StringComparison.OrdinalIgnoreCase))
            {
                return "Represents the data structure for " + SplitIdentifier(typeName).ToLowerInvariant() + ".";
            }

            return "Represents " + SplitIdentifier(typeName).ToLowerInvariant() + ".";
        }

        if (member is PropertyDeclarationSyntax property)
        {
            return BuildPropertySummary(property);
        }

        if (member is FieldDeclarationSyntax field)
        {
            return BuildFieldSummary(field);
        }

        if (member is IndexerDeclarationSyntax indexer)
        {
            return BuildIndexerSummary(indexer);
        }

        if (member is EventDeclarationSyntax || member is EventFieldDeclarationSyntax)
        {
            return BuildEventSummary(member);
        }

        if (member is ConstructorDeclarationSyntax constructor)
        {
            var (containingType, typeKind) = GetContainingTypeInfo(constructor);
            var hasParams = constructor.ParameterList?.Parameters.Count > 0;

            return hasParams
                ? "Initializes a new instance of the " + containingType + " " + typeKind + " with the specified parameters."
                : "Initializes a new instance of the " + containingType + " " + typeKind + ".";
        }

        var method = (MethodDeclarationSyntax)member;
        var methodName = method.Identifier.ValueText;
        var splitMethod = SplitIdentifier(methodName).ToLowerInvariant();

        return "Executes " + (string.IsNullOrWhiteSpace(splitMethod) ? "the method" : splitMethod) + ".";
    }

    /// <summary>
    /// Splits a camelCase or snake_case identifier into separate words by inserting spaces between lowercase/digit-to-uppercase transitions and replacing underscores with spaces, returning &quot;operation&quot; when the input is null or whitespace.
    /// </summary>
    /// <param name="identifier">The identifier.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string SplitIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return "the operation";
        }

        var text = Regex.Replace(identifier, "([a-z0-9])([A-Z])", "$1 $2");

        return text.Replace('_', ' ').Trim();
    }

    /// <summary>
    /// the leading whitespace (spaces and tabs) substring from the start of the line containing the specified position in the source string.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="position">The position.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string GetLineIndent(string source, int position)
    {
        var lineStart = source.LastIndexOf('\n', Math.Max(0, position - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;

        var i = lineStart;
        while (i < source.Length)
        {
            var c = source[i];
            if (c != ' ' && c != '\t')
            {
                break;
            }

            i++;
        }

        return source.Substring(lineStart, i - lineStart);
    }

    /// <summary>
    /// Sanitizes an AI completion by stripping thinking process, chain-of-thought blocks (<think>...</think>, "Thinking Process:", etc.), markdown code fences, and preambles.
    /// </summary>
    internal static string SanitizeAiCompletion(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = text.Trim();

        // 1. Remove XML/HTML thinking tags: <think>...</think>, <thought>...</thought>, etc.
        cleaned = Regex.Replace(cleaned, @"<think>[\s\S]*?</think>", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<thought>[\s\S]*?</thought>", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<reasoning>[\s\S]*?</reasoning>", string.Empty, RegexOptions.IgnoreCase);
        // If an unclosed <think> tag was cut off by max_tokens, strip everything after <think>
        cleaned = Regex.Replace(cleaned, @"<think>[\s\S]*$", string.Empty, RegexOptions.IgnoreCase);

        // 2. Strip explicit "Thinking Process:" or "Thought:" blocks if present
        if (Regex.IsMatch(cleaned, @"^(?:\*{0,2})Thinking Process(?:\*{0,2})\s*:", RegexOptions.IgnoreCase))
        {
            // If the model produced drafts like "Draft 1:", "Draft 2:", or "Summary:", try to pick the last non-empty draft
            var draftMatches = Regex.Matches(cleaned, @"(?:Draft\s*\d+|Final Draft|Summary)\s*(?:\([^)]*\))?\s*:\s*\*?\*?([^\n\r*]+)", RegexOptions.IgnoreCase);
            if (draftMatches.Count > 0)
            {
                var candidate = draftMatches[draftMatches.Count - 1].Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(candidate) && candidate.Length > 5)
                {
                    cleaned = candidate;
                }
                else
                {
                    // Strip the entire thinking process prefix
                    cleaned = string.Empty;
                }
            }
            else
            {
                cleaned = string.Empty;
            }
        }

        // 3. Remove markdown backticks and code fences if any
        cleaned = Regex.Replace(cleaned, @"^```[a-zA-Z]*\s*", string.Empty);
        cleaned = Regex.Replace(cleaned, @"\s*```$", string.Empty);

        // 4. Remove common introductory preambles (e.g., "Here is the summary:", "Summary:", "Description:")
        cleaned = Regex.Replace(cleaned, @"^(?:Here is (?:the|a) (?:concise )?summary(?:\s+sentence)?:\s*|Summary:\s*|Description:\s*)", string.Empty, RegexOptions.IgnoreCase);

        // 5. Remove any leading XML comment markers if the model hallucinated them
        cleaned = Regex.Replace(cleaned, @"^(?:\s*///\s*(?:<summary>)?\s*)+", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"(?:\s*///\s*</summary>\s*)+$", string.Empty, RegexOptions.IgnoreCase);

        return cleaned.Trim();
    }

    /// <summary>
    /// Normalizes a sentence by stripping reasoning/thinking artifacts, collapsing whitespace, trimming surrounding quotes, appending a period if lacking ending punctuation, and returning the fallback "Performs the operation." when empty.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A string value produced by this method.</returns>
    internal static string NormalizeSentence(string text)
    {
        var sanitized = SanitizeAiCompletion(text);
        var compact = Regex.Replace(sanitized ?? string.Empty, "\\s+", " ").Trim();
        if (string.IsNullOrEmpty(compact))
        {
            return "Performs the operation.";
        }

        compact = compact.Trim('"', '\'', '`', '*');
        if (!compact.EndsWith(".", StringComparison.Ordinal) &&
            !compact.EndsWith("!", StringComparison.Ordinal) &&
            !compact.EndsWith("?", StringComparison.Ordinal))
        {
            compact += ".";
        }

        return compact;
    }

    /// <summary>
    /// Escapes XML special characters (`&amp;`, `&lt;`, `&gt;`, `&quot;`, `&apos;`) in the input string to their corresponding entity references, returning `string.Empty` if the input is null.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string XmlEscape(string text)
    {
        return (text ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    /// <summary>
    /// Returns the specified text unchanged if it is null, empty, or within the maximum length; otherwise, truncates the text to the specified maximum length and appends a period.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="maxLength">The max length.</param>
    /// <returns>The string result.</returns>
    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text.Substring(0, maxLength) + "...";
    }

    /// <summary>
    /// Constructs a formatted line-by-line diff preview comparing the original and updated text, limited to the specified maximum number of changed lines and marked as truncated when that threshold is reached.
    /// </summary>
    /// <param name="originalText">The original text.</param>
    /// <param name="updatedText">The updated text.</param>
    /// <param name="maxChangedLines">The max changed lines.</param>
    /// <returns>The string result.</returns>
    private static string BuildChangesPreview(string originalText, string updatedText, int maxChangedLines)
    {
        var oldLines = (originalText ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        var newLines = (updatedText ?? string.Empty).Replace("\r\n", "\n").Split('\n');

        var lineCount = Math.Max(oldLines.Length, newLines.Length);
        var builder = new StringBuilder();
        var changed = 0;

        for (var i = 0; i < lineCount; i++)
        {
            var oldLine = i < oldLines.Length ? oldLines[i] : string.Empty;
            var newLine = i < newLines.Length ? newLines[i] : string.Empty;
            if (string.Equals(oldLine, newLine, StringComparison.Ordinal))
            {
                continue;
            }

            changed++;
            builder.AppendLine("Line " + (i + 1) + ":");
            builder.AppendLine("- " + oldLine);
            builder.AppendLine("+ " + newLine);

            if (changed >= maxChangedLines)
            {
                builder.AppendLine("... diff truncated ...");
                break;
            }
        }

        return builder.Length == 0 ? "No textual changes." : builder.ToString();
    }
}
