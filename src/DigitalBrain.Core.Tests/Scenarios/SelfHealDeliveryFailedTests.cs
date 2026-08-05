using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class SelfHealDeliveryFailedTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<MeetingSummarizer>()
            .AddModule<SummaryLedger>()
            .AddModule<HealRouter>()
            .AddModule<EmailFallback>()
            .AddModule<RecoveryLedger>()
            .AddModule<SlackUnavailable>()
            .AddModule<DispatchLedger>();

    [Fact(DisplayName = "Self-heal: DeliveryFailed triggers alternate email route")]
    public async Task DeliveryFailedHealsViaEmailFallback()
    {
        var ct = Cancellation;
        var context = "meeting-9";
        var session = Brain.Session(context);
        var summarizerId = new NeuronId("meetingsummarizer", context);
        var healerId = new NeuronId("healrouter", context);
        var emailId = new NeuronId("emailfallback", context);
        var ledgerId = new NeuronId("recoveryledger", context);
        var dispatchLedgerId = new NeuronId("dispatchledger", context);
        var meetingId = "standup-42";
        var summary = "Shipped DeliveryFailed self-heal.";

        await session.EmitAsync(new PostMeetingSummary(meetingId, summary), ct);

        var healerReading = await WaitForJournalAsync(
            healerId,
            reading => reading.AllHeard<DeliveryFailed>().Count == 1
                && reading.AllSaid<RecoveryAttempted>().Count == 1
                && reading.AllSaid<EmailSummaryReady>().Count == 1,
            "DeliveryFailed heard and RecoveryAttempted + EmailSummaryReady said",
            ct);

        var emailReading = await WaitForJournalAsync(
            emailId,
            reading => reading.AllSaid<EmailDispatched>().Count == 1
                && reading.AllSaid<RouteHealed>().Count == 1,
            "EmailDispatched and RouteHealed",
            ct);

        var summarizerReading = await WaitForJournalAsync(
            summarizerId,
            reading => reading.AllSaid<DeliveryFailed>().Count == 1
                && reading.AllSaid<PostSlackSummary>().Count == 1
                && reading.AllSaid<SummaryReady>().Count == 1,
            "summarizer said SummaryReady, failed Slack ask, and DeliveryFailed",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var postSaid = sessionReading.SaidSingle<PostMeetingSummary>();
        Assert.Equal("declared", postSaid.DeliveryTo(summarizerId).Via);

        var postHeard = summarizerReading.HeardSingle<PostMeetingSummary>();
        Assert.Equal(session.Id, postHeard.Metadata.Source);
        Assert.Equal(postSaid.Position, postHeard.Metadata.Sequence);

        var readySaid = summarizerReading.SaidSingle<SummaryReady>();
        Assert.Equal(new SynapseRef(session.Id, postSaid.Position), readySaid.Cause);
        Assert.Equal(meetingId, Assert.IsType<SummaryReady>(readySaid.Body).MeetingId);

        var slackAskSaid = summarizerReading.SaidSingle<PostSlackSummary>();
        // Catalogued via SlackUnavailable (declared sink) so KindOfFact works; no IAnswers → no ask via.
        Assert.Equal(
            "declared",
            slackAskSaid.DeliveryTo(new NeuronId("slackunavailable", context)).Via);
        Assert.DoesNotContain(slackAskSaid.To ?? [], delivery => delivery.Via == "ask");
        Assert.Equal(new SynapseRef(session.Id, postSaid.Position), slackAskSaid.Cause);

        var failedSaid = summarizerReading.SaidSingle<DeliveryFailed>();
        var failed = Assert.IsType<DeliveryFailed>(failedSaid.Body);
        Assert.Equal("no-answerer", failed.Reason);
        Assert.Equal(0, failed.Attempts);
        Assert.Equal(new SynapseRef(summarizerId, slackAskSaid.Position), failed.Fact);
        Assert.Equal("declared", failedSaid.DeliveryTo(healerId).Via);
        Assert.Equal(new SynapseRef(session.Id, postSaid.Position), failedSaid.Cause);

        var failedHeard = healerReading.HeardSingle<DeliveryFailed>();
        Assert.Equal(summarizerId, failedHeard.Metadata.Source);
        Assert.Equal(failedSaid.Position, failedHeard.Metadata.Sequence);

        var recoverySaid = healerReading.SaidSingle<RecoveryAttempted>();
        Assert.Equal(new SynapseRef(summarizerId, failedSaid.Position), recoverySaid.Cause);
        Assert.Equal("declared", recoverySaid.DeliveryTo(ledgerId).Via);
        var recovery = Assert.IsType<RecoveryAttempted>(recoverySaid.Body);
        Assert.Equal(failed.Fact, recovery.FailedFact);
        Assert.Equal("email-fallback", recovery.AlternateRoute);

        var emailReadySaid = healerReading.SaidSingle<EmailSummaryReady>();
        Assert.Equal(new SynapseRef(summarizerId, failedSaid.Position), emailReadySaid.Cause);
        Assert.Equal("declared", emailReadySaid.DeliveryTo(emailId).Via);
        var emailReady = Assert.IsType<EmailSummaryReady>(emailReadySaid.Body);
        Assert.Equal(failed.Fact, emailReady.HealedFrom);

        var emailHeard = emailReading.HeardSingle<EmailSummaryReady>();
        Assert.Equal(healerId, emailHeard.Metadata.Source);
        Assert.Equal(emailReadySaid.Position, emailHeard.Metadata.Sequence);

        var dispatchedSaid = emailReading.SaidSingle<EmailDispatched>();
        Assert.Equal(new SynapseRef(healerId, emailReadySaid.Position), dispatchedSaid.Cause);
        Assert.Equal("declared", dispatchedSaid.DeliveryTo(dispatchLedgerId).Via);
        Assert.Equal("email", Assert.IsType<EmailDispatched>(dispatchedSaid.Body).Channel);

        var healedSaid = emailReading.SaidSingle<RouteHealed>();
        Assert.Equal(new SynapseRef(healerId, emailReadySaid.Position), healedSaid.Cause);
        Assert.Equal("declared", healedSaid.DeliveryTo(ledgerId).Via);
        Assert.Equal("email-fallback", Assert.IsType<RouteHealed>(healedSaid.Body).Via);

        var ledgerReading = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<RecoveryAttempted>().Count == 1
                && reading.AllHeard<RouteHealed>().Count == 1,
            "ledger hears recovery and heal",
            ct);
        Assert.Equal(healerId, ledgerReading.HeardSingle<RecoveryAttempted>().Metadata.Source);
        Assert.Equal(emailId, ledgerReading.HeardSingle<RouteHealed>().Metadata.Source);
    }
}
