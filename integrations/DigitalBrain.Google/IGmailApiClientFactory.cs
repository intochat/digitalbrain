using DigitalBrain.Core;

namespace DigitalBrain.Google;

public interface IGmailApiClientFactory
{
    Task<IGmailApiClient> CreateAsync(NeuronScope scope);
}