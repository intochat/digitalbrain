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
    private const string CommandIdProperty = "commandId";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static AIFunction Materialize(
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
        return new DirectedSynapseFunction(
            capability,
            grains,
            owner,
            target,
            requestType,
            responseType);
    }

    public static string ModelSchemaFor(string jsonSchema)
    {
        var node = JsonNode.Parse(CapabilitySchema.NormalizeForToolProviders(jsonSchema))
            ?? throw new InvalidOperationException("Capability JSON schema parsed to null.");
        if (node is JsonObject schema)
        {
            if (schema["properties"] is JsonObject properties)
            {
                properties.Remove(CommandIdProperty);
            }

            if (schema["required"] is JsonArray required)
            {
                for (var index = required.Count - 1; index >= 0; index--)
                {
                    if (required[index] is JsonValue value
                        && value.TryGetValue<string>(out var name)
                        && string.Equals(name, CommandIdProperty, StringComparison.Ordinal))
                    {
                        required.RemoveAt(index);
                    }
                }

                if (required.Count == 0)
                {
                    schema.Remove("required");
                }
            }
        }

        return node.ToJsonString();
    }

    public static Synapse BindModelArguments(
        Type requestType,
        string contractId,
        IEnumerable<KeyValuePair<string, object?>> arguments)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        ArgumentNullException.ThrowIfNull(arguments);

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

        if (requestType.GetProperty(nameof(CommandId))?.PropertyType == typeof(CommandId))
        {
            node[CommandIdProperty] = new JsonObject { ["value"] = CommandId.New().Value };
        }

        var request = JsonSerializer.Deserialize(node, requestType, SerializerOptions)
            ?? throw new InvalidOperationException(
                $"Model arguments could not be bound to '{contractId}'.");
        if (request is not Synapse synapse)
        {
            throw new InvalidOperationException(
                $"Bound request for '{contractId}' is not a Synapse.");
        }

        return synapse;
    }

    private static AIFunction MaterializeBehavior(
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

        return AIFunctionFactory.Create(
            InvokeAsync,
            capability.ToolName,
            description);
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

    // A capability that outlives one outbox delivery attempt is not a failed capability: the outbox
    // keeps retrying it for DeliveryPolicy.RetryHorizon. Giving up after a single attempt would tell
    // the model the request failed while its side effect is still in flight, so the tool waits
    // several attempts, and when it does give up it says the request may still complete and names
    // the correlation the journals can be searched by.
    internal static readonly TimeSpan ToolResponseWait = DeliveryPolicy.DeliveryAttemptTimeout * 3;

    internal static string ResponseTimeoutMessage(
        NeuronId target,
        Type responseType,
        CorrelationId correlation,
        TimeSpan waited)
    {
        ArgumentNullException.ThrowIfNull(responseType);

        return $"No '{responseType.Name}' reply from '{target}' arrived within {waited.TotalSeconds} seconds. "
            + "The request is committed and may still complete; read the journals for correlation "
            + $"'{correlation}' to find its outcome before sending it again.";
    }

    private sealed class DirectedSynapseFunction : AIFunction
    {
        private const long BeyondJournalEnd = long.MaxValue;
        private static readonly TimeSpan ResponsePoll = TimeSpan.FromMilliseconds(25);

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
            using var document = JsonDocument.Parse(ModelSchemaFor(capability.JsonSchema));
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
            var request = BindModelArguments(_requestType, _capability.ContractId, arguments);
            var brain = DigitalBrainClient.Connect(_grains, _owner.Value);
            await brain.ActivateAsync(cancellationToken).ConfigureAwait(false);
            var response = await AwaitDirectedResponseAsync(request, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(response, _responseType, SerializerOptions);
        }

        // A model tool runs inside the agent neuron's turn. IDigitalBrain.SendRequestAsync awaits a
        // grain observer callback, and Orleans dispatches that callback onto the activation that
        // created the reference — the very turn that is blocked on this tool. Polling the owner
        // session's incoming journal reads the same directed reply without any callback into an
        // occupied activation.
        private async Task<Synapse> AwaitDirectedResponseAsync(Synapse request, CancellationToken cancellationToken)
        {
            var sessionId = ISessionNeuron.ForOwner(_owner);
            var session = _grains.GetGrain<ISessionNeuron>(sessionId.ToGrainId());
            var opened = await session
                .ReadNeuronJournal(sessionId, JournalKind.Incoming, BeyondJournalEnd)
                .ConfigureAwait(false);
            var cursor = opened.ResumeSequence;

            var delivery = await session.Fire(_target, request).ConfigureAwait(false);
            var abandonAfter = DateTimeOffset.UtcNow + ToolResponseWait;

            while (true)
            {
                var read = await session
                    .ReadNeuronJournal(sessionId, JournalKind.Incoming, cursor)
                    .ConfigureAwait(false);

                if (read.ResetSnapshot is not null)
                {
                    throw new InvalidOperationException(
                        $"Journal compaction for '{sessionId}' {JournalKind.Incoming} after {cursor}; "
                        + $"the '{_responseType.Name}' reply from '{_target}' for correlation "
                        + $"'{delivery.CorrelationId}' can no longer be read back.");
                }

                foreach (var journaled in read.Delta)
                {
                    if (journaled.CorrelationId == delivery.CorrelationId
                        && _responseType.IsInstanceOfType(journaled.Synapse))
                    {
                        return journaled.Synapse;
                    }
                }

                cursor = read.ResumeSequence;

                if (DateTimeOffset.UtcNow >= abandonAfter)
                {
                    throw new TimeoutException(
                        ResponseTimeoutMessage(_target, _responseType, delivery.CorrelationId, ToolResponseWait));
                }

                await Task.Delay(ResponsePoll, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
