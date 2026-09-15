using System.Text.Json;
using DigitalBrain.Abstractions.Behaviors;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core.Behaviors;
/// <summary>Registered composition operations over the existing descriptor and neuron runtime.</summary>
public sealed class BehaviorCatalog(INeuronInvoker invoker, DescriptorTable descriptors, IServiceProvider services)
{
    public IReadOnlyList<BehaviorCapability> All =>
    [
        new("source", "", BehaviorOwnership.Shared,
            "An existing source neuron. Set sharedNeuron and output signal/schema; configuration {}. Registered source contracts are authoritative. Plain signal sources use an explicitly declared contract. Does not change its rules or state.",
            [.. services.GetServices<BehaviorSourceContract>()]),
        new("filter", "behavior-filter", BehaviorOwnership.Owned,
            "Pass matching input unchanged. Configuration: {path: 'text', operator: 'contains|equals|greaterThan|lessThan', value: ...}. Declare identical input/output schemas."),
        new("map", "behavior-map", BehaviorOwnership.Owned,
            "Map JSON using {template: {...}}. Strings $input.path reference required input fields; $signalId is the triggering event id. Declare input/output contracts."),
        new("action", "behavior-action", BehaviorOwnership.Owned,
            "Call an existing typed neuron method. Configuration {target:'chart:name', interface:'ui.chart', method:'append'}. Input is the method argument schema without the runtime-injected command id; ids are deterministic. Optional output must match the method result. Unknown command outcomes pause this node."),
        .. services.GetService<IBehaviorDecision>() is null ? Array.Empty<BehaviorCapability>() :
            [new BehaviorCapability("decision", "behavior-decision", BehaviorOwnership.Owned,
                "Tool-free structured AI output. Configuration {instructions:'...', provider:null, model:null}; required input/output contracts. Invalid output pauses the node.")],
    ];

    public BehaviorCapability Get(string id) => All.SingleOrDefault(value => value.Id == id) ?? throw new ArgumentException($"Capability '{id}' is not registered. Read behavior.catalog.");
    public BehaviorValidation Validate(BehaviorDefinition definition)
    {
        var errors = new List<string>();
        if (definition is null)
        {
            return new(false, ["A definition is required."]);
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            errors.Add("Name is required.");
        }

        if (definition.Nodes is null || definition.Connections is null)
        {
            return new(false, ["Nodes and connections are required."]);
        }

        if (definition.Nodes.Count is < 1 or > 64 || definition.Connections.Count > 256)
        {
            return new(false, ["A behavior needs 1–64 nodes and at most 256 connections."]);
        }

        var nodes = new Dictionary<string, BehaviorNodeDefinition>(StringComparer.Ordinal);
        foreach (var node in definition.Nodes)
        {
            try
            {
                if (node is null || string.IsNullOrWhiteSpace(node.Role) || !nodes.TryAdd(node.Role, node))
                {
                    errors.Add("Each node needs a unique, nonempty role.");
                    continue;
                }

                var capability = Get(node.Capability);
                if (capability.Ownership == BehaviorOwnership.Shared != node.SharedNeuron.HasValue)
                {
                    errors.Add($"{node.Role}: {capability.Id} requires {capability.Ownership} ownership.");
                }

                foreach (var contract in new[]
                {
                    node.Input,
                    node.Output
                }.OfType<PayloadContract>())
                {
                    Signal.Create(contract.SignalType, "{}");
                    errors.AddRange(BehaviorSchema.CheckSchema(contract.Schema).Select(error => $"{node.Role}: {error}"));
                }

                using var config = JsonDocument.Parse(node.Configuration);
                if (config.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new ArgumentException("Configuration must be an object.");
                }
                string[] allowed = node.Capability switch
                {
                    "source" => [],
                    "filter" => ["path", "operator", "value"],
                    "map" => ["template"],
                    "action" => ["target", "interface", "method"],
                    "decision" => ["instructions", "provider", "model"],
                    _ => [],
                };
                foreach (var property in config.RootElement.EnumerateObject())
                {
                    if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                    {
                        errors.Add($"{node.Role}: unknown configuration field '{property.Name}'.");
                    }
                }

                if (node.Capability == "source")
                {
                    if (node.Input is not null || node.Output is null)
                    {
                        errors.Add($"{node.Role}: a source has an output and no input.");
                    }

                    if (node.SharedNeuron is { } shared && shared.Type.StartsWith("behavior", StringComparison.Ordinal))
                    {
                        errors.Add($"{node.Role}: behavior processors cannot be shared sources.");
                    }

                    if (node.SharedNeuron is { } source && !descriptors.Contains(source.ToGrainId().Type))
                    {
                        errors.Add($"{node.Role}: source neuron type is not registered.");
                    }
                    if (node.SharedNeuron is { } typed && node.Output is { } declared
                        && services.GetServices<BehaviorSourceContract>().FirstOrDefault(item => item.GrainType == typed.Type) is { } registered)
                    {
                        var actual = registered.Outputs.SingleOrDefault(item => item.SignalType == declared.SignalType);
                        if (actual is null || !BehaviorSchema.IsAssignable(actual.Schema, declared.Schema))
                        {
                            errors.Add($"{node.Role}: declared output is incompatible with the registered source contract.");
                        }
                    }
                }
                else
                {
                    if (node.Input is null)
                    {
                        errors.Add($"{node.Role}: an input contract is required.");
                    }

                    if (node.Output is null && node.Capability != "action")
                    {
                        errors.Add($"{node.Role}: an output contract is required.");
                    }

                    ValidateProcessor(node, config.RootElement, errors);
                }
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or JsonException or KeyNotFoundException)
            {
                errors.Add($"{node?.Role}: {error.Message}");
            }
        }

        var edges = new HashSet<(string, string)>();
        foreach (var edge in definition.Connections)
        {
            if (edge is null || string.IsNullOrWhiteSpace(edge.From) || string.IsNullOrWhiteSpace(edge.To) ||
                !nodes.TryGetValue(edge.From, out var from) || !nodes.TryGetValue(edge.To, out var to))
            {
                errors.Add("Each connection must refer to declared roles.");
                continue;
            }

            if (edge.From == edge.To || !edges.Add((edge.From, edge.To)))
            {
                errors.Add("Self/duplicate connections are not allowed.");
            }

            if (from.Output is not { } output || to.Input is not { } input || output.SignalType != input.SignalType || !BehaviorSchema.IsAssignable(output.Schema, input.Schema))
            {
                errors.Add($"{edge.From} -> {edge.To}: incompatible signal contracts; add a mapping neuron.");
            }
        }

        return new(errors.Count == 0, errors);
    }

    private void ValidateProcessor(BehaviorNodeDefinition node, JsonElement config, List<string> errors)
    {
        if (node.Input is null)
        {
            return;
        }

        switch (node.Capability)
        {
            case "filter":
                if (node.Output is not null && !BehaviorSchema.IsAssignable(node.Input.Schema, node.Output.Schema))
                {
                    errors.Add($"{node.Role}: filtering preserves the payload; output must accept every input.");
                }

                BehaviorMapping.ValidateFilter(config, node.Input.Schema);
                break;
            case "map":
                if (node.Output is not null)
                {
                    var inferred = BehaviorMapping.InferSchema(config.GetProperty("template"), node.Input.Schema);
                    if (!BehaviorSchema.IsAssignable(inferred, node.Output.Schema))
                    {
                        errors.Add($"{node.Role}: mapping cannot satisfy the output contract.");
                    }
                }

                break;
            case "decision":
                if (string.IsNullOrWhiteSpace(config.GetProperty("instructions").GetString()))
                {
                    errors.Add($"{node.Role}: instructions required.");
                }

                if (config.TryGetProperty("tools", out _))
                {
                    errors.Add($"{node.Role}: decision neurons cannot have tools.");
                }

                break;
            case "action":
                var target = BehaviorMapping.Target(config);
                var iface = config.GetProperty("interface").GetString()!;
                var method = config.GetProperty("method").GetString()!;
                var descriptor = invoker.Describe(target).SingleOrDefault(value => value.InterfaceAlias == iface && value.MethodAlias == method) ?? throw new ArgumentException("Target does not expose the requested method. Use describe.");
                var expected = descriptor.ArgsSchema?.GetRawText() ?? "{\"type\":\"object\"}";
                expected = BehaviorMapping.WithoutCommandIdentity(expected, invoker.ArgumentContractOf(iface, method)?.CommandIdPropertyName);
                if (!BehaviorSchema.IsAssignable(node.Input.Schema, expected))
                {
                    errors.Add($"{node.Role}: input does not satisfy the method argument schema.");
                }

                if (node.Output is not null && (descriptor.ResultSchema is null || !BehaviorSchema.IsAssignable(descriptor.ResultSchema.Value.GetRawText(), node.Output.Schema)))
                {
                    errors.Add($"{node.Role}: output does not accept the method result.");
                }

                break;
        }
    }
}


