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
    .AddDevelopmentJournalStorage());

using var host = builder.Build();

await host.StartAsync();

var brain = new BrainClient(host.Services.GetRequiredService<IGrainFactory>(), new OwnerId("panel"));

foreach (var panellist in (string[])[nameof(Optimist), nameof(Skeptic), nameof(Scribe)])
{
    await brain.Neuron(panellist, "one").ReadJournalAsync(JournalKind.Incoming, afterSequence: 0);
}

await brain.FireAsync(nameof(Moderator), "chair", new QuestionAsked("should we ship it?"));

var verdicts = await Settled(brain.Neuron(nameof(Scribe), "one"));

Console.WriteLine(verdicts.Count == 0
    ? "the panel has not reached a verdict yet"
    : $"the scribe recorded: {string.Join(" | ", verdicts.Select(delivery => delivery.Synapse).OfType<VerdictReached>().Select(verdict => verdict.Verdict))}");

await host.StopAsync();

static async Task<IReadOnlyList<SynapseDelivery>> Settled(NeuronHandle neuron)
{
    long cursor = 0;

    for (var probe = 0; probe < 100; probe++)
    {
        var journal = await neuron.ReadJournalAsync(JournalKind.Incoming, cursor);
        var delta = DeltaOrThrow(journal);
        cursor = journal.ResumeSequence;

        if (delta.Count > 0)
        {
            return delta;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(100));
    }

    var final = await neuron.ReadJournalAsync(JournalKind.Incoming, cursor);

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
