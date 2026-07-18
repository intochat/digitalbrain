using System.Text.Json.Nodes;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using Orleans.Journaling;

namespace DigitalBrain.Kernel.Visualization;

[ImplicitStreamSubscription(nameof(ConstellationNeuron))]
public sealed class ConstellationNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ILogger<ConstellationNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      INeuronMetadata
{
    public static NeuronId Id => new("kernel/constellation");
    public static string Icon => "space_dashboard";
    public static NeuronCapability Capabilities => NeuronCapability.None;

    public async Task RefreshConstellationAsync(CancellationToken ct = default)
    {
        Logger.LogInformation("ConstellationNeuron assembling L1 brain cluster...");
        
        // Emits a high-end visual configuration representing the celestial constellation
        var data = new JsonObject
        {
            ["clusterName"] = "Global User Constellation",
            ["brains"] = new JsonArray(
                new JsonObject { ["id"] = "primary", ["name"] = "Primary", ["version"] = "v1.0.0", ["color"] = "#3B82F6" },
                new JsonObject { ["id"] = "acme-client", ["name"] = "Acme Client", ["version"] = "v2.4.1", ["color"] = "#10B981" },
                new JsonObject { ["id"] = "research", ["name"] = "Research", ["version"] = "v0.3.0", ["color"] = "#D946EF" }
            )
        };

        await RenderAsync("digitalbrain", "ConstellationSceneCard", data, ct);
    }
}
