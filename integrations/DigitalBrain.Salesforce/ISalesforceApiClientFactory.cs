using DigitalBrain.Core;

namespace DigitalBrain.Salesforce;

public interface ISalesforceApiClientFactory
{
    Task<ISalesforceApiClient> CreateAsync(NeuronScope scope);
}
