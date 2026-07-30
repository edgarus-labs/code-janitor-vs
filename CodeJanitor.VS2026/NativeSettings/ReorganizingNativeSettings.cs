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

        [VisualStudioContribution]
        internal static SettingCategory TypesCategory { get; } = new("codeJanitorReorganizingTypes", "Types", ReorganizingCategory);

        [VisualStudioContribution]
        internal static Setting.String MemberTypeClasses { get; } = new(
            "codeJanitorReorganizingTypesMemberTypeClasses", "Classes", TypesCategory, defaultValue: "Classes||12||Classes");

        [VisualStudioContribution]
        internal static Setting.String MemberTypeConstructors { get; } = new(
            "codeJanitorReorganizingTypesMemberTypeConstructors", "Constructors", TypesCategory, defaultValue: "Constructors||2||Constructors");

        [VisualStudioContribution]
        internal static Setting.String MemberTypeDelegates { get; } = new(
            "codeJanitorReorganizingTypesMemberTypeDelegates", "Delegates", TypesCategory, defaultValue: "Delegates||4||Delegates");

        [VisualStudioContribution]
        internal static Setting.String MemberTypeDestructors { get; } = new(
            "codeJanitorReorganizingTypesMemberTypeDestructors", "Destructors", TypesCategory, defaultValue: "Destructors||3||Destructors");

        [VisualStudioContribution]
        internal static Setting.String MemberTypeEnums { get; } = new(
            "codeJanitorReorganizingTypesMemberTypeEnums", "Enums", TypesCategory, defaultValue: "Enums||6||Enums");

        [VisualStudioContribution]
        internal static Setting.String MemberTypeEvents { get; } = new(
            "codeJanitorReorganizingTypesMemberTypeEvents", "Events", TypesCategory, defaultValue: "Events||5||Events");

        [VisualStudioContribution]
        internal static Setting.String MemberTypeFields { get; } = new(
            "codeJanitorReorganizingTypesMemberTypeFields", "Fields", TypesCategory, defaultValue: "Fields||1||Fields");

        [VisualStudioContribution]
        internal static Setting.String MemberTypeIndexers { get; } = new(
            "codeJanitorReorganizingTypesMemberTypeIndexers", "Indexers", TypesCategory, defaultValue: "Indexers||9||Indexers");

        [VisualStudioContribution]
        internal static Setting.String MemberTypeInterfaces { get; } = new(
            "codeJanitorReorganizingTypesMemberTypeInterfaces", "Interfaces", TypesCategory, defaultValue: "Interfaces||7||Interfaces");

        [VisualStudioContribution]
        internal static Setting.String MemberTypeMethods { get; } = new(
            "codeJanitorReorganizingTypesMemberTypeMethods", "Methods", TypesCategory, defaultValue: "Methods||10||Methods");

        [VisualStudioContribution]
        internal static Setting.String MemberTypeProperties { get; } = new(
            "codeJanitorReorganizingTypesMemberTypeProperties", "Properties", TypesCategory, defaultValue: "Properties||8||Properties");

        [VisualStudioContribution]
        internal static Setting.String MemberTypeStructs { get; } = new(
            "codeJanitorReorganizingTypesMemberTypeStructs", "Structs", TypesCategory, defaultValue: "Structs||11||Structs");

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
