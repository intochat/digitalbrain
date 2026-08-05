using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class TravelBookingMultiApprovalTests(BrainTestClusters clusters)
    : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<TravelBookingDesk>()
            .AddModule<TravelSagaLedger>();

    [Fact(DisplayName =
        "Travel multi-approval: offers+policy → selection → TravelApprovalRequired gates hold; manager approve → hold → book → calendar")]
    public async Task OutOfPolicySelectionRequiresManagerBeforeBook()
    {
        var ct = Cancellation;
        var context = "travel-austin";
        var session = Brain.Session(context);
        var deskId = new NeuronId("travelbookingdesk", context);
        var ledgerId = new NeuronId("travelsagaledger", context);
        var tripId = "trip-acme-onsite";

        await session.EmitAsync(new TravelSearchAsked(tripId, "Austin", "Tue-Thu"), ct);

        var afterSearch = await WaitForJournalAsync(
            deskId,
            reading => reading.AllSaid<TravelOffersPresented>().Count == 1
                && reading.AllSaid<TravelPolicyEvaluated>().Count == 2,
            "offers presented and policy evaluated",
            ct);

        Assert.Empty(afterSearch.AllSaid<TravelHoldPlaced>());
        Assert.Empty(afterSearch.AllSaid<TravelBooked>());

        var offers = Assert.IsType<TravelOffersPresented>(
            afterSearch.SaidSingle<TravelOffersPresented>().Body);
        Assert.Equal(2, offers.Offers.Length);
        var outOfPolicy = offers.Offers.Single(o => !o.InPolicy);

        var policies = afterSearch.AllSaid<TravelPolicyEvaluated>()
            .Select(said => Assert.IsType<TravelPolicyEvaluated>(said.Body))
            .ToArray();
        Assert.Contains(policies, p => p is { OfferId: "hotel-out", InPolicy: false });
        Assert.Contains(policies, p => p is { OfferId: "air-in", InPolicy: true });

        await session.EmitAsync(new TravelSelectionMade(tripId, outOfPolicy.OfferId), ct);

        var afterSelect = await WaitForJournalAsync(
            deskId,
            reading => reading.AllSaid<TravelApprovalRequired>().Count == 1,
            "approval required after out-of-policy selection",
            ct);

        Assert.Empty(afterSelect.AllSaid<TravelHoldPlaced>());
        Assert.Empty(afterSelect.AllSaid<TravelBooked>());
        var approval = Assert.IsType<TravelApprovalRequired>(
            afterSelect.SaidSingle<TravelApprovalRequired>().Body);
        Assert.Equal(outOfPolicy.OfferId, approval.OfferId);

        await session.EmitAsync(
            new TravelManagerApproved(approval.BundleId, tripId, outOfPolicy.OfferId),
            ct);

        var afterApprove = await WaitForJournalAsync(
            deskId,
            reading => reading.AllSaid<TravelHoldPlaced>().Count == 1,
            "hold placed after manager approval",
            ct);

        Assert.Empty(afterApprove.AllSaid<TravelBooked>());
        var hold = Assert.IsType<TravelHoldPlaced>(afterApprove.SaidSingle<TravelHoldPlaced>().Body);
        Assert.Equal(outOfPolicy.OfferId, hold.OfferId);

        var sessionAfterApprove = await ReadAsync(session.Id, ct);
        var managerSaid = sessionAfterApprove.SaidSingle<TravelManagerApproved>();
        Assert.Equal(
            new SynapseRef(session.Id, managerSaid.Position),
            afterApprove.SaidSingle<TravelHoldPlaced>().Cause);

        await session.EmitAsync(new TravelBookConfirmed(tripId), ct);

        var afterBook = await WaitForJournalAsync(
            deskId,
            reading => reading.AllSaid<TravelBooked>().Count == 1
                && reading.AllSaid<TravelCalendarHoldCreated>().Count == 1,
            "booked + calendar after confirm",
            ct);

        var ledger = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<TravelBooked>().Count == 1
                && reading.AllHeard<TravelCalendarHoldCreated>().Count == 1
                && reading.AllHeard<TravelHoldPlaced>().Count == 1
                && reading.AllHeard<TravelApprovalRequired>().Count == 1,
            "ledger heard saga terminals",
            ct);

        var booked = Assert.IsType<TravelBooked>(afterBook.SaidSingle<TravelBooked>().Body);
        Assert.Equal($"PNR-{tripId}", booked.Pnr);
        Assert.Equal(outOfPolicy.OfferId, booked.OfferId);

        var sessionFinal = await ReadAsync(session.Id, ct);
        var confirmSaid = sessionFinal.SaidSingle<TravelBookConfirmed>();
        Assert.Equal(
            new SynapseRef(session.Id, confirmSaid.Position),
            afterBook.SaidSingle<TravelBooked>().Cause);
        Assert.Equal(deskId, ledger.HeardSingle<TravelBooked>().Metadata.Source);
    }
}
