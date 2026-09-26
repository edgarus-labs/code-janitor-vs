using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace CodeJanitor.Logic.Cleaning.Diagnostics;

/// <summary>
/// Fixes the Roslyn diagnostics of one document with the existing analyzers and code fix providers. Which rules are
/// active, and how they are configured, comes from .editorconfig (the project's analyzer config documents); which rule
/// families may be fixed comes from <see cref="DiagnosticCleanupOptions.EnabledCategories" />.
/// </summary>
/// <remarks>
/// <para>
/// Host-agnostic: only the public Roslyn API is used and nothing is applied to a workspace; the caller applies
/// <see cref="DiagnosticCleanupResult.ChangedSolution" />. Rules are never re-implemented, diagnostics are never
/// suppressed, and no diagnostic ID is special-cased.
/// </para>
/// <para>
/// A diagnostic is actionable when it is located in the document, is not suppressed, is reported with at least Info
/// severity (hidden/silent rules and rules configured as none stay untouched) and its category is enabled. Severities
/// are the ones Roslyn reports for the project's analyzer config: <c>dotnet_diagnostic.&lt;id&gt;.severity</c>, the
/// <c>option = value:severity</c> suffix, or a naming rule's severity. The document is analyzed like the IDE analyzes
/// an open document (syntax and semantic analysis of its tree), so diagnostics that analyzers only report at
/// compilation end are not seen.
/// </para>
/// <para>
/// Each pass analyzes the document, picks the fix of every actionable diagnostic (the first top-level action without
/// nested actions, from the first provider in catalog order that offers one) and applies the first group of diagnostics
/// that share provider and equivalence key: through the provider's fix-all provider when it supports document scope,
/// otherwise for the first diagnostic of the group only. When the provider also offers other actions with the same
/// equivalence key, the batch fixer is used with only the chosen action of each diagnostic. A fix is accepted only when its operations contain
/// exactly one solution change, that change merely changes document texts, and it adds no compiler error to any changed
/// project. Other operations (host/UI notifications such as Visual Studio's symbol-renamed notification)
/// are never executed by the engine: those of accepted fixes are handed to the host in
/// <see cref="DiagnosticCleanupResult.PostApplyOperations" />. A rejected group is not retried in the same run. Passes
/// repeat until no actionable diagnostic can make progress or <see cref="DiagnosticCleanupOptions.MaxPasses" /> fixes
/// were applied.
/// </para>
/// <para>
/// Exceptions thrown by code fix providers propagate to the caller. Analyzer failures are reported by Roslyn without a
/// source location, so they never become actionable.
/// </para>
/// </remarks>
public sealed class DiagnosticCleanupEngine
{
    private static readonly ConditionalWeakTable<DiagnosticAnalyzer, AnalyzerCategories> s_analyzerCategories =
        new ConditionalWeakTable<DiagnosticAnalyzer, AnalyzerCategories>();

    private readonly CodeFixProviderCatalog _catalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticCleanupEngine" /> class.
    /// </summary>
    /// <param name="catalog">The source of the code fix providers.</param>
    public DiagnosticCleanupEngine(CodeFixProviderCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <summary>
    /// Fixes the actionable diagnostics located in <paramref name="document" />. Fixes may edit other documents too
    /// (e.g. a rename updates references), but only diagnostics of <paramref name="document" /> are targeted.
    /// </summary>
    /// <param name="document">The document to clean.</param>
    /// <param name="options">The enabled categories and the pass limit.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The changed solution with applied fixes and the diagnostics left unresolved.</returns>
    public async Task<DiagnosticCleanupResult> CleanupAsync(Document document, DiagnosticCleanupOptions options, CancellationToken cancellationToken)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var project = document.Project;
        var solution = project.Solution;
        if (options.IsEmpty)
        {
            return CreateUnchangedResult(solution);
        }

        var analyzers = GetAnalyzers(project, options);

        // Suppressors alone cannot report anything actionable: no analyzer of an enabled category means no analysis.
        if (analyzers.All(analyzer => analyzer is DiagnosticSuppressor))
        {
            return CreateUnchangedResult(solution);
        }

        var run = new CleanupRun(document.Id, options, analyzers, new Lazy<ILookup<string, CodeFixProvider>>(() => IndexByDiagnosticId(_catalog.GetProviders(project))));

        return await run.ExecuteAsync(solution, cancellationToken).ConfigureAwait(false);
    }

    private static DiagnosticCleanupResult CreateUnchangedResult(Solution solution) =>
        new DiagnosticCleanupResult(solution, solution, Array.Empty<AppliedDiagnosticFix>(), Array.Empty<UnresolvedDiagnostic>(), Array.Empty<CodeActionOperation>());

    /// <summary>
    /// Gets the analyzers of the project and solution analyzer references (the latter are the host analyzers, e.g.
    /// the built-in IDE analyzers in Visual Studio), deduplicated by type with project analyzers taking precedence.
    /// Analyzers that cannot report a diagnostic of an enabled category are skipped, which does not change the result;
    /// diagnostic suppressors always run so suppressed diagnostics stay suppressed.
    /// </summary>
    private static ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(Project project, DiagnosticCleanupOptions options)
    {
        var analyzerTypes = new HashSet<string>(StringComparer.Ordinal);
        var analyzers = ImmutableArray.CreateBuilder<DiagnosticAnalyzer>();

        foreach (var reference in project.AnalyzerReferences.Concat(project.Solution.AnalyzerReferences))
        {
            foreach (var analyzer in reference.GetAnalyzers(project.Language))
            {
                if (analyzerTypes.Add(analyzer.GetType().FullName)
                    && (analyzer is DiagnosticSuppressor || s_analyzerCategories.GetValue(analyzer, ClassifySupportedDiagnostics).Categories.Any(options.IsEnabled)))
                {
                    analyzers.Add(analyzer);
                }
            }
        }

        return analyzers.ToImmutable();
    }

    private static AnalyzerCategories ClassifySupportedDiagnostics(DiagnosticAnalyzer analyzer)
    {
        ImmutableArray<DiagnosticDescriptor> descriptors;
        try
        {
            descriptors = analyzer.SupportedDiagnostics;
        }
        catch (Exception)
        {
            // A broken analyzer whose descriptors cannot be obtained cannot report a valid diagnostic (Roslyn rejects
            // diagnostics outside SupportedDiagnostics), so it is skipped; hosts surface the analyzer failure themselves.
            descriptors = ImmutableArray<DiagnosticDescriptor>.Empty;
        }

        return new AnalyzerCategories(descriptors.IsDefault
            ? ImmutableArray<DiagnosticCleanupCategory>.Empty
            : descriptors
                .Where(descriptor => descriptor != null)
                .Select(descriptor => DiagnosticCleanupCategoryClassifier.Classify(analyzer, descriptor))
                .Where(category => category.HasValue)
                .Select(category => category.Value)
                .Distinct()
                .ToImmutableArray());
    }

    private static ILookup<string, CodeFixProvider> IndexByDiagnosticId(ImmutableArray<CodeFixProvider> providers) =>
        providers
            .SelectMany(provider => (provider.FixableDiagnosticIds.IsDefault ? ImmutableArray<string>.Empty : provider.FixableDiagnosticIds)
                .Distinct(StringComparer.Ordinal)
                .Select(id => new KeyValuePair<string, CodeFixProvider>(id, provider)))
            .ToLookup(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    /// <summary>
    /// State of one <see cref="CleanupAsync" /> call.
    /// </summary>
    private sealed class CleanupRun
    {
        private readonly DocumentId _documentId;
        private readonly DiagnosticCleanupOptions _options;
        private readonly ImmutableArray<DiagnosticAnalyzer> _analyzers;
        private readonly Lazy<ILookup<string, CodeFixProvider>> _providersByDiagnosticId;
        private readonly Dictionary<(CodeFixProvider Provider, string EquivalenceKey), UnresolvedDiagnosticReason> _rejectedGroups =
            new Dictionary<(CodeFixProvider Provider, string EquivalenceKey), UnresolvedDiagnosticReason>();

        private readonly List<(string DiagnosticId, DiagnosticCleanupCategory Category, CodeFixProvider Provider)> _appliedOrder =
            new List<(string DiagnosticId, DiagnosticCleanupCategory Category, CodeFixProvider Provider)>();

        private readonly Dictionary<(string DiagnosticId, DiagnosticCleanupCategory Category, CodeFixProvider Provider), int> _appliedCounts =
            new Dictionary<(string DiagnosticId, DiagnosticCleanupCategory Category, CodeFixProvider Provider), int>();

        private readonly List<CodeActionOperation> _postApplyOperations = new List<CodeActionOperation>();

        private Solution _errorSolution;
        private Dictionary<ProjectId, IReadOnlyList<Diagnostic>> _errors = new Dictionary<ProjectId, IReadOnlyList<Diagnostic>>();

        public CleanupRun(
            DocumentId documentId,
            DiagnosticCleanupOptions options,
            ImmutableArray<DiagnosticAnalyzer> analyzers,
            Lazy<ILookup<string, CodeFixProvider>> providersByDiagnosticId)
        {
            _documentId = documentId;
            _options = options;
            _analyzers = analyzers;
            _providersByDiagnosticId = providersByDiagnosticId;
        }

        public async Task<DiagnosticCleanupResult> ExecuteAsync(Solution originalSolution, CancellationToken cancellationToken)
        {
            var solution = originalSolution;

            for (var pass = 0; ; pass++)
            {
                var document = solution.GetDocument(_documentId);
                var plans = await PlanFixesAsync(document, cancellationToken).ConfigureAwait(false);
                if (plans.IsEmpty || pass == _options.MaxPasses)
                {
                    return CreateResult(originalSolution, solution, plans);
                }

                var fixedSolution = await ApplyFirstAcceptedGroupAsync(solution, document, plans, cancellationToken).ConfigureAwait(false);
                if (fixedSolution == null)
                {
                    return CreateResult(originalSolution, solution, plans);
                }

                solution = fixedSolution;
            }
        }

        private async Task<ImmutableArray<FixPlan>> PlanFixesAsync(Document document, CancellationToken cancellationToken)
        {
            var diagnostics = await AnalyzeAsync(document, cancellationToken).ConfigureAwait(false);
            var plans = ImmutableArray.CreateBuilder<FixPlan>(diagnostics.Length);

            foreach (var diagnostic in diagnostics)
            {
                plans.Add(await PlanFixAsync(document, diagnostic, cancellationToken).ConfigureAwait(false));
            }

            return plans.MoveToImmutable();
        }

        /// <summary>
        /// Returns the actionable diagnostics of the document sorted by (span start, id), with deterministic tie-breaks.
        /// </summary>
        private async Task<ImmutableArray<ActionableDiagnostic>> AnalyzeAsync(Document document, CancellationToken cancellationToken)
        {
            var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            var compilation = await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (tree == null || compilation == null)
            {
                return ImmutableArray<ActionableDiagnostic>.Empty;
            }

            var diagnostics = await GetDiagnosticsAsync(compilation, document.Project.AnalyzerOptions, tree, cancellationToken).ConfigureAwait(false);
            var configOptions = document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(tree);
            var treeOptions = compilation.Options.SyntaxTreeOptionsProvider;
            HashSet<(string Id, TextSpan Span, DiagnosticSeverity Severity)> defaultDiagnostics = null;
            var reported = new HashSet<Diagnostic>();
            var actionable = new List<ActionableDiagnostic>();

            foreach (var (analyzer, diagnostic) in diagnostics)
            {
                if (diagnostic.Location.SourceTree != tree
                    || diagnostic.IsSuppressed
                    || diagnostic.Severity < DiagnosticSeverity.Info
                    || !reported.Add(diagnostic))
                {
                    continue;
                }

                var category = DiagnosticCleanupCategoryClassifier.Classify(analyzer, diagnostic.Descriptor);
                if (!category.HasValue || !_options.IsEnabled(category.Value))
                {
                    continue;
                }

                if (!IsSeverityConfigured(diagnostic.Descriptor, tree, configOptions, treeOptions, cancellationToken)
                    && !(category == DiagnosticCleanupCategory.Naming && configOptions.Keys.Any(key => key.StartsWith("dotnet_naming_rule.", StringComparison.Ordinal))))
                {
                    defaultDiagnostics ??= await GetDiagnosticsWithoutEditorConfigAsync(document, cancellationToken).ConfigureAwait(false);
                    if (defaultDiagnostics.Contains((diagnostic.Id, diagnostic.Location.SourceSpan, diagnostic.Severity)))
                    {
                        continue;
                    }
                }

                actionable.Add(new ActionableDiagnostic(diagnostic, category.Value));
            }

            return actionable
                .OrderBy(item => item.Diagnostic.Location.SourceSpan.Start)
                .ThenBy(item => item.Diagnostic.Id, StringComparer.Ordinal)
                .ThenBy(item => item.Diagnostic.Location.SourceSpan.End)
                .ThenBy(item => item.Diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparer.Ordinal)
                .ToImmutableArray();
        }

        private async Task<List<(DiagnosticAnalyzer Analyzer, Diagnostic Diagnostic)>> GetDiagnosticsAsync(
            Compilation compilation,
            AnalyzerOptions analyzerOptions,
            SyntaxTree tree,
            CancellationToken cancellationToken)
        {
            var analysisOptions = new CompilationWithAnalyzersOptions(
                analyzerOptions,
                onAnalyzerException: null,
                concurrentAnalysis: false,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: false);
            var compilationWithAnalyzers = compilation.WithAnalyzers(_analyzers, analysisOptions);
            var syntaxResult = await compilationWithAnalyzers.GetAnalysisResultAsync(tree, cancellationToken).ConfigureAwait(false);
            var semanticModel = compilationWithAnalyzers.Compilation.GetSemanticModel(tree);
            var semanticResult = await compilationWithAnalyzers.GetAnalysisResultAsync(semanticModel, null, cancellationToken).ConfigureAwait(false);

            return new[] { syntaxResult, semanticResult }
                .SelectMany(result => result.Analyzers.SelectMany(analyzer => result.GetAllDiagnostics(analyzer).Select(diagnostic => (analyzer, diagnostic))))
                .ToList();
        }

        private async Task<HashSet<(string Id, TextSpan Span, DiagnosticSeverity Severity)>> GetDiagnosticsWithoutEditorConfigAsync(
            Document document,
            CancellationToken cancellationToken)
        {
            var editorConfigIds = document.Project.AnalyzerConfigDocuments
                .Where(config => string.Equals(Path.GetFileName(config.FilePath), ".editorconfig", StringComparison.OrdinalIgnoreCase))
                .Select(config => config.Id)
                .ToImmutableArray();
            var project = document.Project.Solution.RemoveAnalyzerConfigDocuments(editorConfigIds).GetProject(document.Project.Id);
            var tree = await project.GetDocument(document.Id).GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            var diagnostics = await GetDiagnosticsAsync(compilation, project.AnalyzerOptions, tree, cancellationToken).ConfigureAwait(false);

            return new HashSet<(string, TextSpan, DiagnosticSeverity)>(
                diagnostics
                    .Where(item => item.Diagnostic.Location.SourceTree == tree)
                    .Select(item => (item.Diagnostic.Id, item.Diagnostic.Location.SourceSpan, item.Diagnostic.Severity)));
        }

        private static bool IsSeverityConfigured(
            DiagnosticDescriptor descriptor,
            SyntaxTree tree,
            AnalyzerConfigOptions configOptions,
            SyntaxTreeOptionsProvider treeOptions,
            CancellationToken cancellationToken) =>
            treeOptions?.TryGetDiagnosticValue(tree, descriptor.Id, cancellationToken, out _) == true
            || treeOptions?.TryGetGlobalDiagnosticValue(descriptor.Id, cancellationToken, out _) == true
            || configOptions.TryGetValue($"dotnet_analyzer_diagnostic.category-{descriptor.Category}.severity", out _)
            || configOptions.TryGetValue("dotnet_analyzer_diagnostic.severity", out _);

        private async Task<FixPlan> PlanFixAsync(Document document, ActionableDiagnostic actionable, CancellationToken cancellationToken)
        {
            var providers = _providersByDiagnosticId.Value[actionable.Diagnostic.Id].ToList();
            if (providers.Count == 0)
            {
                return FixPlan.Unfixable(actionable, UnresolvedDiagnosticReason.NoCodeFixProvider);
            }

            foreach (var provider in providers)
            {
                var chosen = await GetFirstApplicableActionAsync(document, provider, actionable.Diagnostic, cancellationToken).ConfigureAwait(false);
                if (chosen.Action != null)
                {
                    return FixPlan.Fixable(actionable, provider, chosen.Action, chosen.HasEquivalentAlternatives);
                }
            }

            return FixPlan.Unfixable(actionable, UnresolvedDiagnosticReason.NoApplicableCodeAction);
        }

        /// <summary>
        /// Gets the first top-level action registered by <paramref name="provider" /> that has no nested actions;
        /// nested actions are choices for a user and are never picked automatically. Also tells whether the provider
        /// registered other actions with the same equivalence key.
        /// </summary>
        private static async Task<(CodeAction Action, bool HasEquivalentAlternatives)> GetFirstApplicableActionAsync(Document document, CodeFixProvider provider, Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            var actions = new List<CodeAction>();
            var context = new CodeFixContext(
                document,
                diagnostic,
                (action, _) =>
                {
                    lock (actions)
                    {
                        actions.Add(action);
                    }
                },
                cancellationToken);

            await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);

            lock (actions)
            {
                var chosen = actions.FirstOrDefault(action => action.NestedActions.IsDefaultOrEmpty);
                var hasEquivalentAlternatives = chosen != null
                    && actions.Any(action => action != chosen && string.Equals(action.EquivalenceKey, chosen.EquivalenceKey, StringComparison.Ordinal));

                return (chosen, hasEquivalentAlternatives);
            }
        }

        private async Task<Solution> ApplyFirstAcceptedGroupAsync(Solution solution, Document document, ImmutableArray<FixPlan> plans, CancellationToken cancellationToken)
        {
            foreach (var group in plans.Where(plan => plan.IsFixable).GroupBy(plan => plan.GroupKey))
            {
                if (_rejectedGroups.ContainsKey(group.Key))
                {
                    continue;
                }

                var attempt = await TryApplyGroupAsync(solution, document, group.ToImmutableArray(), cancellationToken).ConfigureAwait(false);
                if (attempt.Rejection.HasValue)
                {
                    _rejectedGroups.Add(group.Key, attempt.Rejection.Value);
                    continue;
                }

                RecordAppliedFixes(attempt.FixedDiagnostics, group.Key.Provider);
                _postApplyOperations.AddRange(attempt.PostApplyOperations);

                return attempt.Solution;
            }

            return null;
        }

        private async Task<FixAttempt> TryApplyGroupAsync(Solution solution, Document document, ImmutableArray<FixPlan> group, CancellationToken cancellationToken)
        {
            var first = group[0];
            var action = first.Action;
            var fixedDiagnostics = ImmutableArray.Create(first.Actionable);

            var hasAlternatives = group.Any(plan => plan.HasEquivalentAlternatives);
            var fixAllProvider = hasAlternatives ? WellKnownFixAllProviders.BatchFixer : first.Provider.GetFixAllProvider();
            if (fixAllProvider != null && fixAllProvider.GetSupportedFixAllScopes().Contains(FixAllScope.Document))
            {
                var diagnostics = group.Select(plan => plan.Actionable.Diagnostic).ToImmutableArray();
                var fixAllContext = new FixAllContext(
                    document,
                    hasAlternatives ? new ChosenActionCodeFixProvider(first.Provider, group) : first.Provider,
                    FixAllScope.Document,
                    first.Action.EquivalenceKey,
                    diagnostics.Select(diagnostic => diagnostic.Id).Distinct(StringComparer.Ordinal),
                    new GroupDiagnosticProvider(document.Id, diagnostics),
                    cancellationToken);
                var fixAllAction = await fixAllProvider.GetFixAsync(fixAllContext).ConfigureAwait(false);

                // A fix-all provider may decline; the provider's own action for the first diagnostic still applies.
                if (fixAllAction != null)
                {
                    action = fixAllAction;
                    fixedDiagnostics = group.Select(plan => plan.Actionable).ToImmutableArray();
                }
            }

            var operations = await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            if (operations.IsDefaultOrEmpty)
            {
                return FixAttempt.Rejected(UnresolvedDiagnosticReason.NoApplicableCodeAction);
            }

            // Exactly one solution change is required (Roslyn allows at most one per action). Other operations are
            // host/UI notifications, e.g. Visual Studio's symbol-renamed notification next to a rename: the engine never
            // executes them, it hands those of accepted fixes to the host (DiagnosticCleanupResult.PostApplyOperations).
            // Anything that would need them to complete the fix is still caught by the text-only validation and the
            // compiler-error gate below.
            var applyChangesOperations = operations.OfType<ApplyChangesOperation>().ToList();
            if (applyChangesOperations.Count != 1)
            {
                return FixAttempt.Rejected(UnresolvedDiagnosticReason.FixRejectedUnsupportedChanges);
            }

            var changedTexts = await GetChangedDocumentTextsAsync(solution, applyChangesOperations[0].ChangedSolution, cancellationToken).ConfigureAwait(false);
            if (changedTexts.IsDefault)
            {
                return FixAttempt.Rejected(UnresolvedDiagnosticReason.FixRejectedUnsupportedChanges);
            }

            if (changedTexts.IsEmpty)
            {
                return FixAttempt.Rejected(UnresolvedDiagnosticReason.NoApplicableCodeAction);
            }

            var candidate = solution;
            foreach (var changedText in changedTexts)
            {
                candidate = candidate.WithDocumentText(changedText.Key, changedText.Value);
            }

            var changedProjectIds = changedTexts.Select(changedText => changedText.Key.ProjectId).Distinct();
            if (await IntroducesCompilerErrorsAsync(solution, candidate, changedProjectIds, cancellationToken).ConfigureAwait(false))
            {
                return FixAttempt.Rejected(UnresolvedDiagnosticReason.FixRejectedIntroducesCompilerErrors);
            }

            var postApplyOperations = operations.Where(operation => !(operation is ApplyChangesOperation)).ToImmutableArray();

            return FixAttempt.Accepted(candidate, fixedDiagnostics, postApplyOperations);
        }

        /// <summary>
        /// Returns the new texts of the documents changed by <paramref name="changedSolution" />, empty when nothing
        /// changed, or a default array when the change is more than document texts (documents, projects, references,
        /// options or document attributes added, removed or changed).
        /// </summary>
        private static async Task<ImmutableArray<KeyValuePair<DocumentId, SourceText>>> GetChangedDocumentTextsAsync(
            Solution solution,
            Solution changedSolution,
            CancellationToken cancellationToken)
        {
            var solutionChanges = changedSolution.GetChanges(solution);
            if (solutionChanges.GetAddedProjects().Any()
                || solutionChanges.GetRemovedProjects().Any()
                || solutionChanges.GetAddedAnalyzerReferences().Any()
                || solutionChanges.GetRemovedAnalyzerReferences().Any())
            {
                return default;
            }

            var changedTexts = ImmutableArray.CreateBuilder<KeyValuePair<DocumentId, SourceText>>();

            foreach (var projectChanges in solutionChanges.GetProjectChanges())
            {
                if (ChangesMoreThanDocumentTexts(projectChanges))
                {
                    return default;
                }

                foreach (var documentId in projectChanges.GetChangedDocuments())
                {
                    var oldDocument = projectChanges.OldProject.GetDocument(documentId);
                    var newDocument = projectChanges.NewProject.GetDocument(documentId);
                    if (!HaveSameAttributes(oldDocument, newDocument))
                    {
                        return default;
                    }

                    var oldText = await oldDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
                    var newText = await newDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
                    if (!oldText.ContentEquals(newText))
                    {
                        changedTexts.Add(new KeyValuePair<DocumentId, SourceText>(documentId, newText));
                    }
                }
            }

            return changedTexts.ToImmutable();
        }

        private static bool ChangesMoreThanDocumentTexts(ProjectChanges changes)
        {
            var oldProject = changes.OldProject;
            var newProject = changes.NewProject;

            return changes.GetAddedDocuments().Any()
                || changes.GetRemovedDocuments().Any()
                || changes.GetAddedAdditionalDocuments().Any()
                || changes.GetRemovedAdditionalDocuments().Any()
                || changes.GetChangedAdditionalDocuments().Any()
                || changes.GetAddedAnalyzerConfigDocuments().Any()
                || changes.GetRemovedAnalyzerConfigDocuments().Any()
                || changes.GetChangedAnalyzerConfigDocuments().Any()
                || changes.GetAddedMetadataReferences().Any()
                || changes.GetRemovedMetadataReferences().Any()
                || changes.GetAddedProjectReferences().Any()
                || changes.GetRemovedProjectReferences().Any()
                || changes.GetAddedAnalyzerReferences().Any()
                || changes.GetRemovedAnalyzerReferences().Any()
                || !Equals(oldProject.CompilationOptions, newProject.CompilationOptions)
                || !Equals(oldProject.ParseOptions, newProject.ParseOptions)
                || !string.Equals(oldProject.Name, newProject.Name, StringComparison.Ordinal)
                || !string.Equals(oldProject.AssemblyName, newProject.AssemblyName, StringComparison.Ordinal)
                || !string.Equals(oldProject.FilePath, newProject.FilePath, StringComparison.Ordinal)
                || !string.Equals(oldProject.OutputFilePath, newProject.OutputFilePath, StringComparison.Ordinal)
                || !string.Equals(oldProject.DefaultNamespace, newProject.DefaultNamespace, StringComparison.Ordinal);
        }

        private static bool HaveSameAttributes(Document oldDocument, Document newDocument) =>
            string.Equals(oldDocument.Name, newDocument.Name, StringComparison.Ordinal)
            && string.Equals(oldDocument.FilePath, newDocument.FilePath, StringComparison.Ordinal)
            && oldDocument.SourceCodeKind == newDocument.SourceCodeKind
            && oldDocument.Folders.SequenceEqual(newDocument.Folders, StringComparer.Ordinal);

        /// <summary>
        /// Safety gate: compares the Error-severity compiler diagnostics of every changed project before and after the
        /// change (see <see cref="CompilerErrors" />), so a fix that removes one error but adds a different one is still
        /// rejected. Passing the gate is the last acceptance check, so the errors of the candidate are kept as the
        /// errors of the next current solution.
        /// </summary>
        private async Task<bool> IntroducesCompilerErrorsAsync(Solution solution, Solution candidate, IEnumerable<ProjectId> projectIds, CancellationToken cancellationToken)
        {
            if (!ReferenceEquals(_errorSolution, solution))
            {
                _errorSolution = solution;
                _errors = new Dictionary<ProjectId, IReadOnlyList<Diagnostic>>();
            }

            var candidateErrors = new Dictionary<ProjectId, IReadOnlyList<Diagnostic>>();

            foreach (var projectId in projectIds)
            {
                var project = solution.GetProject(projectId);
                if (!_errors.TryGetValue(projectId, out var before))
                {
                    before = await CompilerErrors.GetAsync(project, cancellationToken).ConfigureAwait(false);
                    _errors.Add(projectId, before);
                }

                var candidateProject = candidate.GetProject(projectId);
                var after = await CompilerErrors.GetAsync(candidateProject, cancellationToken).ConfigureAwait(false);
                if (await CompilerErrors.FindFirstNewAsync(project, before, candidateProject, after, cancellationToken).ConfigureAwait(false) is not null)
                {
                    return true;
                }

                candidateErrors.Add(projectId, after);
            }

            _errorSolution = candidate;
            _errors = candidateErrors;

            return false;
        }

        private void RecordAppliedFixes(IEnumerable<ActionableDiagnostic> fixedDiagnostics, CodeFixProvider provider)
        {
            foreach (var fixedDiagnostic in fixedDiagnostics)
            {
                var key = (fixedDiagnostic.Diagnostic.Id, fixedDiagnostic.Category, provider);
                if (_appliedCounts.TryGetValue(key, out var count))
                {
                    _appliedCounts[key] = count + 1;
                }
                else
                {
                    _appliedOrder.Add(key);
                    _appliedCounts.Add(key, 1);
                }
            }
        }

        private DiagnosticCleanupResult CreateResult(Solution originalSolution, Solution solution, ImmutableArray<FixPlan> remainingPlans)
        {
            var appliedFixes = _appliedOrder.Select(key => new AppliedDiagnosticFix(key.DiagnosticId, key.Category, key.Provider.GetType().Name, _appliedCounts[key]));
            var unresolved = remainingPlans.Select(plan => CreateUnresolved(plan.Actionable, GetUnresolvedReason(plan)));

            return new DiagnosticCleanupResult(originalSolution, solution, appliedFixes, unresolved, _postApplyOperations);
        }

        /// <summary>
        /// A remaining diagnostic without usable fix keeps its planning reason, one of a rejected group gets the
        /// rejection reason, and any other one was still fixable when the pass limit was reached.
        /// </summary>
        private UnresolvedDiagnosticReason GetUnresolvedReason(FixPlan plan)
        {
            if (plan.UnfixableReason.HasValue)
            {
                return plan.UnfixableReason.Value;
            }

            return _rejectedGroups.TryGetValue(plan.GroupKey, out var rejection) ? rejection : UnresolvedDiagnosticReason.NotConverged;
        }

        private static UnresolvedDiagnostic CreateUnresolved(ActionableDiagnostic actionable, UnresolvedDiagnosticReason reason)
        {
            var diagnostic = actionable.Diagnostic;
            var lineSpan = diagnostic.Location.GetLineSpan();

            return new UnresolvedDiagnostic(
                diagnostic.Id,
                actionable.Category,
                diagnostic.Severity,
                lineSpan.Path,
                lineSpan.StartLinePosition.Line + 1,
                diagnostic.GetMessage(CultureInfo.CurrentCulture),
                reason);
        }
    }

    private sealed class ChosenActionCodeFixProvider : CodeFixProvider
    {
        private readonly CodeFixProvider _provider;
        private readonly Dictionary<Diagnostic, CodeAction> _chosenActions;

        public ChosenActionCodeFixProvider(CodeFixProvider provider, IEnumerable<FixPlan> plans)
        {
            _provider = provider;
            _chosenActions = plans.ToDictionary(plan => plan.Actionable.Diagnostic, plan => plan.Action);
        }

        public override ImmutableArray<string> FixableDiagnosticIds => _provider.FixableDiagnosticIds;

        public override FixAllProvider GetFixAllProvider() => null;

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            foreach (var diagnostic in context.Diagnostics)
            {
                if (_chosenActions.TryGetValue(diagnostic, out var action))
                {
                    context.RegisterCodeFix(action, diagnostic);
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ActionableDiagnostic
    {
        public ActionableDiagnostic(Diagnostic diagnostic, DiagnosticCleanupCategory category)
        {
            Diagnostic = diagnostic;
            Category = category;
        }

        public Diagnostic Diagnostic { get; }

        public DiagnosticCleanupCategory Category { get; }
    }

    /// <summary>
    /// The fix chosen for one actionable diagnostic, or why there is none.
    /// </summary>
    private sealed class FixPlan
    {
        private FixPlan(ActionableDiagnostic actionable, CodeFixProvider provider, CodeAction action, bool hasEquivalentAlternatives, UnresolvedDiagnosticReason? unfixableReason)
        {
            Actionable = actionable;
            Provider = provider;
            Action = action;
            HasEquivalentAlternatives = hasEquivalentAlternatives;
            UnfixableReason = unfixableReason;
        }

        public ActionableDiagnostic Actionable { get; }

        public CodeFixProvider Provider { get; }

        public CodeAction Action { get; }

        /// <summary>
        /// Gets a value indicating whether the provider also registered other actions with the equivalence key of
        /// <see cref="Action" />.
        /// </summary>
        public bool HasEquivalentAlternatives { get; }

        public UnresolvedDiagnosticReason? UnfixableReason { get; }

        public bool IsFixable => Action != null;

        public (CodeFixProvider Provider, string EquivalenceKey) GroupKey => (Provider, Action?.EquivalenceKey);

        public static FixPlan Fixable(ActionableDiagnostic actionable, CodeFixProvider provider, CodeAction action, bool hasEquivalentAlternatives) =>
            new FixPlan(actionable, provider, action, hasEquivalentAlternatives, null);

        public static FixPlan Unfixable(ActionableDiagnostic actionable, UnresolvedDiagnosticReason reason) =>
            new FixPlan(actionable, null, null, false, reason);
    }

    private sealed class FixAttempt
    {
        private FixAttempt(
            Solution solution,
            ImmutableArray<ActionableDiagnostic> fixedDiagnostics,
            ImmutableArray<CodeActionOperation> postApplyOperations,
            UnresolvedDiagnosticReason? rejection)
        {
            Solution = solution;
            FixedDiagnostics = fixedDiagnostics;
            PostApplyOperations = postApplyOperations;
            Rejection = rejection;
        }

        public Solution Solution { get; }

        public ImmutableArray<ActionableDiagnostic> FixedDiagnostics { get; }

        public ImmutableArray<CodeActionOperation> PostApplyOperations { get; }

        public UnresolvedDiagnosticReason? Rejection { get; }

        public static FixAttempt Accepted(
            Solution solution,
            ImmutableArray<ActionableDiagnostic> fixedDiagnostics,
            ImmutableArray<CodeActionOperation> postApplyOperations) =>
            new FixAttempt(solution, fixedDiagnostics, postApplyOperations, null);

        public static FixAttempt Rejected(UnresolvedDiagnosticReason reason) =>
            new FixAttempt(null, ImmutableArray<ActionableDiagnostic>.Empty, ImmutableArray<CodeActionOperation>.Empty, reason);
    }

    /// <summary>
    /// Feeds exactly the diagnostics of one fix group to a fix-all provider.
    /// </summary>
    private sealed class GroupDiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        private readonly DocumentId _documentId;
        private readonly ImmutableArray<Diagnostic> _diagnostics;

        public GroupDiagnosticProvider(DocumentId documentId, ImmutableArray<Diagnostic> diagnostics)
        {
            _documentId = documentId;
            _diagnostics = diagnostics;
        }

        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(document.Id == _documentId ? _diagnostics : ImmutableArray<Diagnostic>.Empty);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(ImmutableArray<Diagnostic>.Empty);

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(project.Id == _documentId.ProjectId ? _diagnostics : ImmutableArray<Diagnostic>.Empty);
    }

    /// <summary>
    /// The categories an analyzer can report, cached per analyzer instance.
    /// </summary>
    private sealed class AnalyzerCategories
    {
        public AnalyzerCategories(ImmutableArray<DiagnosticCleanupCategory> categories)
        {
            Categories = categories;
        }

        public ImmutableArray<DiagnosticCleanupCategory> Categories { get; }
    }
}
