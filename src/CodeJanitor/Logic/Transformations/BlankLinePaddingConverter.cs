using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Inserts blank line padding before and after declarations according to the effective per-kind settings of the file.
/// Uses Roslyn for line-number discovery, including which comments, case labels and string contents are real code;
/// the blank lines themselves are inserted as text changes (same safe pattern as <see cref="ReturnThrowBlankLinePaddingConverter"/>).
/// </summary>
public sealed class BlankLinePaddingConverter : ISourceTransformation
{
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
    /// Every blank line gets the line break of the line above it, so the line endings of the file are kept.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>A string value produced by this method.</returns>
    public string Apply(string source)
    {
        if (string.IsNullOrEmpty(source) || !AnySettingEnabled())
        {
            return source;
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        var text = tree.GetText();
        var root = tree.GetRoot();
        var lines = text.Lines.Select(line => text.ToString(line.Span)).ToList();

        // Collect wanted insertions (0-based line index to insert a blank line BEFORE)
        var wantBlankBefore = new SortedSet<int>();

        CollectDeclarationPadding(root, tree, lines, wantBlankBefore);
        CollectRegionDirectivePadding(root, tree, wantBlankBefore);
        CollectUsingBlockPadding(root, tree, wantBlankBefore);

        if (_settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeCaseStatements)))
        {
            CollectCaseStatementPadding(root, text, wantBlankBefore);
        }

        if (_settings.GetBoolean(nameof(Settings.Cleaning_InsertBlankLinePaddingBeforeSingleLineComments)))
        {
            CollectSingleLineCommentPadding(root, text, lines, wantBlankBefore);
        }

        // Conditional directives enclose code the way braces do: no blank line goes right after #if/#elif/#else
        // or right before #elif/#else/#endif.
        var afterConditionalOpener = new HashSet<int>();
        var beforeConditionalCloser = new HashSet<int>();
        foreach (var trivia in root.DescendantTrivia().Where(t => t.IsDirective))
        {
            var line = text.Lines.GetLineFromPosition(trivia.SpanStart).LineNumber;
            if (trivia.IsKind(SyntaxKind.IfDirectiveTrivia) || trivia.IsKind(SyntaxKind.ElifDirectiveTrivia) || trivia.IsKind(SyntaxKind.ElseDirectiveTrivia))
            {
                afterConditionalOpener.Add(line + 1);
            }

            if (trivia.IsKind(SyntaxKind.ElifDirectiveTrivia) || trivia.IsKind(SyntaxKind.ElseDirectiveTrivia) || trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia))
            {
                beforeConditionalCloser.Add(line);
            }
        }

        var changes = new List<TextChange>();
        foreach (var idx in wantBlankBefore)
        {
            if (ShouldSkipInsertion(lines, idx) || afterConditionalOpener.Contains(idx) || beforeConditionalCloser.Contains(idx) ||
                !IndentationGuard.CanChangeIndentation(root, text.Lines[idx].Start))
            {
                continue;
            }

            var previousLine = text.Lines[idx - 1];
            var lineBreak = text.ToString(TextSpan.FromBounds(previousLine.End, previousLine.EndIncludingLineBreak));
            changes.Add(new TextChange(new TextSpan(text.Lines[idx].Start, 0), lineBreak));
        }

        return changes.Count == 0 ? source : text.WithChanges(changes).ToString();
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

            if (!padBefore && !padAfter)
            {
                continue;
            }

            var lineSpan = tree.GetLineSpan(node.Span);
            int startLine = GetPaddingStartLine(node, tree);
            int endLine = lineSpan.EndLinePosition.Line;

            if (padBefore && startLine > 0)
            {
                wantBlankBefore.Add(startLine);
            }

            if (padAfter && endLine + 1 < lines.Count)
            {
                wantBlankBefore.Add(endLine + 1);
            }
        }
    }

    /// <summary>
    /// Comments directly above a declaration belong to it, so padding has to go above the comments rather than
    /// between them and the declaration. A plain comment block at the very top of the file (below a shebang line, if
    /// any) is a file header and stays separate; documentation comments always belong to the declaration.
    /// </summary>
    private static int GetPaddingStartLine(SyntaxNode node, SyntaxTree tree)
    {
        int startLine = tree.GetLineSpan(node.Span).StartLinePosition.Line;
        var leadingTrivia = node.GetLeadingTrivia();
        var attachedComments = new List<SyntaxTrivia>();
        int lineBelow = startLine;

        for (int i = leadingTrivia.Count - 1; i >= 0; i--)
        {
            var trivia = leadingTrivia[i];
            if (trivia.IsKind(SyntaxKind.WhitespaceTrivia) || trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                continue;
            }

            if (!IsComment(trivia) || GetLastLine(trivia, tree) < lineBelow - 1)
            {
                break;
            }

            attachedComments.Add(trivia);
            lineBelow = tree.GetLineSpan(trivia.Span).StartLinePosition.Line;
        }

        int count = attachedComments.Count;
        var sourceText = tree.GetText();
        int fileHeaderLine = sourceText.Lines[0].ToString().StartsWith("#!", StringComparison.Ordinal) ? 1 : 0;
        if (count > 0 && lineBelow == fileHeaderLine)
        {
            while (count > 0 && !IsDocumentationComment(attachedComments[count - 1]))
            {
                count--;
            }
        }

        return count == 0 ? startLine : tree.GetLineSpan(attachedComments[count - 1].Span).StartLinePosition.Line;
    }

    private static bool IsComment(SyntaxTrivia trivia)
    {
        return trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
            trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
            IsDocumentationComment(trivia);
    }

    private static bool IsDocumentationComment(SyntaxTrivia trivia)
    {
        return trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
            trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
    }

    /// <summary>
    /// The line holding the last character of the trivia; a single-line documentation comment includes its final line break.
    /// </summary>
    private static int GetLastLine(SyntaxTrivia trivia, SyntaxTree tree)
    {
        int lastPosition = Math.Max(trivia.SpanStart, trivia.Span.End - 1);

        return tree.GetLineSpan(new TextSpan(lastPosition, 0)).StartLinePosition.Line;
    }

    /// <summary>
    /// Pads a switch section whose label directly follows a <c>break;</c> or <c>return ...;</c> that sits alone on
    /// the line above it. Only real switch sections are considered, so string contents are never touched.
    /// </summary>
    private static void CollectCaseStatementPadding(SyntaxNode root, SourceText text, SortedSet<int> wantBlankBefore)
    {
        foreach (var switchStatement in root.DescendantNodes().OfType<SwitchStatementSyntax>())
        {
            for (int i = 1; i < switchStatement.Sections.Count; i++)
            {
                var exit = switchStatement.Sections[i - 1].Statements.LastOrDefault();
                if (!(exit is BreakStatementSyntax) && !(exit is ReturnStatementSyntax))
                {
                    continue;
                }

                var exitLine = text.Lines.GetLineFromPosition(exit.SpanStart);
                var labelLine = text.Lines.GetLineFromPosition(switchStatement.Sections[i].SpanStart);

                if (labelLine.LineNumber != exitLine.LineNumber + 1 ||
                    !IsWhitespace(text, exitLine.Start, exit.SpanStart) ||
                    exit.Span.End > exitLine.End ||
                    !IsWhitespace(text, exit.Span.End, exitLine.End) ||
                    !IsWhitespace(text, labelLine.Start, switchStatement.Sections[i].SpanStart))
                {
                    continue;
                }

                wantBlankBefore.Add(labelLine.LineNumber);
            }
        }
    }

    /// <summary>
    /// Pads a <c>//</c> comment that starts its line when the line above holds code. Comment continuations
    /// (including the continuation of a trailing comment), lines after an opening brace and markers inside
    /// strings or block comments are left alone.
    /// </summary>
    private static void CollectSingleLineCommentPadding(SyntaxNode root, SourceText text, List<string> lines, SortedSet<int> wantBlankBefore)
    {
        var linesWithComment = new HashSet<int>();
        var commentLines = new List<int>();

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
            {
                continue;
            }

            var line = text.Lines.GetLineFromPosition(trivia.SpanStart);
            linesWithComment.Add(line.LineNumber);

            if (!trivia.ToString().StartsWith("///", StringComparison.Ordinal) && IsWhitespace(text, line.Start, trivia.SpanStart))
            {
                commentLines.Add(line.LineNumber);
            }
        }

        foreach (var lineNumber in commentLines)
        {
            if (lineNumber == 0 || linesWithComment.Contains(lineNumber - 1))
            {
                continue;
            }

            var previous = lines[lineNumber - 1].TrimStart();
            if (previous.StartsWith("//", StringComparison.Ordinal) || previous.StartsWith("{", StringComparison.Ordinal))
            {
                continue;
            }

            wantBlankBefore.Add(lineNumber);
        }
    }

    private static bool IsWhitespace(SourceText text, int start, int end)
    {
        for (int position = start; position < end; position++)
        {
            if (!char.IsWhiteSpace(text[position]))
            {
                return false;
            }
        }

        return true;
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
        {
            return;
        }

        foreach (var trivia in root.DescendantTrivia())
        {
            if (trivia.IsKind(SyntaxKind.RegionDirectiveTrivia))
            {
                int line = tree.GetLineSpan(trivia.Span).StartLinePosition.Line;
                if (beforeRegion && line > 0)
                {
                    wantBlankBefore.Add(line);
                }

                if (afterRegion)
                {
                    wantBlankBefore.Add(line + 1);
                }
            }
            else if (trivia.IsKind(SyntaxKind.EndRegionDirectiveTrivia))
            {
                int line = tree.GetLineSpan(trivia.Span).StartLinePosition.Line;
                if (beforeEndRegion && line > 0)
                {
                    wantBlankBefore.Add(line);
                }

                if (afterEndRegion)
                {
                    wantBlankBefore.Add(line + 1);
                }
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

        if (!padBefore && !padAfter)
        {
            return;
        }

        // Process using directives grouped by their parent (compilation unit or namespace)
        var usingGroups = root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .GroupBy(u => u.Parent);

        foreach (var group in usingGroups)
        {
            var usings = group.OrderBy(u => u.SpanStart).ToList();
            if (usings.Count == 0)
            {
                continue;
            }

            // Find consecutive runs of using directives
            var runs = new List<List<UsingDirectiveSyntax>>();
            var currentRun = new List<UsingDirectiveSyntax> { usings[0] };

            for (int i = 1; i < usings.Count; i++)
            {
                var prevEnd = tree.GetLineSpan(usings[i - 1].Span).EndLinePosition.Line;
                var currStart = tree.GetLineSpan(usings[i].Span).StartLinePosition.Line;

                // Consecutive if no gap
                if (currStart <= prevEnd + 1)
                {
                    currentRun.Add(usings[i]);
                }
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
                int firstLine = GetPaddingStartLine(first, tree);
                int lastEndLine = tree.GetLineSpan(last.Span).EndLinePosition.Line;

                if (padBefore && firstLine > 0)
                {
                    wantBlankBefore.Add(firstLine);
                }

                if (padAfter)
                {
                    wantBlankBefore.Add(lastEndLine + 1);
                }
            }
        }
    }

    /// <summary>
    /// Determines whether to skip inserting a line at a given index by returning true for out-of-range positions, blank previous or target lines, lines adjacent to opening braces, or lines starting with a closing brace, and otherwise returns false with no side effects.
    /// </summary>
    /// <param name="lines">The lines.</param>
    /// <param name="idx">The idx.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool ShouldSkipInsertion(List<string> lines, int idx)
    {
        if (idx <= 0 || idx >= lines.Count)
        {
            return true;
        }

        // Already blank above or below (the empty last line of a file with a final line break counts as blank)
        if (string.IsNullOrWhiteSpace(lines[idx - 1]) || string.IsNullOrWhiteSpace(lines[idx]))
        {
            return true;
        }

        // Adjacent to opening brace
        var prevTrimmed = lines[idx - 1].Trim();
        if (prevTrimmed == "{" || prevTrimmed.EndsWith("{", StringComparison.Ordinal))
        {
            return true;
        }

        // Adjacent to closing brace on the target line
        if (idx < lines.Count)
        {
            var nextTrimmed = lines[idx].Trim();
            if (nextTrimmed == "}" || nextTrimmed.StartsWith("}", StringComparison.Ordinal))
            {
                return true;
            }
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
