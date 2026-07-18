using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.Ui.Visuals.Materials;

[ImplicitStreamSubscription(MaterialsCatalogNeuronType)]
internal sealed class MaterialsCatalog(
    [FromKeyedServices("material-overrides")] IDurableList<SurfaceOverrideEntry> overrides,
    IMaterialPlanBroadcaster broadcaster,
    TimeProvider time)
    : Neuron(),
      IMaterialsCatalog, INeuronMetadata,
      IHandle<ResolveMaterialSpec>, IHandle<SetMaterialOverride>
{
    public const string MaterialsCatalogNeuronType = nameof(MaterialsCatalog);

    public static NeuronId         Id           => new("visuals/materials-catalog");
    public static string           Icon         => "materials";
    public static NeuronCapability Capabilities => NeuronCapability.Storage;

    protected override async Task HandleSynapseAsync(Synapse s)
    {
        switch (s)
        {
            case ResolveMaterialSpec req:
                Counter("visuals.materials.resolves").Increment(1);
                var over = overrides.LastOrDefault(e => e.Surface == req.Surface)?.Patch
                           ?? MaterialOverride.None;
                var plan = MaterialPlanResolver.Derive(req.Surface, req.Tier, req.ThemeBrightness, over);
                await FireSynapseAsync(new MaterialSpecResolved(ClientId:           req.ClientId,
        Plan:               plan) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: MaterialsCatalogNeuronType,
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? string.Empty,
            timestamp: time.GetUtcNow()
        ) });
                await broadcaster.BroadcastAsync(req.ClientId, plan);
                break;

            case SetMaterialOverride cmd:
                Counter("visuals.materials.overrides").Increment(1);
                // upsert: remove any existing entry for this surface then append
                for (var i = overrides.Count - 1; i >= 0; i--)
                    if (overrides[i].Surface == cmd.Surface) overrides.RemoveAt(i);
                overrides.Add(new SurfaceOverrideEntry(cmd.Surface, cmd.Patch));
                await WriteStateAsync();
                Logger.LogInformation("override set client={Client} surface={Surface}",
                    cmd.ClientId, cmd.Surface);
                break;
        }
    }
}

[GenerateSerializer]
public sealed record SurfaceOverrideEntry(
    [property: Id(0)] string Surface,
    [property: Id(1)] MaterialOverride Patch);
