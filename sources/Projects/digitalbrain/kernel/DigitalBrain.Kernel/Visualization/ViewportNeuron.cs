using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Ui;
using Orleans.Journaling;

namespace DigitalBrain.Kernel.Visualization;

[ImplicitStreamSubscription(nameof(ViewportNeuron))]
public sealed class ViewportNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ILogger<ViewportNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      IHandle<MoveCameraCommand>,
      IHandle<FocusNeuronCommand>,
      INeuronMetadata
{
    public static NeuronId Id => new("kernel/viewport");
    public static string Icon => "videocam";
    public static NeuronCapability Capabilities => NeuronCapability.None;

    public async Task HandleAsync(MoveCameraCommand cmd, CancellationToken ct)
    {
        Logger.LogInformation("ViewportNeuron moving camera target to ({X}, {Y}, {Z})", 
            cmd.TargetX, cmd.TargetY, cmd.TargetZ);

        // Broadcaster could serialize and dispatch a camera update synapse
        // that the Gateway UiGatewaySession intercepts and streams.
        // We persist or relay via internal Orleans streams.
        await Task.CompletedTask;
    }

    public async Task HandleAsync(FocusNeuronCommand cmd, CancellationToken ct)
    {
        Logger.LogInformation("ViewportNeuron focusing camera on neuron {NeuronId} with zoom {Zoom}", 
            cmd.NeuronId, cmd.ZoomDepth);

        // Find neuron positions inside global catalog or dynamically computed coordinate space
        await Task.CompletedTask;
    }
}
