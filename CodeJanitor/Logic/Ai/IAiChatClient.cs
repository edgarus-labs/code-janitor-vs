using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.Logic.Ai;

/// <summary>
/// Abstraction for AI chat completion clients to facilitate testing and loose coupling (DIP).
/// </summary>
public interface IAiChatClient
{
    /// <summary>
    /// Gets a value indicating whether the client endpoint is configured.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Asynchronously requests a chat completion from the AI endpoint.
    /// </summary>
    Task<string> GetChatCompletionContentAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default, int maxTokens = 2048);
}
