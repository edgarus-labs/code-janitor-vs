using CodeJanitor.Helpers;
using CodeJanitor.Logic.Transformations;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// The outcome of one attempt of the semantic using move during cleanup.
/// </summary>
internal enum UsingsMoveOutcome
{
    /// <summary>
    /// Nothing was attempted: no using directive placement is enforced for the file, the file is not a C# file, or no
    /// using directive is on the side it would move from.
    /// </summary>
    NotApplicable,

    /// <summary>
    /// The using directives were moved to the enforced placement (outside or inside the namespace).
    /// </summary>
    Moved,

    /// <summary>
    /// The using directives were left in place because the move was not safe or could not be performed; the reason
    /// was written to the output pane. Later steps of the same cleanup must not retry the move.
    /// </summary>
    LeftInPlace,
}
