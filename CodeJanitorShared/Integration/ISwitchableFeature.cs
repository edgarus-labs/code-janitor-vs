using System.Threading.Tasks;

namespace CodeJanitor.Integration;

/// <summary>
/// Represents a feature that can be toggled on or off asynchronously.
/// </summary>
internal interface ISwitchableFeature
{
    Task SwitchAsync(bool on);
}
