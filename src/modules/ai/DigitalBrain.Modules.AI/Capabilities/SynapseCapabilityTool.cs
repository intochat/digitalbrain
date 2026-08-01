using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

public static class SynapseCapabilityTool
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static CapabilityTool Materialize(
        ValidatedCapability capability,
        IGrainFactory grains,
        OwnerId owner,
        ActiveModuleContractTypeMap typeMap)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentNullException.ThrowIfNull(typeMap);

        if (string.Equals(capability.Kind, CapabilityKinds.Behavior, StringComparison.Ordinal))
        {
            return MaterializeBehavior(capability, grains, owner);
        }

        if (!typeMap.TryGetSynapseType(capability.ContractId, capability.SchemaVersion, out var requestType)
            || requestType is null)
        {
            throw new InvalidOperationException(
                $"No runtime type is mapped for request synapse '{capability.ContractId}' v{capability.SchemaVersion}.");
        }

        if (!typeMap.TryGetNeuronGrainType(capability.NeuronContractId, out var grainType)
            || string.IsNullOrWhiteSpace(grainType))
        {
            throw new InvalidOperationException(
                $"No grain type is mapped for neuron '{capability.NeuronContractId}'.");
        }

        if (!TryGetResponseType(requestType, out var responseType) || responseType is null)
        {
            throw new InvalidOperationException(
                $"Request synapse '{capability.ContractId}' is not a RequestSynapse<>.");
        }

        var target = new NeuronId(grainType, owner, capability.DefaultInstanceName);
        var function = new DirectedSynapseFunction(
            capability,
            grains,
            owner,
            target,
            requestType,
            responseType);
        return CapabilityTool.FromFunction(function);
    }

    private static CapabilityTool MaterializeBehavior(
        ValidatedCapability capability,
        IGrainFactory grains,
        OwnerId owner)
    {
        if (string.IsNullOrWhiteSpace(capability.ArtifactHash))
        {
            throw new InvalidOperationException(
                $"Published behavior '{capability.BehaviorId}' is missing an exact active artifact hash.");
        }

        // Exact catalog artifact hash is captured here; vector metadata never chooses the revision.
        var boundArtifactHash = capability.ArtifactHash;
        var instanceName = capability.DefaultInstanceName;
        var description =
            $"{capability.Description} Active revision {boundArtifactHash}. "
            + "Provide the trigger CLR type name and its JSON payload to run once.";

        async Task<string> InvokeAsync(string triggerTypeName, string triggerJson)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(triggerTypeName);
            ArgumentException.ThrowIfNullOrWhiteSpace(triggerJson);
            ArgumentException.ThrowIfNullOrWhiteSpace(boundArtifactHash);

            var behavior = grains.GetGrain<DigitalBrain.Behaviors.IBehaviorNeuron>(
                NeuronId.For<DigitalBrain.Behaviors.IBehaviorNeuron>(owner, instanceName).ToGrainId());
            var snapshot = await behavior.Read().ConfigureAwait(false);
            if (!string.Equals(snapshot.ActiveArtifactHash, boundArtifactHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Exact active revision for behavior '{capability.BehaviorId}' is "
                    + $"'{snapshot.ActiveArtifactHash}', not vector-suggested '{boundArtifactHash}'.");
            }

            var result = await behavior.Execute(
                new DigitalBrain.Behaviors.ExecuteBehaviorRevision(
                    CommandId.New(),
                    triggerTypeName,
                    triggerJson))
                .ConfigureAwait(false);
            return result.Outcome ?? "behavior executed";
        }

        var function = AIFunctionFactory.Create(
            InvokeAsync,
            capability.ToolName,
            description);
        return CapabilityTool.FromFunction(function);
    }

    private static bool TryGetResponseType(Type requestType, out Type? responseType)
    {
        responseType = null;
        var current = requestType;
        while (current is not null)
        {
            if (current.IsGenericType
                && current.GetGenericTypeDefinition() == typeof(RequestSynapse<>))
            {
                responseType = current.GetGenericArguments()[0];
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private sealed class DirectedSynapseFunction : AIFunction
    {
        private readonly ValidatedCapability _capability;
        private readonly IGrainFactory _grains;
        private readonly OwnerId _owner;
        private readonly NeuronId _target;
        private readonly Type _requestType;
        private readonly Type _responseType;
        private readonly JsonElement _schema;

        public DirectedSynapseFunction(
            ValidatedCapability capability,
            IGrainFactory grains,
            OwnerId owner,
            NeuronId target,
            Type requestType,
            Type responseType)
        {
            _capability = capability;
            _grains = grains;
            _owner = owner;
            _target = target;
            _requestType = requestType;
            _responseType = responseType;
            using var document = JsonDocument.Parse(capability.JsonSchema);
            _schema = document.RootElement.Clone();
        }

        public override string Name => _capability.ToolName;

        public override string Description => _capability.Description;

        public override JsonElement JsonSchema => _schema;

        [SuppressMessage(
            "Usage",
            "CA1849:Call async methods when in an async method",
            Justification = "DigitalBrainClient.Connect is the in-silo factory; ConnectAsync is the behavior-worker surface.")]
        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = DeserializeRequest(arguments);
            var brain = DigitalBrainClient.Connect(_grains, _owner.Value);
            var response = await brain
                .SendRequestAsync(_target, request, _responseType, cancellationToken)
                .ConfigureAwait(false);
            return JsonSerializer.Serialize(response, _responseType, SerializerOptions);
        }

        private Synapse DeserializeRequest(AIFunctionArguments arguments)
        {
            var node = new JsonObject();
            foreach (var (key, value) in arguments)
            {
                node[key] = value switch
                {
                    null => null,
                    JsonElement element => JsonNode.Parse(element.GetRawText()),
                    JsonNode jsonNode => jsonNode,
                    _ => JsonSerializer.SerializeToNode(value, SerializerOptions),
                };
            }

            var request = JsonSerializer.Deserialize(node, _requestType, SerializerOptions)
                ?? throw new InvalidOperationException(
                    $"Model arguments could not be bound to '{_capability.ContractId}'.");
            if (request is not Synapse synapse)
            {
                throw new InvalidOperationException(
                    $"Bound request for '{_capability.ContractId}' is not a Synapse.");
            }

            return synapse;
        }
    }
}
