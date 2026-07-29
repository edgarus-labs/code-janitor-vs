using System.Threading.Tasks;

namespace SteveCadwallader.CodeJanitor.Integration
{
    internal interface ISwitchableFeature
    {
        Task SwitchAsync(bool on);
    }
}