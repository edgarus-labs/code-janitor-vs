namespace CodeJanitor.UI.Enumerations;

/// <summary>
/// is an enum that specifies the placement of a header within a source code document, indicating whether it should appear at the document start or after the using directives.
/// </summary>
public enum HeaderPosition
{
    DocumentStart = 0,
    AfterUsings = 1,
}
