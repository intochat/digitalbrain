using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Host;
using DigitalBrain.Tasks;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class HostBehaviorSynapseBrokerTests
{
    private static readonly OwnerId Owner = new("owner-host-broker");
    private static readonly NeuronId TaskNeuron = NeuronId.For<ITask>(Owner, "host-broker-task");
    private static readonly AttemptId Attempt = new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
    private const string TargetInstance = "work";
    private static readonly NeuronId TargetNeuron = new("test.host-broker.marker", Owner, TargetInstance);

    private const string RequestAlias = "test.host-broker.request";
    private const string ResponseAlias = "test.host-broker.response";
    private const int RequestSchemaVersion = 3;
    private const int ResponseSchemaVersion = 2;

    [Fact(DisplayName = "typed request/result uses exact grant and protected payloads")]
    public async Task TypedRequestResultUsesExactGrantAndProtectedPayloads()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = new RecordingHostBrokerClient();
        var broker = CreateBroker(client, ExactGrant());

        var response = await broker.SendAsync<IHostBrokerMarker, HostBrokerResponse>(
            TargetInstance,
            new HostBrokerRequest("hello-work"),
            cancellationToken);

        Assert.Equal("hello-work:ok", response.Status);
        Assert.Equal("ok", response.DetailCode);
        Assert.Equal(1, client.DispatchCount);
        Assert.NotNull(client.LastDispatchedEdge);
        Assert.Equal(TargetNeuron, client.LastDispatchedEdge!.Target);
        Assert.Equal(RequestAlias, client.LastDispatchedEdge.RequestSynapseId);
        Assert.Equal(RequestSchemaVersion, client.LastDispatchedEdge.RequestSchemaVersion);
        Assert.Equal(ResponseAlias, client.LastDispatchedEdge.ResponseSynapseId);
        Assert.Equal(ResponseSchemaVersion, client.LastDispatchedEdge.ResponseSchemaVersion);
        Assert.True(client.StoreCount >= 1);
        Assert.True(client.LoadCount >= 1);
    }

    [Fact(DisplayName = "Host response load uses shared payload JSON contract so non-default camelCase properties survive")]
    public async Task HostResponseLoadPreservesCamelCasePropertiesViaSharedPayloadContract()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = new RecordingHostBrokerClient(serializeDispatchResponseAsCamelCaseOnly: true);
        var broker = CreateBroker(client, ExactGrant());

        var response = await broker.SendAsync<IHostBrokerMarker, HostBrokerResponse>(
            TargetInstance,
            new HostBrokerRequest("case-roundtrip"),
            cancellationToken);

        Assert.Equal("case-roundtrip:ok", response.Status);
        Assert.Equal("ok", response.DetailCode);

        var raw = Encoding.UTF8.GetString(client.LastResponseBytes!);
        Assert.Contains("\"detailCode\"", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\"DetailCode\"", raw, StringComparison.Ordinal);

        var withoutContract = JsonSerializer.Deserialize<HostBrokerResponse>(client.LastResponseBytes);
        Assert.NotNull(withoutContract);
        Assert.Null(withoutContract.DetailCode);
    }

    [Fact(DisplayName = "new broker instance replays completed Task result without redispatch")]
    public async Task NewBrokerInstanceReplaysCompletedTaskResultWithoutRedispatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = new RecordingHostBrokerClient();
        var first = CreateBroker(client, ExactGrant());

        var firstResponse = await first.SendAsync<IHostBrokerMarker, HostBrokerResponse>(
            TargetInstance,
            new HostBrokerRequest("replay-me"),
            cancellationToken);

        Assert.Equal("replay-me:ok", firstResponse.Status);
        Assert.Equal(1, client.DispatchCount);

        var second = CreateBroker(client, ExactGrant());
        var secondResponse = await second.SendAsync<IHostBrokerMarker, HostBrokerResponse>(
            TargetInstance,
            new HostBrokerRequest("replay-me"),
            cancellationToken);

        Assert.Equal(firstResponse, secondResponse);
        Assert.Equal("replay-me:ok", secondResponse.Status);
        Assert.Equal(1, client.DispatchCount);
    }

    [Fact(DisplayName = "grant built from catalog contract Alias (distinct from grain type) dispatches; wrong alias refuses before store")]
    public async Task CatalogContractAliasDistinctFromGrainTypeDispatchesAndWrongAliasRefuses()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Mimic BehaviorHostEngine.DeriveResultBearingEdges: target type is contract alias, not grain type.
        var grantFromSignedManifest = new BehaviorCapabilityEdge(
            new NeuronId("test.host-broker.marker", Owner, TargetInstance),
            RequestAlias,
            RequestSchemaVersion,
            ResponseAlias,
            ResponseSchemaVersion);
        var grainType = NeuronId.GrainTypeNameOf(typeof(IHostBrokerMarker));
        Assert.NotEqual("test.host-broker.marker", grainType);

        var client = new RecordingHostBrokerClient();
        var broker = CreateBroker(client, grantFromSignedManifest);
        var response = await broker.SendAsync<IHostBrokerMarker, HostBrokerResponse>(
            TargetInstance,
            new HostBrokerRequest("alias-ok"),
            cancellationToken);
        Assert.Equal("alias-ok:ok", response.Status);
        Assert.Equal(1, client.DispatchCount);
        Assert.True(client.StoreCount >= 1);

        var wrongAliasClient = new RecordingHostBrokerClient();
        var wrongAliasGrant = new BehaviorCapabilityEdge(
            new NeuronId(grainType, Owner, TargetInstance),
            RequestAlias,
            RequestSchemaVersion,
            ResponseAlias,
            ResponseSchemaVersion);
        var wrongAliasBroker = CreateBroker(wrongAliasClient, wrongAliasGrant);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            wrongAliasBroker.SendAsync<IHostBrokerMarker, HostBrokerResponse>(
                TargetInstance,
                new HostBrokerRequest("nope"),
                cancellationToken));
        Assert.Equal(0, wrongAliasClient.StoreCount);
        Assert.Equal(0, wrongAliasClient.DispatchCount);
    }

    [Fact(DisplayName = "wrong target or alias is rejected before payload store or dispatch")]
    public async Task WrongTargetOrAliasIsRejectedBeforePayloadStoreOrDispatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = new RecordingHostBrokerClient();
        var broker = CreateBroker(client, ExactGrant());

        await Assert.ThrowsAsync<InvalidOperationException>(() => broker.SendAsync<IHostBrokerMarker, HostBrokerResponse>(
            "other",
            new HostBrokerRequest("nope"),
            cancellationToken));

        Assert.Equal(0, client.StoreCount);
        Assert.Equal(0, client.DispatchCount);

        var wrongAliasClient = new RecordingHostBrokerClient();
        var wrongGrant = new BehaviorCapabilityEdge(
            TargetNeuron,
            "test.host-broker.other-request",
            RequestSchemaVersion,
            ResponseAlias,
            ResponseSchemaVersion);
        var wrongAliasBroker = CreateBroker(wrongAliasClient, wrongGrant);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            wrongAliasBroker.SendAsync<IHostBrokerMarker, HostBrokerResponse>(
                TargetInstance,
                new HostBrokerRequest("nope"),
                cancellationToken));

        Assert.Equal(0, wrongAliasClient.StoreCount);
        Assert.Equal(0, wrongAliasClient.DispatchCount);
    }

    [Fact(DisplayName = "one-way send fails closed before payload store or dispatch")]
    public async Task OneWaySendFailsClosedBeforePayloadStoreOrDispatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = new RecordingHostBrokerClient();
        var broker = CreateBroker(client, ExactGrant());

        await Assert.ThrowsAsync<NotSupportedException>(() => broker.SendAsync<IHostBrokerMarker>(
            TargetInstance,
            new HostBrokerOneWay("ping"),
            cancellationToken));

        Assert.Equal(0, client.StoreCount);
        Assert.Equal(0, client.DispatchCount);
    }

    private static HostBehaviorSynapseBroker CreateBroker(
        RecordingHostBrokerClient client,
        BehaviorCapabilityEdge grant)
    {
        var metadata = new BehaviorExecutionMetadata(
            Owner: Owner,
            Behavior: new BehaviorId("com.digitalbrain.host-broker-test"),
            Revision: new BehaviorRevisionId(
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            Execution: new BehaviorExecutionId(Guid.Parse("4b050fe8-45d0-4a16-b6a5-1b4b6683880a")));

        return new HostBehaviorSynapseBroker(
            metadata,
            TaskNeuron,
            Attempt,
            [grant],
            client);
    }

    private static BehaviorCapabilityEdge ExactGrant()
        => new(
            TargetNeuron,
            RequestAlias,
            RequestSchemaVersion,
            ResponseAlias,
            ResponseSchemaVersion);

    [Alias("test.host-broker.marker")]
    internal interface IHostBrokerMarker : INeuron;

    [Alias(RequestAlias)]
    internal sealed record HostBrokerRequest(string Prompt) : RequestSynapse<HostBrokerResponse>;

    [Alias(ResponseAlias)]
    internal sealed record HostBrokerResponse(string Status, string? DetailCode = null) : Synapse;

    [Alias("test.host-broker.one-way")]
    internal sealed record HostBrokerOneWay(string Note) : Synapse;

    private sealed class RecordingHostBrokerClient(bool serializeDispatchResponseAsCamelCaseOnly = false)
        : IBehaviorHostBrokerClient
    {
    public List<(BehaviorId Behavior, string Alias, string Json)> EmittedFacts { get; } = [];

    public ValueTask EmitFactAsync(
        BehaviorId behavior,
        string emitAlias,
        ReadOnlyMemory<byte> factJson,
        CancellationToken cancellationToken)
    {
        EmittedFacts.Add((behavior, emitAlias, System.Text.Encoding.UTF8.GetString(factJson.Span)));
        return ValueTask.CompletedTask;
    }

        private readonly Dictionary<Guid, byte[]> payloads = new();
        private readonly Dictionary<(Guid Attempt, int Sequence), TaskOperationSnapshot> operations = new();

        public int StoreCount { get; private set; }

        public int LoadCount { get; private set; }

        public int DispatchCount { get; private set; }

        public BehaviorCapabilityEdge? LastDispatchedEdge { get; private set; }

        public byte[]? LastResponseBytes { get; private set; }

        public ValueTask<ProtectedPayloadReference> StorePayloadAsync(
            OwnerId owner,
            NeuronId task,
            AttemptId attempt,
            ReadOnlyMemory<byte> plaintext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (owner == default || task == default || attempt == default || attempt.Value == Guid.Empty)
            {
                throw new InvalidOperationException("Owner, task, and attempt are required.");
            }

            var id = Guid.NewGuid();
            payloads[id] = plaintext.ToArray();
            StoreCount++;
            return ValueTask.FromResult(new ProtectedPayloadReference(id, DateTimeOffset.UtcNow.AddHours(1)));
        }

        public ValueTask<ReadOnlyMemory<byte>> LoadPayloadAsync(
            OwnerId owner,
            NeuronId task,
            AttemptId attempt,
            ProtectedPayloadReference reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.Id == Guid.Empty)
            {
                throw new InvalidOperationException("Protected payload reference cannot be empty.");
            }

            if (!payloads.TryGetValue(reference.Id, out var bytes))
            {
                throw new InvalidOperationException($"Unknown payload reference '{reference.Id}'.");
            }

            LoadCount++;
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(bytes);
        }

        public ValueTask<ReadOnlyMemory<byte>> LoadTriggerAsync(
            OwnerId owner,
            NeuronId task,
            BehaviorId behavior,
            BehaviorRevisionId revision,
            string caseId,
            ProtectedPayloadReference reference,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("Trigger load is not used by HostBehaviorSynapseBroker tests.");

        public ValueTask<TaskOperationSnapshot> PrepareAsync(
            PrepareTaskOperation command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(command);

            var key = (command.Attempt.Value, command.Sequence);
            if (operations.TryGetValue(key, out var existing))
            {
                return ValueTask.FromResult(existing);
            }

            var snapshot = new TaskOperationSnapshot(
                command.Attempt,
                command.Sequence,
                command.Edge,
                command.RequestPayload,
                TaskOperationPhase.Prepared,
                ResponsePayload: null,
                RedactedSummary: null);
            operations[key] = snapshot;
            return ValueTask.FromResult(snapshot);
        }

        public ValueTask<ReadTaskOperationResult> ReadAsync(
            ReadTaskOperation command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(command);

            if (operations.TryGetValue((command.Attempt.Value, command.Sequence), out var existing))
            {
                return ValueTask.FromResult(new ReadTaskOperationResult(existing));
            }

            return ValueTask.FromResult(new ReadTaskOperationResult(null));
        }

        public ValueTask<TaskOperationSnapshot> TransitionAsync(
            TransitionTaskOperation command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(command);

            var key = (command.Attempt.Value, command.Sequence);
            if (!operations.TryGetValue(key, out var existing))
            {
                throw new InvalidOperationException("Operation does not exist.");
            }

            if (existing.Phase == command.Phase
                && existing.ResponsePayload == command.ResponsePayload
                && existing.RedactedSummary == command.RedactedSummary)
            {
                return ValueTask.FromResult(existing);
            }

            if (existing.Phase != command.ExpectedPhase)
            {
                throw new InvalidOperationException("Expected phase does not match durable phase.");
            }

            var updated = existing with
            {
                Phase = command.Phase,
                ResponsePayload = command.ResponsePayload,
                RedactedSummary = command.RedactedSummary,
            };
            operations[key] = updated;
            return ValueTask.FromResult(updated);
        }

        public ValueTask<ProtectedPayloadReference> DispatchAsync(
            BehaviorCapabilityEdge edge,
            ProtectedPayloadReference requestPayload,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(edge);
            if (requestPayload.Id == Guid.Empty)
            {
                throw new InvalidOperationException("Request payload reference cannot be empty.");
            }

            if (!payloads.TryGetValue(requestPayload.Id, out var requestBytes))
            {
                throw new InvalidOperationException($"Unknown request payload '{requestPayload.Id}'.");
            }

            var request = BehaviorPayloadJson.Deserialize<HostBrokerRequest>(requestBytes)
                ?? throw new InvalidOperationException("Request payload deserialized to null.");

            var response = new HostBrokerResponse($"{request.Prompt}:ok", DetailCode: "ok");
            var responseBytes = serializeDispatchResponseAsCamelCaseOnly
                ? BehaviorPayloadJson.Serialize(response, typeof(HostBrokerResponse))
                : BehaviorPayloadJson.Serialize(response, typeof(HostBrokerResponse));
            LastResponseBytes = responseBytes;
            var responseId = Guid.NewGuid();
            payloads[responseId] = responseBytes;

            DispatchCount++;
            LastDispatchedEdge = edge;
            return ValueTask.FromResult(
                new ProtectedPayloadReference(responseId, DateTimeOffset.UtcNow.AddHours(1)));
        }
    }
}
