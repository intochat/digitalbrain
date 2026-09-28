using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Core.Registry;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI.Agents;

// Every public neuron method as an agent tool. The model names the neuron it calls; "new" asks for a
// fresh neuron whose id is derived from the run and tool call, so a replayed call reaches the same one.
public sealed class NeuronTools(NeuronRegistry registry, NeuronInvoker invoker) : IAgentToolFactory
{
    public const string NewNeuron = "new";

    private IReadOnlyList<Described>? _described;

    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
        => [.. (_described ??= [.. registry.Methods.Select(Describe)]).Select(described => new NeuronFunction(described, invoker, context))];

    public static JsonElement Schema(NeuronMethod method)
    {
        var properties = new JsonObject
        {
            ["neuronId"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = $"Id of the {method.Contract.Name} to call, or \"{NewNeuron}\" to create a fresh one.",
            },
        };
        var required = new JsonArray("neuronId");
        foreach (var parameter in method.Arguments)
        {
            properties[parameter.Name!] = JsonNode.Parse(AIJsonUtilities.CreateJsonSchema(parameter.ParameterType,
                hasDefaultValue: parameter.HasDefaultValue, defaultValue: parameter.HasDefaultValue ? parameter.DefaultValue : null,
                serializerOptions: NeuronInvoker.Json).GetRawText());
            if (!parameter.HasDefaultValue) { required.Add(parameter.Name); }
        }
        return JsonSerializer.SerializeToElement(new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required });
    }

    private static Described Describe(NeuronMethod method) => new(method.Id, NeuronToolName.Of(method.Id),
        method.Description ?? $"{method.Contract.Name}.{method.Method.Name} ({method.Id}).", Schema(method));

    private sealed record Described(string MethodId, string Name, string Description, JsonElement Schema);

    private sealed class NeuronFunction(Described described, NeuronInvoker invoker, Func<AgentToolContext> context) : AIFunction
    {
        public override string Name => described.Name;
        public override string Description => described.Description;
        public override JsonElement JsonSchema => described.Schema;

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.SerializeToElement(arguments.Where(static pair => pair.Key != "neuronId")
                .ToDictionary(static pair => pair.Key, static pair => pair.Value), NeuronInvoker.Json);
            var neuronId = arguments.TryGetValue("neuronId", out var id) ? id?.ToString() ?? "" : "";
            if (neuronId == NewNeuron)
            {
                var call = context();
                neuronId = "n-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(call.ScopeId + "\n" + call.RunId + "\n" + call.CallId)))[..16].ToLowerInvariant();
            }
            return await invoker.Invoke(described.MethodId, neuronId, json, cancellationToken).ConfigureAwait(false);
        }
    }
}
