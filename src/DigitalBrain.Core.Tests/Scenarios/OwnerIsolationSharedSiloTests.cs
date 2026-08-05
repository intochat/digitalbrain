using DigitalBrain.Mocks;
using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class OwnerIsolationSharedSiloTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<MockGmail>()
            .AddModule<OwnerInbox>()
            .AddModule<OwnerJournalQuery>()
            .AddModule<OwnerMailLedger>()
            .AddModule<OwnerSecurityAudit>();

    [Fact(DisplayName =
        "Owner isolation shared silo (Stage-1: different context Names, not OwnerId in NeuronId): A mail never lands in B journals; B query slice is B-only")]
    public async Task ContextNamesFenceMailAndJournalQuery()
    {
        var ct = Cancellation;
        var ownerA = "owner-a";
        var ownerB = "owner-b";
        var sessionA = Brain.Session(ownerA);
        var sessionB = Brain.Session(ownerB);
        var inboxA = new NeuronId("ownerinbox", ownerA);
        var inboxB = new NeuronId("ownerinbox", ownerB);
        var queryA = new NeuronId("ownerjournalquery", ownerA);
        var queryB = new NeuronId("ownerjournalquery", ownerB);
        var gmailA = new NeuronId("mockgmail", ownerA);
        var gmailB = new NeuronId("mockgmail", ownerB);

        await sessionA.EmitAsync(
            new ObserveEmail("a-1", "ceo@a.example", "a.example", "A secret pipeline", "do not share"),
            ct);

        var inboxAReading = await WaitForJournalAsync(
            inboxA,
            reading => reading.AllHeard<EmailReceived>().Count == 1
                && reading.AllSaid<OwnerMailLogged>().Count == 1,
            "owner-a inbox heard own mail",
            ct);

        Assert.Equal("a-1", Assert.IsType<EmailReceived>(
            inboxAReading.HeardSingle<EmailReceived>().Body).MessageId);
        Assert.Equal(ownerA, Assert.IsType<OwnerMailLogged>(
            inboxAReading.SaidSingle<OwnerMailLogged>().Body).Owner);

        // Owner B parallel traffic.
        await sessionB.EmitAsync(
            new ObserveEmail("b-1", "cfo@b.example", "b.example", "B payroll", "private"),
            ct);

        var inboxBReading = await WaitForJournalAsync(
            inboxB,
            reading => reading.AllHeard<EmailReceived>().Count == 1,
            "owner-b inbox heard own mail",
            ct);
        Assert.Equal("b-1", Assert.IsType<EmailReceived>(
            inboxBReading.HeardSingle<EmailReceived>().Body).MessageId);

        // Cross-fence: B's inbox journal never holds A's message.
        Assert.DoesNotContain(
            inboxBReading.AllHeard<EmailReceived>(),
            h => Assert.IsType<EmailReceived>(h.Body).MessageId == "a-1");
        Assert.DoesNotContain(
            (await ReadAsync(inboxA, ct)).AllHeard<EmailReceived>(),
            h => Assert.IsType<EmailReceived>(h.Body).MessageId == "b-1");

        var sessionASaid = (await ReadAsync(sessionA.Id, ct)).SaidSingle<ObserveEmail>();
        Assert.Equal("declared", sessionASaid.DeliveryTo(gmailA).Via);
        Assert.Null(sessionASaid.DeliveryToOrNull(gmailB));

        // Wait query grains to hear OwnerMailLogged in their context.
        await WaitForJournalAsync(
            queryA,
            reading => reading.AllHeard<OwnerMailLogged>().Count == 1,
            "query@a heard OwnerMailLogged",
            ct);
        await WaitForJournalAsync(
            queryB,
            reading => reading.AllHeard<OwnerMailLogged>().Count == 1,
            "query@b heard OwnerMailLogged",
            ct);

        // B asks "all emails in the silo" against B's query — only B ids.
        var sliceB = await sessionB.AskAsync<OwnerJournalSlice>(
            new OwnerJournalRangeAsked(ownerB, "show all emails in the silo"),
            ct);
        Assert.Equal(ownerB, sliceB.Owner);
        Assert.Equal(["b-1"], sliceB.MessageIds);
        Assert.DoesNotContain("a-1", sliceB.MessageIds);
        Assert.DoesNotContain("secret", string.Join(' ', sliceB.Subjects), StringComparison.OrdinalIgnoreCase);

        var sliceA = await sessionA.AskAsync<OwnerJournalSlice>(
            new OwnerJournalRangeAsked(ownerA, "my mail"),
            ct);
        Assert.Equal(["a-1"], sliceA.MessageIds);
        Assert.DoesNotContain("b-1", sliceA.MessageIds);

        // Adversarial note: attempted cross-owner scrape is journaled as metadata-only audit.
        await sessionB.EmitAsync(
            new CrossOwnerAttemptObserved(ownerB, ownerA, "behavior tried EmailReceived foreign listen"),
            ct);
        var auditB = await WaitForJournalAsync(
            new NeuronId("ownersecurityaudit", ownerB),
            reading => reading.AllHeard<CrossOwnerAttemptObserved>().Count == 1,
            "security audit on B heard attempt",
            ct);
        Assert.Equal(ownerA, Assert.IsType<CrossOwnerAttemptObserved>(
            auditB.HeardSingle<CrossOwnerAttemptObserved>().Body).TargetOwner);
        // A audit journal empty of that attempt body.
        Assert.Empty((await ReadAsync(new NeuronId("ownersecurityaudit", ownerA), ct))
            .AllHeard<CrossOwnerAttemptObserved>());
    }
}
