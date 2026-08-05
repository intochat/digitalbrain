using DigitalBrain.Mocks;
using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class ComplianceLegalHoldTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<ComplianceHoldRegister>()
            .AddModule<HoldAwareMailbox>()
            .AddModule<MockGmail>()
            .AddModule<ComplianceAuditLedger>();

    [Fact(DisplayName =
        "Legal hold: LegalHoldPlaced → DestructiveDeleteAsked blocked; Contoso EmailReceived still journals RetentionExtended")]
    public async Task HoldBlocksDeleteButRetainsInbound()
    {
        var ct = Cancellation;
        var context = "legal-contoso";
        var session = Brain.Session(context);
        var registerId = new NeuronId("complianceholdregister", context);
        var mailboxId = new NeuronId("holdawaremailbox", context);
        var gmailId = new NeuronId("mockgmail", context);
        var auditId = new NeuronId("complianceauditledger", context);
        var holdId = "hold-contoso-1";

        await session.EmitAsync(
            new LegalHoldPlaceAsked(holdId, SubjectAccount: "Contoso", Policy: "litigation-preserve"),
            ct);

        var registerReading = await WaitForJournalAsync(
            registerId,
            reading => reading.AllSaid<LegalHoldPlaced>().Count == 1,
            "register said LegalHoldPlaced",
            ct);

        await WaitForJournalAsync(
            mailboxId,
            reading => reading.AllHeard<LegalHoldPlaced>().Count == 1,
            "mailbox heard LegalHoldPlaced",
            ct);

        var auditHold = await WaitForJournalAsync(
            auditId,
            reading => reading.AllHeard<LegalHoldPlaced>().Count == 1,
            "audit heard LegalHoldPlaced",
            ct);

        var placedSaid = registerReading.SaidSingle<LegalHoldPlaced>();
        Assert.Equal("declared", placedSaid.DeliveryTo(mailboxId).Via);
        Assert.Equal("declared", placedSaid.DeliveryTo(auditId).Via);
        Assert.Equal(holdId, Assert.IsType<LegalHoldPlaced>(placedSaid.Body).HoldId);
        Assert.Equal(registerId, auditHold.HeardSingle<LegalHoldPlaced>().Metadata.Source);

        await session.EmitAsync(
            new DestructiveDeleteAsked("Contoso", Scope: "all-email", Actor: "owner"),
            ct);

        var mailboxBlocked = await WaitForJournalAsync(
            mailboxId,
            reading => reading.AllSaid<DestructiveActionBlocked>().Count == 1,
            "mailbox said DestructiveActionBlocked",
            ct);

        Assert.Empty(mailboxBlocked.AllSaid<DestructiveDeleteExecuted>());
        var blocked = Assert.IsType<DestructiveActionBlocked>(
            mailboxBlocked.SaidSingle<DestructiveActionBlocked>().Body);
        Assert.Equal("legal_hold", blocked.Reason);
        Assert.Equal(holdId, blocked.HoldId);

        await session.EmitAsync(
            new ObserveEmail(
                MessageId: "msg-contoso-keep",
                From: "legal@contoso.test",
                Domain: "contoso.test",
                Subject: "Contoso discovery packet",
                Snippet: "Please retain."),
            ct);

        var mailboxRetain = await WaitForJournalAsync(
            mailboxId,
            reading => reading.AllHeard<EmailReceived>().Count == 1
                && reading.AllSaid<RetentionExtended>().Count == 1,
            "inbound Contoso email retained under hold",
            ct);

        var gmailReading = await ReadAsync(gmailId, ct);
        var emailSaid = gmailReading.SaidSingle<EmailReceived>();
        Assert.Equal("declared", emailSaid.DeliveryTo(mailboxId).Via);
        Assert.Equal(
            new SynapseRef(gmailId, emailSaid.Position),
            mailboxRetain.SaidSingle<RetentionExtended>().Cause);
        Assert.Equal(
            holdId,
            Assert.IsType<RetentionExtended>(mailboxRetain.SaidSingle<RetentionExtended>().Body).HoldId);

        await WaitForJournalAsync(
            auditId,
            reading => reading.AllHeard<DestructiveActionBlocked>().Count == 1
                && reading.AllHeard<RetentionExtended>().Count == 1,
            "audit heard block + retention",
            ct);
    }

    [Fact(DisplayName =
        "Legal hold lift: after LegalHoldLifted, DestructiveDeleteAsked executes")]
    public async Task LiftAllowsDestructiveDelete()
    {
        var ct = Cancellation;
        var context = "legal-lift";
        var session = Brain.Session(context);
        var mailboxId = new NeuronId("holdawaremailbox", context);
        var holdId = "hold-lift-1";

        await session.EmitAsync(
            new LegalHoldPlaceAsked(holdId, SubjectAccount: "Contoso", Policy: "litigation-preserve"),
            ct);
        await WaitForJournalAsync(
            mailboxId,
            reading => reading.AllHeard<LegalHoldPlaced>().Count == 1,
            "hold active on mailbox",
            ct);

        await session.EmitAsync(new LegalHoldLifted(holdId, SubjectAccount: "Contoso"), ct);
        await WaitForJournalAsync(
            mailboxId,
            reading => reading.AllHeard<LegalHoldLifted>().Count == 1,
            "mailbox heard lift",
            ct);

        await session.EmitAsync(
            new DestructiveDeleteAsked("Contoso", Scope: "archive-purge", Actor: "owner"),
            ct);

        var mailbox = await WaitForJournalAsync(
            mailboxId,
            reading => reading.AllSaid<DestructiveDeleteExecuted>().Count == 1,
            "delete executes after lift",
            ct);

        Assert.Empty(mailbox.AllSaid<DestructiveActionBlocked>());
        Assert.Equal(
            "archive-purge",
            Assert.IsType<DestructiveDeleteExecuted>(
                mailbox.SaidSingle<DestructiveDeleteExecuted>().Body).Scope);
    }
}
