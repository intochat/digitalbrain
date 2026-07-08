using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Salesforce;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Salesforce;

[GrainType("digitalbrain.salesforce.crm.v1")]
public class SalesforceCrmNeuron(
    ILogger<SalesforceCrmNeuron> logger,
    NeuronJournals journals,
    ISalesforceApiClientFactory apiClientFactory)
    : Neuron(logger, journals), ISalesforceCrmNeuron
{
    public async Task<string[]> QueryAsync(string soql, CancellationToken ct = default)
    {
        var client = await apiClientFactory.CreateAsync(Self.AsScope(), ct);
        return await client.QueryAsync(soql, ct);
    }

    public async Task<string[]> ListAccountsAsync(int maxResults = 20, CancellationToken ct = default)
    {
        var client = await apiClientFactory.CreateAsync(Self.AsScope(), ct);
        return await client.ListAccountsAsync(maxResults, ct);
    }
}
