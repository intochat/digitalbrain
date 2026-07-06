using DigitalBrain.Core;
using DigitalBrain.Core.Config;

namespace DigitalBrain.Salesforce;

public sealed class SalesforceApiClientFactory(IPackConfigStore store) : ISalesforceApiClientFactory
{
    public async Task<ISalesforceApiClient> CreateAsync(NeuronScope scope)
    {
        var merged = await SalesforceClientFactory.GetMergedScopedValuesAsync(store, scope).ConfigureAwait(false);
        return new SalesforceApiClient(await SalesforceClientFactory.CreateForceClientAsync(merged).ConfigureAwait(false));
    }
}
