using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Roslyn-based static branch analyzer that discovers all decision paths, guard clauses, and edge conditions in C# code.
/// </summary>
public sealed class RoslynCodeBranchAnalyzer : ICodeBranchAnalyzer
{
    public IReadOnlyList<CodeBranchDescriptor> AnalyzeBranches(string sourceCode)
    {
        var branches = new List<CodeBranchDescriptor>();
        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            return branches;
        }

        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = syntaxTree.GetRoot();
            var text = syntaxTree.GetText();

            // 1. If / Else statements
            var ifStatements = root.DescendantNodes().OfType<IfStatementSyntax>();
            int branchIdx = 1;
            foreach (var ifStmt in ifStatements)
            {
                var line = text.Lines.GetLinePosition(ifStmt.SpanStart).Line + 1;
                var conditionText = ifStmt.Condition.ToString();

                // Check if this is a guard clause (throws or returns immediately)
                bool isGuard = ifStmt.Statement is BlockSyntax block
                    ? block.Statements.Any(s => s is ReturnStatementSyntax || s is ThrowStatementSyntax)
                    : (ifStmt.Statement is ReturnStatementSyntax || ifStmt.Statement is ThrowStatementSyntax);

                branches.Add(new CodeBranchDescriptor
                {
                    Id = $"B{branchIdx++}",
                    BranchType = isGuard ? CodeBranchType.GuardClause : CodeBranchType.IfBranch,
                    Description = isGuard ? $"Guard Clause: If ({conditionText}) early exit/throw" : $"If branch: ({conditionText}) is true",
                    ConditionSnippet = conditionText,
                    LineNumber = line
                });

                if (ifStmt.Else != null)
                {
                    var elseLine = text.Lines.GetLinePosition(ifStmt.Else.SpanStart).Line + 1;
                    if (!(ifStmt.Else.Statement is IfStatementSyntax))
                    {
                        branches.Add(new CodeBranchDescriptor
                        {
                            Id = $"B{branchIdx++}",
                            BranchType = CodeBranchType.ElseBranch,
                            Description = $"Else branch: ({conditionText}) is false",
                            ConditionSnippet = $"!({conditionText})",
                            LineNumber = elseLine
                        });
                    }
                }
            }

            // 2. Switch Statements & Expressions
            var switchSections = root.DescendantNodes().OfType<SwitchSectionSyntax>();
            foreach (var section in switchSections)
            {
                var line = text.Lines.GetLinePosition(section.SpanStart).Line + 1;
                var labels = string.Join(", ", section.Labels.Select(l => l.ToString()));
                branches.Add(new CodeBranchDescriptor
                {
                    Id = $"B{branchIdx++}",
                    BranchType = CodeBranchType.SwitchCase,
                    Description = $"Switch case: {labels}",
                    ConditionSnippet = labels,
                    LineNumber = line
                });
            }

            var switchArms = root.DescendantNodes().OfType<SwitchExpressionArmSyntax>();
            foreach (var arm in switchArms)
            {
                var line = text.Lines.GetLinePosition(arm.SpanStart).Line + 1;
                branches.Add(new CodeBranchDescriptor
                {
                    Id = $"B{branchIdx++}",
                    BranchType = CodeBranchType.SwitchCase,
                    Description = $"Switch expression arm: {arm.Pattern}",
                    ConditionSnippet = arm.Pattern.ToString(),
                    LineNumber = line
                });
            }

            // 3. Ternary Conditional Expressions ( ? : )
            var conditionals = root.DescendantNodes().OfType<ConditionalExpressionSyntax>();
            foreach (var cond in conditionals)
            {
                var line = text.Lines.GetLinePosition(cond.SpanStart).Line + 1;
                branches.Add(new CodeBranchDescriptor
                {
                    Id = $"B{branchIdx++}",
                    BranchType = CodeBranchType.Ternary,
                    Description = $"Ternary path: ({cond.Condition}) ? {cond.WhenTrue} : {cond.WhenFalse}",
                    ConditionSnippet = cond.Condition.ToString(),
                    LineNumber = line
                });
            }

            // 4. Null-coalescing Expressions ( ?? )
            var nullCoalesce = root.DescendantNodes().OfType<BinaryExpressionSyntax>()
                .Where(b => b.Kind() == SyntaxKind.CoalesceExpression);
            foreach (var nc in nullCoalesce)
            {
                var line = text.Lines.GetLinePosition(nc.SpanStart).Line + 1;
                branches.Add(new CodeBranchDescriptor
                {
                    Id = $"B{branchIdx++}",
                    BranchType = CodeBranchType.NullCoalescing,
                    Description = $"Null fallback path: {nc.Left} ?? {nc.Right}",
                    ConditionSnippet = $"{nc.Left} is null",
                    LineNumber = line
                });
            }

            // 5. Catch Clauses
            var catchClauses = root.DescendantNodes().OfType<CatchClauseSyntax>();
            foreach (var catchClause in catchClauses)
            {
                var line = text.Lines.GetLinePosition(catchClause.SpanStart).Line + 1;
                var excType = catchClause.Declaration?.Type?.ToString() ?? "Exception";
                branches.Add(new CodeBranchDescriptor
                {
                    Id = $"B{branchIdx++}",
                    BranchType = CodeBranchType.CatchBlock,
                    Description = $"Exception handled: catch ({excType})",
                    ConditionSnippet = excType,
                    LineNumber = line
                });
            }

            // 6. Explicit Throws
            var throwStatements = root.DescendantNodes().OfType<ThrowStatementSyntax>();
            foreach (var throwStmt in throwStatements)
            {
                var line = text.Lines.GetLinePosition(throwStmt.SpanStart).Line + 1;
                var expr = throwStmt.Expression?.ToString() ?? "throw";
                branches.Add(new CodeBranchDescriptor
                {
                    Id = $"B{branchIdx++}",
                    BranchType = CodeBranchType.ThrowException,
                    Description = $"Explicit error path: {expr}",
                    ConditionSnippet = expr,
                    LineNumber = line
                });
            }

            // Default branch for linear happy path if no branches found
            if (branches.Count == 0)
            {
                branches.Add(new CodeBranchDescriptor
                {
                    Id = "B1",
                    BranchType = CodeBranchType.IfBranch,
                    Description = "Standard primary execution path (Happy path)",
                    ConditionSnippet = "default",
                    LineNumber = 1
                });
            }
        }
        catch
        {
            // If syntax parsing fails, provide a default branch
            branches.Add(new CodeBranchDescriptor
            {
                Id = "B1",
                BranchType = CodeBranchType.IfBranch,
                Description = "Primary execution flow",
                ConditionSnippet = "default",
                LineNumber = 1
            });
        }

        return branches;
    }
}
