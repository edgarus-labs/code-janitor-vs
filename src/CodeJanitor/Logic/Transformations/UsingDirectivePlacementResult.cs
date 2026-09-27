namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// The result of moving the using directives of one document outside or inside its namespace.
/// </summary>
public sealed class UsingDirectivePlacementResult
{
    private UsingDirectivePlacementResult(UsingDirectivePlacementStatus status, string text, string reason)
    {
        Status = status;
        Text = text;
        Reason = reason;
    }

    /// <summary>
    /// Gets the outcome kind.
    /// </summary>
    public UsingDirectivePlacementStatus Status { get; }

    /// <summary>
    /// Gets the new document text when <see cref="Status" /> is <see cref="UsingDirectivePlacementStatus.Moved" />, otherwise null.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets why the move was skipped when <see cref="Status" /> is <see cref="UsingDirectivePlacementStatus.Skipped" />, otherwise null.
    /// </summary>
    public string Reason { get; }

    internal static UsingDirectivePlacementResult NothingToMove { get; } =
        new UsingDirectivePlacementResult(UsingDirectivePlacementStatus.NothingToMove, null, null);

    internal static UsingDirectivePlacementResult Moved(string text) =>
        new UsingDirectivePlacementResult(UsingDirectivePlacementStatus.Moved, text, null);

    internal static UsingDirectivePlacementResult Skipped(string reason) =>
        new UsingDirectivePlacementResult(UsingDirectivePlacementStatus.Skipped, null, reason);
}
