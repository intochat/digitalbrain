using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.DevTools;
using DigitalBrain.Kernel;
using DigitalBrain.Quickstart;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;

var builder = Host.CreateApplicationBuilder(args);

builder.UseOrleans(silo => silo
    .UseLocalhostClustering()
    .UseInMemoryReminderService()
    .AddDigitalBrain()
    .AddDevelopmentJournalStorage());

using var host = builder.Build();

await host.StartAsync();

var grains = host.Services.GetRequiredService<IGrainFactory>();
var brain = DigitalBrainClient.Connect(grains, "quickstart");
var sessionId = new NeuronId(ISessionNeuron.GrainTypeName, brain.Owner, "session");
var session = grains.GetGrain<ISessionNeuron>(sessionId.ToGrainId());
var greeterId = new NeuronId(nameof(Greeter), brain.Owner, "first");

var greeted = new FirstMatchWatch(delivery => delivery.Synapse is Greeted);
var greetedReference = grains.CreateObjectReference<IJournalObserver>(greeted);

await session.WatchNeuronAsync(
    greeterId,
    JournalKind.Outgoing,
    afterSequence: 0,
    greetedReference);

try
{
    await brain.SendAsync<IGreeter>("first", new SayHello());
    await greeted.AwaitMatchAsync(TimeSpan.FromSeconds(30));

    var fired = await session.ReadNeuronJournalAsync(greeterId, JournalKind.Outgoing, afterSequence: 0);
    var firedCount = fired.ResetSnapshot?.TotalRecorded ?? fired.Delta.Count;
    var firedTypes = fired.ResetSnapshot is { } reset
        ? reset.Tallies.Select(tally => tally.SynapseType)
        : fired.Delta.Select(delivery => delivery.Synapse.GetType().Name);

    Console.WriteLine($"the greeter durably recorded {firedCount} outgoing synapse(s): {string.Join(", ", firedTypes)}");
}
finally
{
    await session.UnwatchNeuronAsync(greeterId, greetedReference);
}

await host.StopAsync();

internal sealed class FirstMatchWatch(Func<SynapseDelivery, bool> match) : IJournalObserver
{
    private readonly TaskCompletionSource<SynapseDelivery> _matched =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task ObserveAsync(JournalKind kind, JournalRead read)
    {
        if (read.ResetSnapshot is not null)
        {
            _matched.TrySetException(new InvalidOperationException(
                $"The greeter journal compacted before sequence {read.ResumeSequence}; its payload is no longer available."));

            return Task.CompletedTask;
        }

        foreach (var delivery in read.Delta)
        {
            if (match(delivery))
            {
                _matched.TrySetResult(delivery);
            }
        }

        return Task.CompletedTask;
    }

    public Task<SynapseDelivery> AwaitMatchAsync(TimeSpan limit)
        => _matched.Task.WaitAsync(limit);
}
