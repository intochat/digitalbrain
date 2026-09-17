using System.Text.Json;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrain.Core;

internal sealed class NeuronInvoker(IGrainFactory grains, Func<NeuronToolCatalog> createCatalog) : INeuronInvoker
{
    private readonly Lazy<NeuronToolCatalog> _catalog = new(createCatalog);
    private NeuronToolCatalog Catalog => _catalog.Value;

    public IReadOnlyList<MethodDescriptor> Describe(NeuronId neuron) => Catalog.For(neuron.ToGrainId().Type);

    public MethodDescriptor Describe(string interfaceAlias, string methodAlias) => Catalog.Method(interfaceAlias, methodAlias).Descriptor;

    public ArgumentContract? ArgumentContractOf(string interfaceAlias, string methodAlias)
        => Catalog.ArgumentContractOf(interfaceAlias, methodAlias);

    public async Task<JsonElement?> InvokeAsync(NeuronId neuron, string interfaceAlias, string methodAlias,
        JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var grainId = neuron.ToGrainId();
        if (!Catalog.For(grainId.Type).Any(method => method.InterfaceAlias == interfaceAlias && method.MethodAlias == methodAlias))
        {
            throw new ArgumentException($"Neuron '{neuron}' does not expose tool '{interfaceAlias}/{methodAlias}'. Use describe to list its tools.", nameof(methodAlias));
        }

        var method = Catalog.Method(interfaceAlias, methodAlias);
        var proxy = grains.GetGrain(grainId, method.InterfaceType);
        var argument = method.ArgumentsJson is null ? null : JsonSerializer.Deserialize(arguments, method.ArgumentsJson)
            ?? throw new ArgumentException($"Arguments for interface '{interfaceAlias}', method '{methodAlias}' must match the method's schema.", nameof(arguments));
        var task = method.Invoke(proxy, argument, cancellationToken);
        await task.ConfigureAwait(true);
        if (method.ResultJson is null) { return null; }
        var result = method.Result!(task);
        if (method.ReturnsNeuron && result is INeuron reference)
        {
            result = NeuronId.FromGrainId(reference.GetGrainId());
        }
        return JsonSerializer.SerializeToElement(result, method.ResultJson);
    }
}
