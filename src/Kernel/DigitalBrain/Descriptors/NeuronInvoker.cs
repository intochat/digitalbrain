using System.Text.Json;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Core;

internal sealed class NeuronInvoker(IGrainFactory grains, DescriptorTable table) : INeuronInvoker
{
    public IReadOnlyList<MethodDescriptor> Describe(NeuronId neuron) => table.For(neuron.ToGrainId().Type);

    public MethodDescriptor Describe(string interfaceAlias, string methodAlias) => table.Get(interfaceAlias, methodAlias);

    public async Task<JsonElement?> InvokeAsync(NeuronId neuron, string interfaceAlias, string methodAlias,
        JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var grainId = neuron.ToGrainId();
        var aliases = table.InterfaceAliasesOf(grainId.Type);
        if (!aliases.Contains(interfaceAlias, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Neuron '{neuron}' does not implement interface '{interfaceAlias}'. Use one of: {string.Join(", ", aliases)}.", nameof(interfaceAlias));
        }

        var method = table.Method(interfaceAlias, methodAlias);
        var proxy = grains.GetGrain(grainId, method.InterfaceType);
        var argument = method.ArgumentsJson is null ? null : JsonSerializer.Deserialize(arguments, method.ArgumentsJson);
        var task = method.Invoke(proxy, argument, cancellationToken);
        await task.ConfigureAwait(true);
        return method.ResultJson is null ? null : JsonSerializer.SerializeToElement(method.Result!(task), method.ResultJson);
    }
}
