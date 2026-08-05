using DigitalBrain.Testing;

using DigitalBrain.Core.Tests.Support;

namespace DigitalBrain.Core.Tests.Physics;

public sealed class HandlerThrowTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    private static readonly FragilityGate Gate = new();

    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddService(Gate)
            .AddModule<FragileReceiver>()
            .AddModule<SideEffectObserver>();

    [Fact(DisplayName =
        "Handler throw after staging Emit leaves zero durable journal trace and a later successful redelivery lands exactly once")]
    public async Task HandlerThrowLeavesZeroDurableTraceThenRedeliversOnce()
    {
        var ct = Cancellation;
        var context = "fragile-throw";
        var session = Brain.Session(context);
        var receiverId = new NeuronId("fragilereceiver", context);
        var observerId = new NeuronId("sideeffectobserver", context);
        var work = new FragileWork("turn-1");

        Gate.Refuse = true;
        await session.SendAsync(receiverId, work, ct);

        // Drain retries while the handler refuses: no heard, no staged side-effect said,
        // and no observer reception — ClearTurn discarded the in-memory turn.
        await Task.Delay(TimeSpan.FromMilliseconds(300), ct);

        var midReceiver = await ReadAsync(receiverId, ct);
        Assert.Empty(midReceiver.Journal);
        Assert.Empty(midReceiver.AllHeard<FragileWork>());
        Assert.Empty(midReceiver.AllSaid<FragileSideEffect>());

        var midObserver = await ReadAsync(observerId, ct);
        Assert.Empty(midObserver.AllHeard<FragileSideEffect>());

        var midSession = await ReadAsync(session.Id, ct);
        Assert.Empty(midSession.AllSaid<DeliveryFailed>());
        var unsettled = midSession.SaidSingle<FragileWork>();
        Assert.Equal("directed", unsettled.DeliveryTo(receiverId).Via);

        Gate.Refuse = false;

        var landed = await WaitForJournalAsync(
            receiverId,
            reading => reading.AllHeard<FragileWork>().Count == 1
                && reading.AllSaid<FragileSideEffect>().Count == 1,
            "exactly one heard FragileWork and one said FragileSideEffect after the handler recovers",
            ct);

        Assert.Single(landed.AllHeard<FragileWork>());
        Assert.Single(landed.AllSaid<FragileSideEffect>());
        Assert.Equal("turn-1", Assert.IsType<FragileWork>(landed.HeardSingle<FragileWork>().Body).Note);
        Assert.Equal("turn-1", Assert.IsType<FragileSideEffect>(landed.SaidSingle<FragileSideEffect>().Body).Note);
        Assert.Equal("declared", landed.SaidSingle<FragileSideEffect>().DeliveryTo(observerId).Via);

        var observer = await WaitForJournalAsync(
            observerId,
            reading => reading.AllHeard<FragileSideEffect>().Count == 1,
            "exactly one heard FragileSideEffect on the observer",
            ct);
        Assert.Single(observer.AllHeard<FragileSideEffect>());

        await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        var finalReceiver = await ReadAsync(receiverId, ct);
        Assert.Single(finalReceiver.AllHeard<FragileWork>());
        Assert.Single(finalReceiver.AllSaid<FragileSideEffect>());
        Assert.Empty((await ReadAsync(session.Id, ct)).AllSaid<DeliveryFailed>());
    }
}
