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

var brain = new BrainClient(host.Services.GetRequiredService<IGrainFactory>(), new OwnerId("panel"));

await brain.FireAsync(nameof(Moderator), "chair", new QuestionAsked("should we ship it?"));

var verdicts = await Settled(brain.Neuron(nameof(Moderator), "chair"), JournalKind.Outgoing);
var verdict = verdicts.Select(delivery => delivery.Synapse).OfType<VerdictReached>().LastOrDefault();

if (verdict is null)
{
    Console.WriteLine("the panel has not reached a verdict yet");
}
else
{
    var scribe = NeuronId.BroadcastReceiver(nameof(Scribe), brain.Owner, verdicts
        .Last(delivery => delivery.Synapse is VerdictReached)
        .CorrelationId);
    var recorded = await Settled(brain.Neuron(scribe.Type, scribe.Name), JournalKind.Incoming);

    Console.WriteLine(recorded.Count == 0
        ? "the panel has not reached a verdict yet"
        : $"the scribe recorded: {string.Join(" | ", recorded.Select(delivery => delivery.Synapse).OfType<VerdictReached>().Select(entry => entry.Verdict))}");
}

await host.StopAsync();

static async Task<IReadOnlyList<SynapseDelivery>> Settled(NeuronHandle neuron, JournalKind kind)
{
    long cursor = 0;

    for (var probe = 0; probe < 100; probe++)
    {
        var journal = await neuron.ReadJournalAsync(kind, cursor);
        var delta = DeltaOrThrow(journal);
        cursor = journal.ResumeSequence;

        if (delta.Count > 0 && (kind == JournalKind.Incoming || delta.Any(delivery => delivery.Synapse is VerdictReached)))
        {
            return delta;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(100));
    }

    var final = await neuron.ReadJournalAsync(kind, cursor);

    return DeltaOrThrow(final);
}

static IReadOnlyList<SynapseDelivery> DeltaOrThrow(JournalRead journal)
{
    if (journal.ResetSnapshot is not null)
    {
        throw new InvalidOperationException(
            $"The panel journal compacted before sequence {journal.ResumeSequence}; its verdict payload is no longer available.");
    }

    return journal.Delta;
}
