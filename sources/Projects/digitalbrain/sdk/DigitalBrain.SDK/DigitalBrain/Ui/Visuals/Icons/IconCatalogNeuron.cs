using System.Text.Json;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Ui;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.Ui.Visuals.Icons;

[ImplicitStreamSubscription(IconCatalogNeuronType)]
internal sealed class IconCatalogNeuron(
    [FromKeyedServices("incoming")]       IDurableList<Synapse>     incoming,
    [FromKeyedServices("outgoing")]       IDurableList<Synapse>     outgoing,
    [FromKeyedServices("icon-overrides")] IDurableList<IconOverride> overrides,
    IGrainFactory grains,
    TimeProvider time,
    ILogger<IconCatalogNeuron> log)
    : Neuron(incoming, outgoing, grains, log),
      IIconCatalogNeuron, INeuronMetadata,
      IHandle<ResolveIconSpec>, IHandle<SetIconOverride>
{
    public const string IconCatalogNeuronType = nameof(IconCatalogNeuron);

    public static NeuronId         Id           => new("visuals/icon-catalog");
    public static string           Icon         => "figma";
    public static NeuronCapability Capabilities => NeuronCapability.Storage;

    protected override async Task HandleSynapseAsync(Synapse s)
    {
        switch (s)
        {
            case ResolveIconSpec req:
                Counter("icons.resolved").Increment(1);
                var ovr = overrides.LastOrDefault(o => o.NeuronFqn == req.NeuronFqn);
                var resolved = IconPlan.Resolve(req, ovr, InstanceId, IconCatalogNeuronType, time.GetUtcNow());
                await FireSynapseAsync(resolved);
                await BroadcastCardAsync(resolved);
                break;

            case SetIconOverride o:
                Counter("icons.overrides").Increment(1);
                for (var i = overrides.Count - 1; i >= 0; i--)
                    if (overrides[i].NeuronFqn == o.NeuronFqn) overrides.RemoveAt(i);
                overrides.Add(IconPlan.NewOverrideRecord(o, time.GetUtcNow()));
                await WriteStateAsync();
                break;
        }
    }

    async Task BroadcastCardAsync(IconSpecResolved r)
    {
        var payload  = IconPlan.ToCardPayload(r);
        var dataJson = JsonSerializer.Serialize(payload);
        var card = new RfwCard(LibraryName:        IconPlan.CardLibrary,
        RootWidget:         IconPlan.CardRootWidget,
        DataJson:           dataJson) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: r.CorrelationId,
            causationId: null,
            callerNeuronId: InstanceId,
            callerNeuronType: IconCatalogNeuronType,
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "HomeFeed",
            timestamp: time.GetUtcNow()
        ) };
        await FireSynapseAsync(card);
    }
}
