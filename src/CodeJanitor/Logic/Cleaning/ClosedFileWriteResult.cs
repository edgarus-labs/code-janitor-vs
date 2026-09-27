namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// The outcome of writing the cleaned text of a closed file directly to disk.
/// </summary>
internal enum ClosedFileWriteResult
{
    /// <summary>
    /// The file was written.
    /// </summary>
    Written,

    /// <summary>
    /// The file changed on disk since its text was read; nothing was written.
    /// </summary>
    ChangedOnDisk,

    /// <summary>
    /// The file cannot be written directly (for example it is read-only or needs a source control checkout); nothing
    /// was written, and the change has to go through Visual Studio.
    /// </summary>
    NotWritable
}
