using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class BehaviorHotReloadInflightAsksTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    private const string EmailKind = "inflightemailreceived";

    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<InflightMailHub>()
            .AddModule<InflightBehavior>()
            .AddModule<InflightSlowCrm>()
            .AddModule<InflightTaskLedger>()
            .AddModule<InflightSupersedeSink>();

    [Fact(DisplayName =
        "Hot-reload in-flight asks (Stage-1 honest: Connect rewiring, not ALC): rev1 open AccountLookup completes after supersede; new mail routes to rev2")]
    public async Task ConnectRewireDrainOpenAskThenNewRevHears()
    {
        var ct = Cancellation;
        var context = "hot-reload-inflight";
        var session = Brain.Session(context);
        var hubId = new NeuronId("inflightmailhub", context);
        var rev1 = new NeuronId("inflightbehavior", "rev1");
        var rev2 = new NeuronId("inflightbehavior", "rev2");
        var crmRev1 = new NeuronId("inflightslowcrm", "rev1");
        var ledgerId = new NeuronId("inflighttaskledger", context);

        // Wire live traffic to rev1 only (ghost same-context behavior suppressed).
        await session.SendAsync(hubId, new Connect(EmailKind, rev1), ct);
        await WaitForJournalAsync(
            hubId,
            reading => reading.Connections.TryGetValue(EmailKind, out var targets)
                && targets.Any(t => t == rev1),
            "hub connected InflightEmailReceived → rev1",
            ct);

        await session.EmitAsync(
            new InflightObserveEmail("msg-open", "board.example", "VIP deal"),
            ct);

        var rev1AfterAsk = await WaitForJournalAsync(
            rev1,
            reading => reading.AllSaid<AccountLookupAsked>().Count == 1
                && reading.AllHeard<InflightEmailReceived>().Count == 1,
            "rev1 asked AccountLookup (open pin)",
            ct);

        var askSaid = rev1AfterAsk.SaidSingle<AccountLookupAsked>();
        Assert.Equal("ask", askSaid.DeliveryTo(crmRev1).Via);
        Assert.Empty(rev1AfterAsk.AllSaid<InflightTaskCreated>());

        // Supersede: Connect rewiring to rev2 (honest Stage-1 — not ALC unload of rev1).
        await session.SendAsync(hubId, new Disconnect(EmailKind, rev1), ct);
        await session.SendAsync(hubId, new Connect(EmailKind, rev2), ct);
        await session.EmitAsync(new BehaviorSuperseded("rev1", "rev2", Policy: "drain"), ct);

        await WaitForJournalAsync(
            hubId,
            reading => reading.Connections.TryGetValue(EmailKind, out var targets)
                && targets.Any(t => t == rev2)
                && !targets.Any(t => t == rev1),
            "hub rewired → rev2 only",
            ct);

        // Drain: deferred CRM answer still lands on rev1 (who holds the pin).
        await session.SendAsync(crmRev1, new InflightCrmUnblock("msg-open"), ct);

        var rev1Drained = await WaitForJournalAsync(
            rev1,
            reading => reading.AllSaid<InflightTaskCreated>().Count == 1
                && reading.AllSaid<BehaviorGenerationDrained>().Count == 1
                && reading.AllHeard<AccountLookupAnswered>().Count == 1,
            "rev1 continuation after CRM answer → one task",
            ct);

        var taskSaid = rev1Drained.SaidSingle<InflightTaskCreated>();
        Assert.Equal("rev1", Assert.IsType<InflightTaskCreated>(taskSaid.Body).Rev);
        Assert.Equal("msg-open", Assert.IsType<InflightTaskCreated>(taskSaid.Body).MessageId);
        // Declared fan-out is same-Name locus as the emitter (rev1), not the session context.
        var ledgerRev1 = new NeuronId("inflighttaskledger", "rev1");
        Assert.Equal("declared", taskSaid.DeliveryTo(ledgerRev1).Via);

        // New mail after rewire: only rev2 hears; rev1 task count stays 1.
        await session.EmitAsync(
            new InflightObserveEmail("msg-new", "investors.example", "New after deploy"),
            ct);

        var rev2Reading = await WaitForJournalAsync(
            rev2,
            reading => reading.AllHeard<InflightEmailReceived>().Count == 1
                && reading.AllSaid<AccountLookupAsked>().Count == 1,
            "rev2 heard new email after Connect rewire",
            ct);

        Assert.Equal("msg-new", Assert.IsType<InflightEmailReceived>(
            rev2Reading.HeardSingle<InflightEmailReceived>().Body).MessageId);

        var hubAfter = await ReadAsync(hubId, ct);
        var secondEmail = hubAfter.AllSaid<InflightEmailReceived>()
            .Single(s => Assert.IsType<InflightEmailReceived>(s.Body).MessageId == "msg-new");
        Assert.Equal("connected", secondEmail.DeliveryTo(rev2).Via);
        Assert.Null(secondEmail.DeliveryToOrNull(rev1));

        var rev1Final = await ReadAsync(rev1, ct);
        Assert.Single(rev1Final.AllHeard<InflightEmailReceived>());
        Assert.Single(rev1Final.AllSaid<InflightTaskCreated>());

        var ledgerRev1Reading = await WaitForJournalAsync(
            ledgerRev1,
            reading => reading.AllHeard<InflightTaskCreated>().Count == 1
                && reading.AllHeard<BehaviorGenerationDrained>().Count == 1,
            "ledger@rev1 heard drained task",
            ct);
        Assert.Equal(rev1, ledgerRev1Reading.HeardSingle<InflightTaskCreated>().Metadata.Source);

        // Session-emitted supersede lands on the session context locus.
        var ledgerContext = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<BehaviorSuperseded>().Count == 1,
            "ledger@context heard BehaviorSuperseded",
            ct);
        Assert.Equal(session.Id, ledgerContext.HeardSingle<BehaviorSuperseded>().Metadata.Source);
    }
}
