namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// The outcome kind of <see cref="MoveUsingsOutsideNamespaceConverter.MoveUsingsOutsideAsync" />.
/// </summary>
public enum MoveUsingsOutsideNamespaceStatus
{
    /// <summary>
    /// No namespace declaration contains using directives; the document is unchanged.
    /// </summary>
    NoUsingsInsideNamespace,

    /// <summary>
    /// The using directives were qualified and moved; <see cref="MoveUsingsOutsideNamespaceResult.Text" /> holds the new text.
    /// </summary>
    Moved,

    /// <summary>
    /// The move was not safe; the document is unchanged and <see cref="MoveUsingsOutsideNamespaceResult.Reason" /> explains why.
    /// </summary>
    Skipped,
}

/// <summary>
/// The result of moving the using directives of one document outside its namespaces.
/// </summary>
public sealed class MoveUsingsOutsideNamespaceResult
{
    private MoveUsingsOutsideNamespaceResult(MoveUsingsOutsideNamespaceStatus status, string text, string reason)
    {
        Status = status;
        Text = text;
        Reason = reason;
    }

    /// <summary>
    /// Gets the outcome kind.
    /// </summary>
    public MoveUsingsOutsideNamespaceStatus Status { get; }

    /// <summary>
    /// Gets the new document text when <see cref="Status" /> is <see cref="MoveUsingsOutsideNamespaceStatus.Moved" />, otherwise null.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets why the move was skipped when <see cref="Status" /> is <see cref="MoveUsingsOutsideNamespaceStatus.Skipped" />, otherwise null.
    /// </summary>
    public string Reason { get; }

    internal static MoveUsingsOutsideNamespaceResult NoUsingsInsideNamespace { get; } =
        new MoveUsingsOutsideNamespaceResult(MoveUsingsOutsideNamespaceStatus.NoUsingsInsideNamespace, null, null);

    internal static MoveUsingsOutsideNamespaceResult Moved(string text) =>
        new MoveUsingsOutsideNamespaceResult(MoveUsingsOutsideNamespaceStatus.Moved, text, null);

    internal static MoveUsingsOutsideNamespaceResult Skipped(string reason) =>
        new MoveUsingsOutsideNamespaceResult(MoveUsingsOutsideNamespaceStatus.Skipped, null, reason);
}
