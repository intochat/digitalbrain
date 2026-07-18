using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using Orleans.Journaling;

namespace DigitalBrain.SDK.Canvas.Canvas;

[ImplicitStreamSubscription(CanvasNeuronType)]
internal sealed class CanvasNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ILogger<CanvasNeuron> logger,
    [FromKeyedServices("canvas-scenes")] IDurableList<CanvasSceneRecord> scenes)
    : Neuron(incoming, outgoing, grains, logger),
      ICanvasNeuron,
      INeuronMetadata,
      IHandle<OpenCanvasRequest>,
      IHandle<SaveCanvas>
{
    public const string CanvasNeuronType = CanvasNeuronTypes.CanvasNeuron;

    public static NeuronId         Id           => new("canvas/board");
    public static string           Icon         => "canvas";
    public static NeuronCapability Capabilities => NeuronCapability.Storage;

    protected override async Task HandleSynapseAsync(Synapse synapse)
    {
        switch (synapse)
        {
            case OpenCanvasRequest req:
                var sceneName = CanvasPlan.NormalizeSceneName(req.SceneName);
                var content = CanvasPlan.LatestContent(scenes, req.UserId, sceneName);
                await FireSynapseAsync(CanvasPlan.ToCanvasReady(req, content));
                await FireSynapseAsync(CanvasPlan.ToCanvasCard(req, content));
                break;

            case SaveCanvas save:
                var name = CanvasPlan.NormalizeSceneName(save.SceneName);
                // Upsert: remove any prior record for (UserId, SceneName), append the
                // new one. Keeps one revision per scene; trim guards against unbounded
                // distinct-scene growth.
                for (var i = scenes.Count - 1; i >= 0; i--)
                {
                    if (scenes[i].UserId == save.UserId && scenes[i].SceneName == name)
                        scenes.RemoveAt(i);
                }
                scenes.Add(new CanvasSceneRecord(
                    save.UserId, name, save.Content, TimeProvider.System.GetUtcNow()));
                while (scenes.Count > MaxJournalEntries) scenes.RemoveAt(0);
                await WriteStateAsync();
                await FireSynapseAsync(CanvasPlan.ToCanvasSaved(save));
                break;
        }
    }
}
