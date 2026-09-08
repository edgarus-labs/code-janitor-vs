using EnvDTE;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Model.CodeItems;
using System;
using System.IO;
using System.Linq;

namespace CodeJanitor.Helpers;

/// <summary>
/// Encapsulates the extracted code context for AI operations.
/// </summary>
internal sealed class AiCodeContext
{
    /// <summary>
    /// Gets or sets the target name.
    /// </summary>
    public string TargetName { get; set; }

    /// <summary>
    /// Gets or sets the code snippet.
    /// </summary>
    public string CodeSnippet { get; set; }

    /// <summary>
    /// Gets or sets the file path.
    /// </summary>
    public string FilePath { get; set; }

    /// <summary>
    /// Gets or sets the replace action.
    /// </summary>
    public Action<string> ReplaceAction { get; set; }

    /// <summary>
    /// Gets or sets the insert action.
    /// </summary>
    public Action<string> InsertAction { get; set; }
}
