using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Scenarios;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ScenarioFacts
{
    [Fact]
    public async Task RoutesASignalAlongBoundSynapses()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });

        var from = NeuronId.Plain("elon");
        var to = NeuronId.Plain("watch");
        var scenario = simulation.Grains.GetGrain<IScenario>("musk-watch");
        await scenario.Bind(from, "Note", to);

        var graph = await scenario.Read();
        Assert.Single(graph);
        Assert.Equal(from, graph[0].Source);
        Assert.Equal(to, graph[0].Target);
        Assert.Equal("Note", graph[0].SignalType);

        var webhook = new FakeWebhook(simulation.Grains, "musk-watch");
        await webhook.PostAsync(from, Signal.Create("Note", """{"text":"posted"}"""), cancel);

        var latest = await simulation.Grains.GetGrain<INeuron>(to.ToGrainId()).ReadState();
        Assert.Contains(latest, entry => entry.Signal.Type == "Note" && entry.Source == from);
    }

    [Fact]
    public async Task BindIsIdempotentAndUnbindRemovesTheSynapse()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });

        var from = NeuronId.Plain("elon");
        var to = NeuronId.Plain("watch");
        var scenario = simulation.Grains.GetGrain<IScenario>("musk-watch");
        await scenario.Bind(from, "Note", to);
        await scenario.Bind(from, "Note", to);
        Assert.Single(await scenario.Read());

        await scenario.Unbind(from, "Note", to);
        Assert.Empty(await scenario.Read());
    }

    [Fact]
    public async Task DailyZipCsvClickHouseAndMailAreOneScenarioGraph()
    {
        var cancel = TestContext.Current.CancellationToken;
        const string scenarioName = "rates-daily";
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            ConfigureSilo = silo => silo.Services.AddSingleton<IScenarioSink>(
                services => new ScenarioSink(services.GetRequiredService<IGrainFactory>(), scenarioName)),
        });

        var timer = NeuronId.Plain("timer-07");
        var otherTimer = NeuronId.Plain("timer-noon");
        var downloader = new NeuronId("download", "rates");
        var archive = new NeuronId("archive", "rates");
        var clickhouse = new NeuronId("clickhouse", "fx");
        var mail = new NeuronId("mail", "ops");
        var gmail = NeuronId.Plain("gmail-work");
        var inbox = NeuronId.Plain("inbox-watch");
        var scenario = simulation.Grains.GetGrain<IScenario>(scenarioName);

        // Daily: tick → download zip → unarchive csv → write ClickHouse, and mail ops the csv.
        await scenario.Bind(timer, "Tick", downloader);
        await scenario.Bind(downloader, "Downloaded", archive);
        await scenario.Bind(archive, "CsvReady", clickhouse);
        await scenario.Bind(archive, "CsvReady", mail);
        await scenario.Bind(clickhouse, "Ingested", mail);
        // Separate trigger on the same program: Gmail webhook, not the zip path.
        await scenario.Bind(gmail, "EmailArrived", inbox);

        var graph = await scenario.Read();
        Assert.Equal(6, graph.Count);

        var webhook = new FakeWebhook(simulation.Grains, scenarioName);

        await webhook.PostAsync(gmail, Signal.Create("EmailArrived", """{"from":"alerts@bank","subject":"noise"}"""), cancel);
        await ReactionWait.UntilAsync(async () =>
            (await simulation.Grains.GetGrain<INeuron>(inbox.ToGrainId()).ReadState())
                .Any(entry => entry.Signal.Type == "EmailArrived"), cancel);
        Assert.Empty(await simulation.Grains.GetGrain<INeuron>(clickhouse.ToGrainId()).ReadState());
        Assert.Empty(await simulation.Grains.GetGrain<INeuron>(downloader.ToGrainId()).ReadState());

        await webhook.PostAsync(otherTimer, Signal.Create("Tick", """{"at":"12:00"}"""), cancel);
        await Task.Delay(80, cancel);
        Assert.Empty(await simulation.Grains.GetGrain<INeuron>(downloader.ToGrainId()).ReadState());

        var tick = Signal.Create("Tick", """{"at":"07:00","url":"https://example/rates.zip"}""");
        await webhook.PostAsync(timer, tick, cancel);

        await ReactionWait.UntilAsync(async () =>
        {
            var house = await simulation.Grains.GetGrain<INeuron>(clickhouse.ToGrainId()).ReadState();
            var ingested = await simulation.Grains.GetGrain<INeuron>(clickhouse.ToGrainId()).ReadJournal(JournalKind.Outgoing, 0);
            var ops = await simulation.Grains.GetGrain<INeuron>(mail.ToGrainId()).ReadState();
            return house.Any(entry => entry.Signal.Type == "CsvReady" && entry.Signal.Body.Contains("EUR"))
                && ingested.Delta.Any(entry => entry.Signal.Type == "Ingested" && entry.Signal.Body.Contains("fx"))
                && ops.Any(entry => entry.Signal.Type == "CsvReady")
                && ops.Any(entry => entry.Signal.Type == "Ingested");
        }, cancel);

        var clickhouseState = await simulation.Grains.GetGrain<INeuron>(clickhouse.ToGrainId()).ReadState();
        var tickDelivery = (await simulation.Grains.GetGrain<INeuron>(timer.ToGrainId()).ReadState())
            .Single(entry => entry.Signal.Type == "Tick");
        Assert.Contains(clickhouseState, entry => entry.CorrelationId == tickDelivery.CorrelationId && entry.Signal.Type == "CsvReady");

        Assert.DoesNotContain(
            await simulation.Grains.GetGrain<INeuron>(inbox.ToGrainId()).ReadState(),
            entry => entry.Signal.Type == "CsvReady");
    }
}
