using DigitalBrain.Core;
using DigitalBrain.Core.Config;

namespace DigitalBrain.Salesforce;

public sealed class SalesforceApiClientFactory(IPackConfigStore store) : ISalesforceApiClientFactory
{
    public async Task<ISalesforceApiClient> CreateAsync(NeuronScope scope, CancellationToken cancellationToken = default)
    {
        var merged = await SalesforceClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken).ConfigureAwait(false);
        return new SalesforceApiClient(await SalesforceClientFactory.CreateForceClientAsync(merged, cancellationToken).ConfigureAwait(false));
    }
}
