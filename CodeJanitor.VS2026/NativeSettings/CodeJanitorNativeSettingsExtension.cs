using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeJanitor.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace CodeJanitor.NativeSettings
{
#pragma warning disable VSEXTPREVIEW_SETTINGS // Settings APIs are preview.
    [VisualStudioContribution]
    internal class CodeJanitorNativeSettingsExtension : Extension, IExtensionInitializer
    {
        // Subscriptions must be kept alive for the extension's lifetime - SubscribeAsync
        // returns an IDisposable "unsubscribe handle" and if it isn't held onto, it becomes
        // eligible for garbage collection almost immediately, silently cancelling the
        // subscription (change handlers then stop firing for live edits made in the
        // Options UI, even though the native settings store itself still saves correctly).
        private readonly System.Collections.Generic.List<IDisposable> _settingSubscriptions = new();

        public override ExtensionConfiguration ExtensionConfiguration => new()
        {
            RequiresInProcessHosting = true,
            // Without an explicit LoadedWhen rule, this extension only activates on-demand
            // (e.g. when one of its Commands is invoked) and InitializeAsync never runs at VS
            // startup - meaning the settings bridge to Properties.Settings.Default never wires
            // up, so live edits made in the Options UI are silently never applied. Load
            // unconditionally, whether or not a solution is open, matching the classic VSSDK
            // "always autoload" pattern (NoSolution | Exists covers every solution state).
            LoadedWhen = ActivationConstraint.SolutionState(SolutionState.NoSolution)
                | ActivationConstraint.SolutionState(SolutionState.Exists),
        };

        protected override void InitializeServices(IServiceCollection serviceCollection)
        {
            base.InitializeServices(serviceCollection);
        }

        public async Task InitializeAsync(ExtensionCore extension, IServiceProvider serviceProvider, VisualStudioExtensibility extensibility, CancellationToken cancellationToken)
        {
            DebugLog("InitializeAsync starting.");
            try
            {
                // Push the user's current settings into the native store first, so the native UI
                // reflects reality instead of the Setting.*'s hardcoded compile-time defaults.
                await PushCurrentSettingsToNativeStoreAsync(extensibility, cancellationToken);
                DebugLog("PushCurrentSettingsToNativeStoreAsync completed.");

                _settingSubscriptions.Add(await extensibility.Settings().SubscribeAsync(GeneralSettings, cancellationToken, changeHandler: OnGeneralSettingsChanged));
                _settingSubscriptions.Add(await extensibility.Settings().SubscribeAsync(SwitchingSettings, cancellationToken, changeHandler: OnSwitchingSettingsChanged));
                _settingSubscriptions.Add(await extensibility.Settings().SubscribeAsync(FeaturesSettings, cancellationToken, changeHandler: OnFeaturesSettingsChanged));
                _settingSubscriptions.Add(await extensibility.Settings().SubscribeAsync(CleaningSettings, cancellationToken, changeHandler: OnCleaningSettingsChanged));
                _settingSubscriptions.Add(await extensibility.Settings().SubscribeAsync(CollapsingSettings, cancellationToken, changeHandler: OnCollapsingSettingsChanged));
                _settingSubscriptions.Add(await extensibility.Settings().SubscribeAsync(DiggingSettings, cancellationToken, changeHandler: OnDiggingSettingsChanged));
                _settingSubscriptions.Add(await extensibility.Settings().SubscribeAsync(FindingSettings, cancellationToken, changeHandler: OnFindingSettingsChanged));
                _settingSubscriptions.Add(await extensibility.Settings().SubscribeAsync(FormattingSettings, cancellationToken, changeHandler: OnFormattingSettingsChanged));
                _settingSubscriptions.Add(await extensibility.Settings().SubscribeAsync(ProgressingSettings, cancellationToken, changeHandler: OnProgressingSettingsChanged));
                _settingSubscriptions.Add(await extensibility.Settings().SubscribeAsync(ReorganizingSettings, cancellationToken, changeHandler: OnReorganizingSettingsChanged));
                _settingSubscriptions.Add(await extensibility.Settings().SubscribeAsync(ThirdPartySettings, cancellationToken, changeHandler: OnThirdPartySettingsChanged));

                DebugLog($"InitializeAsync completed successfully. Subscriptions held: {_settingSubscriptions.Count}.");
            }
            catch (Exception ex)
            {
                DebugLog($"InitializeAsync FAILED: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Temporary diagnostic logging that writes directly to a plain text file, bypassing
        /// OutputWindowHelper/classic VSSDK service resolution entirely, so we get a signal even
        /// if this new-style extension can't safely call into classic VS services from here.
        /// </summary>
        private static void DebugLog(string message)
        {
            try
            {
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "codejanitor-settings-init-debug.log");
                System.IO.File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Best-effort diagnostic only - never let logging failures affect the extension.
            }
        }

        private static Setting[] GeneralSettings { get; } = new Setting[]
        {
            GeneralNativeSettings.CacheFiles,
            GeneralNativeSettings.DiagnosticsMode,
            GeneralNativeSettings.LoadModelsAsynchronously,
            GeneralNativeSettings.ShowStartPageOnSolutionClose,
            GeneralNativeSettings.SkipUndoTransactionsDuringAutoCleanupOnSave,
            GeneralNativeSettings.UseUndoTransactions,
        };

        private static Setting[] SwitchingSettings { get; } = new Setting[]
        {
            SwitchingNativeSettings.RelatedFileExtensionsExpression,
        };

        private static Setting[] FeaturesSettings { get; } = new Setting[]
        {
            FeaturesNativeSettings.BuildProgressToolWindow,
            FeaturesNativeSettings.CleanupActiveCode,
            FeaturesNativeSettings.CleanupAllCode,
            FeaturesNativeSettings.CleanupChangedFiles,
            FeaturesNativeSettings.CleanupOpenCode,
            FeaturesNativeSettings.CleanupSelectedCode,
            FeaturesNativeSettings.CloseAllReadOnly,
            FeaturesNativeSettings.CollapseAllSolutionExplorer,
            FeaturesNativeSettings.CollapseSelectedSolutionExplorer,
            FeaturesNativeSettings.CommentFormat,
            FeaturesNativeSettings.FindInSolutionExplorer,
            FeaturesNativeSettings.JoinLines,
            FeaturesNativeSettings.ReadOnlyToggle,
            FeaturesNativeSettings.RemoveRegion,
            FeaturesNativeSettings.ReorganizeActiveCode,
            FeaturesNativeSettings.SettingCleanupOnSave,
            FeaturesNativeSettings.SortLines,
            FeaturesNativeSettings.SpadeToolWindow,
            FeaturesNativeSettings.SwitchFile,
        };

        private static Setting[] CleaningSettings { get; } = new Setting[]
        {
            CleaningNativeSettings.AutoCleanupOnFileSave,
            CleaningNativeSettings.AutoSaveAndCloseIfOpenedByCleanup,
            CleaningNativeSettings.PerformPartialCleanupOnExternal,
            CleaningNativeSettings.ExcludeT4GeneratedCode,
            CleaningNativeSettings.ExclusionExpression,
            CleaningNativeSettings.InclusionExpression,
            CleaningNativeSettings.IncludeCPlusPlus,
            CleaningNativeSettings.IncludeCSharp,
            CleaningNativeSettings.IncludeCSS,
            CleaningNativeSettings.IncludeEverythingElse,
            CleaningNativeSettings.IncludeFSharp,
            CleaningNativeSettings.IncludeHTML,
            CleaningNativeSettings.IncludeJavaScript,
            CleaningNativeSettings.IncludeJSON,
            CleaningNativeSettings.IncludeLESS,
            CleaningNativeSettings.IncludePHP,
            CleaningNativeSettings.IncludePowerShell,
            CleaningNativeSettings.IncludeR,
            CleaningNativeSettings.IncludeSCSS,
            CleaningNativeSettings.IncludeTypeScript,
            CleaningNativeSettings.IncludeVB,
            CleaningNativeSettings.IncludeXAML,
            CleaningNativeSettings.IncludeXML,
            CleaningNativeSettings.RunVisualStudioFormatDocumentCommand,
            CleaningNativeSettings.RunVisualStudioRemoveAndSortUsingStatements,
            CleaningNativeSettings.SkipRemoveAndSortUsingStatementsDuringAutoCleanupOnSave,
            CleaningNativeSettings.UsingStatementsToReinsertWhenRemovedExpression,
            CleaningNativeSettings.InsertBlankLinePaddingAfterClasses,
            CleaningNativeSettings.InsertBlankLinePaddingAfterDelegates,
            CleaningNativeSettings.InsertBlankLinePaddingAfterEndRegionTags,
            CleaningNativeSettings.InsertBlankLinePaddingAfterEnumerations,
            CleaningNativeSettings.InsertBlankLinePaddingAfterEvents,
            CleaningNativeSettings.InsertBlankLinePaddingAfterFieldsMultiLine,
            CleaningNativeSettings.InsertBlankLinePaddingAfterFieldsSingleLine,
            CleaningNativeSettings.InsertBlankLinePaddingAfterInterfaces,
            CleaningNativeSettings.InsertBlankLinePaddingAfterMethods,
            CleaningNativeSettings.InsertBlankLinePaddingAfterNamespaces,
            CleaningNativeSettings.InsertBlankLinePaddingAfterPropertiesMultiLine,
            CleaningNativeSettings.InsertBlankLinePaddingAfterPropertiesSingleLine,
            CleaningNativeSettings.InsertBlankLinePaddingAfterRegionTags,
            CleaningNativeSettings.InsertBlankLinePaddingAfterStructs,
            CleaningNativeSettings.InsertBlankLinePaddingAfterUsingStatementBlocks,
            CleaningNativeSettings.InsertBlankLinePaddingBeforeCaseStatements,
            CleaningNativeSettings.InsertBlankLinePaddingBeforeClasses,
            CleaningNativeSettings.InsertBlankLinePaddingBeforeDelegates,
            CleaningNativeSettings.InsertBlankLinePaddingBeforeEndRegionTags,
            CleaningNativeSettings.InsertBlankLinePaddingBeforeEnumerations,
            CleaningNativeSettings.InsertBlankLinePaddingBeforeEvents,
            CleaningNativeSettings.InsertBlankLinePaddingBeforeFieldsMultiLine,
            CleaningNativeSettings.InsertBlankLinePaddingBeforeFieldsSingleLine,
            CleaningNativeSettings.InsertBlankLinePaddingBeforeInterfaces,
            CleaningNativeSettings.InsertBlankLinePaddingBeforeMethods,
            CleaningNativeSettings.InsertBlankLinePaddingBeforeNamespaces,
            CleaningNativeSettings.InsertBlankLinePaddingBeforePropertiesMultiLine,
            CleaningNativeSettings.InsertBlankLinePaddingBeforePropertiesSingleLine,
            CleaningNativeSettings.InsertBlankLinePaddingBeforeRegionTags,
            CleaningNativeSettings.InsertBlankLinePaddingBeforeSingleLineComments,
            CleaningNativeSettings.InsertBlankLinePaddingBeforeStructs,
            CleaningNativeSettings.InsertBlankLinePaddingBeforeUsingStatementBlocks,
            CleaningNativeSettings.InsertBlankLinePaddingBetweenPropertiesMultiLineAccessors,
            CleaningNativeSettings.InsertBlankSpaceBeforeSelfClosingAngleBrackets,
            CleaningNativeSettings.InsertEndOfFileTrailingNewLine,
            CleaningNativeSettings.InsertExplicitAccessModifiersOnClasses,
            CleaningNativeSettings.InsertExplicitAccessModifiersOnDelegates,
            CleaningNativeSettings.InsertExplicitAccessModifiersOnEnumerations,
            CleaningNativeSettings.InsertExplicitAccessModifiersOnEvents,
            CleaningNativeSettings.InsertExplicitAccessModifiersOnFields,
            CleaningNativeSettings.InsertExplicitAccessModifiersOnInterfaces,
            CleaningNativeSettings.InsertExplicitAccessModifiersOnMethods,
            CleaningNativeSettings.InsertExplicitAccessModifiersOnProperties,
            CleaningNativeSettings.InsertExplicitAccessModifiersOnStructs,
            CleaningNativeSettings.RemoveBlankLinesAfterAttributes,
            CleaningNativeSettings.RemoveBlankLinesAfterOpeningBrace,
            CleaningNativeSettings.RemoveBlankLinesAtBottom,
            CleaningNativeSettings.RemoveBlankLinesAtTop,
            CleaningNativeSettings.RemoveBlankLinesBeforeClosingBrace,
            CleaningNativeSettings.RemoveBlankLinesBeforeClosingTags,
            CleaningNativeSettings.RemoveBlankLinesBetweenChainedStatements,
            CleaningNativeSettings.RemoveBlankSpacesBeforeClosingAngleBrackets,
            CleaningNativeSettings.RemoveEndOfFileTrailingNewLine,
            CleaningNativeSettings.RemoveEndOfLineWhitespace,
            CleaningNativeSettings.RemoveMultipleConsecutiveBlankLines,
            CleaningNativeSettings.RemoveRegions,
            CleaningNativeSettings.UpdateAccessorsToBothBeSingleLineOrMultiLine,
            CleaningNativeSettings.UpdateEndRegionDirectives,
            CleaningNativeSettings.UpdateSingleLineMethods,
            CleaningNativeSettings.ConvertToFileScopedNamespace,
            CleaningNativeSettings.MoveUsingsOutsideNamespace,
            CleaningNativeSettings.ConvertToVarWhenApparent,
            CleaningNativeSettings.MakeFieldsReadonlyWhenSafe,
            CleaningNativeSettings.SealClassesWhenSafe,
            CleaningNativeSettings.UpdateFileHeaderHeaderPosition,
            CleaningNativeSettings.UpdateFileHeaderHeaderUpdateMode,
            CleaningNativeSettings.UpdateFileHeaderCPlusPlus,
            CleaningNativeSettings.UpdateFileHeaderCSharp,
            CleaningNativeSettings.UpdateFileHeaderCSS,
            CleaningNativeSettings.UpdateFileHeaderFSharp,
            CleaningNativeSettings.UpdateFileHeaderHTML,
            CleaningNativeSettings.UpdateFileHeaderJavaScript,
            CleaningNativeSettings.UpdateFileHeaderJSON,
            CleaningNativeSettings.UpdateFileHeaderLESS,
            CleaningNativeSettings.UpdateFileHeaderPHP,
            CleaningNativeSettings.UpdateFileHeaderPowerShell,
            CleaningNativeSettings.UpdateFileHeaderR,
            CleaningNativeSettings.UpdateFileHeaderSCSS,
            CleaningNativeSettings.UpdateFileHeaderTypeScript,
            CleaningNativeSettings.UpdateFileHeaderVB,
            CleaningNativeSettings.UpdateFileHeaderXAML,
            CleaningNativeSettings.UpdateFileHeaderXML,
        };

        private static Setting[] CollapsingSettings { get; } = new Setting[]
        {
            CollapsingNativeSettings.CollapseSolutionWhenOpened,
            CollapsingNativeSettings.KeepSoloProjectExpanded,
        };

        private static Setting[] DiggingSettings { get; } = new Setting[]
        {
            DiggingNativeSettings.CenterOnWhole,
            DiggingNativeSettings.ComplexityAlertThreshold,
            DiggingNativeSettings.ComplexityWarningThreshold,
            DiggingNativeSettings.IndentationMargin,
            DiggingNativeSettings.PrimarySortOrder,
            DiggingNativeSettings.SecondarySortTypeByName,
            DiggingNativeSettings.ShowItemComplexity,
            DiggingNativeSettings.ShowItemMetadata,
            DiggingNativeSettings.ShowItemTypes,
            DiggingNativeSettings.ShowMethodParameters,
            DiggingNativeSettings.SynchronizeOutlining,
        };

        private static Setting[] FindingSettings { get; } = new Setting[]
        {
            FindingNativeSettings.ClearSolutionExplorerSearch,
            FindingNativeSettings.TemporarilyOpenSolutionFolders,
        };

        private static Setting[] FormattingSettings { get; } = new Setting[]
        {
            FormattingNativeSettings.CommentRunDuringCleanup,
            FormattingNativeSettings.CommentSkipWrapOnLastWord,
            FormattingNativeSettings.CommentWrapColumn,
            FormattingNativeSettings.CommentXmlAlignParamTags,
            FormattingNativeSettings.CommentXmlKeepTagsTogether,
            FormattingNativeSettings.CommentXmlSpaceSingleTags,
            FormattingNativeSettings.CommentXmlSpaceTags,
            FormattingNativeSettings.CommentXmlSplitAllTags,
            FormattingNativeSettings.CommentXmlSplitSummaryTagToMultipleLines,
            FormattingNativeSettings.CommentXmlTagsToLowerCase,
            FormattingNativeSettings.CommentXmlValueIndent,
        };

        private static Setting[] ProgressingSettings { get; } = new Setting[]
        {
            ProgressingNativeSettings.HideBuildProgressOnBuildStop,
            ProgressingNativeSettings.ShowBuildProgressOnBuildStart,
            ProgressingNativeSettings.ShowProgressOnWindowsTaskbar,
        };

        private static Setting[] ReorganizingSettings { get; } = new Setting[]
        {
            ReorganizingNativeSettings.AlphabetizeMembersOfTheSameGroup,
            ReorganizingNativeSettings.ExplicitMembersAtEnd,
            ReorganizingNativeSettings.KeepMembersWithinRegions,
            ReorganizingNativeSettings.PerformWhenPreprocessorConditionals,
            ReorganizingNativeSettings.PrimaryOrderByAccessLevel,
            ReorganizingNativeSettings.ReverseOrderByAccessLevel,
            ReorganizingNativeSettings.RunAtStartOfCleanup,
            ReorganizingNativeSettings.MemberTypes,
            ReorganizingNativeSettings.RegionsIncludeAccessLevel,
            ReorganizingNativeSettings.RegionsIncludeAccessLevelForMethodsOnly,
            ReorganizingNativeSettings.RegionsInsertKeepEvenIfEmpty,
            ReorganizingNativeSettings.RegionsInsertNewRegions,
            ReorganizingNativeSettings.RegionsRemoveExistingRegions,
        };

        private static Setting[] ThirdPartySettings { get; } = new Setting[]
        {
            ThirdPartyNativeSettings.UseJetBrainsReSharperCleanup,
            ThirdPartyNativeSettings.UseTelerikJustCodeCleanup,
            ThirdPartyNativeSettings.UseXAMLStylerCleanup,
            ThirdPartyNativeSettings.OtherCleaningCommandsExpression,
        };

        // Bridge: Settings.Default (user.config) stays the runtime source of truth read
        // throughout CodeJanitorShared - native settings are a synced front-end on top of it.
        // Used both on startup and after an explicit "Import Rules" command run.
        internal static async Task PushCurrentSettingsToNativeStoreAsync(VisualStudioExtensibility extensibility, CancellationToken cancellationToken)
        {
            var settings = Properties.Settings.Default;

            await extensibility.Settings().WriteAsync(
                batch =>
                {
                    batch.WriteSetting(GeneralNativeSettings.CacheFiles, settings.General_CacheFiles);
                    batch.WriteSetting(GeneralNativeSettings.DiagnosticsMode, settings.General_DiagnosticsMode);
                    batch.WriteSetting(GeneralNativeSettings.LoadModelsAsynchronously, settings.General_LoadModelsAsynchronously);
                    batch.WriteSetting(GeneralNativeSettings.ShowStartPageOnSolutionClose, settings.General_ShowStartPageOnSolutionClose);
                    batch.WriteSetting(GeneralNativeSettings.SkipUndoTransactionsDuringAutoCleanupOnSave, settings.General_SkipUndoTransactionsDuringAutoCleanupOnSave);
                    batch.WriteSetting(GeneralNativeSettings.UseUndoTransactions, settings.General_UseUndoTransactions);
                    batch.WriteSetting(SwitchingNativeSettings.RelatedFileExtensionsExpression, settings.Switching_RelatedFileExtensionsExpression);

                    batch.WriteSetting(FeaturesNativeSettings.BuildProgressToolWindow, settings.Feature_BuildProgressToolWindow);
                    batch.WriteSetting(FeaturesNativeSettings.CleanupActiveCode, settings.Feature_CleanupActiveCode);
                    batch.WriteSetting(FeaturesNativeSettings.CleanupAllCode, settings.Feature_CleanupAllCode);
                    batch.WriteSetting(FeaturesNativeSettings.CleanupChangedFiles, settings.Feature_CleanupChangedFiles);
                    batch.WriteSetting(FeaturesNativeSettings.CleanupOpenCode, settings.Feature_CleanupOpenCode);
                    batch.WriteSetting(FeaturesNativeSettings.CleanupSelectedCode, settings.Feature_CleanupSelectedCode);
                    batch.WriteSetting(FeaturesNativeSettings.CloseAllReadOnly, settings.Feature_CloseAllReadOnly);
                    batch.WriteSetting(FeaturesNativeSettings.CollapseAllSolutionExplorer, settings.Feature_CollapseAllSolutionExplorer);
                    batch.WriteSetting(FeaturesNativeSettings.CollapseSelectedSolutionExplorer, settings.Feature_CollapseSelectedSolutionExplorer);
                    batch.WriteSetting(FeaturesNativeSettings.CommentFormat, settings.Feature_CommentFormat);
                    batch.WriteSetting(FeaturesNativeSettings.FindInSolutionExplorer, settings.Feature_FindInSolutionExplorer);
                    batch.WriteSetting(FeaturesNativeSettings.JoinLines, settings.Feature_JoinLines);
                    batch.WriteSetting(FeaturesNativeSettings.ReadOnlyToggle, settings.Feature_ReadOnlyToggle);
                    batch.WriteSetting(FeaturesNativeSettings.RemoveRegion, settings.Feature_RemoveRegion);
                    batch.WriteSetting(FeaturesNativeSettings.ReorganizeActiveCode, settings.Feature_ReorganizeActiveCode);
                    batch.WriteSetting(FeaturesNativeSettings.SettingCleanupOnSave, settings.Feature_SettingCleanupOnSave);
                    batch.WriteSetting(FeaturesNativeSettings.SortLines, settings.Feature_SortLines);
                    batch.WriteSetting(FeaturesNativeSettings.SpadeToolWindow, settings.Feature_SpadeToolWindow);
                    batch.WriteSetting(FeaturesNativeSettings.SwitchFile, settings.Feature_SwitchFile);

                    batch.WriteSetting(CleaningNativeSettings.AutoCleanupOnFileSave, settings.Cleaning_AutoCleanupOnFileSave);
                    batch.WriteSetting(CleaningNativeSettings.AutoSaveAndCloseIfOpenedByCleanup, settings.Cleaning_AutoSaveAndCloseIfOpenedByCleanup);
                    batch.WriteSetting(CleaningNativeSettings.EnableParallelCleanup, settings.Cleaning_EnableParallelCleanup);
                    batch.WriteSetting(CleaningNativeSettings.MaxDegreeOfParallelism, settings.Cleaning_MaxDegreeOfParallelism);
                    batch.WriteSetting(CleaningNativeSettings.PerformPartialCleanupOnExternal, settings.Cleaning_PerformPartialCleanupOnExternal);
                    batch.WriteSetting(CleaningNativeSettings.ExcludeT4GeneratedCode, settings.Cleaning_ExcludeT4GeneratedCode);
                    batch.WriteSetting(CleaningNativeSettings.ExclusionExpression, settings.Cleaning_ExclusionExpression);
                    batch.WriteSetting(CleaningNativeSettings.InclusionExpression, settings.Cleaning_InclusionExpression);
                    batch.WriteSetting(CleaningNativeSettings.IncludeCPlusPlus, settings.Cleaning_IncludeCPlusPlus);
                    batch.WriteSetting(CleaningNativeSettings.IncludeCSharp, settings.Cleaning_IncludeCSharp);
                    batch.WriteSetting(CleaningNativeSettings.IncludeCSS, settings.Cleaning_IncludeCSS);
                    batch.WriteSetting(CleaningNativeSettings.IncludeEverythingElse, settings.Cleaning_IncludeEverythingElse);
                    batch.WriteSetting(CleaningNativeSettings.IncludeFSharp, settings.Cleaning_IncludeFSharp);
                    batch.WriteSetting(CleaningNativeSettings.IncludeHTML, settings.Cleaning_IncludeHTML);
                    batch.WriteSetting(CleaningNativeSettings.IncludeJavaScript, settings.Cleaning_IncludeJavaScript);
                    batch.WriteSetting(CleaningNativeSettings.IncludeJSON, settings.Cleaning_IncludeJSON);
                    batch.WriteSetting(CleaningNativeSettings.IncludeLESS, settings.Cleaning_IncludeLESS);
                    batch.WriteSetting(CleaningNativeSettings.IncludePHP, settings.Cleaning_IncludePHP);
                    batch.WriteSetting(CleaningNativeSettings.IncludePowerShell, settings.Cleaning_IncludePowerShell);
                    batch.WriteSetting(CleaningNativeSettings.IncludeR, settings.Cleaning_IncludeR);
                    batch.WriteSetting(CleaningNativeSettings.IncludeSCSS, settings.Cleaning_IncludeSCSS);
                    batch.WriteSetting(CleaningNativeSettings.IncludeTypeScript, settings.Cleaning_IncludeTypeScript);
                    batch.WriteSetting(CleaningNativeSettings.IncludeVB, settings.Cleaning_IncludeVB);
                    batch.WriteSetting(CleaningNativeSettings.IncludeXAML, settings.Cleaning_IncludeXAML);
                    batch.WriteSetting(CleaningNativeSettings.IncludeXML, settings.Cleaning_IncludeXML);
                    batch.WriteSetting(CleaningNativeSettings.RunVisualStudioFormatDocumentCommand, settings.Cleaning_RunVisualStudioFormatDocumentCommand);
                    batch.WriteSetting(CleaningNativeSettings.RunVisualStudioRemoveAndSortUsingStatements, settings.Cleaning_RunVisualStudioRemoveAndSortUsingStatements);
                    batch.WriteSetting(CleaningNativeSettings.SkipRemoveAndSortUsingStatementsDuringAutoCleanupOnSave, settings.Cleaning_SkipRemoveAndSortUsingStatementsDuringAutoCleanupOnSave);
                    batch.WriteSetting(CleaningNativeSettings.UsingStatementsToReinsertWhenRemovedExpression, settings.Cleaning_UsingStatementsToReinsertWhenRemovedExpression);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingAfterClasses, settings.Cleaning_InsertBlankLinePaddingAfterClasses);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingAfterDelegates, settings.Cleaning_InsertBlankLinePaddingAfterDelegates);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingAfterEndRegionTags, settings.Cleaning_InsertBlankLinePaddingAfterEndRegionTags);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingAfterEnumerations, settings.Cleaning_InsertBlankLinePaddingAfterEnumerations);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingAfterEvents, settings.Cleaning_InsertBlankLinePaddingAfterEvents);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingAfterFieldsMultiLine, settings.Cleaning_InsertBlankLinePaddingAfterFieldsMultiLine);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingAfterFieldsSingleLine, settings.Cleaning_InsertBlankLinePaddingAfterFieldsSingleLine);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingAfterInterfaces, settings.Cleaning_InsertBlankLinePaddingAfterInterfaces);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingAfterMethods, settings.Cleaning_InsertBlankLinePaddingAfterMethods);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingAfterNamespaces, settings.Cleaning_InsertBlankLinePaddingAfterNamespaces);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingAfterPropertiesMultiLine, settings.Cleaning_InsertBlankLinePaddingAfterPropertiesMultiLine);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingAfterPropertiesSingleLine, settings.Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingAfterRegionTags, settings.Cleaning_InsertBlankLinePaddingAfterRegionTags);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingAfterStructs, settings.Cleaning_InsertBlankLinePaddingAfterStructs);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingAfterUsingStatementBlocks, settings.Cleaning_InsertBlankLinePaddingAfterUsingStatementBlocks);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingBeforeCaseStatements, settings.Cleaning_InsertBlankLinePaddingBeforeCaseStatements);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingBeforeClasses, settings.Cleaning_InsertBlankLinePaddingBeforeClasses);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingBeforeDelegates, settings.Cleaning_InsertBlankLinePaddingBeforeDelegates);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingBeforeEndRegionTags, settings.Cleaning_InsertBlankLinePaddingBeforeEndRegionTags);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingBeforeEnumerations, settings.Cleaning_InsertBlankLinePaddingBeforeEnumerations);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingBeforeEvents, settings.Cleaning_InsertBlankLinePaddingBeforeEvents);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingBeforeFieldsMultiLine, settings.Cleaning_InsertBlankLinePaddingBeforeFieldsMultiLine);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingBeforeFieldsSingleLine, settings.Cleaning_InsertBlankLinePaddingBeforeFieldsSingleLine);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingBeforeInterfaces, settings.Cleaning_InsertBlankLinePaddingBeforeInterfaces);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingBeforeMethods, settings.Cleaning_InsertBlankLinePaddingBeforeMethods);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingBeforeNamespaces, settings.Cleaning_InsertBlankLinePaddingBeforeNamespaces);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingBeforePropertiesMultiLine, settings.Cleaning_InsertBlankLinePaddingBeforePropertiesMultiLine);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingBeforePropertiesSingleLine, settings.Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingBeforeRegionTags, settings.Cleaning_InsertBlankLinePaddingBeforeRegionTags);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingBeforeSingleLineComments, settings.Cleaning_InsertBlankLinePaddingBeforeSingleLineComments);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingBeforeStructs, settings.Cleaning_InsertBlankLinePaddingBeforeStructs);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingBeforeUsingStatementBlocks, settings.Cleaning_InsertBlankLinePaddingBeforeUsingStatementBlocks);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankLinePaddingBetweenPropertiesMultiLineAccessors, settings.Cleaning_InsertBlankLinePaddingBetweenPropertiesMultiLineAccessors);
                    batch.WriteSetting(CleaningNativeSettings.InsertBlankSpaceBeforeSelfClosingAngleBrackets, settings.Cleaning_InsertBlankSpaceBeforeSelfClosingAngleBrackets);
                    batch.WriteSetting(CleaningNativeSettings.InsertEndOfFileTrailingNewLine, settings.Cleaning_InsertEndOfFileTrailingNewLine);
                    batch.WriteSetting(CleaningNativeSettings.InsertExplicitAccessModifiersOnClasses, settings.Cleaning_InsertExplicitAccessModifiersOnClasses);
                    batch.WriteSetting(CleaningNativeSettings.InsertExplicitAccessModifiersOnDelegates, settings.Cleaning_InsertExplicitAccessModifiersOnDelegates);
                    batch.WriteSetting(CleaningNativeSettings.InsertExplicitAccessModifiersOnEnumerations, settings.Cleaning_InsertExplicitAccessModifiersOnEnumerations);
                    batch.WriteSetting(CleaningNativeSettings.InsertExplicitAccessModifiersOnEvents, settings.Cleaning_InsertExplicitAccessModifiersOnEvents);
                    batch.WriteSetting(CleaningNativeSettings.InsertExplicitAccessModifiersOnFields, settings.Cleaning_InsertExplicitAccessModifiersOnFields);
                    batch.WriteSetting(CleaningNativeSettings.InsertExplicitAccessModifiersOnInterfaces, settings.Cleaning_InsertExplicitAccessModifiersOnInterfaces);
                    batch.WriteSetting(CleaningNativeSettings.InsertExplicitAccessModifiersOnMethods, settings.Cleaning_InsertExplicitAccessModifiersOnMethods);
                    batch.WriteSetting(CleaningNativeSettings.InsertExplicitAccessModifiersOnProperties, settings.Cleaning_InsertExplicitAccessModifiersOnProperties);
                    batch.WriteSetting(CleaningNativeSettings.InsertExplicitAccessModifiersOnStructs, settings.Cleaning_InsertExplicitAccessModifiersOnStructs);
                    batch.WriteSetting(CleaningNativeSettings.RemoveBlankLinesAfterAttributes, settings.Cleaning_RemoveBlankLinesAfterAttributes);
                    batch.WriteSetting(CleaningNativeSettings.RemoveBlankLinesAfterOpeningBrace, settings.Cleaning_RemoveBlankLinesAfterOpeningBrace);
                    batch.WriteSetting(CleaningNativeSettings.RemoveBlankLinesAtBottom, settings.Cleaning_RemoveBlankLinesAtBottom);
                    batch.WriteSetting(CleaningNativeSettings.RemoveBlankLinesAtTop, settings.Cleaning_RemoveBlankLinesAtTop);
                    batch.WriteSetting(CleaningNativeSettings.RemoveBlankLinesBeforeClosingBrace, settings.Cleaning_RemoveBlankLinesBeforeClosingBrace);
                    batch.WriteSetting(CleaningNativeSettings.RemoveBlankLinesBeforeClosingTags, settings.Cleaning_RemoveBlankLinesBeforeClosingTags);
                    batch.WriteSetting(CleaningNativeSettings.RemoveBlankLinesBetweenChainedStatements, settings.Cleaning_RemoveBlankLinesBetweenChainedStatements);
                    batch.WriteSetting(CleaningNativeSettings.RemoveBlankSpacesBeforeClosingAngleBrackets, settings.Cleaning_RemoveBlankSpacesBeforeClosingAngleBrackets);
                    batch.WriteSetting(CleaningNativeSettings.RemoveEndOfFileTrailingNewLine, settings.Cleaning_RemoveEndOfFileTrailingNewLine);
                    batch.WriteSetting(CleaningNativeSettings.RemoveEndOfLineWhitespace, settings.Cleaning_RemoveEndOfLineWhitespace);
                    batch.WriteSetting(CleaningNativeSettings.RemoveMultipleConsecutiveBlankLines, settings.Cleaning_RemoveMultipleConsecutiveBlankLines);
                    batch.WriteSetting(CleaningNativeSettings.RemoveRegions, settings.Cleaning_RemoveRegions);
                    batch.WriteSetting(CleaningNativeSettings.UpdateAccessorsToBothBeSingleLineOrMultiLine, settings.Cleaning_UpdateAccessorsToBothBeSingleLineOrMultiLine);
                    batch.WriteSetting(CleaningNativeSettings.UpdateEndRegionDirectives, settings.Cleaning_UpdateEndRegionDirectives);
                    batch.WriteSetting(CleaningNativeSettings.UpdateSingleLineMethods, settings.Cleaning_UpdateSingleLineMethods);
                    batch.WriteSetting(CleaningNativeSettings.ConvertToFileScopedNamespace, settings.Cleaning_ConvertToFileScopedNamespace);
                    batch.WriteSetting(CleaningNativeSettings.MoveUsingsOutsideNamespace, settings.Cleaning_MoveUsingsOutsideNamespace);
                    batch.WriteSetting(CleaningNativeSettings.ConvertToVarWhenApparent, settings.Cleaning_ConvertToVarWhenApparent);
                    batch.WriteSetting(CleaningNativeSettings.MakeFieldsReadonlyWhenSafe, settings.Cleaning_MakeFieldsReadonlyWhenSafe);
                    batch.WriteSetting(CleaningNativeSettings.SealClassesWhenSafe, settings.Cleaning_SealClassesWhenSafe);
                    batch.WriteSetting(CleaningNativeSettings.UpdateFileHeaderHeaderPosition, settings.Cleaning_UpdateFileHeader_HeaderPosition);
                    batch.WriteSetting(CleaningNativeSettings.UpdateFileHeaderHeaderUpdateMode, settings.Cleaning_UpdateFileHeader_HeaderUpdateMode);
                    batch.WriteSetting(CleaningNativeSettings.UpdateFileHeaderCPlusPlus, settings.Cleaning_UpdateFileHeaderCPlusPlus);
                    batch.WriteSetting(CleaningNativeSettings.UpdateFileHeaderCSharp, settings.Cleaning_UpdateFileHeaderCSharp);
                    batch.WriteSetting(CleaningNativeSettings.UpdateFileHeaderCSS, settings.Cleaning_UpdateFileHeaderCSS);
                    batch.WriteSetting(CleaningNativeSettings.UpdateFileHeaderFSharp, settings.Cleaning_UpdateFileHeaderFSharp);
                    batch.WriteSetting(CleaningNativeSettings.UpdateFileHeaderHTML, settings.Cleaning_UpdateFileHeaderHTML);
                    batch.WriteSetting(CleaningNativeSettings.UpdateFileHeaderJavaScript, settings.Cleaning_UpdateFileHeaderJavaScript);
                    batch.WriteSetting(CleaningNativeSettings.UpdateFileHeaderJSON, settings.Cleaning_UpdateFileHeaderJSON);
                    batch.WriteSetting(CleaningNativeSettings.UpdateFileHeaderLESS, settings.Cleaning_UpdateFileHeaderLESS);
                    batch.WriteSetting(CleaningNativeSettings.UpdateFileHeaderPHP, settings.Cleaning_UpdateFileHeaderPHP);
                    batch.WriteSetting(CleaningNativeSettings.UpdateFileHeaderPowerShell, settings.Cleaning_UpdateFileHeaderPowerShell);
                    batch.WriteSetting(CleaningNativeSettings.UpdateFileHeaderR, settings.Cleaning_UpdateFileHeaderR);
                    batch.WriteSetting(CleaningNativeSettings.UpdateFileHeaderSCSS, settings.Cleaning_UpdateFileHeaderSCSS);
                    batch.WriteSetting(CleaningNativeSettings.UpdateFileHeaderTypeScript, settings.Cleaning_UpdateFileHeaderTypeScript);
                    batch.WriteSetting(CleaningNativeSettings.UpdateFileHeaderVB, settings.Cleaning_UpdateFileHeaderVB);
                    batch.WriteSetting(CleaningNativeSettings.UpdateFileHeaderXAML, settings.Cleaning_UpdateFileHeaderXAML);
                    batch.WriteSetting(CleaningNativeSettings.UpdateFileHeaderXML, settings.Cleaning_UpdateFileHeaderXML);

                    batch.WriteSetting(CollapsingNativeSettings.CollapseSolutionWhenOpened, settings.Collapsing_CollapseSolutionWhenOpened);
                    batch.WriteSetting(CollapsingNativeSettings.KeepSoloProjectExpanded, settings.Collapsing_KeepSoloProjectExpanded);

                    batch.WriteSetting(DiggingNativeSettings.CenterOnWhole, settings.Digging_CenterOnWhole);
                    batch.WriteSetting(DiggingNativeSettings.ComplexityAlertThreshold, settings.Digging_ComplexityAlertThreshold);
                    batch.WriteSetting(DiggingNativeSettings.ComplexityWarningThreshold, settings.Digging_ComplexityWarningThreshold);
                    batch.WriteSetting(DiggingNativeSettings.IndentationMargin, settings.Digging_IndentationMargin);
                    batch.WriteSetting(DiggingNativeSettings.PrimarySortOrder, settings.Digging_PrimarySortOrder);
                    batch.WriteSetting(DiggingNativeSettings.SecondarySortTypeByName, settings.Digging_SecondarySortTypeByName);
                    batch.WriteSetting(DiggingNativeSettings.ShowItemComplexity, settings.Digging_ShowItemComplexity);
                    batch.WriteSetting(DiggingNativeSettings.ShowItemMetadata, settings.Digging_ShowItemMetadata);
                    batch.WriteSetting(DiggingNativeSettings.ShowItemTypes, settings.Digging_ShowItemTypes);
                    batch.WriteSetting(DiggingNativeSettings.ShowMethodParameters, settings.Digging_ShowMethodParameters);
                    batch.WriteSetting(DiggingNativeSettings.SynchronizeOutlining, settings.Digging_SynchronizeOutlining);

                    batch.WriteSetting(FindingNativeSettings.ClearSolutionExplorerSearch, settings.Finding_ClearSolutionExplorerSearch);
                    batch.WriteSetting(FindingNativeSettings.TemporarilyOpenSolutionFolders, settings.Finding_TemporarilyOpenSolutionFolders);

                    batch.WriteSetting(FormattingNativeSettings.CommentRunDuringCleanup, settings.Formatting_CommentRunDuringCleanup);
                    batch.WriteSetting(FormattingNativeSettings.CommentSkipWrapOnLastWord, settings.Formatting_CommentSkipWrapOnLastWord);
                    batch.WriteSetting(FormattingNativeSettings.CommentWrapColumn, settings.Formatting_CommentWrapColumn);
                    batch.WriteSetting(FormattingNativeSettings.CommentXmlAlignParamTags, settings.Formatting_CommentXmlAlignParamTags);
                    batch.WriteSetting(FormattingNativeSettings.CommentXmlKeepTagsTogether, settings.Formatting_CommentXmlKeepTagsTogether);
                    batch.WriteSetting(FormattingNativeSettings.CommentXmlSpaceSingleTags, settings.Formatting_CommentXmlSpaceSingleTags);
                    batch.WriteSetting(FormattingNativeSettings.CommentXmlSpaceTags, settings.Formatting_CommentXmlSpaceTags);
                    batch.WriteSetting(FormattingNativeSettings.CommentXmlSplitAllTags, settings.Formatting_CommentXmlSplitAllTags);
                    batch.WriteSetting(FormattingNativeSettings.CommentXmlSplitSummaryTagToMultipleLines, settings.Formatting_CommentXmlSplitSummaryTagToMultipleLines);
                    batch.WriteSetting(FormattingNativeSettings.CommentXmlTagsToLowerCase, settings.Formatting_CommentXmlTagsToLowerCase);
                    batch.WriteSetting(FormattingNativeSettings.CommentXmlValueIndent, settings.Formatting_CommentXmlValueIndent);

                    batch.WriteSetting(ProgressingNativeSettings.HideBuildProgressOnBuildStop, settings.Progressing_HideBuildProgressOnBuildStop);
                    batch.WriteSetting(ProgressingNativeSettings.ShowBuildProgressOnBuildStart, settings.Progressing_ShowBuildProgressOnBuildStart);
                    batch.WriteSetting(ProgressingNativeSettings.ShowProgressOnWindowsTaskbar, settings.Progressing_ShowProgressOnWindowsTaskbar);

                    batch.WriteSetting(ReorganizingNativeSettings.AlphabetizeMembersOfTheSameGroup, settings.Reorganizing_AlphabetizeMembersOfTheSameGroup);
                    batch.WriteSetting(ReorganizingNativeSettings.ExplicitMembersAtEnd, settings.Reorganizing_ExplicitMembersAtEnd);
                    batch.WriteSetting(ReorganizingNativeSettings.KeepMembersWithinRegions, settings.Reorganizing_KeepMembersWithinRegions);
                    batch.WriteSetting(ReorganizingNativeSettings.PerformWhenPreprocessorConditionals, settings.Reorganizing_PerformWhenPreprocessorConditionals);
                    batch.WriteSetting(ReorganizingNativeSettings.PrimaryOrderByAccessLevel, settings.Reorganizing_PrimaryOrderByAccessLevel);
                    batch.WriteSetting(ReorganizingNativeSettings.ReverseOrderByAccessLevel, settings.Reorganizing_ReverseOrderByAccessLevel);
                    batch.WriteSetting(ReorganizingNativeSettings.RunAtStartOfCleanup, settings.Reorganizing_RunAtStartOfCleanup);

                    var memberTypeSettings = new[]
                    {
                        (MemberTypeSetting)settings.Reorganizing_MemberTypeClasses,
                        (MemberTypeSetting)settings.Reorganizing_MemberTypeConstructors,
                        (MemberTypeSetting)settings.Reorganizing_MemberTypeDelegates,
                        (MemberTypeSetting)settings.Reorganizing_MemberTypeDestructors,
                        (MemberTypeSetting)settings.Reorganizing_MemberTypeEnums,
                        (MemberTypeSetting)settings.Reorganizing_MemberTypeEvents,
                        (MemberTypeSetting)settings.Reorganizing_MemberTypeFields,
                        (MemberTypeSetting)settings.Reorganizing_MemberTypeIndexers,
                        (MemberTypeSetting)settings.Reorganizing_MemberTypeInterfaces,
                        (MemberTypeSetting)settings.Reorganizing_MemberTypeMethods,
                        (MemberTypeSetting)settings.Reorganizing_MemberTypeProperties,
                        (MemberTypeSetting)settings.Reorganizing_MemberTypeStructs,
                    };
                    batch.WriteSetting(ReorganizingNativeSettings.MemberTypes, memberTypeSettings.Select(MemberTypeArrayItem.FromMemberTypeSetting));

                    batch.WriteSetting(ReorganizingNativeSettings.RegionsIncludeAccessLevel, settings.Reorganizing_RegionsIncludeAccessLevel);
                    batch.WriteSetting(ReorganizingNativeSettings.RegionsIncludeAccessLevelForMethodsOnly, settings.Reorganizing_RegionsIncludeAccessLevelForMethodsOnly);
                    batch.WriteSetting(ReorganizingNativeSettings.RegionsInsertKeepEvenIfEmpty, settings.Reorganizing_RegionsInsertKeepEvenIfEmpty);
                    batch.WriteSetting(ReorganizingNativeSettings.RegionsInsertNewRegions, settings.Reorganizing_RegionsInsertNewRegions);
                    batch.WriteSetting(ReorganizingNativeSettings.RegionsRemoveExistingRegions, settings.Reorganizing_RegionsRemoveExistingRegions);

                    batch.WriteSetting(ThirdPartyNativeSettings.UseJetBrainsReSharperCleanup, settings.ThirdParty_UseJetBrainsReSharperCleanup);
                    batch.WriteSetting(ThirdPartyNativeSettings.UseTelerikJustCodeCleanup, settings.ThirdParty_UseTelerikJustCodeCleanup);
                    batch.WriteSetting(ThirdPartyNativeSettings.UseXAMLStylerCleanup, settings.ThirdParty_UseXAMLStylerCleanup);
                    batch.WriteSetting(ThirdPartyNativeSettings.OtherCleaningCommandsExpression, settings.ThirdParty_OtherCleaningCommandsExpression);
                },
                description: "Sync CodeJanitor settings into the native settings store",
                cancellationToken);
        }

        private void OnGeneralSettingsChanged(SettingValues values)
        {
            var settings = Properties.Settings.Default;

            settings.General_CacheFiles = values.ValueOrDefault(GeneralNativeSettings.CacheFiles, settings.General_CacheFiles);
            settings.General_DiagnosticsMode = values.ValueOrDefault(GeneralNativeSettings.DiagnosticsMode, settings.General_DiagnosticsMode);
            settings.General_LoadModelsAsynchronously = values.ValueOrDefault(GeneralNativeSettings.LoadModelsAsynchronously, settings.General_LoadModelsAsynchronously);
            settings.General_ShowStartPageOnSolutionClose = values.ValueOrDefault(GeneralNativeSettings.ShowStartPageOnSolutionClose, settings.General_ShowStartPageOnSolutionClose);
            settings.General_SkipUndoTransactionsDuringAutoCleanupOnSave = values.ValueOrDefault(GeneralNativeSettings.SkipUndoTransactionsDuringAutoCleanupOnSave, settings.General_SkipUndoTransactionsDuringAutoCleanupOnSave);
            settings.General_UseUndoTransactions = values.ValueOrDefault(GeneralNativeSettings.UseUndoTransactions, settings.General_UseUndoTransactions);
            settings.Save();
        }

        private void OnSwitchingSettingsChanged(SettingValues values)
        {
            var settings = Properties.Settings.Default;

            settings.Switching_RelatedFileExtensionsExpression = values.ValueOrDefault(SwitchingNativeSettings.RelatedFileExtensionsExpression, settings.Switching_RelatedFileExtensionsExpression);
            settings.Save();
        }

        private void OnFeaturesSettingsChanged(SettingValues values)
        {
            var settings = Properties.Settings.Default;

            settings.Feature_BuildProgressToolWindow = values.ValueOrDefault(FeaturesNativeSettings.BuildProgressToolWindow, settings.Feature_BuildProgressToolWindow);
            settings.Feature_CleanupActiveCode = values.ValueOrDefault(FeaturesNativeSettings.CleanupActiveCode, settings.Feature_CleanupActiveCode);
            settings.Feature_CleanupAllCode = values.ValueOrDefault(FeaturesNativeSettings.CleanupAllCode, settings.Feature_CleanupAllCode);
            settings.Feature_CleanupChangedFiles = values.ValueOrDefault(FeaturesNativeSettings.CleanupChangedFiles, settings.Feature_CleanupChangedFiles);
            settings.Feature_CleanupOpenCode = values.ValueOrDefault(FeaturesNativeSettings.CleanupOpenCode, settings.Feature_CleanupOpenCode);
            settings.Feature_CleanupSelectedCode = values.ValueOrDefault(FeaturesNativeSettings.CleanupSelectedCode, settings.Feature_CleanupSelectedCode);
            settings.Feature_CloseAllReadOnly = values.ValueOrDefault(FeaturesNativeSettings.CloseAllReadOnly, settings.Feature_CloseAllReadOnly);
            settings.Feature_CollapseAllSolutionExplorer = values.ValueOrDefault(FeaturesNativeSettings.CollapseAllSolutionExplorer, settings.Feature_CollapseAllSolutionExplorer);
            settings.Feature_CollapseSelectedSolutionExplorer = values.ValueOrDefault(FeaturesNativeSettings.CollapseSelectedSolutionExplorer, settings.Feature_CollapseSelectedSolutionExplorer);
            settings.Feature_CommentFormat = values.ValueOrDefault(FeaturesNativeSettings.CommentFormat, settings.Feature_CommentFormat);
            settings.Feature_FindInSolutionExplorer = values.ValueOrDefault(FeaturesNativeSettings.FindInSolutionExplorer, settings.Feature_FindInSolutionExplorer);
            settings.Feature_JoinLines = values.ValueOrDefault(FeaturesNativeSettings.JoinLines, settings.Feature_JoinLines);
            settings.Feature_ReadOnlyToggle = values.ValueOrDefault(FeaturesNativeSettings.ReadOnlyToggle, settings.Feature_ReadOnlyToggle);
            settings.Feature_RemoveRegion = values.ValueOrDefault(FeaturesNativeSettings.RemoveRegion, settings.Feature_RemoveRegion);
            settings.Feature_ReorganizeActiveCode = values.ValueOrDefault(FeaturesNativeSettings.ReorganizeActiveCode, settings.Feature_ReorganizeActiveCode);
            settings.Feature_SettingCleanupOnSave = values.ValueOrDefault(FeaturesNativeSettings.SettingCleanupOnSave, settings.Feature_SettingCleanupOnSave);
            settings.Feature_SortLines = values.ValueOrDefault(FeaturesNativeSettings.SortLines, settings.Feature_SortLines);
            settings.Feature_SpadeToolWindow = values.ValueOrDefault(FeaturesNativeSettings.SpadeToolWindow, settings.Feature_SpadeToolWindow);
            settings.Feature_SwitchFile = values.ValueOrDefault(FeaturesNativeSettings.SwitchFile, settings.Feature_SwitchFile);
            settings.Save();
        }

        private void OnCleaningSettingsChanged(SettingValues values)
        {
            DebugLog($"OnCleaningSettingsChanged fired. ConvertToFileScopedNamespace raw value present: {values.ValueOrDefault(CleaningNativeSettings.ConvertToFileScopedNamespace, false)}.");

            var settings = Properties.Settings.Default;

            settings.Cleaning_AutoCleanupOnFileSave = values.ValueOrDefault(CleaningNativeSettings.AutoCleanupOnFileSave, settings.Cleaning_AutoCleanupOnFileSave);
            settings.Cleaning_AutoSaveAndCloseIfOpenedByCleanup = values.ValueOrDefault(CleaningNativeSettings.AutoSaveAndCloseIfOpenedByCleanup, settings.Cleaning_AutoSaveAndCloseIfOpenedByCleanup);
            settings.Cleaning_EnableParallelCleanup = values.ValueOrDefault(CleaningNativeSettings.EnableParallelCleanup, settings.Cleaning_EnableParallelCleanup);
            settings.Cleaning_MaxDegreeOfParallelism = values.ValueOrDefault(CleaningNativeSettings.MaxDegreeOfParallelism, settings.Cleaning_MaxDegreeOfParallelism);
            settings.Cleaning_PerformPartialCleanupOnExternal = values.ValueOrDefault(CleaningNativeSettings.PerformPartialCleanupOnExternal, settings.Cleaning_PerformPartialCleanupOnExternal);
            settings.Cleaning_ExcludeT4GeneratedCode = values.ValueOrDefault(CleaningNativeSettings.ExcludeT4GeneratedCode, settings.Cleaning_ExcludeT4GeneratedCode);
            settings.Cleaning_ExclusionExpression = values.ValueOrDefault(CleaningNativeSettings.ExclusionExpression, settings.Cleaning_ExclusionExpression);
            settings.Cleaning_InclusionExpression = values.ValueOrDefault(CleaningNativeSettings.InclusionExpression, settings.Cleaning_InclusionExpression);
            settings.Cleaning_IncludeCPlusPlus = values.ValueOrDefault(CleaningNativeSettings.IncludeCPlusPlus, settings.Cleaning_IncludeCPlusPlus);
            settings.Cleaning_IncludeCSharp = values.ValueOrDefault(CleaningNativeSettings.IncludeCSharp, settings.Cleaning_IncludeCSharp);
            settings.Cleaning_IncludeCSS = values.ValueOrDefault(CleaningNativeSettings.IncludeCSS, settings.Cleaning_IncludeCSS);
            settings.Cleaning_IncludeEverythingElse = values.ValueOrDefault(CleaningNativeSettings.IncludeEverythingElse, settings.Cleaning_IncludeEverythingElse);
            settings.Cleaning_IncludeFSharp = values.ValueOrDefault(CleaningNativeSettings.IncludeFSharp, settings.Cleaning_IncludeFSharp);
            settings.Cleaning_IncludeHTML = values.ValueOrDefault(CleaningNativeSettings.IncludeHTML, settings.Cleaning_IncludeHTML);
            settings.Cleaning_IncludeJavaScript = values.ValueOrDefault(CleaningNativeSettings.IncludeJavaScript, settings.Cleaning_IncludeJavaScript);
            settings.Cleaning_IncludeJSON = values.ValueOrDefault(CleaningNativeSettings.IncludeJSON, settings.Cleaning_IncludeJSON);
            settings.Cleaning_IncludeLESS = values.ValueOrDefault(CleaningNativeSettings.IncludeLESS, settings.Cleaning_IncludeLESS);
            settings.Cleaning_IncludePHP = values.ValueOrDefault(CleaningNativeSettings.IncludePHP, settings.Cleaning_IncludePHP);
            settings.Cleaning_IncludePowerShell = values.ValueOrDefault(CleaningNativeSettings.IncludePowerShell, settings.Cleaning_IncludePowerShell);
            settings.Cleaning_IncludeR = values.ValueOrDefault(CleaningNativeSettings.IncludeR, settings.Cleaning_IncludeR);
            settings.Cleaning_IncludeSCSS = values.ValueOrDefault(CleaningNativeSettings.IncludeSCSS, settings.Cleaning_IncludeSCSS);
            settings.Cleaning_IncludeTypeScript = values.ValueOrDefault(CleaningNativeSettings.IncludeTypeScript, settings.Cleaning_IncludeTypeScript);
            settings.Cleaning_IncludeVB = values.ValueOrDefault(CleaningNativeSettings.IncludeVB, settings.Cleaning_IncludeVB);
            settings.Cleaning_IncludeXAML = values.ValueOrDefault(CleaningNativeSettings.IncludeXAML, settings.Cleaning_IncludeXAML);
            settings.Cleaning_IncludeXML = values.ValueOrDefault(CleaningNativeSettings.IncludeXML, settings.Cleaning_IncludeXML);
            settings.Cleaning_RunVisualStudioFormatDocumentCommand = values.ValueOrDefault(CleaningNativeSettings.RunVisualStudioFormatDocumentCommand, settings.Cleaning_RunVisualStudioFormatDocumentCommand);
            settings.Cleaning_RunVisualStudioRemoveAndSortUsingStatements = values.ValueOrDefault(CleaningNativeSettings.RunVisualStudioRemoveAndSortUsingStatements, settings.Cleaning_RunVisualStudioRemoveAndSortUsingStatements);
            settings.Cleaning_SkipRemoveAndSortUsingStatementsDuringAutoCleanupOnSave = values.ValueOrDefault(CleaningNativeSettings.SkipRemoveAndSortUsingStatementsDuringAutoCleanupOnSave, settings.Cleaning_SkipRemoveAndSortUsingStatementsDuringAutoCleanupOnSave);
            settings.Cleaning_UsingStatementsToReinsertWhenRemovedExpression = values.ValueOrDefault(CleaningNativeSettings.UsingStatementsToReinsertWhenRemovedExpression, settings.Cleaning_UsingStatementsToReinsertWhenRemovedExpression);
            settings.Cleaning_InsertBlankLinePaddingAfterClasses = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingAfterClasses, settings.Cleaning_InsertBlankLinePaddingAfterClasses);
            settings.Cleaning_InsertBlankLinePaddingAfterDelegates = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingAfterDelegates, settings.Cleaning_InsertBlankLinePaddingAfterDelegates);
            settings.Cleaning_InsertBlankLinePaddingAfterEndRegionTags = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingAfterEndRegionTags, settings.Cleaning_InsertBlankLinePaddingAfterEndRegionTags);
            settings.Cleaning_InsertBlankLinePaddingAfterEnumerations = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingAfterEnumerations, settings.Cleaning_InsertBlankLinePaddingAfterEnumerations);
            settings.Cleaning_InsertBlankLinePaddingAfterEvents = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingAfterEvents, settings.Cleaning_InsertBlankLinePaddingAfterEvents);
            settings.Cleaning_InsertBlankLinePaddingAfterFieldsMultiLine = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingAfterFieldsMultiLine, settings.Cleaning_InsertBlankLinePaddingAfterFieldsMultiLine);
            settings.Cleaning_InsertBlankLinePaddingAfterFieldsSingleLine = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingAfterFieldsSingleLine, settings.Cleaning_InsertBlankLinePaddingAfterFieldsSingleLine);
            settings.Cleaning_InsertBlankLinePaddingAfterInterfaces = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingAfterInterfaces, settings.Cleaning_InsertBlankLinePaddingAfterInterfaces);
            settings.Cleaning_InsertBlankLinePaddingAfterMethods = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingAfterMethods, settings.Cleaning_InsertBlankLinePaddingAfterMethods);
            settings.Cleaning_InsertBlankLinePaddingAfterNamespaces = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingAfterNamespaces, settings.Cleaning_InsertBlankLinePaddingAfterNamespaces);
            settings.Cleaning_InsertBlankLinePaddingAfterPropertiesMultiLine = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingAfterPropertiesMultiLine, settings.Cleaning_InsertBlankLinePaddingAfterPropertiesMultiLine);
            settings.Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingAfterPropertiesSingleLine, settings.Cleaning_InsertBlankLinePaddingAfterPropertiesSingleLine);
            settings.Cleaning_InsertBlankLinePaddingAfterRegionTags = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingAfterRegionTags, settings.Cleaning_InsertBlankLinePaddingAfterRegionTags);
            settings.Cleaning_InsertBlankLinePaddingAfterStructs = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingAfterStructs, settings.Cleaning_InsertBlankLinePaddingAfterStructs);
            settings.Cleaning_InsertBlankLinePaddingAfterUsingStatementBlocks = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingAfterUsingStatementBlocks, settings.Cleaning_InsertBlankLinePaddingAfterUsingStatementBlocks);
            settings.Cleaning_InsertBlankLinePaddingBeforeCaseStatements = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingBeforeCaseStatements, settings.Cleaning_InsertBlankLinePaddingBeforeCaseStatements);
            settings.Cleaning_InsertBlankLinePaddingBeforeClasses = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingBeforeClasses, settings.Cleaning_InsertBlankLinePaddingBeforeClasses);
            settings.Cleaning_InsertBlankLinePaddingBeforeDelegates = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingBeforeDelegates, settings.Cleaning_InsertBlankLinePaddingBeforeDelegates);
            settings.Cleaning_InsertBlankLinePaddingBeforeEndRegionTags = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingBeforeEndRegionTags, settings.Cleaning_InsertBlankLinePaddingBeforeEndRegionTags);
            settings.Cleaning_InsertBlankLinePaddingBeforeEnumerations = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingBeforeEnumerations, settings.Cleaning_InsertBlankLinePaddingBeforeEnumerations);
            settings.Cleaning_InsertBlankLinePaddingBeforeEvents = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingBeforeEvents, settings.Cleaning_InsertBlankLinePaddingBeforeEvents);
            settings.Cleaning_InsertBlankLinePaddingBeforeFieldsMultiLine = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingBeforeFieldsMultiLine, settings.Cleaning_InsertBlankLinePaddingBeforeFieldsMultiLine);
            settings.Cleaning_InsertBlankLinePaddingBeforeFieldsSingleLine = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingBeforeFieldsSingleLine, settings.Cleaning_InsertBlankLinePaddingBeforeFieldsSingleLine);
            settings.Cleaning_InsertBlankLinePaddingBeforeInterfaces = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingBeforeInterfaces, settings.Cleaning_InsertBlankLinePaddingBeforeInterfaces);
            settings.Cleaning_InsertBlankLinePaddingBeforeMethods = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingBeforeMethods, settings.Cleaning_InsertBlankLinePaddingBeforeMethods);
            settings.Cleaning_InsertBlankLinePaddingBeforeNamespaces = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingBeforeNamespaces, settings.Cleaning_InsertBlankLinePaddingBeforeNamespaces);
            settings.Cleaning_InsertBlankLinePaddingBeforePropertiesMultiLine = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingBeforePropertiesMultiLine, settings.Cleaning_InsertBlankLinePaddingBeforePropertiesMultiLine);
            settings.Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingBeforePropertiesSingleLine, settings.Cleaning_InsertBlankLinePaddingBeforePropertiesSingleLine);
            settings.Cleaning_InsertBlankLinePaddingBeforeRegionTags = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingBeforeRegionTags, settings.Cleaning_InsertBlankLinePaddingBeforeRegionTags);
            settings.Cleaning_InsertBlankLinePaddingBeforeSingleLineComments = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingBeforeSingleLineComments, settings.Cleaning_InsertBlankLinePaddingBeforeSingleLineComments);
            settings.Cleaning_InsertBlankLinePaddingBeforeStructs = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingBeforeStructs, settings.Cleaning_InsertBlankLinePaddingBeforeStructs);
            settings.Cleaning_InsertBlankLinePaddingBeforeUsingStatementBlocks = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingBeforeUsingStatementBlocks, settings.Cleaning_InsertBlankLinePaddingBeforeUsingStatementBlocks);
            settings.Cleaning_InsertBlankLinePaddingBetweenPropertiesMultiLineAccessors = values.ValueOrDefault(CleaningNativeSettings.InsertBlankLinePaddingBetweenPropertiesMultiLineAccessors, settings.Cleaning_InsertBlankLinePaddingBetweenPropertiesMultiLineAccessors);
            settings.Cleaning_InsertBlankSpaceBeforeSelfClosingAngleBrackets = values.ValueOrDefault(CleaningNativeSettings.InsertBlankSpaceBeforeSelfClosingAngleBrackets, settings.Cleaning_InsertBlankSpaceBeforeSelfClosingAngleBrackets);
            settings.Cleaning_InsertEndOfFileTrailingNewLine = values.ValueOrDefault(CleaningNativeSettings.InsertEndOfFileTrailingNewLine, settings.Cleaning_InsertEndOfFileTrailingNewLine);
            settings.Cleaning_InsertExplicitAccessModifiersOnClasses = values.ValueOrDefault(CleaningNativeSettings.InsertExplicitAccessModifiersOnClasses, settings.Cleaning_InsertExplicitAccessModifiersOnClasses);
            settings.Cleaning_InsertExplicitAccessModifiersOnDelegates = values.ValueOrDefault(CleaningNativeSettings.InsertExplicitAccessModifiersOnDelegates, settings.Cleaning_InsertExplicitAccessModifiersOnDelegates);
            settings.Cleaning_InsertExplicitAccessModifiersOnEnumerations = values.ValueOrDefault(CleaningNativeSettings.InsertExplicitAccessModifiersOnEnumerations, settings.Cleaning_InsertExplicitAccessModifiersOnEnumerations);
            settings.Cleaning_InsertExplicitAccessModifiersOnEvents = values.ValueOrDefault(CleaningNativeSettings.InsertExplicitAccessModifiersOnEvents, settings.Cleaning_InsertExplicitAccessModifiersOnEvents);
            settings.Cleaning_InsertExplicitAccessModifiersOnFields = values.ValueOrDefault(CleaningNativeSettings.InsertExplicitAccessModifiersOnFields, settings.Cleaning_InsertExplicitAccessModifiersOnFields);
            settings.Cleaning_InsertExplicitAccessModifiersOnInterfaces = values.ValueOrDefault(CleaningNativeSettings.InsertExplicitAccessModifiersOnInterfaces, settings.Cleaning_InsertExplicitAccessModifiersOnInterfaces);
            settings.Cleaning_InsertExplicitAccessModifiersOnMethods = values.ValueOrDefault(CleaningNativeSettings.InsertExplicitAccessModifiersOnMethods, settings.Cleaning_InsertExplicitAccessModifiersOnMethods);
            settings.Cleaning_InsertExplicitAccessModifiersOnProperties = values.ValueOrDefault(CleaningNativeSettings.InsertExplicitAccessModifiersOnProperties, settings.Cleaning_InsertExplicitAccessModifiersOnProperties);
            settings.Cleaning_InsertExplicitAccessModifiersOnStructs = values.ValueOrDefault(CleaningNativeSettings.InsertExplicitAccessModifiersOnStructs, settings.Cleaning_InsertExplicitAccessModifiersOnStructs);
            settings.Cleaning_RemoveBlankLinesAfterAttributes = values.ValueOrDefault(CleaningNativeSettings.RemoveBlankLinesAfterAttributes, settings.Cleaning_RemoveBlankLinesAfterAttributes);
            settings.Cleaning_RemoveBlankLinesAfterOpeningBrace = values.ValueOrDefault(CleaningNativeSettings.RemoveBlankLinesAfterOpeningBrace, settings.Cleaning_RemoveBlankLinesAfterOpeningBrace);
            settings.Cleaning_RemoveBlankLinesAtBottom = values.ValueOrDefault(CleaningNativeSettings.RemoveBlankLinesAtBottom, settings.Cleaning_RemoveBlankLinesAtBottom);
            settings.Cleaning_RemoveBlankLinesAtTop = values.ValueOrDefault(CleaningNativeSettings.RemoveBlankLinesAtTop, settings.Cleaning_RemoveBlankLinesAtTop);
            settings.Cleaning_RemoveBlankLinesBeforeClosingBrace = values.ValueOrDefault(CleaningNativeSettings.RemoveBlankLinesBeforeClosingBrace, settings.Cleaning_RemoveBlankLinesBeforeClosingBrace);
            settings.Cleaning_RemoveBlankLinesBeforeClosingTags = values.ValueOrDefault(CleaningNativeSettings.RemoveBlankLinesBeforeClosingTags, settings.Cleaning_RemoveBlankLinesBeforeClosingTags);
            settings.Cleaning_RemoveBlankLinesBetweenChainedStatements = values.ValueOrDefault(CleaningNativeSettings.RemoveBlankLinesBetweenChainedStatements, settings.Cleaning_RemoveBlankLinesBetweenChainedStatements);
            settings.Cleaning_RemoveBlankSpacesBeforeClosingAngleBrackets = values.ValueOrDefault(CleaningNativeSettings.RemoveBlankSpacesBeforeClosingAngleBrackets, settings.Cleaning_RemoveBlankSpacesBeforeClosingAngleBrackets);
            settings.Cleaning_RemoveEndOfFileTrailingNewLine = values.ValueOrDefault(CleaningNativeSettings.RemoveEndOfFileTrailingNewLine, settings.Cleaning_RemoveEndOfFileTrailingNewLine);
            settings.Cleaning_RemoveEndOfLineWhitespace = values.ValueOrDefault(CleaningNativeSettings.RemoveEndOfLineWhitespace, settings.Cleaning_RemoveEndOfLineWhitespace);
            settings.Cleaning_RemoveMultipleConsecutiveBlankLines = values.ValueOrDefault(CleaningNativeSettings.RemoveMultipleConsecutiveBlankLines, settings.Cleaning_RemoveMultipleConsecutiveBlankLines);
            settings.Cleaning_RemoveRegions = values.ValueOrDefault(CleaningNativeSettings.RemoveRegions, settings.Cleaning_RemoveRegions);
            settings.Cleaning_UpdateAccessorsToBothBeSingleLineOrMultiLine = values.ValueOrDefault(CleaningNativeSettings.UpdateAccessorsToBothBeSingleLineOrMultiLine, settings.Cleaning_UpdateAccessorsToBothBeSingleLineOrMultiLine);
            settings.Cleaning_UpdateEndRegionDirectives = values.ValueOrDefault(CleaningNativeSettings.UpdateEndRegionDirectives, settings.Cleaning_UpdateEndRegionDirectives);
            settings.Cleaning_UpdateSingleLineMethods = values.ValueOrDefault(CleaningNativeSettings.UpdateSingleLineMethods, settings.Cleaning_UpdateSingleLineMethods);
            settings.Cleaning_ConvertToFileScopedNamespace = values.ValueOrDefault(CleaningNativeSettings.ConvertToFileScopedNamespace, settings.Cleaning_ConvertToFileScopedNamespace);
            settings.Cleaning_MoveUsingsOutsideNamespace = values.ValueOrDefault(CleaningNativeSettings.MoveUsingsOutsideNamespace, settings.Cleaning_MoveUsingsOutsideNamespace);
            settings.Cleaning_ConvertToVarWhenApparent = values.ValueOrDefault(CleaningNativeSettings.ConvertToVarWhenApparent, settings.Cleaning_ConvertToVarWhenApparent);
            settings.Cleaning_MakeFieldsReadonlyWhenSafe = values.ValueOrDefault(CleaningNativeSettings.MakeFieldsReadonlyWhenSafe, settings.Cleaning_MakeFieldsReadonlyWhenSafe);
            settings.Cleaning_SealClassesWhenSafe = values.ValueOrDefault(CleaningNativeSettings.SealClassesWhenSafe, settings.Cleaning_SealClassesWhenSafe);
            settings.Cleaning_UpdateFileHeader_HeaderPosition = values.ValueOrDefault(CleaningNativeSettings.UpdateFileHeaderHeaderPosition, settings.Cleaning_UpdateFileHeader_HeaderPosition);
            settings.Cleaning_UpdateFileHeader_HeaderUpdateMode = values.ValueOrDefault(CleaningNativeSettings.UpdateFileHeaderHeaderUpdateMode, settings.Cleaning_UpdateFileHeader_HeaderUpdateMode);
            settings.Cleaning_UpdateFileHeaderCPlusPlus = values.ValueOrDefault(CleaningNativeSettings.UpdateFileHeaderCPlusPlus, settings.Cleaning_UpdateFileHeaderCPlusPlus);
            settings.Cleaning_UpdateFileHeaderCSharp = values.ValueOrDefault(CleaningNativeSettings.UpdateFileHeaderCSharp, settings.Cleaning_UpdateFileHeaderCSharp);
            settings.Cleaning_UpdateFileHeaderCSS = values.ValueOrDefault(CleaningNativeSettings.UpdateFileHeaderCSS, settings.Cleaning_UpdateFileHeaderCSS);
            settings.Cleaning_UpdateFileHeaderFSharp = values.ValueOrDefault(CleaningNativeSettings.UpdateFileHeaderFSharp, settings.Cleaning_UpdateFileHeaderFSharp);
            settings.Cleaning_UpdateFileHeaderHTML = values.ValueOrDefault(CleaningNativeSettings.UpdateFileHeaderHTML, settings.Cleaning_UpdateFileHeaderHTML);
            settings.Cleaning_UpdateFileHeaderJavaScript = values.ValueOrDefault(CleaningNativeSettings.UpdateFileHeaderJavaScript, settings.Cleaning_UpdateFileHeaderJavaScript);
            settings.Cleaning_UpdateFileHeaderJSON = values.ValueOrDefault(CleaningNativeSettings.UpdateFileHeaderJSON, settings.Cleaning_UpdateFileHeaderJSON);
            settings.Cleaning_UpdateFileHeaderLESS = values.ValueOrDefault(CleaningNativeSettings.UpdateFileHeaderLESS, settings.Cleaning_UpdateFileHeaderLESS);
            settings.Cleaning_UpdateFileHeaderPHP = values.ValueOrDefault(CleaningNativeSettings.UpdateFileHeaderPHP, settings.Cleaning_UpdateFileHeaderPHP);
            settings.Cleaning_UpdateFileHeaderPowerShell = values.ValueOrDefault(CleaningNativeSettings.UpdateFileHeaderPowerShell, settings.Cleaning_UpdateFileHeaderPowerShell);
            settings.Cleaning_UpdateFileHeaderR = values.ValueOrDefault(CleaningNativeSettings.UpdateFileHeaderR, settings.Cleaning_UpdateFileHeaderR);
            settings.Cleaning_UpdateFileHeaderSCSS = values.ValueOrDefault(CleaningNativeSettings.UpdateFileHeaderSCSS, settings.Cleaning_UpdateFileHeaderSCSS);
            settings.Cleaning_UpdateFileHeaderTypeScript = values.ValueOrDefault(CleaningNativeSettings.UpdateFileHeaderTypeScript, settings.Cleaning_UpdateFileHeaderTypeScript);
            settings.Cleaning_UpdateFileHeaderVB = values.ValueOrDefault(CleaningNativeSettings.UpdateFileHeaderVB, settings.Cleaning_UpdateFileHeaderVB);
            settings.Cleaning_UpdateFileHeaderXAML = values.ValueOrDefault(CleaningNativeSettings.UpdateFileHeaderXAML, settings.Cleaning_UpdateFileHeaderXAML);
            settings.Cleaning_UpdateFileHeaderXML = values.ValueOrDefault(CleaningNativeSettings.UpdateFileHeaderXML, settings.Cleaning_UpdateFileHeaderXML);
            settings.Save();

            DebugLog($"OnCleaningSettingsChanged saved. settings.Cleaning_ConvertToFileScopedNamespace is now: {settings.Cleaning_ConvertToFileScopedNamespace}.");
        }

        private void OnCollapsingSettingsChanged(SettingValues values)
        {
            var settings = Properties.Settings.Default;

            settings.Collapsing_CollapseSolutionWhenOpened = values.ValueOrDefault(CollapsingNativeSettings.CollapseSolutionWhenOpened, settings.Collapsing_CollapseSolutionWhenOpened);
            settings.Collapsing_KeepSoloProjectExpanded = values.ValueOrDefault(CollapsingNativeSettings.KeepSoloProjectExpanded, settings.Collapsing_KeepSoloProjectExpanded);
            settings.Save();
        }

        private void OnDiggingSettingsChanged(SettingValues values)
        {
            var settings = Properties.Settings.Default;

            settings.Digging_CenterOnWhole = values.ValueOrDefault(DiggingNativeSettings.CenterOnWhole, settings.Digging_CenterOnWhole);
            settings.Digging_ComplexityAlertThreshold = values.ValueOrDefault(DiggingNativeSettings.ComplexityAlertThreshold, settings.Digging_ComplexityAlertThreshold);
            settings.Digging_ComplexityWarningThreshold = values.ValueOrDefault(DiggingNativeSettings.ComplexityWarningThreshold, settings.Digging_ComplexityWarningThreshold);
            settings.Digging_IndentationMargin = values.ValueOrDefault(DiggingNativeSettings.IndentationMargin, settings.Digging_IndentationMargin);
            settings.Digging_PrimarySortOrder = values.ValueOrDefault(DiggingNativeSettings.PrimarySortOrder, settings.Digging_PrimarySortOrder);
            settings.Digging_SecondarySortTypeByName = values.ValueOrDefault(DiggingNativeSettings.SecondarySortTypeByName, settings.Digging_SecondarySortTypeByName);
            settings.Digging_ShowItemComplexity = values.ValueOrDefault(DiggingNativeSettings.ShowItemComplexity, settings.Digging_ShowItemComplexity);
            settings.Digging_ShowItemMetadata = values.ValueOrDefault(DiggingNativeSettings.ShowItemMetadata, settings.Digging_ShowItemMetadata);
            settings.Digging_ShowItemTypes = values.ValueOrDefault(DiggingNativeSettings.ShowItemTypes, settings.Digging_ShowItemTypes);
            settings.Digging_ShowMethodParameters = values.ValueOrDefault(DiggingNativeSettings.ShowMethodParameters, settings.Digging_ShowMethodParameters);
            settings.Digging_SynchronizeOutlining = values.ValueOrDefault(DiggingNativeSettings.SynchronizeOutlining, settings.Digging_SynchronizeOutlining);
            settings.Save();
        }

        private void OnFindingSettingsChanged(SettingValues values)
        {
            var settings = Properties.Settings.Default;

            settings.Finding_ClearSolutionExplorerSearch = values.ValueOrDefault(FindingNativeSettings.ClearSolutionExplorerSearch, settings.Finding_ClearSolutionExplorerSearch);
            settings.Finding_TemporarilyOpenSolutionFolders = values.ValueOrDefault(FindingNativeSettings.TemporarilyOpenSolutionFolders, settings.Finding_TemporarilyOpenSolutionFolders);
            settings.Save();
        }

        private void OnFormattingSettingsChanged(SettingValues values)
        {
            var settings = Properties.Settings.Default;

            settings.Formatting_CommentRunDuringCleanup = values.ValueOrDefault(FormattingNativeSettings.CommentRunDuringCleanup, settings.Formatting_CommentRunDuringCleanup);
            settings.Formatting_CommentSkipWrapOnLastWord = values.ValueOrDefault(FormattingNativeSettings.CommentSkipWrapOnLastWord, settings.Formatting_CommentSkipWrapOnLastWord);
            settings.Formatting_CommentWrapColumn = values.ValueOrDefault(FormattingNativeSettings.CommentWrapColumn, settings.Formatting_CommentWrapColumn);
            settings.Formatting_CommentXmlAlignParamTags = values.ValueOrDefault(FormattingNativeSettings.CommentXmlAlignParamTags, settings.Formatting_CommentXmlAlignParamTags);
            settings.Formatting_CommentXmlKeepTagsTogether = values.ValueOrDefault(FormattingNativeSettings.CommentXmlKeepTagsTogether, settings.Formatting_CommentXmlKeepTagsTogether);
            settings.Formatting_CommentXmlSpaceSingleTags = values.ValueOrDefault(FormattingNativeSettings.CommentXmlSpaceSingleTags, settings.Formatting_CommentXmlSpaceSingleTags);
            settings.Formatting_CommentXmlSpaceTags = values.ValueOrDefault(FormattingNativeSettings.CommentXmlSpaceTags, settings.Formatting_CommentXmlSpaceTags);
            settings.Formatting_CommentXmlSplitAllTags = values.ValueOrDefault(FormattingNativeSettings.CommentXmlSplitAllTags, settings.Formatting_CommentXmlSplitAllTags);
            settings.Formatting_CommentXmlSplitSummaryTagToMultipleLines = values.ValueOrDefault(FormattingNativeSettings.CommentXmlSplitSummaryTagToMultipleLines, settings.Formatting_CommentXmlSplitSummaryTagToMultipleLines);
            settings.Formatting_CommentXmlTagsToLowerCase = values.ValueOrDefault(FormattingNativeSettings.CommentXmlTagsToLowerCase, settings.Formatting_CommentXmlTagsToLowerCase);
            settings.Formatting_CommentXmlValueIndent = values.ValueOrDefault(FormattingNativeSettings.CommentXmlValueIndent, settings.Formatting_CommentXmlValueIndent);
            settings.Save();
        }

        private void OnProgressingSettingsChanged(SettingValues values)
        {
            var settings = Properties.Settings.Default;

            settings.Progressing_HideBuildProgressOnBuildStop = values.ValueOrDefault(ProgressingNativeSettings.HideBuildProgressOnBuildStop, settings.Progressing_HideBuildProgressOnBuildStop);
            settings.Progressing_ShowBuildProgressOnBuildStart = values.ValueOrDefault(ProgressingNativeSettings.ShowBuildProgressOnBuildStart, settings.Progressing_ShowBuildProgressOnBuildStart);
            settings.Progressing_ShowProgressOnWindowsTaskbar = values.ValueOrDefault(ProgressingNativeSettings.ShowProgressOnWindowsTaskbar, settings.Progressing_ShowProgressOnWindowsTaskbar);
            settings.Save();
        }

        private void OnReorganizingSettingsChanged(SettingValues values)
        {
            var settings = Properties.Settings.Default;

            settings.Reorganizing_AlphabetizeMembersOfTheSameGroup = values.ValueOrDefault(ReorganizingNativeSettings.AlphabetizeMembersOfTheSameGroup, settings.Reorganizing_AlphabetizeMembersOfTheSameGroup);
            settings.Reorganizing_ExplicitMembersAtEnd = values.ValueOrDefault(ReorganizingNativeSettings.ExplicitMembersAtEnd, settings.Reorganizing_ExplicitMembersAtEnd);
            settings.Reorganizing_KeepMembersWithinRegions = values.ValueOrDefault(ReorganizingNativeSettings.KeepMembersWithinRegions, settings.Reorganizing_KeepMembersWithinRegions);
            settings.Reorganizing_PerformWhenPreprocessorConditionals = values.ValueOrDefault(ReorganizingNativeSettings.PerformWhenPreprocessorConditionals, settings.Reorganizing_PerformWhenPreprocessorConditionals);
            settings.Reorganizing_PrimaryOrderByAccessLevel = values.ValueOrDefault(ReorganizingNativeSettings.PrimaryOrderByAccessLevel, settings.Reorganizing_PrimaryOrderByAccessLevel);
            settings.Reorganizing_ReverseOrderByAccessLevel = values.ValueOrDefault(ReorganizingNativeSettings.ReverseOrderByAccessLevel, settings.Reorganizing_ReverseOrderByAccessLevel);
            settings.Reorganizing_RunAtStartOfCleanup = values.ValueOrDefault(ReorganizingNativeSettings.RunAtStartOfCleanup, settings.Reorganizing_RunAtStartOfCleanup);

            foreach (var item in values.ValueOrDefault(ReorganizingNativeSettings.MemberTypes, Array.Empty<MemberTypeArrayItem>()))
            {
                switch (item.Kind)
                {
                    case "Classes": settings.Reorganizing_MemberTypeClasses = item.ToSerializedMemberTypeSetting(); break;
                    case "Constructors": settings.Reorganizing_MemberTypeConstructors = item.ToSerializedMemberTypeSetting(); break;
                    case "Delegates": settings.Reorganizing_MemberTypeDelegates = item.ToSerializedMemberTypeSetting(); break;
                    case "Destructors": settings.Reorganizing_MemberTypeDestructors = item.ToSerializedMemberTypeSetting(); break;
                    case "Enums": settings.Reorganizing_MemberTypeEnums = item.ToSerializedMemberTypeSetting(); break;
                    case "Events": settings.Reorganizing_MemberTypeEvents = item.ToSerializedMemberTypeSetting(); break;
                    case "Fields": settings.Reorganizing_MemberTypeFields = item.ToSerializedMemberTypeSetting(); break;
                    case "Indexers": settings.Reorganizing_MemberTypeIndexers = item.ToSerializedMemberTypeSetting(); break;
                    case "Interfaces": settings.Reorganizing_MemberTypeInterfaces = item.ToSerializedMemberTypeSetting(); break;
                    case "Methods": settings.Reorganizing_MemberTypeMethods = item.ToSerializedMemberTypeSetting(); break;
                    case "Properties": settings.Reorganizing_MemberTypeProperties = item.ToSerializedMemberTypeSetting(); break;
                    case "Structs": settings.Reorganizing_MemberTypeStructs = item.ToSerializedMemberTypeSetting(); break;
                }
            }

            settings.Reorganizing_RegionsIncludeAccessLevel = values.ValueOrDefault(ReorganizingNativeSettings.RegionsIncludeAccessLevel, settings.Reorganizing_RegionsIncludeAccessLevel);
            settings.Reorganizing_RegionsIncludeAccessLevelForMethodsOnly = values.ValueOrDefault(ReorganizingNativeSettings.RegionsIncludeAccessLevelForMethodsOnly, settings.Reorganizing_RegionsIncludeAccessLevelForMethodsOnly);
            settings.Reorganizing_RegionsInsertKeepEvenIfEmpty = values.ValueOrDefault(ReorganizingNativeSettings.RegionsInsertKeepEvenIfEmpty, settings.Reorganizing_RegionsInsertKeepEvenIfEmpty);
            settings.Reorganizing_RegionsInsertNewRegions = values.ValueOrDefault(ReorganizingNativeSettings.RegionsInsertNewRegions, settings.Reorganizing_RegionsInsertNewRegions);
            settings.Reorganizing_RegionsRemoveExistingRegions = values.ValueOrDefault(ReorganizingNativeSettings.RegionsRemoveExistingRegions, settings.Reorganizing_RegionsRemoveExistingRegions);
            settings.Save();
        }

        private void OnThirdPartySettingsChanged(SettingValues values)
        {
            var settings = Properties.Settings.Default;

            settings.ThirdParty_UseJetBrainsReSharperCleanup = values.ValueOrDefault(ThirdPartyNativeSettings.UseJetBrainsReSharperCleanup, settings.ThirdParty_UseJetBrainsReSharperCleanup);
            settings.ThirdParty_UseTelerikJustCodeCleanup = values.ValueOrDefault(ThirdPartyNativeSettings.UseTelerikJustCodeCleanup, settings.ThirdParty_UseTelerikJustCodeCleanup);
            settings.ThirdParty_UseXAMLStylerCleanup = values.ValueOrDefault(ThirdPartyNativeSettings.UseXAMLStylerCleanup, settings.ThirdParty_UseXAMLStylerCleanup);
            settings.ThirdParty_OtherCleaningCommandsExpression = values.ValueOrDefault(ThirdPartyNativeSettings.OtherCleaningCommandsExpression, settings.ThirdParty_OtherCleaningCommandsExpression);
            settings.Save();
        }
    }
#pragma warning restore VSEXTPREVIEW_SETTINGS
}


