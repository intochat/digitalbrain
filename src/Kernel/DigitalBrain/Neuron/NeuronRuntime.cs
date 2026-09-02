using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Core;

public sealed class NeuronRuntime
{
    public NeuronRuntime(TimeProvider clock, SignalRouter router, SynapseOptions options)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(options);

        Clock = clock;
        Router = router;
        Options = options;
    }

    internal TimeProvider Clock { get; }

    internal SignalRouter Router { get; }

    internal SynapseOptions Options { get; }

    internal SignalDispatcher Dispatcher { get; } = new();

    internal NeuronActivationComponents Bind(
        IServiceProvider activationServices,
        NeuronId neuronId)
    {
        ArgumentNullException.ThrowIfNull(activationServices);

        var entries = activationServices.GetRequiredService<Serializer<JournalEntry>>();
        var incoming = Feed("incoming");
        var outgoing = Feed("outgoing");
        var journal = new NeuronJournal(neuronId, incoming, outgoing);
        var synapses = new SynapseSet(
            activationServices.GetRequiredKeyedService<IDurableDictionary<string, Synapse>>("synapses"),
            Options,
            neuronId,
            Clock);

        return new(Clock, Router, journal, synapses, Dispatcher);

        NeuronFeed Feed(string name) => new(
            activationServices.GetRequiredKeyedService<IDurableList<byte[]>>(name),
            activationServices.GetRequiredKeyedService<IDurableDictionary<string, long>>($"{name}.tally"),
            activationServices.GetRequiredKeyedService<IDurableValue<long>>($"{name}.sequence"),
            entries);
    }
}

internal sealed record NeuronActivationComponents(
    TimeProvider Clock,
    SignalRouter Router,
    NeuronJournal Journal,
    SynapseSet Synapses,
    SignalDispatcher Dispatcher);
