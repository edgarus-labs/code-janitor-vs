namespace CodeJanitor.Logic.Cleaning;

internal enum TopLevelTypeSplitSkipReason
{
    None,
    EmptySource,
    UnsupportedStructure,
    NotMultipleEligibleTypes
}
