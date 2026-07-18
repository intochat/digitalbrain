using System.Text.Json.Nodes;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Ui;
using Orleans.Journaling;

namespace DigitalBrain.Kernel.Visualization;

[ImplicitStreamSubscription(nameof(DefaultSurfaceNeuron))]
public sealed class DefaultSurfaceNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ILogger<DefaultSurfaceNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      IHandle<RenderDefaultSurfaceRequest>,
      INeuronMetadata
{
    public static NeuronId Id => new("kernel/default-surface");
    public static string Icon => "default_surface";
    public static NeuronCapability Capabilities => NeuronCapability.None;

    public async Task HandleAsync(RenderDefaultSurfaceRequest req, CancellationToken ct)
    {
        var catalog = Grains.GetGrain<IBrainCatalog>("global");
        var registered = await catalog.ListRegisteredAsync();
        
        // Find matching neuron inside global registration catalog
        var meta = registered.FirstOrDefault(r => 
            r.Id.ToString().Equals(req.NeuronId, StringComparison.OrdinalIgnoreCase) || 
            r.TypeFullName.Equals(req.NeuronId, StringComparison.OrdinalIgnoreCase)
        );

        var icon = meta?.Icon ?? "bubble_chart";
        var title = meta?.TypeFullName ?? req.NeuronId;
        var domain = meta?.Domain ?? "SDK";
        var caps = meta?.Capabilities.ToString() ?? "None";

        var data = new JsonObject
        {
            ["icon"] = icon,
            ["title"] = title,
            ["domain"] = domain,
            ["capabilities"] = caps,
            ["status"] = "Idle",
            ["last"] = new JsonArray(),
            ["ports"] = new JsonArray()
        };

        // Render standard RFW layout template for the target neuron
        await RenderAsync("digitalbrain", "DefaultSurfaceCard", data, ct);
    }
}
