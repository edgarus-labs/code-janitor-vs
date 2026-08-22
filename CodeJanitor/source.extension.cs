namespace CodeJanitor
{
    /// <summary>
    /// The Vsix class represents metadata for a Visual Studio extension package, containing its identifier, name, description, language, version, author, and tags.
    /// </summary>
    static class Vsix
    {
        public const string Id = "4c82e17d-927e-42d2-8460-b473ac7df316";
        public const string Name = "CodeJanitor";
        public const string Description = "CodeJanitor is an open source Visual Studio extension to cleanup and simplify our C#, C++, F#, VB, PHP, PowerShell, R, JSON, XAML, XML, ASP, HTML, CSS, LESS, SCSS, JavaScript and TypeScript coding.";
        public const string Language = "en-US";
        public const string Version = "0.1";
        public const string Author = "John Doe";
        public const string Tags = "build, code, c#, beautify, cleanup, cleaning, digging, reorganizing, formatting";
    }
}
