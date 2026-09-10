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
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<GraphState> state)
    : Neuron<GraphState>(runtime, state), IGraph
{
    public Task<Accepted<string>> Render(RenderGraph command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("render"), command, UIJson.Default.RenderGraph, UIJson.Default.AcceptedString, arguments =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.Title);
            ArgumentNullException.ThrowIfNull(arguments.Nodes);
            ArgumentNullException.ThrowIfNull(arguments.Edges);
            var work = Schedule(UIBodies.Signal(UIVocabulary.GraphRendering, arguments, UIJson.Default.RenderGraph));
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

        var command = UIBodies.Read(delivery, UIJson.Default.RenderGraph);
        await SaveAsync(new GraphState(command.Title, command.Nodes, command.Edges), cancellationToken).ConfigureAwait(true);
        await FireAsync(UIBodies.Card(UIVocabulary.GraphRendered, Id.Name, command.Title),
            cancellationToken: cancellationToken).ConfigureAwait(true);
    }
}
