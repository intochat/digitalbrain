using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType(UIVocabulary.GraphType)]
internal sealed class GraphNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<GraphState>> state)
    : Neuron<GraphState>(runtime, state), IGraph
{
    public Task<Accepted<string>> Render(RenderGraph command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("render"), command, UIJson.Default.RenderGraph, UIJson.Default.AcceptedString, arguments =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.Title);
            ArgumentNullException.ThrowIfNull(arguments.Nodes);
            ArgumentNullException.ThrowIfNull(arguments.Edges);
            var work = Schedule(Signal.FromJson(UIVocabulary.GraphRendering, arguments, UIJson.Default.RenderGraph));
            return new Accepted<string>(Id.Name, work);
        });

    [ReadOnly]
    public Task<GraphState> Read() => Task.FromResult(State ?? new GraphState("", [], []));

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Signal.Type != UIVocabulary.GraphRendering)
        {
            return;
        }

        if (Body(delivery, UIJson.Default.RenderGraph) is not { } command)
        {
            return;
        }

        Announce(Signal.FromJson(UIVocabulary.GraphRendered, new KitCard(Id.Name, command.Title), UIJson.Default.KitCard));
        await SaveAsync(new GraphState(command.Title, command.Nodes, command.Edges), cancellationToken).ConfigureAwait(true);
    }
}
