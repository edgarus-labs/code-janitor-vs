namespace CodeJanitor;

/// <summary>
/// presents the metadata of a Visual Studio Extension (VSIX) package, including its identification, descriptive details, supported language, versioning, authorship, and categorization tags.
/// </summary>
internal static class Vsix
{
    /// <summary>
    /// The id.
    /// </summary>
    public const string Id = "b1b6d05b-97f7-426d-9d6f-fdf8c7662ab2";

    /// <summary>
    /// The name.
    /// </summary>
    public const string Name = "CodeJanitor";

    /// <summary>
    /// The description.
    /// </summary>
    public const string Description = "CodeJanitor (a fork of CodeMaid) is an open source Visual Studio extension to cleanup and simplify our C#, C++, F#, VB, PHP, PowerShell, R, JSON, XAML, XML, ASP, HTML, CSS, LESS, SCSS, JavaScript and TypeScript coding.";

    /// <summary>
    /// The language.
    /// </summary>
    public const string Language = "en-US";

    /// <summary>
    /// The version.
    /// </summary>
    public const string Version = "0.1.0.55";

    /// <summary>
    /// The author.
    /// </summary>
    public const string Author = "John Doe";

    /// <summary>
    /// The tags.
    /// </summary>
    public const string Tags = "build, code, c#, beautify, cleanup, cleaning, digging, reorganizing, formatting";
}
