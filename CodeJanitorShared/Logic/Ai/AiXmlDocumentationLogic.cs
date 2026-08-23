using EnvDTE;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Properties;
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

        public int EstimatedTokensUsed { get; set; }

        public long ElapsedMilliseconds { get; set; }
    }

    internal static bool IsConfigurationPresent()
    {
        return OpenAiCompatibleClient.IsEndpointConfigured(
            Settings.Default.Cleaning_AiXmlDocumentationEndpointUrl);
    }

    internal static async Task<OpenAiCompatibleClient.ConnectionTestResult> ValidateConnectionAsync(
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

        return await client.TestConnectionAsync().ConfigureAwait(false);
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

        // Methods and types that require AI requests are subject to MaxMethodsPerFile.
        // Deterministic members (fields, properties, indexers, events) don't consume AI requests and are fully documented in one pass.
        var aiCandidates = eligibleMethods.Where(x => x is MethodDeclarationSyntax || x is BaseTypeDeclarationSyntax).ToList();
        var deterministicMembers = eligibleMethods.Where(x => !(x is MethodDeclarationSyntax || x is BaseTypeDeclarationSyntax)).ToList();

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

            var exceptions = method is MethodDeclarationSyntax methodForExceptions
                ? DetectThrownExceptions(methodForExceptions).ToList()
                : new List<string>();
            var xmlBlock = BuildXmlCommentBlock(indent, method, summary, exceptions);

            builder.Insert(insertPosition, xmlBlock);
            stats.DocumentedMethods++;
        }

        return builder.ToString();
    }

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

    private static int PositiveOrDefault(int value, int fallback)
    {
        return value > 0 ? value : fallback;
    }

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

        if (options.IgnoreTestMethods && member is BaseTypeDeclarationSyntax testType &&
            (testType.Identifier.ValueText.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) ||
             testType.Identifier.ValueText.EndsWith("Test", StringComparison.OrdinalIgnoreCase)))
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

        var containingTypeName = (method.Parent as TypeDeclarationSyntax)?.Identifier.ValueText ?? string.Empty;

        return containingTypeName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) ||
               containingTypeName.EndsWith("Test", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether a method&apos;s fully qualified name (namespace.type.method) matches the given regex pattern case-insensitively, returning false for null/whitespace patterns or any exception.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <param name="ignorePattern">The ignore pattern.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool MatchesIgnorePattern(MethodDeclarationSyntax method, string ignorePattern)
    {
        if (string.IsNullOrWhiteSpace(ignorePattern))
        {
            return false;
        }

        try
        {
            var containingType = (method.Parent as TypeDeclarationSyntax)?.Identifier.ValueText ?? string.Empty;
            var containingNamespace = method.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? string.Empty;
            var fullName = string.IsNullOrWhiteSpace(containingNamespace)
                ? containingType + "." + method.Identifier.ValueText
                : containingNamespace + "." + containingType + "." + method.Identifier.ValueText;

            return Regex.IsMatch(fullName, ignorePattern, RegexOptions.IgnoreCase);
        }
        catch (Exception)
        {
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

        var prompt = member is MethodDeclarationSyntax method
            ? BuildMethodPrompt(method, options)
            : BuildTypePrompt((BaseTypeDeclarationSyntax)member, options);

        var estimatedTokens = EstimateRequestTokens(prompt, options.MaxTokensPerRequest);
        if (stats.EstimatedTokensUsed + estimatedTokens > options.MaxEstimatedTokensPerCleanup)
        {
            return null;
        }

        stats.EstimatedTokensUsed += estimatedTokens;
        stats.AttemptedMethods++;

        if (!client.TryGenerateDocumentation(prompt, out var completion, out var error, options.MaxTokensPerRequest, RunToken) || string.IsNullOrWhiteSpace(completion))
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
            "Analyze this C# method and produce exactly one concise summary sentence (plain text only, no XML, no quotes). " +
            "Mention key behavior and side effects.\n" +
            "Signature:\n" + signature + "\n" +
            "Method body:\n" + Truncate(bodyText, options.MaxInputCharsPerMethod) + "\n" +
            "Detected thrown exceptions: " + exceptionList;
    }

    /// <summary>
    /// Describes a type by its declaration header and member names only - including member bodies
    /// would blow up the context for little gain.
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
        if (type is TypeDeclarationSyntax typeDeclaration)
        {
            foreach (var member in typeDeclaration.Members)
            {
                if (member is PropertyDeclarationSyntax p) memberNames.Add(p.Identifier.ValueText);
                else if (member is MethodDeclarationSyntax m) memberNames.Add(m.Identifier.ValueText + "()");
                else if (member is FieldDeclarationSyntax f) memberNames.AddRange(f.Declaration.Variables.Select(v => v.Identifier.ValueText));
            }
        }
        else if (type is EnumDeclarationSyntax enumDeclaration)
        {
            memberNames.AddRange(enumDeclaration.Members.Select(x => x.Identifier.ValueText));
        }

        var members = memberNames.Count == 0 ? "none" : string.Join(", ", memberNames);

        return
            "Analyze this C# type and produce exactly one concise summary sentence (plain text only, no XML, no quotes). " +
            "Describe what the type represents, not how it is implemented.\n" +
            "Kind: " + kind + "\n" +
            "Name: " + header + "\n" +
            "Members: " + Truncate(members, options.MaxInputCharsPerMethod);
    }

    /// <summary>
    /// Builds a documentation summary string for a property by detecting its accessors—using a get accessor or expression body to set `hasGet`, and a set or init accessor for `hasSet`—then concatenating a verb (&quot;Gets or sets&quot;, &quot;Sets&quot;, or &quot;Gets&quot;) with &quot; the &quot; and the lowercased, split identifier name plus a period, with no side effects.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string BuildPropertySummary(PropertyDeclarationSyntax property)
    {
        var accessors = property.AccessorList?.Accessors;
        var hasGet = accessors?.Any(x => x.IsKind(SyntaxKind.GetAccessorDeclaration)) ?? property.ExpressionBody is not null;
        var hasSet = accessors?.Any(x => x.IsKind(SyntaxKind.SetAccessorDeclaration) || x.IsKind(SyntaxKind.InitAccessorDeclaration)) ?? false;

        if (!hasGet && !hasSet && property.Initializer is not null)
        {
            hasGet = true;
        }

        var verb = hasGet && hasSet ? "Gets or sets" : hasSet ? "Sets" : "Gets";

        return verb + " the " + SplitIdentifier(property.Identifier.ValueText).ToLowerInvariant() + ".";
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
    private static IEnumerable<string> DetectThrownExceptions(MethodDeclarationSyntax method)
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

    private static int GetLineStart(string text, int index)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, Math.Min(index, text.Length) - 1));

        return lineStart < 0 ? 0 : lineStart + 1;
    }

    /// <summary>
    /// an XML documentation comment block string for a member by appending an indented `&lt;summary&gt;` element, and for methods additionally appending `&lt;param&gt;` elements for each parameter, a `&lt;returns&gt;` element (unless the return type is void), and alphabetically-ordered `&lt;exception&gt;` elements using XML-escaped content.
    /// </summary>
    /// <param name="indent">The indent.</param>
    /// <param name="member">The member.</param>
    /// <param name="summary">The summary.</param>
    /// <param name="exceptionTypes">The exception types.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string BuildXmlCommentBlock(string indent, MemberDeclarationSyntax member, string summary, IEnumerable<string> exceptionTypes)
    {
        var sb = new StringBuilder();

        sb.Append(indent).AppendLine("/// <summary>");
        sb.Append(indent).Append("/// ").AppendLine(XmlEscape(summary));
        sb.Append(indent).AppendLine("/// </summary>");

        var method = member as MethodDeclarationSyntax;
        if (method is null)
        {
            return sb.ToString();
        }

        foreach (var parameter in method.ParameterList.Parameters)
        {
            var parameterName = parameter.Identifier.ValueText;
            if (!string.IsNullOrWhiteSpace(parameterName))
            {
                sb.Append(indent)
                    .Append("/// <param name=\"")
                    .Append(parameterName)
                    .Append("\">")
                    .Append(XmlEscape(BuildParameterDescription(parameterName)))
                    .AppendLine("</param>");
            }
        }

        var returnsVoid = method.ReturnType is not null && method.ReturnType.ToString() == "void";
        if (!returnsVoid)
        {
            sb.Append(indent)
                .Append("/// <returns>")
                .Append(XmlEscape(BuildReturnDescription(method.ReturnType?.ToString())))
                .AppendLine("</returns>");
        }

        foreach (var exceptionType in exceptionTypes.OrderBy(x => x, StringComparer.Ordinal))
        {
            sb.Append(indent)
                .Append("/// <exception cref=\"")
                .Append(XmlEscape(exceptionType))
                .Append("\">")
                .Append(XmlEscape("Thrown when method validation or execution fails for this exception type."))
                .AppendLine("</exception>");
        }

        return sb.ToString();
    }

    /// <summary>
    /// s a human-readable description string for a parameter by splitting the identifier on casing boundaries, lowercasing the result, and prefixing it with &quot;The &quot; and suffixing it with a period.
    /// </summary>
    /// <param name="parameterName">The parameter name.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string BuildParameterDescription(string parameterName)
    {
        return "The " + SplitIdentifier(parameterName).ToLowerInvariant() + ".";
    }

    /// <summary>
    /// BuildReturnDescription returns a formatted description string, defaulting to &quot;result of operation.&quot; when the input returnType is null, empty, or whitespace, otherwise prefixing the type with &quot;A &quot; and appending &quot; value produced by this method.&quot;, with no side effects.
    /// </summary>
    /// <param name="returnType">The return type.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string BuildReturnDescription(string returnType)
    {
        if (string.IsNullOrWhiteSpace(returnType))
        {
            return "The result of the operation.";
        }

        return "A " + returnType + " value produced by this method.";
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
            return "Represents " + SplitIdentifier(type.Identifier.ValueText).ToLowerInvariant() + ".";
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

        var method = (MethodDeclarationSyntax)member;

        return "Performs " + SplitIdentifier(method.Identifier.ValueText).ToLowerInvariant() + ".";
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
    /// izes a sentence by collapsing whitespace, trimming surrounding quotes (&quot;, &apos;, `), appending a period if it lacks sentence-ending punctuation, and returning the fallback &quot;Performs operation.&quot; when the input is null or empty.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A string value produced by this method.</returns>
    private static string NormalizeSentence(string text)
    {
        var compact = Regex.Replace(text ?? string.Empty, "\\s+", " ").Trim();
        if (string.IsNullOrEmpty(compact))
        {
            return "Performs the operation.";
        }

        compact = compact.Trim('"', '\'', '`');
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

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text.Substring(0, maxLength) + "...";
    }

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
