using System.Text.Json;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Descriptors;

public interface INeuronInvoker
{
    IReadOnlyList<MethodDescriptor> Describe(NeuronId neuron);
    MethodDescriptor Describe(string interfaceAlias, string methodAlias);
    Task<JsonElement?> InvokeAsync(NeuronId neuron, string interfaceAlias, string methodAlias, JsonElement arguments, CancellationToken cancellationToken = default);
}
