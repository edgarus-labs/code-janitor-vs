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
using System.Windows;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// Adds XML documentation comments for C# methods that do not already have them,
/// using an OpenAI-compatible endpoint for concise summaries and deterministic
/// tags for parameters, returns and detected exceptions.
/// </summary>

internal sealed class AiXmlDocumentationLogic
{
    private readonly CodeJanitorPackage _package;

    private static AiXmlDocumentationLogic _instance;

    /// <summary>
    /// Returns the cached singleton AiXmlDocumentationLogic instance, lazily creating and assigning a new one with the provided package if none exists yet.
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

    internal sealed class AiXmlDocumentationRunOptions
    {
        public int MaxMethodsPerFile { get; set; }

        public int MaxRequestsPerCleanup { get; set; }

        public int MaxInputCharsPerMethod { get; set; }

        public int MaxTokensPerRequest { get; set; }

        public int MaxEstimatedTokensPerCleanup { get; set; }

        public int GlobalTimeoutSeconds { get; set; }

        public bool AllowDeterministicFallback { get; set; }

        public bool IgnoreGeneratedCode { get; set; }

        public bool IgnoreObsolete { get; set; }

        public bool IgnoreTestMethods { get; set; }

        public string IgnorePattern { get; set; }

        public bool PreviewChanges { get; set; }
    }

    internal sealed class AiXmlDocumentationRunStats
    {
        public int EligibleMethods { get; set; }

        public int AttemptedMethods { get; set; }

        public int DocumentedMethods { get; set; }

        public int SkippedByFilters { get; set; }

        public int SkippedByBudget { get; set; }

        public int AiFailures { get; set; }

        public int FallbacksUsed { get; set; }

        public int EstimatedTokensUsed { get; set; }

        public long ElapsedMilliseconds { get; set; }
    }

    /// <summary>
    /// Checks whether an OpenAI-compatible endpoint and its API key are configured by retrieving the API key and validating it against the default cleaning endpoint URL, with no side effects or thrown exceptions.
    /// </summary>
    /// <returns>A bool value produced by this method.</returns>

    internal static bool IsConfigurationPresent()
    {
        var apiKey = GetConfiguredApiKey();

        return OpenAiCompatibleClient.IsEndpointConfigured(
            Settings.Default.Cleaning_AiXmlDocumentationEndpointUrl,
            apiKey);
    }

    /// <summary>
    /// This method validates the AI XML documentation connection by delegating to a parameterized overload with configured endpoint, API key, header, model, and timeout, outputting any error via the message parameter and returning success as a boolean without throwing exceptions.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A bool value produced by this method.</returns>

    internal static bool TryValidateConnection(out string message)
    {
        return TryValidateConnection(
            Settings.Default.Cleaning_AiXmlDocumentationEndpointUrl,
            GetConfiguredApiKey(),
            Settings.Default.Cleaning_AiXmlDocumentationApiKeyHeader,
            Settings.Default.Cleaning_AiXmlDocumentationModel,
            Settings.Default.Cleaning_AiXmlDocumentationTimeoutSeconds,
            out message);
    }

    /// <summary>
    /// This method creates an AI client from the supplied parameters, immediately returns false with an invalid-endpoint-or-key message if the client cannot be created, otherwise attempts a connection test and sets the output message to the test result or error before returning the success flag, with no exception propagation.
    /// </summary>
    /// <param name="endpointUrl">The endpoint url.</param>
    /// <param name="apiKey">The api key.</param>
    /// <param name="apiKeyHeader">The api key header.</param>
    /// <param name="model">The model.</param>
    /// <param name="timeoutSeconds">The timeout seconds.</param>
    /// <param name="message">The message.</param>
    /// <returns>A bool value produced by this method.</returns>

    internal static bool TryValidateConnection(string endpointUrl, string apiKey, string apiKeyHeader, string model, int timeoutSeconds, out string message)
    {
        var client = CreateClient(endpointUrl, apiKey, apiKeyHeader, model, timeoutSeconds);
        if (client == null)
        {
            message = "AI XML documentation endpoint URL or API key is missing/invalid.";

            return false;
        }

        string error;
        var ok = client.TryTestConnection(out error);
        message = ok ? "Connection successful." : error;

        return ok;
    }

    /// <summary>
    /// Determines whether the specified project item is an eligible C# file for AI XML documentation.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <returns>True if eligible, otherwise false.</returns>

    internal bool CanDocumentProjectItem(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (projectItem == null || !projectItem.IsPhysicalFile())
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
        if (client == null)
        {
            return false;
        }

        var document = projectItem.Document;
        if (document != null)
        {
            var textDocument = document.GetTextDocument();
            if (textDocument != null)
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
        OutputWindowHelper.InfoWriteLine($"AiXmlDocumentationLogic.ApplyXmlDocumentation updated '{filePath}'.");

        return true;
    }

    /// <summary>
    /// Applies AI-generated XML documentation to the entire document text when the setting is enabled, replacing the content only if changed unless preview mode is active, in which case it invokes a preview flow instead.
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
        if (client == null)
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
    /// If XML documentation cleaning is disabled or preview mode is enabled, the method returns the source unchanged; otherwise it creates a client from settings and, if the client exists, applies XML documentation to the source via an internal method, potentially modifying the source and making an external client call.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>A string value produced by this method.</returns>

    internal string ApplyXmlDocumentationToSource(string source)
    {
        if (!Settings.Default.Cleaning_AiXmlDocumentationEnabled ||
            Settings.Default.Cleaning_AiXmlDocumentationPreviewChanges)
        {
            return source;
        }

        var client = CreateClientFromSettings();

        return client == null ? source : ApplyXmlDocumentationToSourceInternal(source, client);
    }

    /// <summary>
    /// Generates XML documentation for the entire source text via AI, logs statistics, and if changes exist, shows a modal preview dialog before replacing the whole document content on user confirmation.
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
    /// Applies AI-generated XML documentation to the source text via a client callback, updates execution statistics and elapsed time, writes a diagnostic line summarizing those stats, and returns the modified source text.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="client">The client.</param>
    /// <returns>A string value produced by this method.</returns>

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
    /// Creates an AiXmlDocumentationRunOptions with default limits (sanitizing maxMethodsPerFile to at least 25) and delegates to the internal generation method with fresh stats, producing XML documentation without side effects.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="summaryProvider">The summary provider.</param>
    /// <param name="maxMethodsPerFile">The max methods per file.</param>
    /// <returns>A string value produced by this method.</returns>

    internal static string GenerateXmlDocumentationForSource(string source, Func<MethodDeclarationSyntax, string> summaryProvider, int maxMethodsPerFile)
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
    /// Parses C# source, selects eligible methods by filters and budget, generates and inserts XML doc comments before method declarations with stats counters updated, returning the modified source or the original input when no methods are eligible.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="summaryProvider">The summary provider.</param>
    /// <param name="options">The options.</param>
    /// <param name="stats">The stats.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string GenerateXmlDocumentationForSourceInternal(string source, Func<MethodDeclarationSyntax, string> summaryProvider, AiXmlDocumentationRunOptions options, AiXmlDocumentationRunStats stats)
    {
        if (string.IsNullOrWhiteSpace(source) || summaryProvider == null)
        {
            return source;
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var skippedByFilter = 0;
        var eligibleMethods = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(x => CanDocumentMethod(x, options, ref skippedByFilter))
            .OrderBy(x => x.GetFirstToken().SpanStart)
            .ToList();

        stats.SkippedByFilters += skippedByFilter;

        stats.EligibleMethods = eligibleMethods.Count;

        if (!eligibleMethods.Any())
        {
            return source;
        }

        var methodLimit = PositiveOrDefault(options.MaxMethodsPerFile, 25);
        if (eligibleMethods.Count > methodLimit)
        {
            eligibleMethods = eligibleMethods.Take(methodLimit).ToList();
            stats.SkippedByBudget += (stats.EligibleMethods - eligibleMethods.Count);
        }

        var builder = new StringBuilder(source);
        var deadlineUtc = DateTime.UtcNow.AddSeconds(options.GlobalTimeoutSeconds > 0 ? options.GlobalTimeoutSeconds : 60);
        foreach (var method in eligibleMethods.OrderByDescending(x => x.GetFirstToken().SpanStart))
        {
            if (DateTime.UtcNow > deadlineUtc)
            {
                stats.SkippedByBudget++;
                continue;
            }

            var insertPosition = method.GetFirstToken().SpanStart;
            var indent = GetLineIndent(builder.ToString(), insertPosition);

            var rawSummary = summaryProvider(method);
            if (string.IsNullOrWhiteSpace(rawSummary))
            {
                stats.SkippedByBudget++;
                continue;
            }

            var summary = NormalizeSentence(rawSummary);

            var exceptions = DetectThrownExceptions(method).ToList();
            var xmlBlock = BuildXmlCommentBlock(indent, method, summary, exceptions);

            builder.Insert(insertPosition, xmlBlock);
            stats.DocumentedMethods++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Constructs an AiXmlDocumentationRunOptions from application settings, applying positive-value defaults for numeric limits via PositiveOrDefault and directly copying boolean/string options, with no side effects or exceptions.
    /// </summary>
    /// <returns>A AiXmlDocumentationRunOptions value produced by this method.</returns>

    private static AiXmlDocumentationRunOptions LoadRunOptionsFromSettings()
    {
        return new AiXmlDocumentationRunOptions
        {
            MaxMethodsPerFile = PositiveOrDefault(Settings.Default.Cleaning_AiXmlDocumentationMaxMethodsPerFile, 25),
            MaxRequestsPerCleanup = PositiveOrDefault(Settings.Default.Cleaning_AiXmlDocumentationMaxRequestsPerCleanup, 25),
            MaxInputCharsPerMethod = PositiveOrDefault(Settings.Default.Cleaning_AiXmlDocumentationMaxInputCharsPerMethod, 2500),
            MaxTokensPerRequest = PositiveOrDefault(Settings.Default.Cleaning_AiXmlDocumentationMaxTokensPerRequest, 256),
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
    /// Returns the input value if it is greater than zero, otherwise returns the fallback, with no side effects or exceptions thrown.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="fallback">The fallback.</param>
    /// <returns>A int value produced by this method.</returns>

    private static int PositiveOrDefault(int value, int fallback)
    {
        return value > 0 ? value : fallback;
    }

    /// <summary>
    /// Creates and returns an OpenAiCompatibleClient by passing endpoint, API key, header, model, and timeout values from application settings, with no side effects or thrown exceptions detected.
    /// </summary>
    /// <returns>A OpenAiCompatibleClient value produced by this method.</returns>

    private static OpenAiCompatibleClient CreateClientFromSettings()
    {
        return CreateClient(
            Settings.Default.Cleaning_AiXmlDocumentationEndpointUrl,
            GetConfiguredApiKey(),
            Settings.Default.Cleaning_AiXmlDocumentationApiKeyHeader,
            Settings.Default.Cleaning_AiXmlDocumentationModel,
            Settings.Default.Cleaning_AiXmlDocumentationTimeoutSeconds);
    }

    /// <summary>
    /// Creates an OpenAiCompatibleClient with the specified settings, or returns null if the endpoint URL or API key is not configured, and throws no exceptions.
    /// </summary>
    /// <param name="endpointUrl">The endpoint url.</param>
    /// <param name="apiKey">The api key.</param>
    /// <param name="apiKeyHeader">The api key header.</param>
    /// <param name="model">The model.</param>
    /// <param name="timeoutSeconds">The timeout seconds.</param>
    /// <returns>A OpenAiCompatibleClient value produced by this method.</returns>

    private static OpenAiCompatibleClient CreateClient(string endpointUrl, string apiKey, string apiKeyHeader, string model, int timeoutSeconds)
    {
        if (!OpenAiCompatibleClient.IsEndpointConfigured(endpointUrl, apiKey))
        {
            return null;
        }

        return new OpenAiCompatibleClient(
            endpointUrl,
            apiKey,
            apiKeyHeader,
            model,
            timeoutSeconds);
    }

    /// <summary>
    /// Attempts to decrypt the encrypted API key setting for the current user and returns it if valid, otherwise falls back to the legacy plain-text setting, with no thrown exceptions.
    /// </summary>
    /// <returns>A string value produced by this method.</returns>

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
    /// Determines whether a method is eligible for documentation by filtering out null, interface, abstract/extern, bodyless, already-documented, obsolete, generated, test, or pattern-matching methods, incrementing the filtered counter for each exclusion except the first three cases.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <param name="options">The options.</param>
    /// <param name="filteredCounter">The filtered counter.</param>
    /// <returns>A bool value produced by this method.</returns>

    private static bool CanDocumentMethod(MethodDeclarationSyntax method, AiXmlDocumentationRunOptions options, ref int filteredCounter)
    {
        if (method == null)
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

        if (method.Body == null && method.ExpressionBody == null)
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
    /// Checks whether a member declaration has any attribute whose simple name (stripped of namespace and &quot;Attribute&quot; suffix) case-insensitively matches one of the given names, returning false for null declarations or null name arrays and producing no side effects.
    /// </summary>
    /// <param name="declaration">The declaration.</param>
    /// <param name="names">The names.</param>
    /// <returns>A bool value produced by this method.</returns>

    private static bool HasAnyAttribute(MemberDeclarationSyntax declaration, params string[] names)
    {
        if (declaration == null)
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
    /// Determines if a method is likely a test method by returning true when it has any recognized test attribute or its containing type name ends with &quot;Test&quot; or &quot;Tests&quot; (case-insensitive), with no side effects.
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
    /// Returns whether a method&apos;s fully qualified name matches the given ignore pattern (case-insensitive), returning false for null/whitespace patterns or any exception, with no side effects.
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
    /// Checks whether the given method declaration has any single-line documentation comment trivia in its leading trivia, returning true if found and false otherwise, with no side effects.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>A bool value produced by this method.</returns>

    private static bool HasDocumentationComment(MethodDeclarationSyntax method)
    {
        return method.GetLeadingTrivia().Any(trivia =>
        {
            var structure = trivia.GetStructure();

            return structure != null && structure.Kind() == SyntaxKind.SingleLineDocumentationCommentTrivia;
        });
    }

    /// <summary>
    /// Returns an AI-generated summary for a method after enforcing per-cleanup request and token limits (incrementing stats counters), and falls back to a deterministic summary if generation fails and fallback is allowed.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="method">The method.</param>
    /// <param name="options">The options.</param>
    /// <param name="stats">The stats.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string CreateSummary(OpenAiCompatibleClient client, MethodDeclarationSyntax method, AiXmlDocumentationRunOptions options, AiXmlDocumentationRunStats stats)
    {
        if (stats.AttemptedMethods >= options.MaxRequestsPerCleanup)
        {
            return null;
        }

        var signature = method.WithBody(null)
            .WithExpressionBody(null)
            .WithSemicolonToken(default(SyntaxToken))
            .NormalizeWhitespace()
            .ToFullString();

        var bodyText = method.Body != null
            ? method.Body.ToFullString().Trim()
            : method.ExpressionBody?.ToFullString().Trim() ?? string.Empty;

        var exceptionList = string.Join(", ", DetectThrownExceptions(method));
        if (string.IsNullOrWhiteSpace(exceptionList))
        {
            exceptionList = "none detected";
        }

        var prompt =
            "Analyze this C# method and produce exactly one concise summary sentence (plain text only, no XML, no quotes). " +
            "Mention key behavior and side effects.\n" +
            "Signature:\n" + signature + "\n" +
            "Method body:\n" + Truncate(bodyText, options.MaxInputCharsPerMethod) + "\n" +
            "Detected thrown exceptions: " + exceptionList;

        var estimatedTokens = EstimateRequestTokens(prompt, options.MaxTokensPerRequest);
        if (stats.EstimatedTokensUsed + estimatedTokens > options.MaxEstimatedTokensPerCleanup)
        {
            return null;
        }

        stats.EstimatedTokensUsed += estimatedTokens;
        stats.AttemptedMethods++;

        string completion;
        string error;
        if (!client.TryGenerateDocumentation(prompt, out completion, out error, options.MaxTokensPerRequest) || string.IsNullOrWhiteSpace(completion))
        {
            stats.AiFailures++;
            if (!options.AllowDeterministicFallback)
            {
                return null;
            }

            stats.FallbacksUsed++;

            return BuildFallbackSummary(method);
        }

        return NormalizeSentence(completion);
    }

    /// <summary>
    /// Estimates total token count as roughly one token per four prompt characters (plus one if the prompt is non-blank) plus the specified max output tokens, defaulting to 256 when that value is non-positive, with no side effects.
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
    /// Detects exception type names from throw statements, throw expressions, and common guard helper invocations in a method&apos;s syntax tree, returning a case-sensitive set of strings with no side effects.
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
    /// Returns the type name string when the expression is an object creation with a non-null type, otherwise returns null, with no side effects or exceptions.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string TryGetThrownTypeName(ExpressionSyntax expression)
    {
        if (expression is ObjectCreationExpressionSyntax creation && creation.Type != null)
        {
            return creation.Type.ToString();
        }

        return null;
    }

    /// <summary>
    /// Builds an XML documentation comment string with escaped summary, parameter, return (if not void), and ordinally sorted exception tags based on the given method syntax, with no side effects.
    /// </summary>
    /// <param name="indent">The indent.</param>
    /// <param name="method">The method.</param>
    /// <param name="summary">The summary.</param>
    /// <param name="exceptionTypes">The exception types.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string BuildXmlCommentBlock(string indent, MethodDeclarationSyntax method, string summary, IEnumerable<string> exceptionTypes)
    {
        var sb = new StringBuilder();

        sb.Append(indent).AppendLine("/// <summary>");
        sb.Append(indent).Append("/// ").AppendLine(XmlEscape(summary));
        sb.Append(indent).AppendLine("/// </summary>");

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

        var returnsVoid = method.ReturnType != null && method.ReturnType.ToString() == "void";
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
    /// Builds a description string by splitting the parameter name into words, converting them to lowercase, and returning &quot;The &lt;words&gt;.&quot; with no side effects or exceptions thrown.
    /// </summary>
    /// <param name="parameterName">The parameter name.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string BuildParameterDescription(string parameterName)
    {
        return "The " + SplitIdentifier(parameterName).ToLowerInvariant() + ".";
    }

    /// <summary>
    /// Builds a human-readable return description from the given type name, returning a default phrase for null or whitespace input, and otherwise composing a type-specific phrase, with no side effects or exceptions.
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
    /// Builds a lowercase fallback summary sentence from the method name by splitting its identifier, with no side effects or exceptions thrown.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string BuildFallbackSummary(MethodDeclarationSyntax method)
    {
        return "Performs " + SplitIdentifier(method.Identifier.ValueText).ToLowerInvariant() + ".";
    }

    /// <summary>
    /// Returns a human-readable, trimmed version of an identifier by inserting spaces before capitals and replacing underscores, or &quot;the operation&quot; for null/whitespace input, with no side effects.
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
    /// Finds the line&apos;s leading whitespace (spaces and tabs) from the line containing the given position by scanning back to the line start and forward until the first non-whitespace character, returning that indentation substring with no side effects.
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
    /// Normalizes a string by collapsing whitespace, trimming quotes, appending a period if no terminal punctuation exists, and returning a default phrase for empty input.
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
    /// Returns the input string with XML special characters replaced by their corresponding entities, treating null as an empty string, with no side effects or exceptions.
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
    /// Truncates the input text to the given maxLength by appending &quot;...&quot; when the text exceeds that limit, otherwise returns the original string unchanged, with no side effects.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="maxLength">The max length.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text.Substring(0, maxLength) + "...";
    }

    /// <summary>
    /// Compares two strings line-by-line after normalizing line endings, returns a numbered diff preview of up to maxChangedLines changes with a truncation marker, or &quot;No textual changes.&quot; if identical, with no external side effects.
    /// </summary>
    /// <param name="originalText">The original text.</param>
    /// <param name="updatedText">The updated text.</param>
    /// <param name="maxChangedLines">The max changed lines.</param>
    /// <returns>A string value produced by this method.</returns>

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
