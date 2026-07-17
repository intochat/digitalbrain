using Brain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using System.Text.Json;

namespace Brain.Kernel;

[GrainType("neuron")]
public sealed class NeuronGrain([NeuronState] NeuronDurableState state, IServiceProvider services) : DurableGrain, INeuron
{
    private NeuronAddress _address;
    private INeuronKind? _kind;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _address = NeuronAddress.Parse(this.GetPrimaryKeyString());
        _kind = services.GetKeyedService<INeuronKind>(_address.Kind);
        return base.OnActivateAsync(cancellationToken);
    }

    private long Revision => state.Journal.Count;

    private NeuronContext Context(string callerKey) =>
        new(_address, callerKey, Revision, [.. state.Synapses], [.. state.Journal]);

    public Task<NeuronDescription> DescribeAsync() =>
        Task.FromResult(new NeuronDescription(_address.Kind, Revision, RequireKind().Contracts));

    public Task<NeuronSnapshot> ReadAsync(string projection) =>
        Task.FromResult(new NeuronSnapshot(Revision, RequireKind().Project(Context(""), projection)));

    public Task<NeuronEventPage> ReadEventsAsync(long fromRevision, int max)
    {
        var events = state.Journal.Skip((int)fromRevision).Take(Math.Clamp(max, 1, 500)).ToArray();
        return Task.FromResult(new NeuronEventPage(events, fromRevision + events.Length));
    }

    public async Task<NeuronReceipt> InvokeAsync(NeuronInvocation invocation)
    {
        var kind = RequireKind();
        if (state.Receipts.TryGetValue(invocation.CommandId, out var replay))
            return replay;

        NeuronAddress caller;
        try
        {
            caller = NeuronAddress.Parse(invocation.CallerKey);
        }
        catch (ArgumentException)
        {
            throw new BrainException(BrainErrors.CallerMalformed, invocation.CallerKey);
        }
        var requiresGrant = caller.OwnerId != _address.OwnerId || caller.SpaceId.StartsWith("behavior/", StringComparison.Ordinal);
        if (requiresGrant && !state.Synapses.Any(s =>
                s.Relation == SynapseRelation.Grants
                && s.TargetKey == invocation.CallerKey
                && s.Constraint == invocation.Contract))
            throw new BrainException(BrainErrors.GrantMissing, $"{invocation.CallerKey} lacks {invocation.Contract}");

        if (invocation.ExpectedRevision is { } expected && expected != Revision)
            throw new BrainException(BrainErrors.RevisionConflict, $"expected {expected}, actual {Revision}");

        var result = await kind.InvokeAsync(Context(invocation.CallerKey), invocation);

        string? effectKey = null;
        if (result.Effect is { } proposal)
        {
            effectKey = new NeuronAddress(_address.OwnerId, _address.SpaceId, $"effect/{invocation.CommandId}").ToGrainKey();
            var effect = GrainFactory.GetGrain<INeuron>(effectKey);
            await effect.InvokeAsync(new("effect.propose.v1",
                JsonSerializer.Serialize(proposal), invocation.CommandId, this.GetPrimaryKeyString()));
            state.Synapses.Add(new SynapseRecord(SynapseRelation.Awaits, effectKey, invocation.Contract, Revision));
        }

        foreach (var (eventKind, payload) in result.Events)
            state.Journal.Add(new NeuronEvent(Revision + 1, eventKind, payload, invocation.CommandId, DateTimeOffset.UtcNow));
        if (result.Synapse is { } synapse)
            state.Synapses.Add(synapse with { Revision = Revision });

        var receipt = new NeuronReceipt(invocation.CommandId, Revision, "accepted", result.OutputJson, effectKey);
        if (!result.TransientReceipt)
            state.Receipts[invocation.CommandId] = receipt;
        await WriteStateAsync();

        if (_address.Kind != "feed")
        {
            try
            {
                var feedKey = new NeuronAddress(_address.OwnerId, _address.SpaceId, "feed/main").ToGrainKey();
                await GrainFactory.GetGrain<INeuron>(feedKey).InvokeAsync(new(
                    "feed.append.v1",
                    JsonSerializer.Serialize(new { sourceKey = this.GetPrimaryKeyString(), revision = receipt.Revision, kind = _address.Kind }),
                    $"{this.GetPrimaryKeyString()}:{receipt.Revision}",
                    this.GetPrimaryKeyString()));
            }
            catch
            {
            }
        }

        return receipt;
    }

    private INeuronKind RequireKind() =>
        _kind ?? throw new BrainException(BrainErrors.UnknownKind, _address.Kind);
}
