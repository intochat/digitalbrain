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

        if (invocation.Contract is "neuron.grant.v1" or "neuron.revoke.v1")
            return await InvokeGrantContractAsync(invocation, caller);

        var requiresGrant = caller.OwnerId != _address.OwnerId || caller.SpaceId.StartsWith("behavior/", StringComparison.Ordinal);
        if (requiresGrant && !state.Synapses.Any(s =>
                s.Relation == SynapseRelation.Grants
                && s.TargetKey == invocation.CallerKey
                && s.Constraint == invocation.Contract))
            throw new BrainException(BrainErrors.GrantMissing, $"{invocation.CallerKey} lacks {invocation.Contract}");

        if (invocation.ExpectedRevision is { } expected && expected != Revision)
            throw new BrainException(BrainErrors.RevisionConflict, $"expected {expected}, actual {Revision}");

        var kind = RequireKind();
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

    private async Task<NeuronReceipt> InvokeGrantContractAsync(NeuronInvocation invocation, NeuronAddress caller)
    {
        if (caller.OwnerId != _address.OwnerId || caller.SpaceId.StartsWith("behavior/", StringComparison.Ordinal))
            throw new BrainException(BrainErrors.GrantDenied, $"{invocation.CallerKey} cannot manage grants on {this.GetPrimaryKeyString()}");

        var (granteeKey, contract) = ParseGrantInput(invocation.InputJson);

        string outputJson;
        if (invocation.Contract == "neuron.grant.v1")
        {
            var alreadyGranted = state.Synapses.Any(s =>
                s.Relation == SynapseRelation.Grants && s.TargetKey == granteeKey && s.Constraint == contract);
            if (!alreadyGranted)
                state.Synapses.Add(new SynapseRecord(SynapseRelation.Grants, granteeKey, contract, Revision));
            outputJson = """{"granted":true}""";
        }
        else
        {
            var matches = state.Synapses.Where(s =>
                s.Relation == SynapseRelation.Grants && s.TargetKey == granteeKey && s.Constraint == contract).ToArray();
            foreach (var synapse in matches)
                state.Synapses.Remove(synapse);
            outputJson = JsonSerializer.Serialize(new { revoked = matches.Length });
        }

        var receipt = new NeuronReceipt(invocation.CommandId, Revision, "accepted", outputJson, null);
        state.Receipts[invocation.CommandId] = receipt;
        await WriteStateAsync();
        return receipt;
    }

    private static (string GranteeKey, string Contract) ParseGrantInput(string inputJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(inputJson);
        }
        catch (JsonException)
        {
            throw new BrainException("input.invalid", "malformed json");
        }

        using (doc)
        {
            var root = doc.RootElement;
            return (RequireStringField(root, "granteeKey"), RequireStringField(root, "contract"));
        }
    }

    private static string RequireStringField(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var element) || element.ValueKind != JsonValueKind.String)
            throw new BrainException("input.invalid", $"{field} field is required");

        var value = element.GetString();
        if (string.IsNullOrEmpty(value))
            throw new BrainException("input.invalid", $"{field} cannot be empty");

        return value;
    }

    private INeuronKind RequireKind() =>
        _kind ?? throw new BrainException(BrainErrors.UnknownKind, _address.Kind);
}
