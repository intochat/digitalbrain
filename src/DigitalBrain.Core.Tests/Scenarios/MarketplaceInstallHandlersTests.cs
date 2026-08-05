using DigitalBrain.Mocks;
using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class MarketplaceInstallHandlersTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<MockGmail>()
            .AddModule<MarketplaceInboxLedger>()
            .AddModule<TravelDisruptionAssistant>()
            .AddModule<MarketplaceInstaller>()
            .AddModule<MarketplaceTopologyLedger>();

    [Fact(DisplayName =
        "Marketplace install N+1 (Stage-1 honest: second module type in Compose, not dynamic ALC): EmailReceived To[] includes both listeners after BehaviorActivated")]
    public async Task SecondModuleTypeIncreasesEmailListenersWithoutRestart()
    {
        var ct = Cancellation;
        var context = "marketplace-owner";
        var session = Brain.Session(context);
        var gmailId = new NeuronId("mockgmail", context);
        var inboxId = new NeuronId("marketplaceinboxledger", context);
        var travelId = new NeuronId("traveldisruptionassistant", context);
        var installerId = new NeuronId("marketplaceinstaller", context);
        var topologyId = new NeuronId("marketplacetopologyledger", context);
        var packageId = "pkg-travel-disruption";

        await session.EmitAsync(
            new MarketplaceInstallRequested(packageId, BehaviorKind: "traveldisruptionassistant"),
            ct);

        var installerReading = await WaitForJournalAsync(
            installerId,
            reading => reading.AllSaid<MarketplaceBehaviorActivated>().Count == 1
                && reading.AllSaid<MarketplaceCapabilitiesChanged>().Count == 1,
            "install pipeline activated package",
            ct);

        var activatedSaid = installerReading.SaidSingle<MarketplaceBehaviorActivated>();
        Assert.Equal(new SynapseRef(
            session.Id,
            (await ReadAsync(session.Id, ct)).SaidSingle<MarketplaceInstallRequested>().Position),
            activatedSaid.Cause);
        Assert.Equal(packageId, Assert.IsType<MarketplaceBehaviorActivated>(activatedSaid.Body).PackageId);
        Assert.Equal(["emailreceived"], Assert.IsType<MarketplaceBehaviorActivated>(activatedSaid.Body).Listens);

        await WaitForJournalAsync(
            topologyId,
            reading => reading.AllHeard<MarketplaceBehaviorActivated>().Count == 1,
            "topology heard activation",
            ct);

        // Live traffic: N (inbox ledger) + N+1 (travel) both hear the same EmailReceived.
        await session.EmitAsync(
            new ObserveEmail(
                "msg-flight-1",
                From: "airlines@carrier.example",
                Domain: "carrier.example",
                Subject: "Your flight is delayed",
                Snippet: "UA 100 delayed 2h"),
            ct);

        var gmailReading = await WaitForJournalAsync(
            gmailId,
            reading => reading.AllSaid<EmailReceived>().Count == 1,
            "gmail said EmailReceived",
            ct);

        var emailSaid = gmailReading.SaidSingle<EmailReceived>();
        Assert.Equal("declared", emailSaid.DeliveryTo(inboxId).Via);
        Assert.Equal("declared", emailSaid.DeliveryTo(travelId).Via);
        // N+1: both declared receivers on the same Source/Sequence fan-out.
        Assert.Equal(emailSaid.DeliveryTo(inboxId).Via, emailSaid.DeliveryTo(travelId).Via);

        var inboxReading = await WaitForJournalAsync(
            inboxId,
            reading => reading.AllHeard<EmailReceived>().Count == 1,
            "N inbox ledger heard EmailReceived",
            ct);
        Assert.Equal(gmailId, inboxReading.HeardSingle<EmailReceived>().Metadata.Source);
        Assert.Equal(emailSaid.Position, inboxReading.HeardSingle<EmailReceived>().Metadata.Sequence);

        var travelReading = await WaitForJournalAsync(
            travelId,
            reading => reading.AllHeard<EmailReceived>().Count == 1
                && reading.AllSaid<TravelDisruptionDetected>().Count == 1,
            "N+1 travel assistant heard same EmailReceived and detected disruption",
            ct);
        Assert.Equal(gmailId, travelReading.HeardSingle<EmailReceived>().Metadata.Source);
        Assert.Equal(emailSaid.Position, travelReading.HeardSingle<EmailReceived>().Metadata.Sequence);
        Assert.Equal(
            "msg-flight-1",
            Assert.IsType<TravelDisruptionDetected>(
                travelReading.SaidSingle<TravelDisruptionDetected>().Body).MessageId);

        var topologyFinal = await WaitForJournalAsync(
            topologyId,
            reading => reading.AllHeard<TravelDisruptionDetected>().Count == 1,
            "topology heard travel detection",
            ct);
        Assert.Equal(travelId, topologyFinal.HeardSingle<TravelDisruptionDetected>().Metadata.Source);

        // Capability change claimed listener count hint ≥ 2 (inbox + travel).
        Assert.True(
            Assert.IsType<MarketplaceCapabilitiesChanged>(
                installerReading.SaidSingle<MarketplaceCapabilitiesChanged>().Body).ListenerCountHint >= 2);
    }
}
