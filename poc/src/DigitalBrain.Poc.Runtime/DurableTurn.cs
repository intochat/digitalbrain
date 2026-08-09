using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Poc.Abstractions;

namespace DigitalBrain.Poc.Runtime;

public sealed class DurableTurn
{
    public const int DefaultMaximumStateBytes = 65_536;

    private readonly RunStore _store;
    private readonly int _maximumStateBytes;

    public DurableTurn(PocDataRoot root, int maximumStateBytes = DefaultMaximumStateBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStateBytes);
        _store = new RunStore(root);
        _maximumStateBytes = maximumStateBytes;
        Root = root;
    }

    internal PocDataRoot Root { get; }

    public async Task ExecuteAsync<TState>(
        string receiptId,
        string inputKind,
        string stateKey,
        TState initialState,
        Func<IDurableState<TState>, IDigitalBrain, Task> handler,
        CancellationToken cancellationToken = default)
    {
        _ = await ExecuteAsync(
            receiptId,
            inputKind,
            stateKey,
            initialState,
            ownerId: null,
            candidateFamily: null,
            producingRevision: null,
            producingModuleIdentity: null,
            targetRevision: null,
            targetModuleIdentity: null,
            handledCountKey: null,
            familyHandledCountKey: null,
            journalInput: true,
            envelopeAt: null,
            serializeCandidatePayload: null,
            handler,
            cancellationToken);
    }

    internal Task<bool> ExecuteAsync<TState>(
        string receiptId,
        string inputKind,
        string stateKey,
        TState initialState,
        string? ownerId,
        CandidateFamilyId? candidateFamily,
        string? producingRevision,
        CandidateModuleIdentity? producingModuleIdentity,
        string? targetRevision,
        CandidateModuleIdentity? targetModuleIdentity,
        string? handledCountKey,
        string? familyHandledCountKey,
        bool journalInput,
        Func<int, SynapseEnvelope?>? envelopeAt,
        Func<SynapseEnvelope, byte[]>? serializeCandidatePayload,
        Func<IDurableState<TState>, IDigitalBrain, Task> handler,
        CancellationToken cancellationToken = default)
    {
        Validate(receiptId, nameof(receiptId));
        Validate(inputKind, nameof(inputKind));
        Validate(stateKey, nameof(stateKey));
        ArgumentNullException.ThrowIfNull(initialState);
        ArgumentNullException.ThrowIfNull(handler);

        return _store.TransactAsync(
            async document =>
            {
                if (document.AcknowledgedReceipts.Contains(receiptId))
                {
                    return (false, false);
                }

                var current = document.States.TryGetValue(stateKey, out var stored)
                    ? stored.Deserialize<TState>() ?? throw new InvalidDataException(
                        $"Stored state '{stateKey}' deserialized to null.")
                    : initialState;
                var state = new DurableState<TState>(current);
                var outgoing = new List<Synapse>();
                var brain = new StagedBrain(outgoing);

                await handler(state, brain);
                cancellationToken.ThrowIfCancellationRequested();

                var serializedState = JsonSerializer.SerializeToUtf8Bytes(state.Value);
                if (serializedState.Length > _maximumStateBytes)
                {
                    throw new StateTooLargeException(serializedState.Length, _maximumStateBytes);
                }

                document.States[stateKey] = JsonSerializer.Deserialize<JsonElement>(serializedState);
                document.AcknowledgedReceipts.Add(receiptId);
                if (journalInput)
                {
                    document.Journal.Add(new JournalEntry(receiptId, inputKind, "in"));
                }
                for (var ordinal = 0; ordinal < outgoing.Count; ordinal++)
                {
                    var synapse = outgoing[ordinal];
                    var envelope = envelopeAt?.Invoke(ordinal);
                    var isCandidatePayload = envelope?.TargetRevision is not null;
                    var outputTargetRevision = envelope is null
                        ? targetRevision
                        : envelope.TargetRevision;
                    var outputTargetModuleIdentity = envelope is null
                        ? targetModuleIdentity
                        : envelope.TargetModuleIdentity;
                    var payload = isCandidatePayload
                        ? serializeCandidatePayload?.Invoke(envelope!) ?? throw new InvalidDataException(
                            "A candidate-local outbox payload requires the configured Orleans serializer.")
                        : JsonSerializer.SerializeToUtf8Bytes(synapse, synapse.GetType());
                    document.Journal.Add(new JournalEntry(receiptId, synapse.GetType().Name, "out"));
                    document.Outbox.Add(new OutboxEntry(
                        envelope?.DeliveryId ?? DeriveDeliveryId(receiptId, ordinal),
                        receiptId,
                        ordinal,
                        synapse.GetType().Name,
                        envelope?.ContractAlias ?? ContractAlias.For(synapse.GetType()),
                        isCandidatePayload
                            ? CandidatePayloadCodec.OrleansObjectSerializerFormat
                            : "json",
                        Convert.ToBase64String(payload),
                        envelope?.OwnerId ?? ownerId,
                        (envelope?.CandidateFamily ?? candidateFamily)?.Value,
                        envelope?.ProducingRevision ?? producingRevision,
                        envelope?.ProducingModuleIdentity ?? producingModuleIdentity,
                        outputTargetRevision,
                        outputTargetModuleIdentity,
                        envelope?.TargetNeuronType ?? string.Empty,
                        Delivered: false,
                        TargetScope: envelope?.TargetScope));
                }

                if (handledCountKey is not null)
                {
                    document.HandledCounts.TryGetValue(handledCountKey, out var count);
                    document.HandledCounts[handledCountKey] = checked(count + 1);
                }

                if (familyHandledCountKey is not null)
                {
                    document.HandledCounts.TryGetValue(familyHandledCountKey, out var familyCount);
                    document.HandledCounts[familyHandledCountKey] = checked(familyCount + 1);
                }

                return (true, true);
            },
            cancellationToken);
    }

    public Task<TState> ReadStateAsync<TState>(
        string stateKey,
        TState initialState,
        CancellationToken cancellationToken = default) =>
        _store.ReadAsync(
            document => document.States.TryGetValue(stateKey, out var stored)
                ? stored.Deserialize<TState>() ?? throw new InvalidDataException(
                    $"Stored state '{stateKey}' deserialized to null.")
                : initialState,
            cancellationToken);

    public async Task ExecuteTerminalFactAsync<TState, TFact>(
        string effectId,
        string inputKind,
        string stateKey,
        TState initialState,
        Func<IDurableState<TState>, TFact> handler,
        CancellationToken cancellationToken = default)
        where TFact : Synapse
    {
        _ = await ExecuteTerminalFactWithCommitAsync(
            effectId,
            inputKind,
            stateKey,
            initialState,
            handler,
            cancellationToken);
    }

    public Task<bool> ExecuteTerminalFactWithCommitAsync<TState, TFact>(
        string effectId,
        string inputKind,
        string stateKey,
        TState initialState,
        Func<IDurableState<TState>, TFact> handler,
        CancellationToken cancellationToken = default)
        where TFact : Synapse
    {
        Validate(effectId, nameof(effectId));
        Validate(inputKind, nameof(inputKind));
        Validate(stateKey, nameof(stateKey));
        ArgumentNullException.ThrowIfNull(initialState);
        ArgumentNullException.ThrowIfNull(handler);

        return _store.TransactAsync(
            document =>
            {
                if (document.AcknowledgedReceipts.Contains(effectId))
                {
                    return Task.FromResult((false, false));
                }

                var current = document.States.TryGetValue(stateKey, out var stored)
                    ? stored.Deserialize<TState>() ?? throw new InvalidDataException(
                        $"Stored state '{stateKey}' deserialized to null.")
                    : initialState;
                var state = new DurableState<TState>(current);
                var fact = handler(state) ?? throw new InvalidOperationException(
                    "A terminal durable turn must produce one fact.");
                var serializedState = JsonSerializer.SerializeToUtf8Bytes(state.Value);
                if (serializedState.Length > _maximumStateBytes)
                {
                    throw new StateTooLargeException(serializedState.Length, _maximumStateBytes);
                }

                document.States[stateKey] = JsonSerializer.Deserialize<JsonElement>(serializedState);
                document.AcknowledgedReceipts.Add(effectId);
                document.Journal.Add(new JournalEntry(effectId, inputKind, "in"));
                document.Journal.Add(new JournalEntry(
                    effectId,
                    fact.GetType().Name,
                    "fact",
                    JsonSerializer.Serialize(fact, fact.GetType())));
                return Task.FromResult((true, true));
            },
            cancellationToken);
    }

    internal Task<int> ReadHandledCountAsync(
        string handledCountKey,
        CancellationToken cancellationToken = default) =>
        _store.ReadAsync(
            document => document.HandledCounts.GetValueOrDefault(handledCountKey),
            cancellationToken);

    internal Task<int> ReadHandledCountPrefixAsync(
        string handledCountPrefix,
        CancellationToken cancellationToken = default) =>
        _store.ReadAsync(
            document => document.HandledCounts
                .Where(entry => entry.Key.StartsWith(handledCountPrefix, StringComparison.Ordinal))
                .Sum(entry => entry.Value),
            cancellationToken);

    internal Task<IReadOnlyList<PendingOutboxEnvelope>> ReadPendingCandidateOutboxAsync(
        CancellationToken cancellationToken = default) =>
        _store.ReadAsync<IReadOnlyList<PendingOutboxEnvelope>>(
            document => document.Outbox
                .Where(entry => !entry.Delivered && entry.TargetRevision is not null)
                .Select(entry => new PendingOutboxEnvelope(
                    entry.DeliveryId,
                    entry.ContractAlias,
                    entry.PayloadBase64,
                    entry.OwnerId!,
                    CandidateFamilyId.Parse(entry.CandidateFamily!),
                    entry.ProducingRevision,
                    entry.ProducingModuleIdentity,
                    entry.TargetRevision!,
                    entry.TargetModuleIdentity ?? throw new InvalidDataException(
                        $"Committed candidate outbox payload '{entry.DeliveryId}' is missing its immutable module identity."),
                    entry.PayloadFormat,
                    entry.TargetNeuronType))
                .ToArray(),
            cancellationToken);

    internal Task<IReadOnlyList<PendingTrustedTargetOutboxEnvelope>> ReadPendingTrustedTargetOutboxAsync(
        CancellationToken cancellationToken = default) =>
        _store.ReadAsync<IReadOnlyList<PendingTrustedTargetOutboxEnvelope>>(
            document => document.Outbox
                .Where(entry =>
                    !entry.Delivered &&
                    entry.TargetRevision is null &&
                    !string.IsNullOrWhiteSpace(entry.TargetScope))
                .Select(ToPendingTrustedTarget)
                .ToArray(),
            cancellationToken);

    internal Task MarkOutboxDeliveredAsync(
        string deliveryId,
        CancellationToken cancellationToken = default) =>
        _store.TransactAsync(
            document =>
            {
                var index = document.Outbox.FindIndex(entry => entry.DeliveryId == deliveryId);
                if (index < 0)
                {
                    throw new InvalidDataException($"Committed outbox delivery '{deliveryId}' is missing.");
                }

                document.Outbox[index] = document.Outbox[index] with { Delivered = true };
                return Task.FromResult((true, true));
            },
            cancellationToken);

    private static string DeriveDeliveryId(string receiptId, int ordinal)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{receiptId}\n{ordinal}"));
        return $"delivery-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static PendingTrustedTargetOutboxEnvelope ToPendingTrustedTarget(OutboxEntry entry)
    {
        if (entry.TargetModuleIdentity is not null ||
            !string.IsNullOrEmpty(entry.TargetNeuronType) ||
            string.IsNullOrWhiteSpace(entry.OwnerId) ||
            string.IsNullOrWhiteSpace(entry.CandidateFamily) ||
            string.IsNullOrWhiteSpace(entry.ProducingRevision) ||
            entry.ProducingModuleIdentity is null)
        {
            throw new InvalidDataException(
                $"Committed trusted target payload '{entry.DeliveryId}' is missing immutable candidate provenance.");
        }

        return new PendingTrustedTargetOutboxEnvelope(
            entry.DeliveryId,
            entry.Kind,
            entry.ContractAlias,
            entry.PayloadFormat,
            entry.PayloadBase64,
            entry.OwnerId,
            CandidateFamilyId.Parse(entry.CandidateFamily),
            entry.ProducingRevision,
            entry.ProducingModuleIdentity,
            entry.TargetScope!);
    }

    private static void Validate(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value cannot be empty.", parameterName);
        }
    }

    private sealed class StagedBrain(List<Synapse> outgoing) : IDigitalBrain
    {
        public Task FireSynapse(Synapse synapse, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(synapse);
            cancellationToken.ThrowIfCancellationRequested();
            outgoing.Add(synapse);
            return Task.CompletedTask;
        }
    }
}
