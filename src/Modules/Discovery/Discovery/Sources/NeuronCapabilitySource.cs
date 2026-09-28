using DigitalBrain.Core.Registry;

namespace DigitalBrain.Discovery.Sources;

internal sealed class NeuronCapabilitySource(NeuronRegistry registry) : ICapabilitySource
{
    public Task<IReadOnlyList<CapabilityDocument>> Read(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CapabilityDocument>>([.. registry.All.Select(static contract =>
            new CapabilityDocument(contract.Id, CapabilityKind.Neuron, contract.Name, contract.SearchText))]);
}
