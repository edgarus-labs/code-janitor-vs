using EnvDTE;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeJanitor.Logic.Cleaning
{
    /// <summary>
    /// Adds XML documentation comments for C# methods that do not already have them,
    /// using an OpenAI-compatible endpoint for concise summaries and deterministic
    /// tags for parameters, returns and detected exceptions.
    /// </summary>
    internal sealed class AiXmlDocumentationLogic
    {
        #region Fields

        private readonly CodeJanitorPackage _package;

        #endregion Fields

        #region Constructors

        private static AiXmlDocumentationLogic _instance;

        internal static AiXmlDocumentationLogic GetInstance(CodeJanitorPackage package)
        {
            return _instance ?? (_instance = new AiXmlDocumentationLogic(package));
        }

        private AiXmlDocumentationLogic(CodeJanitorPackage package)
        {
            _package = package;
        }

        #endregion Constructors

        #region Internal Methods

        internal static bool IsConfigurationPresent()
        {
            var apiKey = GetConfiguredApiKey();
            return OpenAiCompatibleClient.IsEndpointConfigured(
                Settings.Default.Cleaning_AiXmlDocumentationEndpointUrl,
                apiKey);
        }

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

            var startPoint = textDocument.StartPoint.CreateEditPoint();
            var originalText = startPoint.GetText(textDocument.EndPoint);
            var updatedText = GenerateXmlDocumentationForSource(
                originalText,
                method => CreateSummary(client, method),
                Settings.Default.Cleaning_AiXmlDocumentationMaxMethodsPerFile);

            if (updatedText == originalText)
            {
                return;
            }

            var endPoint = textDocument.EndPoint.CreateEditPoint();
            startPoint.ReplaceText(endPoint, updatedText, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
        }

        #endregion Internal Methods

        #region Private Methods

        internal static string GenerateXmlDocumentationForSource(string source, Func<MethodDeclarationSyntax, string> summaryProvider, int maxMethodsPerFile)
        {
            if (string.IsNullOrWhiteSpace(source) || summaryProvider == null)
            {
                return source;
            }

            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetRoot();

            var eligibleMethods = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(CanDocumentMethod)
                .OrderBy(x => x.GetFirstToken().SpanStart)
                .ToList();

            if (!eligibleMethods.Any())
            {
                return source;
            }

            var methodLimit = maxMethodsPerFile > 0 ? maxMethodsPerFile : 25;
            if (eligibleMethods.Count > methodLimit)
            {
                eligibleMethods = eligibleMethods.Take(methodLimit).ToList();
            }

            var builder = new StringBuilder(source);
            foreach (var method in eligibleMethods.OrderByDescending(x => x.GetFirstToken().SpanStart))
            {
                var insertPosition = method.GetFirstToken().SpanStart;
                var indent = GetLineIndent(builder.ToString(), insertPosition);

                var summary = NormalizeSentence(summaryProvider(method));
                var exceptions = DetectThrownExceptions(method).ToList();
                var xmlBlock = BuildXmlCommentBlock(indent, method, summary, exceptions);

                builder.Insert(insertPosition, xmlBlock);
            }

            return builder.ToString();
        }

        private static OpenAiCompatibleClient CreateClientFromSettings()
        {
            return CreateClient(
                Settings.Default.Cleaning_AiXmlDocumentationEndpointUrl,
                GetConfiguredApiKey(),
                Settings.Default.Cleaning_AiXmlDocumentationApiKeyHeader,
                Settings.Default.Cleaning_AiXmlDocumentationModel,
                Settings.Default.Cleaning_AiXmlDocumentationTimeoutSeconds);
        }

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

        private static bool CanDocumentMethod(MethodDeclarationSyntax method)
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

            return !HasDocumentationComment(method);
        }

        private static bool HasDocumentationComment(MethodDeclarationSyntax method)
        {
            return method.GetLeadingTrivia().Any(trivia =>
            {
                var structure = trivia.GetStructure();
                return structure != null && structure.Kind() == SyntaxKind.SingleLineDocumentationCommentTrivia;
            });
        }

        private static string CreateSummary(OpenAiCompatibleClient client, MethodDeclarationSyntax method)
        {
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
                "Method body:\n" + Truncate(bodyText, 2500) + "\n" +
                "Detected thrown exceptions: " + exceptionList;

            string completion;
            string error;
            if (!client.TryGenerateDocumentation(prompt, out completion, out error) || string.IsNullOrWhiteSpace(completion))
            {
                return BuildFallbackSummary(method);
            }

            return NormalizeSentence(completion);
        }

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

            return exceptions;
        }

        private static string TryGetThrownTypeName(ExpressionSyntax expression)
        {
            if (expression is ObjectCreationExpressionSyntax creation && creation.Type != null)
            {
                return creation.Type.ToString();
            }

            return null;
        }

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

        private static string BuildParameterDescription(string parameterName)
        {
            return "The " + SplitIdentifier(parameterName).ToLowerInvariant() + ".";
        }

        private static string BuildReturnDescription(string returnType)
        {
            if (string.IsNullOrWhiteSpace(returnType))
            {
                return "The result of the operation.";
            }

            return "A " + returnType + " value produced by this method.";
        }

        private static string BuildFallbackSummary(MethodDeclarationSyntax method)
        {
            return "Performs " + SplitIdentifier(method.Identifier.ValueText).ToLowerInvariant() + ".";
        }

        private static string SplitIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return "the operation";
            }

            var text = Regex.Replace(identifier, "([a-z0-9])([A-Z])", "$1 $2");
            return text.Replace('_', ' ').Trim();
        }

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

        #endregion Private Methods
    }
}