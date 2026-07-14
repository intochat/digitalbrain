using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Integrations.Salesforce;

internal interface ISalesforceApiClientFactory
{
    Task<ISalesforceApiClient> CreateAsync(NeuronScope scope, CancellationToken cancellationToken = default);
}
