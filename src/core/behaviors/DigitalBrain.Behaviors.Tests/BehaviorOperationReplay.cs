using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Abstractions;
using DigitalBrain.Security;
using DigitalBrain.Tasks;
using Orleans.Journaling;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorOperationReplay
{
    private static readonly OwnerId OwnerA = new("owner-a");
    private static readonly OwnerId OwnerB = new("owner-b");
    private static readonly NeuronId TaskNeuron = NeuronId.For<ITask>(OwnerA, "operation-replay");
    private static readonly NeuronId WorkerNeuron = NeuronId.For<IWorker>(OwnerA, "operation-replay");
    private static readonly NeuronId TargetNeuron = new("provider", OwnerA, "gmail");
    private static readonly AttemptId Attempt = new(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));

    private const string RequestSynapseId = "test.provider-request";
    private const int RequestSchemaVersion = 1;
    private const string ResponseSynapseId = "test.provider-response";
    private const int ResponseSchemaVersion = 1;

    [Fact(DisplayName = "operation identity is Task NeuronId + AttemptId + non-negative sequence")]
    public void OperationIdentityIsTaskAttemptAndSequence()
    {
        var identity = new BehaviorOperationIdentity(TaskNeuron, Attempt, sequence: 0);

        Assert.Equal(TaskNeuron, identity.Task);
        Assert.Equal(Attempt, identity.Attempt);
        Assert.Equal(0, identity.Sequence);
        Assert.True(identity.Sequence >= 0);

        var next = new BehaviorOperationIdentity(TaskNeuron, Attempt, sequence: 1);
        Assert.NotEqual(identity, next);
        Assert.Equal(identity.Task, next.Task);
        Assert.Equal(identity.Attempt, next.Attempt);
        Assert.Equal(1, next.Sequence);
    }

    [Fact(DisplayName = "protected payload round-trips for OwnerId and carries expiry")]
    public async Task ProtectedPayloadRoundTripsForOwningOwner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new AdjustableTimeProvider(DateTimeOffset.Parse("2026-07-31T12:00:00Z", CultureInfo.InvariantCulture));
        var state = new TestDurableValue<byte[]>([]);
        ValueTask Commit() => ValueTask.CompletedTask;
        var store = new DurableProtectedPayloadStore(state, Commit, new RecordingProtector(), OwnerA, time);
        var plaintext = Encoding.UTF8.GetBytes("trigger-payload");
        var lifetime = TimeSpan.FromMinutes(15);

        var reference = await store.StoreAsync(
            OwnerA,
            TaskNeuron,
            Attempt.Value,
            plaintext,
            lifetime,
            cancellationToken);

        Assert.NotEqual(Guid.Empty, reference.Id);
        Assert.NotNull(reference.ExpiresAt);
        Assert.True(reference.ExpiresAt > time.GetUtcNow());

        var recovered = new DurableProtectedPayloadStore(state, Commit, new RecordingProtector(), OwnerA, time);
        var restored = await recovered.LoadAsync(
            OwnerA,
            TaskNeuron,
            Attempt.Value,
            reference,
            cancellationToken);
        Assert.Equal(plaintext, restored.ToArray());
    }

    [Fact(DisplayName = "protected payload expires and refuses cross-owner or wrong task/attempt access")]
    public async Task ProtectedPayloadExpiresAndRefusesCrossOwner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new AdjustableTimeProvider(DateTimeOffset.Parse("2026-07-31T12:00:00Z", CultureInfo.InvariantCulture));
        var state = new TestDurableValue<byte[]>([]);
        ValueTask Commit() => ValueTask.CompletedTask;
        var store = new DurableProtectedPayloadStore(state, Commit, new RecordingProtector(), OwnerA, time);
        var plaintext = Encoding.UTF8.GetBytes("owner-scoped-secret");

        var expired = await store.StoreAsync(
            OwnerA,
            TaskNeuron,
            Attempt.Value,
            plaintext,
            TimeSpan.FromMinutes(5),
            cancellationToken);
        time.Advance(TimeSpan.FromMinutes(6));
        await Assert.ThrowsAsync<CryptographicException>(
            () => store.LoadAsync(OwnerA, TaskNeuron, Attempt.Value, expired, cancellationToken).AsTask());

        time.Advance(TimeSpan.FromHours(-1));
        var live = await store.StoreAsync(
            OwnerA,
            TaskNeuron,
            Attempt.Value,
            plaintext,
            TimeSpan.FromHours(1),
            cancellationToken);
        await Assert.ThrowsAsync<CryptographicException>(
            () => store.LoadAsync(OwnerB, TaskNeuron, Attempt.Value, live, cancellationToken).AsTask());

        var otherTask = NeuronId.For<ITask>(OwnerA, "other-task");
        await Assert.ThrowsAsync<CryptographicException>(
            () => store.LoadAsync(OwnerA, otherTask, Attempt.Value, live, cancellationToken).AsTask());

        var otherAttempt = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        await Assert.ThrowsAsync<CryptographicException>(
            () => store.LoadAsync(OwnerA, TaskNeuron, otherAttempt, live, cancellationToken).AsTask());
    }

    [Fact(DisplayName = "durable protected payload state holds ciphertext and expiry only, never plaintext")]
    public async Task DurableStateHoldsCiphertextNotPlaintext()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new AdjustableTimeProvider(DateTimeOffset.Parse("2026-07-31T12:00:00Z", CultureInfo.InvariantCulture));
        var state = new TestDurableValue<byte[]>([]);
        ValueTask Commit() => ValueTask.CompletedTask;
        var store = new DurableProtectedPayloadStore(state, Commit, new XorProtector(), OwnerA, time);
        var secret = "owner-scoped-secret-never-journaled"u8.ToArray();

        await store.StoreAsync(
            OwnerA,
            TaskNeuron,
            Attempt.Value,
            secret,
            TimeSpan.FromHours(1),
            cancellationToken);

        Assert.NotNull(state.Value);
        Assert.NotEmpty(state.Value);
        var durable = Encoding.UTF8.GetString(state.Value);
        Assert.DoesNotContain("owner-scoped-secret-never-journaled", durable, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(secret), durable, StringComparison.Ordinal);
        Assert.Contains("expiresAt", durable, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("protectedPayload", durable, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "protected payload store and load observe cancellation")]
    public async Task ProtectedPayloadStoreAndLoadObserveCancellation()
    {
        var time = new AdjustableTimeProvider(DateTimeOffset.Parse("2026-07-31T12:00:00Z", CultureInfo.InvariantCulture));
        var state = new TestDurableValue<byte[]>([]);
        ValueTask Commit() => ValueTask.CompletedTask;
        var store = new DurableProtectedPayloadStore(state, Commit, new RecordingProtector(), OwnerA, time);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.StoreAsync(
                OwnerA,
                TaskNeuron,
                Attempt.Value,
                "x"u8.ToArray(),
                TimeSpan.FromHours(1),
                cancelled.Token));

        var liveToken = TestContext.Current.CancellationToken;
        var reference = await store.StoreAsync(
            OwnerA,
            TaskNeuron,
            Attempt.Value,
            "live"u8.ToArray(),
            TimeSpan.FromHours(1),
            liveToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.LoadAsync(OwnerA, TaskNeuron, Attempt.Value, reference, cancelled.Token));
    }

    [Fact(DisplayName = "ProtectedPayloadReference stays opaque: Id + ExpiresAt only")]
    public void ProtectedPayloadReferenceCarriesNoPlaintextOrCiphertext()
    {
        var members = typeof(ProtectedPayloadReference)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { nameof(ProtectedPayloadReference.ExpiresAt), nameof(ProtectedPayloadReference.Id) },
            members);

        var reference = new ProtectedPayloadReference(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1));
        Assert.NotEqual(Guid.Empty, reference.Id);
        Assert.NotNull(reference.ExpiresAt);

        var surface = reference.ToString();
        Assert.DoesNotContain("plaintext", surface, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ciphertext", surface, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload", surface, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "completed operation replays recorded result without a second provider call")]
    public async Task CompletedOperationReplaysWithoutSecondProviderCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var state = new TestDurableValue<byte[]>([]);
        var provider = new CountingProvider();
        var clientA = new DurableTaskOperationClient(state, TaskNeuron, WorkerNeuron, Attempt);
        var historyA = new TaskOwnedOperationHistory(TaskNeuron, Attempt, clientA);
        var brokerA = new BehaviorOperationBroker(historyA, ExactGrant(), provider);

        var first = await brokerA.ExecuteAsync(
            TargetNeuron,
            RequestSynapseId,
            RequestSchemaVersion,
            ResponseSynapseId,
            ResponseSchemaVersion,
            requestPayload: new ProtectedPayloadReference(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)),
            cancellationToken);

        Assert.Equal(BehaviorOperationPhase.Completed, first.Phase);
        Assert.NotNull(first.ResponsePayload);
        Assert.Equal(1, provider.InvokeCount);

        var clientB = new DurableTaskOperationClient(state, TaskNeuron, WorkerNeuron, Attempt);
        var historyB = new TaskOwnedOperationHistory(TaskNeuron, Attempt, clientB);
        var brokerB = new BehaviorOperationBroker(historyB, ExactGrant(), provider);

        var replay = await brokerB.ExecuteAsync(
            TargetNeuron,
            RequestSynapseId,
            RequestSchemaVersion,
            ResponseSynapseId,
            ResponseSchemaVersion,
            requestPayload: new ProtectedPayloadReference(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)),
            cancellationToken);

        Assert.Equal(first.Identity, replay.Identity);
        Assert.Equal(first.ResponsePayload, replay.ResponsePayload);
        Assert.Equal(BehaviorOperationPhase.Completed, replay.Phase);
        Assert.Equal(1, provider.InvokeCount);
    }

    [Fact(DisplayName = "crash before dispatch (Prepared) retries safely")]
    public async Task CrashBeforeDispatchRetriesSafely()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var state = new TestDurableValue<byte[]>([]);
        var provider = new CountingProvider();
        var clientA = new DurableTaskOperationClient(state, TaskNeuron, WorkerNeuron, Attempt);
        var historyA = new TaskOwnedOperationHistory(TaskNeuron, Attempt, clientA);
        var brokerA = new BehaviorOperationBroker(historyA, ExactGrant(), provider);

        var prepared = await brokerA.PrepareAsync(
            TargetNeuron,
            RequestSynapseId,
            RequestSchemaVersion,
            ResponseSynapseId,
            ResponseSchemaVersion,
            requestPayload: new ProtectedPayloadReference(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)),
            cancellationToken);

        Assert.Equal(BehaviorOperationPhase.Prepared, prepared.Phase);
        Assert.Equal(0, provider.InvokeCount);

        var clientB = new DurableTaskOperationClient(state, TaskNeuron, WorkerNeuron, Attempt);
        var historyB = new TaskOwnedOperationHistory(TaskNeuron, Attempt, clientB);
        var brokerB = new BehaviorOperationBroker(historyB, ExactGrant(), provider);
        var recovered = await brokerB.RecoverAsync(prepared.Identity, cancellationToken);

        Assert.Equal(BehaviorOperationPhase.Completed, recovered.Phase);
        Assert.Equal(1, provider.InvokeCount);
        Assert.NotNull(recovered.ResponsePayload);
    }

    [Fact(DisplayName = "in-flight Dispatched recovery is uncertain, no second provider call, AttemptOutcomeUncertain")]
    public async Task InFlightEffectMapsToExistingAttemptOutcomeUncertain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var state = new TestDurableValue<byte[]>([]);
        var provider = new CountingProvider();
        var clientA = new DurableTaskOperationClient(state, TaskNeuron, WorkerNeuron, Attempt);
        var historyA = new TaskOwnedOperationHistory(TaskNeuron, Attempt, clientA);
        var brokerA = new BehaviorOperationBroker(historyA, ExactGrant(), provider);

        var prepared = await brokerA.PrepareAsync(
            TargetNeuron,
            RequestSynapseId,
            RequestSchemaVersion,
            ResponseSynapseId,
            ResponseSchemaVersion,
            requestPayload: new ProtectedPayloadReference(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)),
            cancellationToken);
        var dispatched = await brokerA.MarkDispatchedAsync(prepared.Identity, cancellationToken);

        Assert.Equal(BehaviorOperationPhase.Dispatched, dispatched.Phase);
        Assert.Equal(0, provider.InvokeCount);

        var clientB = new DurableTaskOperationClient(state, TaskNeuron, WorkerNeuron, Attempt);
        var historyB = new TaskOwnedOperationHistory(TaskNeuron, Attempt, clientB);
        var brokerB = new BehaviorOperationBroker(historyB, ExactGrant(), provider);
        var recovered = await brokerB.RecoverAsync(dispatched.Identity, cancellationToken);

        Assert.Equal(BehaviorOperationPhase.Uncertain, recovered.Phase);
        Assert.Equal(0, provider.InvokeCount);
        Assert.Null(recovered.ResponsePayload);

        var uncertain = Assert.Single(clientB.Uncertain);
        Assert.IsType<AttemptOutcomeUncertain>(uncertain);
        Assert.Equal(TaskNeuron, uncertain.Task);
        Assert.Equal(Attempt, uncertain.Attempt);
        Assert.Equal(WorkerNeuron, uncertain.Worker);
    }

    [Fact(DisplayName = "exact target NeuronId + request/response synapse contract identity is accepted")]
    public async Task ExactTargetRequestResponseEdgeIsAccepted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var state = new TestDurableValue<byte[]>([]);
        var provider = new CountingProvider();
        var client = new DurableTaskOperationClient(state, TaskNeuron, WorkerNeuron, Attempt);
        var broker = new BehaviorOperationBroker(
            new TaskOwnedOperationHistory(TaskNeuron, Attempt, client),
            ExactGrant(),
            provider);

        var result = await broker.ExecuteAsync(
            TargetNeuron,
            RequestSynapseId,
            RequestSchemaVersion,
            ResponseSynapseId,
            ResponseSchemaVersion,
            requestPayload: new ProtectedPayloadReference(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)),
            cancellationToken);

        Assert.Equal(BehaviorOperationPhase.Completed, result.Phase);
        Assert.Equal(1, provider.InvokeCount);
        Assert.NotNull(result.ResponsePayload);
    }

    [Fact(DisplayName = "method alias, wrong target, wrong request, and wrong response are refused")]
    public async Task MethodAliasWrongTargetOrWrongResponseIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var state = new TestDurableValue<byte[]>([]);
        var provider = new CountingProvider();
        var client = new DurableTaskOperationClient(state, TaskNeuron, WorkerNeuron, Attempt);
        var broker = new BehaviorOperationBroker(
            new TaskOwnedOperationHistory(TaskNeuron, Attempt, client),
            ExactGrant(),
            provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() => broker.ExecuteAsync(
            TargetNeuron,
            "ReadMessage",
            RequestSchemaVersion,
            ResponseSynapseId,
            ResponseSchemaVersion,
            requestPayload: new ProtectedPayloadReference(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)),
            cancellationToken).AsTask());

        await Assert.ThrowsAsync<InvalidOperationException>(() => broker.ExecuteAsync(
            new NeuronId("provider", OwnerA, "other"),
            RequestSynapseId,
            RequestSchemaVersion,
            ResponseSynapseId,
            ResponseSchemaVersion,
            requestPayload: new ProtectedPayloadReference(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)),
            cancellationToken).AsTask());

        await Assert.ThrowsAsync<InvalidOperationException>(() => broker.ExecuteAsync(
            TargetNeuron,
            "test.provider-other-request",
            RequestSchemaVersion,
            ResponseSynapseId,
            ResponseSchemaVersion,
            requestPayload: new ProtectedPayloadReference(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)),
            cancellationToken).AsTask());

        await Assert.ThrowsAsync<InvalidOperationException>(() => broker.ExecuteAsync(
            TargetNeuron,
            RequestSynapseId,
            RequestSchemaVersion,
            "test.provider-other-response",
            ResponseSchemaVersion,
            requestPayload: new ProtectedPayloadReference(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)),
            cancellationToken).AsTask());

        Assert.Equal(0, provider.InvokeCount);
    }

    private static BehaviorCapabilityEdge ExactGrant() => new(
        TargetNeuron,
        RequestSynapseId,
        RequestSchemaVersion,
        ResponseSynapseId,
        ResponseSchemaVersion);

    private sealed class CountingProvider : IBehaviorOperationDispatcher
    {
        public int InvokeCount { get; private set; }

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

            InvokeCount++;
            return ValueTask.FromResult(
                new ProtectedPayloadReference(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)));
        }
    }

    private sealed class RecordingProtector : IDurablePayloadProtector
    {
        public byte[] Protect(string purpose, ReadOnlySpan<byte> plaintext) => plaintext.ToArray();

        public byte[] Unprotect(string purpose, ReadOnlySpan<byte> protectedPayload) => protectedPayload.ToArray();
    }

    private sealed class XorProtector : IDurablePayloadProtector
    {
        public byte[] Protect(string purpose, ReadOnlySpan<byte> plaintext)
        {
            var protectedPayload = plaintext.ToArray();
            for (var index = 0; index < protectedPayload.Length; index++)
            {
                protectedPayload[index] ^= 0x5A;
            }

            return protectedPayload;
        }

        public byte[] Unprotect(string purpose, ReadOnlySpan<byte> protectedPayload)
            => Protect(purpose, protectedPayload);
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset utcNow = start;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan delta) => utcNow += delta;
    }

    private sealed class TestDurableValue<T>(T value) : IDurableValue<T>
    {
        [AllowNull]
        public T Value { get; set; } = value;
    }

    private sealed class DurableTaskOperationClient(
        TestDurableValue<byte[]> state,
        NeuronId task,
        NeuronId worker,
        AttemptId attempt) : ITaskOperationClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public List<AttemptOutcomeUncertain> Uncertain { get; } = [];

        public ValueTask<TaskOperationSnapshot> PrepareAsync(
            PrepareTaskOperation command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(command);
            RequireAttempt(command.Attempt);
            ValidateSequence(command.Sequence);
            ValidateEdge(command.Edge);
            ValidateReference(command.RequestPayload);

            var operations = Load();
            var key = OperationKey(command.Attempt, command.Sequence);
            if (operations.TryGetValue(key, out var existing))
            {
                if (!EdgesEqual(existing.Edge, command.Edge))
                {
                    throw new InvalidOperationException("Existing operation edge does not match.");
                }

                return ValueTask.FromResult(ToSnapshot(existing));
            }

            RequireNextSequence(operations, command.Attempt, command.Sequence);
            var record = new OperationRecord(
                command.Attempt.Value,
                command.Sequence,
                ToEdgeRecord(command.Edge),
                command.RequestPayload.Id,
                command.RequestPayload.ExpiresAt,
                TaskOperationPhase.Prepared,
                ResponseId: null,
                ResponseExpiresAt: null,
                RedactedSummary: null);
            operations[key] = record;
            Save(operations);
            return ValueTask.FromResult(ToSnapshot(record));
        }

        public ValueTask<ReadTaskOperationResult> ReadAsync(
            ReadTaskOperation command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(command);
            RequireAttempt(command.Attempt);
            ValidateSequence(command.Sequence);

            var operations = Load();
            var key = OperationKey(command.Attempt, command.Sequence);
            if (!operations.TryGetValue(key, out var existing))
            {
                return ValueTask.FromResult(new ReadTaskOperationResult(null));
            }

            return ValueTask.FromResult(new ReadTaskOperationResult(ToSnapshot(existing)));
        }

        public ValueTask<TaskOperationSnapshot> TransitionAsync(
            TransitionTaskOperation command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(command);
            RequireAttempt(command.Attempt);
            ValidateSequence(command.Sequence);

            var operations = Load();
            var key = OperationKey(command.Attempt, command.Sequence);
            if (!operations.TryGetValue(key, out var existing))
            {
                throw new InvalidOperationException("Operation does not exist.");
            }

            if (existing.Phase == command.Phase
                && existing.ResponseId == command.ResponsePayload?.Id
                && existing.RedactedSummary == command.RedactedSummary)
            {
                return ValueTask.FromResult(ToSnapshot(existing));
            }

            if (existing.Phase != command.ExpectedPhase)
            {
                throw new InvalidOperationException("Expected phase does not match durable phase.");
            }

            ValidateTransition(existing.Phase, command.Phase, command.ResponsePayload);

            var updated = existing with
            {
                Phase = command.Phase,
                ResponseId = command.ResponsePayload?.Id,
                ResponseExpiresAt = command.ResponsePayload?.ExpiresAt,
                RedactedSummary = command.RedactedSummary,
            };
            operations[key] = updated;
            Save(operations);

            if (command.Phase == TaskOperationPhase.Uncertain)
            {
                Uncertain.Add(new AttemptOutcomeUncertain(
                    task,
                    worker,
                    attempt,
                    Revision: 0,
                    OperationBlockerId(task, command.Attempt, command.Sequence)));
            }

            return ValueTask.FromResult(ToSnapshot(updated));
        }

        private Dictionary<string, OperationRecord> Load()
        {
            if (state.Value is not { Length: > 0 } bytes)
            {
                return new Dictionary<string, OperationRecord>(StringComparer.Ordinal);
            }

            var records = JsonSerializer.Deserialize<Dictionary<string, OperationRecord>>(bytes, JsonOptions);
            return records ?? new Dictionary<string, OperationRecord>(StringComparer.Ordinal);
        }

        private void Save(Dictionary<string, OperationRecord> operations)
            => state.Value = JsonSerializer.SerializeToUtf8Bytes(operations, JsonOptions);

        private void RequireAttempt(AttemptId commandAttempt)
        {
            if (commandAttempt != attempt)
            {
                throw new InvalidOperationException("Attempt does not match client attempt.");
            }
        }

        private static void ValidateSequence(int sequence)
        {
            if (sequence < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Sequence must be non-negative.");
            }
        }

        private static void RequireNextSequence(
            Dictionary<string, OperationRecord> operations,
            AttemptId commandAttempt,
            int sequence)
        {
            var next = 0;
            foreach (var record in operations.Values)
            {
                if (record.Attempt == commandAttempt.Value && record.Sequence >= next)
                {
                    next = record.Sequence + 1;
                }
            }

            if (sequence != next)
            {
                throw new InvalidOperationException(
                    $"Operation sequence must be contiguous; expected {next}, received {sequence}.");
            }
        }

        private static void ValidateEdge(TaskOperationEdge edge)
        {
            ArgumentNullException.ThrowIfNull(edge);
            if (edge.Target == default
                || string.IsNullOrWhiteSpace(edge.RequestSynapseId)
                || string.IsNullOrWhiteSpace(edge.ResponseSynapseId)
                || edge.RequestSchemaVersion <= 0
                || edge.ResponseSchemaVersion <= 0)
            {
                throw new ArgumentException("Edge is invalid.", nameof(edge));
            }
        }

        private static void ValidateReference(ProtectedPayloadReference reference)
        {
            if (reference.Id == Guid.Empty)
            {
                throw new ArgumentException("Protected payload reference cannot be empty.");
            }
        }

        private static void ValidateTransition(
            TaskOperationPhase current,
            TaskOperationPhase target,
            ProtectedPayloadReference? responsePayload)
        {
            switch (current, target)
            {
                case (TaskOperationPhase.Prepared, TaskOperationPhase.Dispatched):
                    if (responsePayload is not null)
                    {
                        throw new InvalidOperationException("Prepared→Dispatched cannot carry a response reference.");
                    }

                    break;

                case (TaskOperationPhase.Dispatched, TaskOperationPhase.Completed):
                    if (responsePayload is null || responsePayload.Value.Id == Guid.Empty)
                    {
                        throw new InvalidOperationException("Dispatched→Completed requires a response reference.");
                    }

                    break;

                case (TaskOperationPhase.Dispatched, TaskOperationPhase.Uncertain):
                    if (responsePayload is not null)
                    {
                        throw new InvalidOperationException("Dispatched→Uncertain cannot carry a response reference.");
                    }

                    break;

                default:
                    throw new InvalidOperationException($"Transition from '{current}' to '{target}' is not allowed.");
            }
        }

        private static string OperationKey(AttemptId commandAttempt, int sequence)
            => $"{commandAttempt.Value:N}:{sequence.ToString(CultureInfo.InvariantCulture)}";

        private static bool EdgesEqual(EdgeRecord left, TaskOperationEdge right)
            => left.TargetType == right.Target.Type
                && left.TargetOwner == right.Target.Owner.Value
                && left.TargetName == right.Target.Name
                && left.RequestSynapseId == right.RequestSynapseId
                && left.RequestSchemaVersion == right.RequestSchemaVersion
                && left.ResponseSynapseId == right.ResponseSynapseId
                && left.ResponseSchemaVersion == right.ResponseSchemaVersion;

        private static EdgeRecord ToEdgeRecord(TaskOperationEdge edge)
            => new(
                edge.Target.Type,
                edge.Target.Owner.Value,
                edge.Target.Name,
                edge.RequestSynapseId,
                edge.RequestSchemaVersion,
                edge.ResponseSynapseId,
                edge.ResponseSchemaVersion);

        private static TaskOperationSnapshot ToSnapshot(OperationRecord record)
            => new(
                new AttemptId(record.Attempt),
                record.Sequence,
                new TaskOperationEdge(
                    new NeuronId(record.Edge.TargetType, new OwnerId(record.Edge.TargetOwner), record.Edge.TargetName),
                    record.Edge.RequestSynapseId,
                    record.Edge.RequestSchemaVersion,
                    record.Edge.ResponseSynapseId,
                    record.Edge.ResponseSchemaVersion),
                new ProtectedPayloadReference(record.RequestId, record.RequestExpiresAt),
                record.Phase,
                record.ResponseId is { } responseId
                    ? new ProtectedPayloadReference(responseId, record.ResponseExpiresAt)
                    : null,
                record.RedactedSummary);

        private static BlockerId OperationBlockerId(NeuronId taskNeuron, AttemptId commandAttempt, int sequence)
        {
            var material = $"{taskNeuron}:{commandAttempt.Value:N}:{sequence.ToString(CultureInfo.InvariantCulture)}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
            var guidBytes = hash.AsSpan(0, 16).ToArray();
            if (guidBytes.All(static b => b == 0))
            {
                guidBytes[^1] = 1;
            }

            return new BlockerId(new Guid(guidBytes));
        }

        private sealed record EdgeRecord(
            string TargetType,
            string TargetOwner,
            string TargetName,
            string RequestSynapseId,
            int RequestSchemaVersion,
            string ResponseSynapseId,
            int ResponseSchemaVersion);

        private sealed record OperationRecord(
            Guid Attempt,
            int Sequence,
            EdgeRecord Edge,
            Guid RequestId,
            DateTimeOffset? RequestExpiresAt,
            TaskOperationPhase Phase,
            Guid? ResponseId,
            DateTimeOffset? ResponseExpiresAt,
            string? RedactedSummary);
    }
}
