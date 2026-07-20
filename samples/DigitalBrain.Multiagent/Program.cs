using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.DevTools;
using DigitalBrain.Kernel;
using DigitalBrain.Multiagent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;

var builder = Host.CreateApplicationBuilder(args);

builder.UseOrleans(silo => silo
    .UseLocalhostClustering()
    .UseInMemoryReminderService()
    .AddDigitalBrain()
    .AddBroadcastHandlers(typeof(Moderator).Assembly)
    .AddDevelopmentJournalStorage());

using var host = builder.Build();

await host.StartAsync();

var grains = host.Services.GetRequiredService<IGrainFactory>();
var brain = new BrainClient(grains, new OwnerId("panel"));
var moderator = brain.Neuron(nameof(Moderator), "chair");

var verdicts = new FirstMatchWatch(delivery => delivery.Synapse is VerdictReached);
var verdictReference = grains.CreateObjectReference<IJournalObserver>(verdicts);

await moderator.WatchAsync(JournalKind.Outgoing, afterSequence: 0, verdictReference);

try
{
    await brain.FireAsync(nameof(Moderator), "chair", new QuestionAsked("should we ship it?"));

    var verdictDelivery = await verdicts.AwaitMatchAsync(TimeSpan.FromSeconds(30));
    var scribeId = NeuronId.BroadcastReceiver(nameof(Scribe), brain.Owner, verdictDelivery.CorrelationId);
    var scribe = brain.Neuron(scribeId.Type, scribeId.Name);

    var recorded = new FirstMatchWatch(delivery => delivery.Synapse is VerdictReached);
    var scribeReference = grains.CreateObjectReference<IJournalObserver>(recorded);

    await scribe.WatchAsync(JournalKind.Incoming, afterSequence: 0, scribeReference);

    try
    {
        var arrival = await recorded.AwaitMatchAsync(TimeSpan.FromSeconds(30));
        var verdict = (VerdictReached)arrival.Synapse;

        Console.WriteLine($"the scribe recorded: {verdict.Verdict}");
    }
    finally
    {
        await scribe.UnwatchAsync(scribeReference);
    }
}
finally
{
    await moderator.UnwatchAsync(verdictReference);
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
                $"The panel journal compacted before sequence {read.ResumeSequence}; its verdict payload is no longer available."));

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
