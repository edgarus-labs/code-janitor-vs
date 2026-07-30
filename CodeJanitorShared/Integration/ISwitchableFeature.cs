using System.Threading.Tasks;

namespace CodeJanitor.Integration
{
    internal interface ISwitchableFeature
    {
        Task SwitchAsync(bool on);
    }
}