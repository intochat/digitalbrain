using DigitalBrain.Kernel.Contracts;
namespace DigitalBrain.Integrations.Google;

internal interface IGmailApiClientFactory
{
    Task<IGmailApiClient> CreateAsync(NeuronScope scope, CancellationToken cancellationToken = default);
}
