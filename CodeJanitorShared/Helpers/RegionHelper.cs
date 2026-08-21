using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;

namespace CodeJanitor.Helpers;

/// <summary>
/// A static helper class for working with regions.
/// </summary>

internal static class RegionHelper
{
    /// <summary>
    /// This method extracts a region name from the supplied text based on the code language (C# trims after the first 8 characters; Visual Basic also strips surrounding quotes), throws NotImplementedException for unsupported languages, and enforces a UI-thread requirement.
    /// </summary>
    /// <param name="editPoint">The edit point.</param>
    /// <param name="regionText">The region text.</param>
    /// <returns>A string value produced by this method.</returns>
    /// <exception cref="NotImplementedException">Thrown when method validation or execution fails for this exception type.</exception>

    internal static string GetRegionName(EditPoint editPoint, string regionText)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var codeLanguage = editPoint.GetCodeLanguage();
        switch (codeLanguage)
        {
            case CodeLanguage.CSharp:
                return regionText.Substring(8).Trim();

            case CodeLanguage.VisualBasic:
                // Remove the leading/trailing double quote character.
                var text = regionText.Substring(8).Trim();
                text = text.Substring(1, text.Length - 2);
                return text;

            default:
                throw new NotImplementedException($"Regions are not supported for '{codeLanguage}'.");
        }
    }

    /// <summary>
    /// Returns a language-specific region directive string for C# or Visual Basic based on the edit point&apos;s code language, throwing NotImplementedException for unsupported languages and requiring the caller to be on the UI thread.
    /// </summary>
    /// <param name="editPoint">The edit point.</param>
    /// <param name="name">The name.</param>
    /// <returns>A string value produced by this method.</returns>
    /// <exception cref="NotImplementedException">Thrown when method validation or execution fails for this exception type.</exception>

    internal static string GetRegionTagText(EditPoint editPoint, string name = null)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var codeLanguage = editPoint.GetCodeLanguage();
        switch (codeLanguage)
        {
            case CodeLanguage.CSharp:
                return "#region " +
                       (name ?? string.Empty);

            case CodeLanguage.VisualBasic:
                return "#Region " +
                       (name != null ? $"\"{name}\"" : string.Empty);

            default:
                throw new NotImplementedException($"Regions are not supported for '{codeLanguage}'.");
        }
    }

    /// <summary>
    /// Returns the language-specific end region directive for the given edit point (C# &quot;#endregion&quot; or VB &quot;#End Region&quot;), throwing NotImplementedException for unsupported languages, and enforces UI thread execution via ThrowIfNotOnUIThread as a side effect.
    /// </summary>
    /// <param name="editPoint">The edit point.</param>
    /// <returns>A string value produced by this method.</returns>
    /// <exception cref="NotImplementedException">Thrown when method validation or execution fails for this exception type.</exception>

    internal static string GetEndRegionTagText(EditPoint editPoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var codeLanguage = editPoint.GetCodeLanguage();
        switch (codeLanguage)
        {
            case CodeLanguage.CSharp:
                return "#endregion";

            case CodeLanguage.VisualBasic:
                return "#End Region";

            default:
                throw new NotImplementedException($"Regions are not supported for '{codeLanguage}'.");
        }
    }

    /// <summary>
    /// Determines whether an edit point&apos;s code language supports updating end-region directives by returning true only for C# and false otherwise, while throwing if not called on the UI thread.
    /// </summary>
    /// <param name="editPoint">The edit point.</param>
    /// <returns>A bool value produced by this method.</returns>

    internal static bool LanguageSupportsUpdatingEndRegionDirectives(EditPoint editPoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var codeLanguage = editPoint.GetCodeLanguage();

        switch (codeLanguage)
        {
            case CodeLanguage.CSharp:
                return true;

            default:
                return false;
        }
    }
}
