using DigitalBrain.Salesforce;

namespace DigitalBrain.Kernel.Salesforce;

[GrainType("digitalbrain.salesforce.crm.v1")]
public class SalesforceCrmNeuron(
    ILogger<SalesforceCrmNeuron> logger,
    NeuronJournals journals,
    ISalesforceApiClient client)
    : Neuron(logger, journals), ISalesforceCrmNeuron
{
    public Task<string[]> QueryAsync(string soql, CancellationToken ct = default) =>
        client.QueryAsync(soql, ct);

    public Task<string[]> ListAccountsAsync(int maxResults = 20, CancellationToken ct = default) =>
        client.ListAccountsAsync(maxResults, ct);
}
