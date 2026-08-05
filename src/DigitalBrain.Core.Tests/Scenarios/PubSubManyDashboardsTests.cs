using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class PubSubManyDashboardsTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<IncidentDesk>()
            .AddModule<WallOpsDashboard>()
            .AddModule<MobileGlanceDashboard>()
            .AddModule<DashboardRefreshLedger>();

    [Fact(DisplayName = "Pub-sub: many dashboards hear the same IncidentOpened Source/Sequence by declaration")]
    public async Task TwoDashboardsHearSameBroadcastSourceAndSequence()
    {
        var ct = Cancellation;
        var context = "ops-floor";
        var session = Brain.Session(context);
        var deskId = new NeuronId("incidentdesk", context);
        var wallId = new NeuronId("wallopsdashboard", context);
        var mobileId = new NeuronId("mobileglancedashboard", context);
        var ledgerId = new NeuronId("dashboardrefreshledger", context);
        var incidentId = "inc-77";
        var title = "Elevator outage — floor 3";

        await session.EmitAsync(new RaiseIncident(incidentId, title), ct);

        var wallReading = await WaitForJournalAsync(
            wallId,
            reading => reading.AllHeard<IncidentOpened>().Count == 1
                && reading.AllSaid<DashboardPaneRefreshed>().Count == 1,
            "wall dashboard heard IncidentOpened and refreshed",
            ct);

        var mobileReading = await WaitForJournalAsync(
            mobileId,
            reading => reading.AllHeard<IncidentOpened>().Count == 1
                && reading.AllSaid<DashboardPaneRefreshed>().Count == 1,
            "mobile dashboard heard IncidentOpened and refreshed",
            ct);

        var deskReading = await WaitForJournalAsync(
            deskId,
            reading => reading.AllSaid<IncidentOpened>().Count == 1,
            "IncidentDesk said IncidentOpened",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var raiseSaid = sessionReading.SaidSingle<RaiseIncident>();
        Assert.Equal("declared", raiseSaid.DeliveryTo(deskId).Via);

        var raiseHeard = deskReading.HeardSingle<RaiseIncident>();
        Assert.Equal(session.Id, raiseHeard.Metadata.Source);
        Assert.Equal(raiseSaid.Position, raiseHeard.Metadata.Sequence);

        var openedSaid = deskReading.SaidSingle<IncidentOpened>();
        Assert.Equal(new SynapseRef(session.Id, raiseSaid.Position), openedSaid.Cause);
        Assert.Equal("declared", openedSaid.DeliveryTo(wallId).Via);
        Assert.Equal("declared", openedSaid.DeliveryTo(mobileId).Via);
        var opened = Assert.IsType<IncidentOpened>(openedSaid.Body);
        Assert.Equal(incidentId, opened.IncidentId);
        Assert.Equal(title, opened.Title);

        var wallHeard = wallReading.HeardSingle<IncidentOpened>();
        var mobileHeard = mobileReading.HeardSingle<IncidentOpened>();

        // Same Source/Sequence on both journals — one broadcast, two declared receivers.
        Assert.Equal(deskId, wallHeard.Metadata.Source);
        Assert.Equal(deskId, mobileHeard.Metadata.Source);
        Assert.Equal(openedSaid.Position, wallHeard.Metadata.Sequence);
        Assert.Equal(openedSaid.Position, mobileHeard.Metadata.Sequence);
        Assert.Equal(wallHeard.Metadata.Source, mobileHeard.Metadata.Source);
        Assert.Equal(wallHeard.Metadata.Sequence, mobileHeard.Metadata.Sequence);
        Assert.Equal(incidentId, Assert.IsType<IncidentOpened>(wallHeard.Body).IncidentId);
        Assert.Equal(incidentId, Assert.IsType<IncidentOpened>(mobileHeard.Body).IncidentId);

        var wallRefresh = wallReading.SaidSingle<DashboardPaneRefreshed>();
        Assert.Equal(new SynapseRef(deskId, openedSaid.Position), wallRefresh.Cause);
        Assert.Equal("declared", wallRefresh.DeliveryTo(ledgerId).Via);
        Assert.Equal(WallOpsDashboard.Pane, Assert.IsType<DashboardPaneRefreshed>(wallRefresh.Body).Pane);

        var mobileRefresh = mobileReading.SaidSingle<DashboardPaneRefreshed>();
        Assert.Equal(new SynapseRef(deskId, openedSaid.Position), mobileRefresh.Cause);
        Assert.Equal("declared", mobileRefresh.DeliveryTo(ledgerId).Via);
        Assert.Equal(MobileGlanceDashboard.Pane, Assert.IsType<DashboardPaneRefreshed>(mobileRefresh.Body).Pane);

        var ledgerReading = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<DashboardPaneRefreshed>().Count == 2,
            "ledger heard both pane refreshes",
            ct);
        Assert.Contains(
            ledgerReading.AllHeard<DashboardPaneRefreshed>(),
            heard => Assert.IsType<DashboardPaneRefreshed>(heard.Body).Pane == WallOpsDashboard.Pane);
        Assert.Contains(
            ledgerReading.AllHeard<DashboardPaneRefreshed>(),
            heard => Assert.IsType<DashboardPaneRefreshed>(heard.Body).Pane == MobileGlanceDashboard.Pane);
    }
}
