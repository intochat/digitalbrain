using DigitalBrain.Poc.Abstractions;
using Orleans.Serialization;

namespace DigitalBrain.Poc.Runtime;

internal sealed class CandidateRuntimeSet : IDisposable
{
    private readonly IReadOnlyList<LoadedCandidate> _candidates;
    private readonly ImmutableRouteTable _routes;
    private readonly IServiceProvider _services;
    private readonly ObjectSerializer _serializer;
    private readonly DurableTurn _turns;
    private readonly Func<SynapseEnvelope, CancellationToken, Task>? _afterGeneratedLocalOutboxCommit;
    private readonly Func<PendingOutboxEnvelope, CancellationToken, Task>? _afterCandidateDeliveryCommit;

    internal CandidateRuntimeSet(
        PocDataRoot root,
        IReadOnlyList<LoadedCandidate> candidates,
        ImmutableRouteTable routes,
        IServiceProvider services,
        ObjectSerializer serializer,
        Func<SynapseEnvelope, CancellationToken, Task>? afterGeneratedLocalOutboxCommit,
        Func<PendingOutboxEnvelope, CancellationToken, Task>? afterCandidateDeliveryCommit)
    {
        _candidates = candidates;
        _routes = routes;
        _services = services;
        _serializer = serializer;
        _turns = new DurableTurn(root);
        _afterGeneratedLocalOutboxCommit = afterGeneratedLocalOutboxCommit;
        _afterCandidateDeliveryCommit = afterCandidateDeliveryCommit;
    }

    public async Task FireTrustedAsync(
        string ownerId,
        Synapse input,
        string receiptId,
        CancellationToken cancellationToken = default)
    {
        await RestoreCommittedOutboxAsync(cancellationToken);
        await StageTrustedAsync(
            ownerId,
            input,
            receiptId,
            _afterGeneratedLocalOutboxCommit,
            cancellationToken);
        await RestoreCommittedOutboxAsync(cancellationToken);
    }

    internal async Task StageTrustedAsync(
        string ownerId,
        Synapse input,
        string receiptId,
        CancellationToken cancellationToken = default)
        => await StageTrustedAsync(ownerId, input, receiptId, null, cancellationToken);

    private async Task StageTrustedAsync(
        string ownerId,
        Synapse input,
        string receiptId,
        Func<SynapseEnvelope, CancellationToken, Task>? afterGeneratedLocalOutboxCommit,
        CancellationToken cancellationToken)
    {
        var envelopes = _routes.ExpandTrustedInput(ownerId, receiptId, input);
        if (envelopes.Count == 0)
        {
            var alias = ContractAlias.For(input.GetType());
            if (_routes.Routes.Any(route => route.ContractAlias == alias))
            {
                throw new AuthorizationException(
                    $"Owner '{ownerId}' cannot invoke a route owned by another principal.");
            }

            throw new UnknownSynapseAliasException(alias);
        }

        await new RunStore(_turns.Root).BindTrustedInputDeliveriesAsync(
            ownerId,
            receiptId,
            envelopes.Select(envelope => envelope.DeliveryId),
            cancellationToken);

        foreach (var envelope in envelopes)
        {
            var candidate = ResolvePinned(envelope);
            var activation = new NeuronActivationGrain(
                _turns,
                candidate,
                new CandidatePayloadCodec(_serializer, candidate));
            var result = await activation.InvokeWithCommitAsync(
                envelope,
                journalInput: true,
                cancellationToken);
            if (result.Committed && afterGeneratedLocalOutboxCommit is not null)
            {
                foreach (var output in result.Outputs.Where(output => output.TargetRevision is not null))
                {
                    await afterGeneratedLocalOutboxCommit(output, cancellationToken);
                }
            }
        }

    }

    public async Task RestoreCommittedOutboxAsync(
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var pending = await _turns.ReadPendingCandidateOutboxAsync(cancellationToken);
            if (pending.Count == 0)
            {
                return;
            }

            foreach (var item in pending)
            {
                if (item.ProducingRevision != item.TargetRevision ||
                    item.ProducingModuleIdentity != item.TargetModuleIdentity)
                {
                    throw new InvalidOperationException(
                        "A POC-0 candidate-local envelope must target its producing immutable module identity.");
                }

                var candidate = ResolvePinned(item);
                var synapse = new CandidatePayloadCodec(_serializer, candidate).Deserialize(item);
                var envelope = SynapseEnvelope.Restore(
                    item.DeliveryId,
                    item.OwnerId,
                    item.ContractAlias,
                    synapse,
                    item.Family,
                    item.ProducingRevision,
                    item.ProducingModuleIdentity,
                    item.TargetRevision,
                    item.TargetModuleIdentity,
                    item.TargetNeuronType);

                _ = await new NeuronActivationGrain(
                    _turns,
                    candidate,
                    new CandidatePayloadCodec(_serializer, candidate)).InvokeAsync(
                    envelope,
                    journalInput: false,
                    cancellationToken);
                if (_afterCandidateDeliveryCommit is not null)
                {
                    await _afterCandidateDeliveryCommit(item, cancellationToken);
                }

                await _turns.MarkOutboxDeliveredAsync(item.DeliveryId, cancellationToken);
            }
        }
    }

    public Task<int> ReadHandledCountAsync(
        string contractAlias,
        CancellationToken cancellationToken = default) =>
        _turns.ReadHandledCountAsync($"handled|{contractAlias}", cancellationToken);

    public Task<int> ReadTurnCountAsync(
        CandidateFamilyId family,
        CancellationToken cancellationToken = default) =>
        _turns.ReadHandledCountPrefixAsync(
            $"family|{family.Value}|",
            cancellationToken);

    internal async Task<PersistedCandidatePayloadView> ReadPersistedCandidatePayloadAsync(
        CancellationToken cancellationToken = default)
    {
        var pending = await _turns.ReadPendingCandidateOutboxAsync(cancellationToken);
        var item = pending.SingleOrDefault() ?? throw new InvalidDataException(
            "The fixture has no pending candidate payload to inspect.");
        var candidate = ResolvePinned(item);
        var synapse = new CandidatePayloadCodec(_serializer, candidate).Deserialize(item);
        var probeId = synapse.GetType().GetProperty("Value")?.GetValue(synapse) as string ??
            throw new InvalidDataException(
                "The actual persisted candidate payload did not restore the fixed fixture probe value.");
        return new PersistedCandidatePayloadView(
            item.DeliveryId,
            probeId,
            item.ContractAlias,
            Convert.FromBase64String(item.PayloadBase64).Length);
    }

    public void Dispose()
    {
        if (_services is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private LoadedCandidate ResolvePinned(SynapseEnvelope envelope)
    {
        var identity = envelope.TargetModuleIdentity ?? throw new InvalidDataException(
            "Pinned candidate envelope is missing its immutable module identity.");
        var candidate = _candidates.SingleOrDefault(candidate =>
            candidate.Module.OwnerId == envelope.OwnerId &&
            candidate.Module.Family == envelope.CandidateFamily &&
            candidate.Module.Revision == envelope.TargetRevision &&
            candidate.Identity == identity);
        return candidate ?? throw new InvalidOperationException(
            $"Pinned candidate module identity '{envelope.CandidateFamily}/{envelope.TargetRevision}' is not loaded.");
    }

    private LoadedCandidate ResolvePinned(PendingOutboxEnvelope envelope)
    {
        var candidate = _candidates.SingleOrDefault(candidate =>
            candidate.Module.OwnerId == envelope.OwnerId &&
            candidate.Module.Family == envelope.Family &&
            candidate.Module.Revision == envelope.TargetRevision &&
            candidate.Identity == envelope.TargetModuleIdentity);
        return candidate ?? throw new InvalidOperationException(
            $"Pinned candidate module identity '{envelope.Family}/{envelope.TargetRevision}' is not loaded.");
    }
}
