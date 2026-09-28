using DigitalBrain.Core.Registry;

namespace DigitalBrain.Discovery.Sources;

internal sealed class NeuronCapabilitySource(NeuronRegistry registry) : ICapabilitySource
{
    public Task<IReadOnlyList<CapabilityDocument>> Read(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CapabilityDocument>>([.. registry.Methods.Select(static method =>
            new CapabilityDocument(method.Id, CapabilityKind.Neuron, method.Contract.Name + "." + method.Method.Name, method.SearchText))]);
}
