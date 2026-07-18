using DigitalBrain.Protocol;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;

namespace DigitalBrain.Hosting;

public interface ICluster : INeuron
{
    Task<IReadOnlyList<NeuronId>> ListSubscribersAsync(string synapseTypeName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListActiveNeuronTypesAsync(CancellationToken cancellationToken = default);

    Task<WorldConnectionInfo?> GetCurrentWorldAsync(CancellationToken cancellationToken = default) => Task.FromResult<WorldConnectionInfo?>(null);
}
