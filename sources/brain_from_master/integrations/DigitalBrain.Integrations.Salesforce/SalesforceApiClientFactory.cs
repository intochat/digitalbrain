using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Configuration;
namespace DigitalBrain.Integrations.Salesforce;

internal sealed class SalesforceApiClientFactory(IIntegrationConfigStore store) : ISalesforceApiClientFactory
{
    public async Task<ISalesforceApiClient> CreateAsync(NeuronScope scope, CancellationToken cancellationToken = default)
    {
        var merged = await SalesforceClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken).ConfigureAwait(false);
        var session = await SalesforceClientFactory.CreateSessionAsync(merged, cancellationToken).ConfigureAwait(false);
        return new SalesforceApiClient(session.Client);
    }
}
