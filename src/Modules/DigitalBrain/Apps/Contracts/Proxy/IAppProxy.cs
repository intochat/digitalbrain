using DigitalBrain.Contracts;
using Orleans.Metadata;

namespace DigitalBrain.Apps;

[Alias("app-proxy"), DefaultGrainType("app-proxy")]
public interface IAppProxy : INeuron
{
    Task<AppProxyOutcome> Invoke(AppProxyRequest request);
}
