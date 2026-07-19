using DigitalBrain;
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
    await brain.Neuron(panellist, "one").ReadJournalAsync(JournalKind.Incoming);
}

await brain.FireAsync(nameof(Moderator), "chair", new QuestionAsked("should we ship it?"));

var verdicts = await Settled(brain.Neuron(nameof(Scribe), "one"));

Console.WriteLine(verdicts.Count == 0
    ? "the panel has not reached a verdict yet"
    : $"the scribe recorded: {string.Join(" | ", verdicts.OfType<VerdictReached>().Select(verdict => verdict.Verdict))}");

await host.StopAsync();

static async Task<IReadOnlyList<Synapse>> Settled(NeuronHandle neuron)
{
    for (var probe = 0; probe < 100; probe++)
    {
        var journal = await neuron.ReadJournalAsync(JournalKind.Incoming);

        if (journal.Count > 0)
        {
            return journal;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(100));
    }

    return await neuron.ReadJournalAsync(JournalKind.Incoming);
}
