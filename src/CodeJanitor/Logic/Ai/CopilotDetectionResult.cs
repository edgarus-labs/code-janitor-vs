using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Result of GitHub Copilot environment detection.
/// </summary>
public sealed class CopilotDetectionResult
{
    /// <summary>
    /// Gets or sets a value indicating whether GitHub Copilot is installed/present in Visual Studio or on the system.
    /// </summary>
    public bool IsInstalled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an active GitHub Copilot session or token was detected.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the human-readable status description.
    /// </summary>
    public string StatusDescription { get; set; }

    /// <summary>
    /// Gets or sets the detected OAuth/access token if found, or null.
    /// </summary>
    public string DetectedToken { get; set; }

    /// <summary>
    /// Gets the recommended Copilot API endpoint URL.
    /// </summary>
    public string RecommendedEndpoint => GitHubCopilotDetector.DefaultCopilotEndpoint;

    /// <summary>
    /// Gets the recommended default model.
    /// </summary>
    public string RecommendedModel => GitHubCopilotDetector.DefaultCopilotModel;
}
