namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// The outcome kind of <see cref="UsingDirectivePlacementConverter.MoveUsingsOutsideAsync" /> and
/// <see cref="UsingDirectivePlacementConverter.MoveUsingsInsideAsync" />.
/// </summary>
public enum UsingDirectivePlacementStatus
{
    /// <summary>
    /// No using directive is on the side it would move from; the document is unchanged.
    /// </summary>
    NothingToMove,

    /// <summary>
    /// The using directives were qualified where needed and moved; <see cref="UsingDirectivePlacementResult.Text" />
    /// holds the new text.
    /// </summary>
    Moved,

    /// <summary>
    /// The move was not safe; the document is unchanged and <see cref="UsingDirectivePlacementResult.Reason" /> explains why.
    /// </summary>
    Skipped,
}
