using DigitalBrain.Contracts;
using Orleans.Concurrency;
using Orleans.Metadata;

namespace DigitalBrain.Apps;

// One consent sheet per workspace, keyed by the workspace scope id.
[Alias("app-consent"), DefaultGrainType("app-consent")]
public interface IAppConsent : INeuron
{
    Task<AppConsentSheet> Review(string appId);

    Task<AppConsentSheet> Approve(string appId);

    [ReadOnly, Alias("is-approved")]
    Task<bool> IsApproved(string appId);
}
