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
