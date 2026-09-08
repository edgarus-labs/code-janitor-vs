using CodeJanitor.Helpers;
using CodeJanitor.Properties;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Result of an AI Clean Refactoring operation containing the refactored code and explanation.
/// </summary>
internal sealed class AiRefactorResult
{
    /// <summary>
    /// Gets or sets the success.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the refactored code.
    /// </summary>
    public string RefactoredCode { get; set; }

    /// <summary>
    /// Gets or sets the explanation.
    /// </summary>
    public string Explanation { get; set; }

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string ErrorMessage { get; set; }
}
