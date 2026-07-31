using System.Collections.Concurrent;
using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors;

internal sealed class InMemoryBehaviorHostPayloadStore
{
    private readonly ConcurrentDictionary<Guid, StoredPayload> payloads = new();

    public ProtectedPayloadReference Store(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ReadOnlyMemory<byte> plaintext,
        TimeSpan lifetime)
    {
        if (plaintext.IsEmpty)
        {
            throw new BehaviorHostException("empty-payload");
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new BehaviorHostException("invalid-payload-lifetime");
        }

        var id = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow + lifetime;
        payloads[id] = new StoredPayload(owner, task, attempt, plaintext.ToArray(), expiresAt);
        return new ProtectedPayloadReference(id, expiresAt);
    }

    public ReadOnlyMemory<byte> Load(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ProtectedPayloadReference reference)
    {
        if (!payloads.TryGetValue(reference.Id, out var stored))
        {
            throw new BehaviorHostException("payload-not-found");
        }

        if (stored.Owner != owner
            || stored.Task != task
            || stored.Attempt != attempt)
        {
            throw new BehaviorHostException("payload-identity-mismatch");
        }

        if (stored.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            throw new BehaviorHostException("payload-expired");
        }

        return stored.Bytes;
    }

    private sealed record StoredPayload(
        OwnerId Owner,
        NeuronId Task,
        AttemptId Attempt,
        byte[] Bytes,
        DateTimeOffset? ExpiresAt);
}

internal sealed class InMemoryBehaviorHostBrokerClientFactory(InMemoryBehaviorHostPayloadStore store)
    : IBehaviorHostBrokerClientFactory
{
    public IBehaviorHostBrokerClient Create(OwnerId owner, NeuronId task, AttemptId attempt)
        => new InMemoryBehaviorHostBrokerClient(store, owner, task, attempt);
}

internal sealed class InMemoryBehaviorHostBrokerClient : IBehaviorHostBrokerClient
{
    private readonly InMemoryBehaviorHostPayloadStore store;
    private readonly OwnerId boundOwner;
    private readonly NeuronId boundTask;
    private readonly AttemptId boundAttempt;

    public InMemoryBehaviorHostBrokerClient(
        InMemoryBehaviorHostPayloadStore store,
        OwnerId owner,
        NeuronId task,
        AttemptId attempt)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
        boundOwner = owner;
        boundTask = task;
        boundAttempt = attempt;
    }

    public ValueTask<ProtectedPayloadReference> StorePayloadAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireBoundIdentity(owner, task, attempt);
        return ValueTask.FromResult(
            store.Store(boundOwner, boundTask, boundAttempt, plaintext, TimeSpan.FromHours(1)));
    }

    public ValueTask<ReadOnlyMemory<byte>> LoadPayloadAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireBoundIdentity(owner, task, attempt);
        return ValueTask.FromResult(store.Load(boundOwner, boundTask, boundAttempt, reference));
    }

    public ValueTask<TaskOperationSnapshot> PrepareAsync(
        PrepareTaskOperation command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new BehaviorHostException("in-memory-broker-operations-unsupported");
    }

    public ValueTask<ReadTaskOperationResult> ReadAsync(
        ReadTaskOperation command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new BehaviorHostException("in-memory-broker-operations-unsupported");
    }

    public ValueTask<TaskOperationSnapshot> TransitionAsync(
        TransitionTaskOperation command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new BehaviorHostException("in-memory-broker-operations-unsupported");
    }

    public ValueTask<ProtectedPayloadReference> DispatchAsync(
        BehaviorCapabilityEdge edge,
        ProtectedPayloadReference requestPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new BehaviorHostException("in-memory-broker-operations-unsupported");
    }

    private void RequireBoundIdentity(OwnerId owner, NeuronId task, AttemptId attempt)
    {
        if (owner != boundOwner || task != boundTask || attempt != boundAttempt)
        {
            throw new BehaviorHostException("broker-identity-mismatch");
        }
    }
}
