using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;
using Orleans.Serialization.Session;

namespace DigitalBrain.Core;

public sealed class NeuronRuntime(TimeProvider clock)
{
    internal TimeProvider Clock { get; } = clock;

    internal NeuronActivationComponents Bind(IServiceProvider services)
    {
        var entries = services.GetRequiredService<Serializer<JournalEntry>>();
        var sessions = services.GetRequiredService<SerializerSessionPool>();
        JournalWindow Window(string name) => new(
            services.GetRequiredKeyedService<IDurableList<byte[]>>(name),
            services.GetRequiredKeyedService<IDurableDictionary<string, long>>($"{name}.tally"),
            services.GetRequiredKeyedService<IDurableValue<long>>($"{name}.sequence"),
            entries,
            sessions);

        return new(
            Clock,
            Window("incoming"),
            Window("outgoing"),
            services.GetRequiredKeyedService<IDurableDictionary<string, SignalDelivery>>("latest"),
            new PendingWork(
                services.GetRequiredKeyedService<IDurableQueue<SignalDelivery>>("pending"),
                services.GetRequiredKeyedService<IDurableSet<SignalId>>("pending.cancelled"),
                services.GetRequiredKeyedService<IDurableList<SignalId>>("reacted")));
    }
}
