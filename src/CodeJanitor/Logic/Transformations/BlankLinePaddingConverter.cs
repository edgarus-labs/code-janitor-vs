using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Inserts blank line padding before and after declarations according to the effective per-kind settings of the file.
/// Uses Roslyn only for line-number discovery; actual insertion is done on the line list
/// (same safe pattern as <see cref="ReturnThrowBlankLinePaddingConverter"/>).
/// </summary>

public sealed class BlankLinePaddingConverter : ISourceTransformation
{
    private static readonly Regex CaseStatementPattern = new Regex(
        @"(^[ \t]*)(break;|return(?:[ \t][^;\r\n]*)?;)\r?\n([ \t]*)(case\b|default\s*:)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex SingleLineCommentPaddingPattern = new Regex(
        @"(^[ \t]*(?!//)[^ \t\r\n{].*)\r?\n([ \t]*//(?!/))",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly EffectiveCleanupSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlankLinePaddingConverter" /> class.
    /// </summary>
    /// <param name="settings">The effective cleanup settings of the file, which decide per kind whether padding is inserted.</param>

    internal BlankLinePaddingConverter(EffectiveCleanupSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name => "Insert blank line padding";

    /// <summary>
    /// Analyzes C# source text and returns a new string with blank-line padding inserted before declarations, regions, using blocks, case statements, and single-line comments based on settings, without modifying the original input.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>A string value produced by this method.</returns>

    public string Apply(string source)
    {
        if (string.IsNullOrEmpty(source) || !AnySettingEnabled())
            return source;

        var newline = source.Contains("\r\n") ? "\r\n" : (source.Contains("\r") ? "\r" : "\n");
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var lines = source.Split(new[] { newline }, StringSplitOptions.None).ToList();

        // Collect wanted insertions (0-based line index to insert a blank line BEFORE)
        var wantBlankBefore = new SortedSet<int>();

        CollectDeclarationPadding(root, tree, lines, wantBlankBefore);
        CollectRegionDirectivePadding(root, tree, wantBlankBefore);
        CollectUsingBlockPadding(root, tree, wantBlankBefore);

        foreach (var idx in wantBlankBefore.OrderByDescending(i => i))
        {
            if (!ShouldSkipInsertion(lines, idx))
                lines.Insert(idx, string.Empty);
        }

        var result = string.Join(newline, lines);

        // Regex-based: case statements
        if (_settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeCaseStatements)))
        {
            result = CaseStatementPattern.Replace(result, m =>
                m.Groups[1].Value + m.Groups[2].Value + newline + newline + m.Groups[3].Value + m.Groups[4].Value);
        }

        // Regex-based: single-line comments
        if (_settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeSingleLineComments)))
        {
            result = SingleLineCommentPaddingPattern.Replace(result, m =>
                m.Groups[1].Value + newline + newline + m.Groups[2].Value);
        }

        return result;
    }

    /// <summary>
    /// CollectDeclarationPadding traverses all descendant syntax nodes, determines blank-line padding requirements before and after each declaration type (classes, records, delegates, enums, events, fields, interfaces, namespaces, etc.) based on configuration settings (with field declarations additionally distinguished by single-line vs multi-line spans), and records the affected line positions in the provided `wantBlankBefore` set and `lines` list as a side effect, without throwing exceptions.
    /// </summary>
    /// <param name="root">The root.</param>
    /// <param name="tree">The tree.</param>
    /// <param name="lines">The lines.</param>
    /// <param name="wantBlankBefore">The want blank before.</param>

    private void CollectDeclarationPadding(SyntaxNode root, SyntaxTree tree, List<string> lines, SortedSet<int> wantBlankBefore)
    {
        foreach (var node in root.DescendantNodes())
        {
            bool padBefore = false, padAfter = false;

            if (node is ClassDeclarationSyntax || node is RecordDeclarationSyntax)
            {
                padBefore = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeClasses));
                padAfter = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterClasses));
            }
            else if (node is DelegateDeclarationSyntax)
            {
                padBefore = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeDelegates));
                padAfter = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterDelegates));
            }
            else if (node is EnumDeclarationSyntax)
            {
                padBefore = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeEnumerations));
                padAfter = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterEnumerations));
            }
            else if (node is EventDeclarationSyntax || node is EventFieldDeclarationSyntax)
            {
                padBefore = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeEvents));
                padAfter = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterEvents));
            }
            else if (node is FieldDeclarationSyntax)
            {
                var span = tree.GetLineSpan(node.Span);
                bool isMultiLine = span.EndLinePosition.Line > span.StartLinePosition.Line;
                padBefore = isMultiLine
                    ? _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeFieldsMultiLine))
                    : _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeFieldsSingleLine));
                padAfter = isMultiLine
                    ? _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterFieldsMultiLine))
                    : _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterFieldsSingleLine));
            }
            else if (node is InterfaceDeclarationSyntax)
            {
                padBefore = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeInterfaces));
                padAfter = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterInterfaces));
            }
            else if (node is NamespaceDeclarationSyntax || node is FileScopedNamespaceDeclarationSyntax)
            {
                padBefore = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeNamespaces));
                padAfter = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterNamespaces));
            }
            else if (node is MethodDeclarationSyntax || node is ConstructorDeclarationSyntax ||
                     node is DestructorDeclarationSyntax || node is OperatorDeclarationSyntax ||
                     node is ConversionOperatorDeclarationSyntax)
            {
                padBefore = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeMethods));
                padAfter = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterMethods));
            }
            else if (node is PropertyDeclarationSyntax || node is IndexerDeclarationSyntax)
            {
                var span = tree.GetLineSpan(node.Span);
                bool isMultiLine = span.EndLinePosition.Line > span.StartLinePosition.Line;
                padBefore = isMultiLine
                    ? _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforePropertiesMultiLine))
                    : _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine));
                padAfter = isMultiLine
                    ? _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterPropertiesMultiLine))
                    : _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine));
            }
            else if (node is StructDeclarationSyntax)
            {
                padBefore = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeStructs));
                padAfter = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterStructs));
            }
            else
            {
                continue;
            }

            if (!padBefore && !padAfter) continue;

            var lineSpan = tree.GetLineSpan(node.Span);
            int startLine = GetPaddingStartLine(node, tree);
            int endLine = lineSpan.EndLinePosition.Line;

            if (padBefore && startLine > 0)
                wantBlankBefore.Add(startLine);

            if (padAfter && endLine + 1 < lines.Count)
                wantBlankBefore.Add(endLine + 1);
        }
    }

    /// <summary>
    /// A documentation comment belongs to the member below it, so padding has to go above the
    /// comment rather than between the comment and the declaration.
    /// </summary>
    private static int GetPaddingStartLine(SyntaxNode node, SyntaxTree tree)
    {
        foreach (var trivia in node.GetLeadingTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
                return tree.GetLineSpan(trivia.Span).StartLinePosition.Line;
            }
        }

        return tree.GetLineSpan(node.Span).StartLinePosition.Line;
    }

    /// <summary>
    /// Collects line numbers of #region/#endregion directives into the provided SortedSet based on blank-line padding settings, mutating the set to indicate where blank lines should be inserted, and returns early if no padding options are enabled.
    /// </summary>
    /// <param name="root">The root.</param>
    /// <param name="tree">The tree.</param>
    /// <param name="wantBlankBefore">The want blank before.</param>

    private void CollectRegionDirectivePadding(SyntaxNode root, SyntaxTree tree, SortedSet<int> wantBlankBefore)
    {
        bool beforeRegion = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeRegionTags));
        bool afterRegion = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterRegionTags));
        bool beforeEndRegion = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeEndRegionTags));
        bool afterEndRegion = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterEndRegionTags));

        if (!beforeRegion && !afterRegion && !beforeEndRegion && !afterEndRegion)
            return;

        foreach (var trivia in root.DescendantTrivia())
        {
            if (trivia.IsKind(SyntaxKind.RegionDirectiveTrivia))
            {
                int line = tree.GetLineSpan(trivia.Span).StartLinePosition.Line;
                if (beforeRegion && line > 0) wantBlankBefore.Add(line);
                if (afterRegion) wantBlankBefore.Add(line + 1);
            }
            else if (trivia.IsKind(SyntaxKind.EndRegionDirectiveTrivia))
            {
                int line = tree.GetLineSpan(trivia.Span).StartLinePosition.Line;
                if (beforeEndRegion && line > 0) wantBlankBefore.Add(line);
                if (afterEndRegion) wantBlankBefore.Add(line + 1);
            }
        }
    }

    /// <summary>
    /// Collects using-directive groups by parent, splits them into consecutive line runs, and mutates the provided SortedSet by adding the first line of each run when blank-line padding before is enabled and the line after each run&apos;s end when padding after is enabled, returning early if neither setting is active.
    /// </summary>
    /// <param name="root">The root.</param>
    /// <param name="tree">The tree.</param>
    /// <param name="wantBlankBefore">The want blank before.</param>

    private void CollectUsingBlockPadding(SyntaxNode root, SyntaxTree tree, SortedSet<int> wantBlankBefore)
    {
        bool padBefore = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeUsingStatementBlocks));
        bool padAfter = _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterUsingStatementBlocks));

        if (!padBefore && !padAfter) return;

        // Process using directives grouped by their parent (compilation unit or namespace)
        var usingGroups = root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .GroupBy(u => u.Parent);

        foreach (var group in usingGroups)
        {
            var usings = group.OrderBy(u => u.SpanStart).ToList();
            if (usings.Count == 0) continue;

            // Find consecutive runs of using directives
            var runs = new List<List<UsingDirectiveSyntax>>();
            var currentRun = new List<UsingDirectiveSyntax> { usings[0] };

            for (int i = 1; i < usings.Count; i++)
            {
                var prevEnd = tree.GetLineSpan(usings[i - 1].Span).EndLinePosition.Line;
                var currStart = tree.GetLineSpan(usings[i].Span).StartLinePosition.Line;

                // Consecutive if no gap
                if (currStart <= prevEnd + 1)
                    currentRun.Add(usings[i]);
                else
                {
                    runs.Add(currentRun);
                    currentRun = new List<UsingDirectiveSyntax> { usings[i] };
                }
            }
            runs.Add(currentRun);

            foreach (var run in runs)
            {
                var first = run.First();
                var last = run.Last();
                int firstLine = tree.GetLineSpan(first.Span).StartLinePosition.Line;
                int lastEndLine = tree.GetLineSpan(last.Span).EndLinePosition.Line;

                if (padBefore && firstLine > 0) wantBlankBefore.Add(firstLine);
                if (padAfter) wantBlankBefore.Add(lastEndLine + 1);
            }
        }
    }

    /// <summary>
    /// Determines whether to skip inserting a line at a given index by returning true for out-of-range positions, blank previous lines, lines adjacent to opening braces, or lines starting with a closing brace, and otherwise returns false with no side effects.
    /// </summary>
    /// <param name="lines">The lines.</param>
    /// <param name="idx">The idx.</param>
    /// <returns>A bool value produced by this method.</returns>

    private static bool ShouldSkipInsertion(List<string> lines, int idx)
    {
        if (idx <= 0 || idx >= lines.Count) return true;

        // Already blank
        if (string.IsNullOrWhiteSpace(lines[idx - 1])) return true;

        // Adjacent to opening brace
        var prevTrimmed = lines[idx - 1].Trim();
        if (prevTrimmed == "{" || prevTrimmed.EndsWith("{", StringComparison.Ordinal)) return true;

        // Adjacent to closing brace on the target line
        if (idx < lines.Count)
        {
            var nextTrimmed = lines[idx].Trim();
            if (nextTrimmed == "}" || nextTrimmed.StartsWith("}", StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if any of the listed blank-line padding settings is enabled in the effective settings, otherwise false, with no side effects.
    /// </summary>
    /// <returns>A bool value produced by this method.</returns>

    private bool AnySettingEnabled()
    {
        return _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeClasses)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterClasses)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeDelegates)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterDelegates)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeEnumerations)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterEnumerations)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeEvents)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterEvents)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeFieldsSingleLine)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterFieldsSingleLine)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeFieldsMultiLine)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterFieldsMultiLine)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeInterfaces)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterInterfaces)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeMethods)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterMethods)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeNamespaces)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterNamespaces)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforePropertiesMultiLine)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterPropertiesMultiLine)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeRegionTags)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterRegionTags)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeEndRegionTags)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterEndRegionTags)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeStructs)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterStructs)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeUsingStatementBlocks)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingAfterUsingStatementBlocks)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeCaseStatements)) ||
               _settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeSingleLineComments));
    }
}
