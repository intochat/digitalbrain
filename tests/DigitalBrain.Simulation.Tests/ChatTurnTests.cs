using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

// The full-turn safety net: one durable Send driven end-to-end through the production turn
// machinery against the corpus-scripted mock LLM (tests/corpus/mvp-chart.feature). Pins the
// frozen journal footprint UserMessaged → Pending → Running → Responded + Completed and the
// side effects the scripted fires must leave behind (the chart entity's points).
[Collection(SimulationCollection.Name)]
public sealed class ChatTurnTests(SimulationFixture fixture)
{
    private static readonly TimeSpan TurnTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task SendRunsAWholeTurnThroughTheScriptedResponder()
    {
        var brain = fixture.Sim.Brain;
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = CommandId.New();
        var actor = new ActorContext(new PrincipalId(Guid.NewGuid()), "owner");

        var accepted = await brain.GetGrainProxy<IChat>("main")
            .Send(new SendMessage(command, "plot these values: a=1, b=3", actor));

        Assert.Equal(command, accepted.CommandId);

        var chatId = NeuronId.For<IChat>(brain.Owner, "main");
        var responded = await JournalWait.ForAsync(
            brain,
            chatId,
            JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded reply && reply.CommandId == command,
            TurnTimeout);
        Assert.Equal("Plotted 2 points on demo.", ((Responded)responded.Synapse).Text);

        await JournalWait.ForAsync(
            brain,
            chatId,
            JournalKind.Outgoing,
            delivery => delivery.Synapse is TurnLifecycle { Status: ChatTurnStatus.Completed } life
                && life.TurnId == accepted.TurnId,
            TurnTimeout);

        // The scripted fires ran through the real pipeline; the entity write drains
        // asynchronously behind the reply, so poll the exact state under test.
        var state = await PollForPointsAsync(
            () => brain.GetEntity<IChartEntity>("demo").Read(),
            cancellationToken);

        Assert.Collection(
            state.Points,
            point =>
            {
                Assert.Equal("a", point.Label);
                Assert.Equal(1, point.Value);
            },
            point =>
            {
                Assert.Equal("b", point.Label);
                Assert.Equal(3, point.Value);
            });
    }

    private static async Task<ChartState> PollForPointsAsync(
        Func<Task<ChartState?>> read,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + PollTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await read() is { Points.Count: 2 } state)
            {
                return state;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"The chart entity did not hold both scripted points within {PollTimeout}.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }
}
