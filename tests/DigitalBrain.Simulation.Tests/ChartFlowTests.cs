using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using DigitalBrain.Memory;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Collection(SimulationCollection.Name)]
public sealed class ChartFlowTests(SimulationFixture fixture)
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task ChartCardEmitsRespondedOffer()
    {
        var chatName = fixture.Sim.UniqueId("chat");
        var chartName = fixture.Sim.UniqueId("chart");
        var cancellationToken = TestContext.Current.CancellationToken;

        var chatId = NeuronId.For<IChat>(fixture.Sim.Brain.Owner, chatName);

        await fixture.Sim.Brain.FireAsync<IChat>(
            chatName,
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
}
