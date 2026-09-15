using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Core;

internal sealed class NeuronInvoker(IGrainFactory grains, DescriptorTable table) : INeuronInvoker
{
    public IReadOnlyList<MethodDescriptor> Describe(NeuronId neuron) => table.For(neuron.ToGrainId().Type);

    public MethodDescriptor Describe(string interfaceAlias, string methodAlias) => table.Get(interfaceAlias, methodAlias);

    public ArgumentContract? ArgumentContractOf(string interfaceAlias, string methodAlias)
        => table.ArgumentContractOf(interfaceAlias, methodAlias);

    public async Task<JsonElement?> InvokeAsync(NeuronId neuron, string interfaceAlias, string methodAlias,
        JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var grainId = neuron.ToGrainId();
        var aliases = table.InterfaceAliasesOf(grainId.Type);
        if (!aliases.Contains(interfaceAlias, StringComparer.Ordinal))
        {
            if (aliases.Count == 0)
            {
                throw new ArgumentException($"Neuron '{neuron}' implements no callable interfaces. The kernel operations are the fire/connect/disconnect/read/cancel tools.", nameof(interfaceAlias));
            }

            throw new ArgumentException($"Neuron '{neuron}' does not implement interface '{interfaceAlias}'. Use one of: {string.Join(", ", aliases)}.", nameof(interfaceAlias));
        }

        var method = table.Method(interfaceAlias, methodAlias);
        var payload = WithCommandId(arguments, method);
        var proxy = grains.GetGrain(grainId, method.InterfaceType);
        var argument = method.ArgumentsJson is null ? null : JsonSerializer.Deserialize(payload, method.ArgumentsJson)
            ?? throw new ArgumentException($"Arguments for interface '{interfaceAlias}', method '{methodAlias}' must match the method's schema.", nameof(arguments));
        var task = method.Invoke(proxy, argument, cancellationToken);
        await task.ConfigureAwait(true);
        return method.ResultJson is null ? null : JsonSerializer.SerializeToElement(method.Result!(task), method.ResultJson);
    }

    private static JsonElement WithCommandId(JsonElement arguments, InvocationMetadata method)
    {
        if (method.ArgumentsJson is null || method.CommandIdPropertyName is not { } commandId)
        {
            return arguments;
        }

        if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(commandId, out _))
        {
            return arguments;
        }

        var node = arguments.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(arguments.GetRawText()) as JsonObject ?? []
            : [];
        node[commandId] = JsonSerializer.SerializeToNode(CommandId.New(), method.ArgumentsJson.Options);
        return JsonSerializer.SerializeToElement(node);
    }
}
