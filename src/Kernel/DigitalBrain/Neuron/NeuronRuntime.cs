using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;
using Orleans.Serialization.Session;

namespace DigitalBrain.Core;

public sealed class NeuronRuntime(TimeProvider clock, NeuronOptions options)
{
    internal TimeProvider Clock { get; } = clock;
    internal NeuronOptions Options { get; } = options;

    internal NeuronActivationComponents Bind(IServiceProvider services, NeuronId neuronId)
    {
        var entries = services.GetRequiredService<Serializer<JournalEntry>>();
        var sessions = services.GetRequiredService<SerializerSessionPool>();
        JournalWindow Window(string name) => new(
            services.GetRequiredKeyedService<IDurableList<byte[]>>(name),
            services.GetRequiredKeyedService<IDurableDictionary<string, long>>($"{name}.tally"),
            services.GetRequiredKeyedService<IDurableValue<long>>($"{name}.sequence"),
            entries,
            sessions);

        var commands = new CommandJournal(
            services.GetRequiredKeyedService<IDurableList<byte[]>>("commands"),
            services.GetRequiredKeyedService<IDurableValue<long>>("commands.sequence"),
            services.GetRequiredService<Serializer<CommandRecord>>(),
            sessions);
        var dedup = new CommandDedup(services.GetRequiredKeyedService<IDurableDictionary<CommandId, CommandOutcome>>("dedup"));

        return new(
            Clock,
            Options,
            new NeuronJournals(Window("incoming"), Window("outgoing")),
            commands,
            dedup,
            new CommandExecution(commands, dedup, Clock, services.GetService<ICommandCrashPoint>()),
            new NeuronSynapses(services.GetRequiredKeyedService<IDurableDictionary<string, Synapse>>("synapses"), neuronId, Clock),
            services.GetRequiredKeyedService<IDurableDictionary<string, SignalDelivery>>("latest"),
            new PendingWork(
                services.GetRequiredKeyedService<IDurableQueue<SignalDelivery>>("pending"),
                services.GetRequiredKeyedService<IDurableSet<SignalId>>("pending.cancelled"),
                services.GetRequiredKeyedService<IDurableList<SignalId>>("reacted")));
    }
}
