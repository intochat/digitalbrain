using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using DigitalBrain.Memory;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Collection(SimulationCollection.Name)]
public sealed class ChartFlowTests(SimulationFixture fixture)
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task ChartPointFiredAtTheChartNeuronLandsInTheChartEntity()
    {
        var name = fixture.Sim.UniqueId("chart");
        var cancellationToken = TestContext.Current.CancellationToken;

        // "chart-<8 hex>" never parses as a "{principal:N}.{local}" partitioned name (the
        // 32-hex-then-dot shape PrincipalPartition.TryParse requires), and this test client
        // never runs through Chat.SendStreaming/SystemTools.fire so VerifiedActor.Current stays
        // null for the whole call. GrantsNeuron.RequireReadAccessAsync's unattributed-actor
        // branch (GrantsNeuron.cs:106-117) allows any non-partitioned name through without a
        // grants bootstrap, so both the neuron's write-side check (HandleAsync) and read-side
        // check (Read) pass deterministically for a fresh owner with no grants configured.
        await fixture.Sim.Brain.FireAsync<IChart>(
            name,
            new ChartPoint("series-a", "jan", 42),
            cancellationToken);

        // Fire is not delivery: NeuronMessagePipeline.FireAsync stages the ChartPoint into the
        // outbox and calls outbox.ScheduleDrain() (a background drain), it does not synchronously
        // run ChartNeuron.HandleAsync -- so the entity write can still be in flight when this
        // client call returns. Poll the entity read (the exact thing under test) rather than
        // inventing a journal-based proxy signal for "handler ran".
        var state = await PollUntilPresentAsync(
            () => fixture.Sim.Brain.GetEntity<IChartEntity>(name).Read(),
            cancellationToken);

        var point = Assert.Single(state.Points);
        Assert.Equal("series-a", point.Series);
        Assert.Equal("jan", point.Label);
        Assert.Equal(42, point.Value);
    }

    [Fact]
    public async Task ChartCardEmitsRespondedOffer()
    {
        var chatName = fixture.Sim.UniqueId("chat");
        var chartName = fixture.Sim.UniqueId("chart");
        var cancellationToken = TestContext.Current.CancellationToken;

        var chat = fixture.Sim.Brain.Get<IChat>(chatName);
        var chatId = chat.Id;

        await fixture.Sim.Brain.FireAsync(
            chatId,
            new ChartCard(chartName),
            cancellationToken);

        var outgoing = await PollUntilJournalPresentAsync(
            () => fixture.Sim.Brain.ReadJournalAsync(chatId, JournalKind.Outgoing, cancellationToken: cancellationToken),
            cancellationToken);

        var respondedDelivery = Assert.Single(
            outgoing.Delta,
            d => d.Synapse is Responded);
        Assert.IsType<Responded>(respondedDelivery.Synapse);

        // The no-store-on-offers pin: ChartCard's handler emits Responded directly (it is not a
        // turn completion, so it never reaches TryEmitRespondedAsync/StoreFact) -- the outgoing
        // Responded delivery above already proves the single-threaded handler ran to completion,
        // so one bounded ReadFacts call (no polling) deterministically catches a regression.
        // chartName is a fresh UniqueId, so no legitimate fact anywhere can carry it as Text.
        var facts = await fixture.Sim.Brain.Get<IFactMemory>(IFactMemory.InstanceName)
            .FireAsync<FactsRead>(
                new ReadFacts(CommandId.New(), Kind: "chat.responded", Limit: 500),
                cancellationToken)
            .WaitAsync(PollTimeout, cancellationToken);

        Assert.DoesNotContain(facts.Facts, fact => fact.Text == chartName);
    }

    private static async Task<JournalRead> PollUntilJournalPresentAsync(
        Func<Task<JournalRead>> read,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + PollTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await read();
            if (result.Delta.Count > 0)
            {
                return result;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"The journal had no entries within {PollTimeout}.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private static async Task<ChartState> PollUntilPresentAsync(
        Func<Task<ChartState?>> read,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + PollTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await read() is { } state)
            {
                return state;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"The chart entity had no state within {PollTimeout}.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }
}
