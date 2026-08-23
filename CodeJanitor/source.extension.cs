namespace CodeJanitor;

/// <summary>
/// The Vsix class represents metadata for a Visual Studio extension package, containing its identifier, name, description, language, version, author, and tags.
/// </summary>
internal static class Vsix
{
    /// <summary>
    /// The id.
    /// </summary>
    public const string Id = "4c82e17d-927e-42d2-8460-b473ac7df316";

    /// <summary>
    /// The name.
    /// </summary>
    public const string Name = "CodeJanitor";

    /// <summary>
    /// The description.
    /// </summary>
    public const string Description = "CodeJanitor is an open source Visual Studio extension to cleanup and simplify our C#, C++, F#, VB, PHP, PowerShell, R, JSON, XAML, XML, ASP, HTML, CSS, LESS, SCSS, JavaScript and TypeScript coding.";

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
