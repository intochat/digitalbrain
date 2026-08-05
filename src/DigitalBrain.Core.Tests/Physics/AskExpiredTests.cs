using DigitalBrain.Testing;

using DigitalBrain.Core.Tests.Support;

namespace DigitalBrain.Core.Tests.Physics;

public sealed class AskExpiredTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain.AddModule<DeferredProber>().AddModule<ProbeContinuation>();

    [Fact(DisplayName = "When no answer arrives within AskHorizon the asker journals AskExpired and a late reply does not dispatch the open-ask continuation")]
    public async Task AskExpiresAndLateReplyDoesNotContinue()
    {
        var ct = Cancellation;
        var session = Brain.Session("ask-horizon");
        var askerId = new NeuronId("probecontinuation", "ask-horizon");
        var answererId = new NeuronId("deferredprober", "ask-horizon");

        await session.EmitAsync(new StartProbe("weather"), ct);

        var askerArmed = await WaitForJournalAsync(
            askerId,
            reading => reading.AllSaid<Probe>().Count == 1,
            "a said Probe open ask",
            ct);
        var askSaid = askerArmed.SaidSingle<Probe>();
        var askRef = new SynapseRef(askerId, askSaid.Position);

        _ = await WaitForJournalAsync(
            answererId,
            reading => reading.AllHeard<Probe>().Count == 1,
            "the deferred answerer heard the probe",
            ct);

        await Clock.AdvanceAsync(DeliveryPolicy.AskHorizon + TimeSpan.FromSeconds(1), ct);

        var askerExpired = await WaitForJournalAsync(
            askerId,
            reading => reading.AllSaid<AskExpired>().Count == 1,
            "a said AskExpired after the ask horizon",
            ct);
        var expiredSaid = askerExpired.SaidSingle<AskExpired>();
        var expired = Assert.IsType<AskExpired>(expiredSaid.Body);
        Assert.Equal(askRef, expired.Ask);
        Assert.Equal(NeuronId.KindOf(typeof(Probe)), expired.Question);

        await session.EmitAsync(new ReleaseProbe(), ct);

        var askerAfterLate = await WaitForJournalAsync(
            askerId,
            reading => reading.AllHeard<ProbeReply>().Count == 1,
            "a late ProbeReply journaled without open-ask continuation",
            ct);

        var lateHeard = askerAfterLate.HeardSingle<ProbeReply>();
        Assert.Equal(answererId, lateHeard.Metadata.Source);
        Assert.Equal(askRef, lateHeard.Answers);
        Assert.Equal("late", Assert.IsType<ProbeReply>(lateHeard.Body).Text);
        Assert.Empty(askerAfterLate.AllSaid<ProbeContinued>());
        Assert.Single(askerAfterLate.AllSaid<AskExpired>());
    }

    [Fact(DisplayName = "Session AskAsync fails with AskExpired when the answerer never replies inside the horizon")]
    public async Task SessionAskAsyncSurfacesAskExpired()
    {
        var ct = Cancellation;
        var session = Brain.Session("ask-edge-horizon");

        var askTask = session.AskAsync<ProbeReply>(new Probe("edge"), ct);

        // Delivery must land before the horizon advance so the pin is the only open work.
        _ = await WaitForJournalAsync(
            new NeuronId("deferredprober", "ask-edge-horizon"),
            reading => reading.AllHeard<Probe>().Count == 1,
            "the deferred answerer heard the edge probe",
            ct);

        await Clock.AdvanceAsync(DeliveryPolicy.AskHorizon + TimeSpan.FromSeconds(1), ct);

        var failure = await Assert.ThrowsAsync<AskFailedException>(async () => await askTask);
        var expired = Assert.IsType<AskExpired>(failure.Fact);
        Assert.Equal(NeuronId.KindOf(typeof(Probe)), expired.Question);

        var sessionReading = await ReadAsync(session.Id, ct);
        Assert.Single(sessionReading.AllSaid<AskExpired>());
        Assert.Empty(sessionReading.AllHeard<ProbeReply>());
    }
}
