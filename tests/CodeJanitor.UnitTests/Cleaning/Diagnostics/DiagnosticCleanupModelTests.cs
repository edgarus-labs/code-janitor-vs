using System;
using System.Collections.Generic;
using System.Linq;
using CodeJanitor.Logic.Cleaning.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning.Diagnostics;

/// <summary>
/// Unit tests for the public result and option types of the diagnostics cleanup: argument validation, the
/// normalization the host relies on, and the log lines produced by <c>ToString</c>.
/// </summary>
[TestClass]
public sealed class DiagnosticCleanupModelTests
{
    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Options_MissingCategoriesOrNoPass_AreRejected()
    {
        Assert.AreEqual("enabledCategories", Assert.ThrowsExactly<ArgumentNullException>(() => new DiagnosticCleanupOptions(null)).ParamName);
        Assert.AreEqual("maxPasses", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DiagnosticCleanupOptions(new[] { DiagnosticCleanupCategory.Naming }, 0)).ParamName);
        Assert.AreEqual("maxPasses", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DiagnosticCleanupOptions(new[] { DiagnosticCleanupCategory.Naming }, -1)).ParamName);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void Options_NormalizeCategoriesAndCopyTheOverrides()
    {
        Dictionary<string, string> overrides = new Dictionary<string, string> { ["end_of_line"] = "lf" };

        DiagnosticCleanupOptions options = new DiagnosticCleanupOptions(
            new[] { DiagnosticCleanupCategory.Naming, DiagnosticCleanupCategory.Formatting, DiagnosticCleanupCategory.Naming },
            1,
            overrides);
        overrides["end_of_line"] = "crlf";

        Assert.AreSequenceEqual(new[] { DiagnosticCleanupCategory.Formatting, DiagnosticCleanupCategory.Naming }.OrderBy(category => category).ToArray(), options.EnabledCategories.ToArray());
        Assert.AreEqual(1, options.MaxPasses);
        Assert.IsFalse(options.IsEmpty);
        Assert.AreEqual("lf", options.AnalyzerConfigOverrides["end_of_line"]);
        Assert.IsEmpty(new DiagnosticCleanupOptions(Array.Empty<DiagnosticCleanupCategory>()).AnalyzerConfigOverrides);
        Assert.IsTrue(new DiagnosticCleanupOptions(Array.Empty<DiagnosticCleanupCategory>()).IsEmpty);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(0, "originalSolution")]
    [DataRow(1, "changedSolution")]
    [DataRow(2, "appliedFixes")]
    [DataRow(3, "unresolved")]
    [DataRow(4, "postApplyOperations")]
    public void Result_MissingArgument_IsRejected(int missingArgument, string expectedParameter)
    {
        using AdhocWorkspace workspace = new AdhocWorkspace();
        Solution solution = workspace.CurrentSolution;

        ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() => new DiagnosticCleanupResult(
            missingArgument == 0 ? null : solution,
            missingArgument == 1 ? null : solution,
            missingArgument == 2 ? null : Array.Empty<AppliedDiagnosticFix>(),
            missingArgument == 3 ? null : Array.Empty<UnresolvedDiagnostic>(),
            missingArgument == 4 ? null : Array.Empty<CodeActionOperation>()));

        Assert.AreEqual(expectedParameter, exception.ParamName);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    [DataRow(UnresolvedDiagnosticReason.NoCodeFixProvider, true)]
    [DataRow(UnresolvedDiagnosticReason.NoApplicableCodeAction, true)]
    [DataRow(UnresolvedDiagnosticReason.FixRejectedIntroducesCompilerErrors, false)]
    [DataRow(UnresolvedDiagnosticReason.FixRejectedUnsupportedChanges, false)]
    [DataRow(UnresolvedDiagnosticReason.NotConverged, false)]
    public void Result_IsComplete_OnlyWhenNoUnresolvedDiagnosticBlocks(UnresolvedDiagnosticReason reason, bool expectComplete)
    {
        using AdhocWorkspace workspace = new AdhocWorkspace();
        UnresolvedDiagnostic unresolved = new UnresolvedDiagnostic("CJT0300", DiagnosticCleanupCategory.CodeStyle, DiagnosticSeverity.Warning, "A.cs", 1, "Message", reason);

        DiagnosticCleanupResult result = new DiagnosticCleanupResult(
            workspace.CurrentSolution,
            workspace.CurrentSolution,
            Array.Empty<AppliedDiagnosticFix>(),
            new[] { unresolved },
            Array.Empty<CodeActionOperation>());

        Assert.AreEqual(expectComplete, result.IsComplete);
        Assert.IsFalse(result.HasChanges);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void UnresolvedDiagnostic_NormalizesMissingTextAndFormatsALogLine()
    {
        UnresolvedDiagnostic withoutText = new UnresolvedDiagnostic("IDE1006", DiagnosticCleanupCategory.Naming, DiagnosticSeverity.Info, null, 0, null, UnresolvedDiagnosticReason.NotConverged);
        UnresolvedDiagnostic full = new UnresolvedDiagnostic(
            "IDE0055",
            DiagnosticCleanupCategory.Formatting,
            DiagnosticSeverity.Warning,
            @"C:\repo\Program.cs",
            12,
            "Fix formatting",
            UnresolvedDiagnosticReason.FixRejectedIntroducesCompilerErrors);

        Assert.AreEqual(string.Empty, withoutText.FilePath);
        Assert.AreEqual(string.Empty, withoutText.Message);
        Assert.AreEqual(@"C:\repo\Program.cs(12): Warning IDE0055 [Formatting] FixRejectedIntroducesCompilerErrors: Fix formatting", full.ToString());
        Assert.AreEqual("diagnosticId", Assert.ThrowsExactly<ArgumentNullException>(
            () => new UnresolvedDiagnostic(null, DiagnosticCleanupCategory.Naming, DiagnosticSeverity.Info, "A.cs", 1, "M", UnresolvedDiagnosticReason.NotConverged)).ParamName);
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void AppliedDiagnosticFix_RequiresIdAndProviderAndFormatsALogLine()
    {
        AppliedDiagnosticFix fix = new AppliedDiagnosticFix("IDE1006", DiagnosticCleanupCategory.Naming, "NamingStyleCodeFixProvider", 3);

        Assert.AreEqual("NamingStyleCodeFixProvider", fix.ProviderName);
        Assert.AreEqual("IDE1006 (Naming) fixed 3x by NamingStyleCodeFixProvider", fix.ToString());
        Assert.AreEqual("diagnosticId", Assert.ThrowsExactly<ArgumentNullException>(() => new AppliedDiagnosticFix(null, DiagnosticCleanupCategory.Naming, "P", 1)).ParamName);
        Assert.AreEqual("providerName", Assert.ThrowsExactly<ArgumentNullException>(() => new AppliedDiagnosticFix("IDE1006", DiagnosticCleanupCategory.Naming, null, 1)).ParamName);
    }
}
