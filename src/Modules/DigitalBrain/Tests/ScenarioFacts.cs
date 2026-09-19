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

        var from = Plain(simulation, "elon");
        var to = Plain(simulation, "watch");
        var scenario = simulation.Grains.GetGrain<IScenario>("musk-watch");
        await scenario.Bind(from, "Note", to);

        var graph = await scenario.Read();
        Assert.Single(graph);
        Assert.Equal(from.GetGrainId(), graph[0].Source.GetGrainId());
        Assert.Equal(to.GetGrainId(), graph[0].Target.GetGrainId());
        Assert.Equal("Note", graph[0].SignalType);

        await new FakeWebhook(simulation.Grains, "musk-watch").PostAsync(from, Signal.Create("Note", """{"text":"posted"}"""), cancel);

        var latest = await to.ReadState();
        Assert.Contains(latest, entry => entry.Signal.Type == "Note" && entry.Source.GetGrainId() == from.GetGrainId());
    }

    [Fact]
    public async Task BindIsIdempotentAndUnbindRemovesTheSynapse()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });

        var from = Plain(simulation, "elon");
        var to = Plain(simulation, "watch");
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

        var timer = Plain(simulation, "timer-07");
        var otherTimer = Plain(simulation, "timer-noon");
        var downloader = Named(simulation, "download", "rates");
        var archive = Named(simulation, "archive", "rates");
        var clickhouse = Named(simulation, "clickhouse", "fx");
        var mail = Named(simulation, "mail", "ops");
        var gmail = Plain(simulation, "gmail-work");
        var inbox = Plain(simulation, "inbox-watch");
        var scenario = simulation.Grains.GetGrain<IScenario>(scenarioName);

        await scenario.Bind(timer, "Tick", downloader);
        await scenario.Bind(downloader, "Downloaded", archive);
        await scenario.Bind(archive, "CsvReady", clickhouse);
        await scenario.Bind(archive, "CsvReady", mail);
        await scenario.Bind(clickhouse, "Ingested", mail);
        await scenario.Bind(gmail, "EmailArrived", inbox);

        Assert.Equal(6, (await scenario.Read()).Count);

        var webhook = new FakeWebhook(simulation.Grains, scenarioName);

        await webhook.PostAsync(gmail, Signal.Create("EmailArrived", """{"from":"alerts@bank","subject":"noise"}"""), cancel);
        await ReactionWait.UntilAsync(async () =>
            (await inbox.ReadState()).Any(entry => entry.Signal.Type == "EmailArrived"), cancel);
        Assert.Empty(await clickhouse.ReadState());
        Assert.Empty(await downloader.ReadState());

        await webhook.PostAsync(otherTimer, Signal.Create("Tick", """{"at":"12:00"}"""), cancel);
        await Task.Delay(80, cancel);
        Assert.Empty(await downloader.ReadState());

        await webhook.PostAsync(timer, Signal.Create("Tick", """{"at":"07:00","url":"https://example/rates.zip"}"""), cancel);

        await ReactionWait.UntilAsync(async () =>
        {
            var house = await clickhouse.ReadState();
            var ingested = await clickhouse.ReadJournal(JournalKind.Outgoing, 0);
            var ops = await mail.ReadState();
            return house.Any(entry => entry.Signal.Type == "CsvReady" && entry.Signal.Body.Contains("EUR"))
                && ingested.Delta.Any(entry => entry.Signal.Type == "Ingested" && entry.Signal.Body.Contains("fx"))
                && ops.Any(entry => entry.Signal.Type == "CsvReady")
                && ops.Any(entry => entry.Signal.Type == "Ingested");
        }, cancel);

        var tickDelivery = (await timer.ReadState()).Single(entry => entry.Signal.Type == "Tick");
        Assert.Contains(await clickhouse.ReadState(),
            entry => entry.CorrelationId == tickDelivery.CorrelationId && entry.Signal.Type == "CsvReady");
        Assert.DoesNotContain(await inbox.ReadState(), entry => entry.Signal.Type == "CsvReady");
    }

    private static INeuron Plain(BrainSimulation simulation, string name)
        => Named(simulation, NeuronId.PlainType, name);

    private static INeuron Named(BrainSimulation simulation, string type, string name)
        => simulation.Grains.GetGrain<INeuron>(GrainId.Create(type, name));
}
