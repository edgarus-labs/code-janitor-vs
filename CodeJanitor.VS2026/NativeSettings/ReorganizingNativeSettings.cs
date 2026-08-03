using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace CodeJanitor.NativeSettings
{
    // Reorganizing category and its General/Types/Regions sub-pages, migrated from
    // CodeJanitorShared/Properties/Settings.settings (Reorganizing_*).
#pragma warning disable VSEXTPREVIEW_SETTINGS // Settings APIs are preview.
    internal static class ReorganizingNativeSettings
    {
        [VisualStudioContribution]
        internal static SettingCategory ReorganizingCategory { get; } = new("codeJanitorReorganizing", "Reorganizing", NativeSettingCategories.RootCategory);

        [VisualStudioContribution]
        internal static SettingCategory GeneralCategory { get; } = new("codeJanitorReorganizingGeneral", "General", ReorganizingCategory);

        [VisualStudioContribution]
        internal static Setting.Boolean AlphabetizeMembersOfTheSameGroup { get; } = new(
            "codeJanitorReorganizingAlphabetizeMembersOfTheSameGroup", "Alphabetize members of the same group", GeneralCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Boolean ExplicitMembersAtEnd { get; } = new(
            "codeJanitorReorganizingExplicitMembersAtEnd", "Explicit interface members at the end", GeneralCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean KeepMembersWithinRegions { get; } = new(
            "codeJanitorReorganizingKeepMembersWithinRegions", "Keep members within regions", GeneralCategory, defaultValue: true);

        [VisualStudioContribution]
        internal static Setting.Integer PerformWhenPreprocessorConditionals { get; } = new(
            "codeJanitorReorganizingPerformWhenPreprocessorConditionals", "Perform when preprocessor conditionals are present", GeneralCategory, defaultValue: 0);

        [VisualStudioContribution]
        internal static Setting.Boolean PrimaryOrderByAccessLevel { get; } = new(
            "codeJanitorReorganizingPrimaryOrderByAccessLevel", "Primary order by access level", GeneralCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean ReverseOrderByAccessLevel { get; } = new(
            "codeJanitorReorganizingReverseOrderByAccessLevel", "Reverse order by access level", GeneralCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean RunAtStartOfCleanup { get; } = new(
            "codeJanitorReorganizingRunAtStartOfCleanup", "Run at start of cleanup", GeneralCategory, defaultValue: false);

        // Each classic Reorganizing_MemberTypeX setting is one System.String storing a
        // serialized "DefaultName||Order||EffectiveName" triple (see MemberTypeSetting.cs).
        // The old custom WPF control edited all 12 as one drag/drop/rename list; here they're
        // exposed as a single Setting.ObjectArray - the same grid-of-rows primitive used for
        // things like the NuGet Package Manager sources list - with an Order/Display name
        // column per row instead of showing the raw serialized string.
        [VisualStudioContribution]
        internal static SettingCategory TypesCategory { get; } = new("codeJanitorReorganizingTypes", "Types", ReorganizingCategory);

        internal const string MemberTypeKindPropertyId = "kind";
        internal const string MemberTypeOrderPropertyId = "order";
        internal const string MemberTypeNamePropertyId = "name";

        private static ArraySettingItemProperty.String MemberTypeKindProperty { get; } = new(
            MemberTypeKindPropertyId, "Type", defaultValue: "")
        { IsEditable = false };

        private static ArraySettingItemProperty.Integer MemberTypeOrderProperty { get; } = new(
            MemberTypeOrderPropertyId, "Order", defaultValue: 0);

        private static ArraySettingItemProperty.String MemberTypeNameProperty { get; } = new(
            MemberTypeNamePropertyId, "Display name", defaultValue: "");

        [VisualStudioContribution]
        internal static Setting.ObjectArray MemberTypes { get; } = new(
            "codeJanitorReorganizingTypesMemberTypes",
            "Member types",
            TypesCategory,
            new ArraySettingItemProperty[] { MemberTypeKindProperty, MemberTypeOrderProperty, MemberTypeNameProperty },
            new[]
            {
                new ArraySettingItem { { MemberTypeKindPropertyId, "Fields" }, { MemberTypeOrderPropertyId, 1 }, { MemberTypeNamePropertyId, "Fields" } },
                new ArraySettingItem { { MemberTypeKindPropertyId, "Constructors" }, { MemberTypeOrderPropertyId, 2 }, { MemberTypeNamePropertyId, "Constructors" } },
                new ArraySettingItem { { MemberTypeKindPropertyId, "Destructors" }, { MemberTypeOrderPropertyId, 3 }, { MemberTypeNamePropertyId, "Destructors" } },
                new ArraySettingItem { { MemberTypeKindPropertyId, "Delegates" }, { MemberTypeOrderPropertyId, 4 }, { MemberTypeNamePropertyId, "Delegates" } },
                new ArraySettingItem { { MemberTypeKindPropertyId, "Events" }, { MemberTypeOrderPropertyId, 5 }, { MemberTypeNamePropertyId, "Events" } },
                new ArraySettingItem { { MemberTypeKindPropertyId, "Enums" }, { MemberTypeOrderPropertyId, 6 }, { MemberTypeNamePropertyId, "Enums" } },
                new ArraySettingItem { { MemberTypeKindPropertyId, "Interfaces" }, { MemberTypeOrderPropertyId, 7 }, { MemberTypeNamePropertyId, "Interfaces" } },
                new ArraySettingItem { { MemberTypeKindPropertyId, "Properties" }, { MemberTypeOrderPropertyId, 8 }, { MemberTypeNamePropertyId, "Properties" } },
                new ArraySettingItem { { MemberTypeKindPropertyId, "Indexers" }, { MemberTypeOrderPropertyId, 9 }, { MemberTypeNamePropertyId, "Indexers" } },
                new ArraySettingItem { { MemberTypeKindPropertyId, "Methods" }, { MemberTypeOrderPropertyId, 10 }, { MemberTypeNamePropertyId, "Methods" } },
                new ArraySettingItem { { MemberTypeKindPropertyId, "Structs" }, { MemberTypeOrderPropertyId, 11 }, { MemberTypeNamePropertyId, "Structs" } },
                new ArraySettingItem { { MemberTypeKindPropertyId, "Classes" }, { MemberTypeOrderPropertyId, 12 }, { MemberTypeNamePropertyId, "Classes" } },
            })
        {
            // The 12 member types are fixed - users reorder/rename/group them, not add/remove rows.
            AllowAdditionsAndRemovals = false,
        };

        [VisualStudioContribution]
        internal static SettingCategory RegionsCategory { get; } = new("codeJanitorReorganizingRegions", "Regions", ReorganizingCategory);

        [VisualStudioContribution]
        internal static Setting.Boolean RegionsIncludeAccessLevel { get; } = new(
            "codeJanitorReorganizingRegionsIncludeAccessLevel", "Include access level", RegionsCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean RegionsIncludeAccessLevelForMethodsOnly { get; } = new(
            "codeJanitorReorganizingRegionsIncludeAccessLevelForMethodsOnly", "Include access level for methods only", RegionsCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean RegionsInsertKeepEvenIfEmpty { get; } = new(
            "codeJanitorReorganizingRegionsInsertKeepEvenIfEmpty", "Insert/keep even if empty", RegionsCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean RegionsInsertNewRegions { get; } = new(
            "codeJanitorReorganizingRegionsInsertNewRegions", "Insert new regions", RegionsCategory, defaultValue: false);

        [VisualStudioContribution]
        internal static Setting.Boolean RegionsRemoveExistingRegions { get; } = new(
            "codeJanitorReorganizingRegionsRemoveExistingRegions", "Remove existing regions", RegionsCategory, defaultValue: false);
    }
#pragma warning restore VSEXTPREVIEW_SETTINGS
}
