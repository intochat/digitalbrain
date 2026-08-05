using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class BehaviorHotReloadLiveTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    private const string EmailKind = "liveemailreceived";

    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<LiveMailHub>()
            .AddModule<LivePolicyClassifier>()
            .AddModule<LiveClassifierLedger>();

    [Fact(DisplayName =
        "Hot-reload live traffic (Stage-1 honest: Connect rewiring, not ALC): next LiveEmailReceived only hits classifier v2; v1 journals freeze")]
    public async Task ConnectRewireSwitchesClassifierUnderLiveTraffic()
    {
        var ct = Cancellation;
        var context = "hot-reload-live";
        var session = Brain.Session(context);
        var hubId = new NeuronId("livemailhub", context);
        var v1 = new NeuronId("livepolicyclassifier", "v1");
        var v2 = new NeuronId("livepolicyclassifier", "v2");
        var ledgerId = new NeuronId("liveclassifierledger", context);

        await session.SendAsync(hubId, new Connect(EmailKind, v1), ct);
        await WaitForJournalAsync(
            hubId,
            reading => reading.Connections.TryGetValue(EmailKind, out var targets)
                && targets.Any(t => t == v1),
            "connected → v1",
            ct);

        await session.EmitAsync(new LiveObserveEmail("e1", "board.example", "Board pack"), ct);

        var v1Reading = await WaitForJournalAsync(
            v1,
            reading => reading.AllSaid<LiveEmailClassified>().Count == 1
                && reading.AllSaid<LiveUiSurface>().Count == 1,
            "v1 classified VIP under board.example rule",
            ct);

        var classifiedV1 = Assert.IsType<LiveEmailClassified>(v1Reading.SaidSingle<LiveEmailClassified>().Body);
        Assert.Equal("vip", classifiedV1.Label);
        Assert.Equal("v1", classifiedV1.ClassifierRev);
        Assert.Equal("VipCard-v1", Assert.IsType<LiveUiSurface>(
            v1Reading.SaidSingle<LiveUiSurface>().Body).CardKind);

        // Activate N+1: Disconnect v1, Connect v2, journal BehaviorPackageActivated.
        await session.SendAsync(hubId, new Disconnect(EmailKind, v1), ct);
        await session.SendAsync(hubId, new Connect(EmailKind, v2), ct);
        await session.EmitAsync(
            new BehaviorPackageActivated(Kind: "livepolicyclassifier", Version: "v2", ActiveName: "v2"),
            ct);

        await WaitForJournalAsync(
            hubId,
            reading => reading.Connections.TryGetValue(EmailKind, out var targets)
                && targets.Any(t => t == v2)
                && !targets.Any(t => t == v1),
            "rewired → v2",
            ct);

        await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<BehaviorPackageActivated>().Count == 1,
            "ledger heard package activated",
            ct);

        // Next email under v2 rules (investors.example is VIP for v2).
        await session.EmitAsync(new LiveObserveEmail("e2", "investors.example", "LP update"), ct);

        var v2Reading = await WaitForJournalAsync(
            v2,
            reading => reading.AllHeard<LiveEmailReceived>().Count == 1
                && reading.AllSaid<LiveEmailClassified>().Count == 1
                && reading.AllSaid<LiveUiSurface>().Count == 1,
            "v2 only hears post-activate email",
            ct);

        Assert.Equal("e2", Assert.IsType<LiveEmailReceived>(
            v2Reading.HeardSingle<LiveEmailReceived>().Body).MessageId);
        Assert.Equal("v2", Assert.IsType<LiveEmailClassified>(
            v2Reading.SaidSingle<LiveEmailClassified>().Body).ClassifierRev);
        Assert.Equal("vip", Assert.IsType<LiveEmailClassified>(
            v2Reading.SaidSingle<LiveEmailClassified>().Body).Label);

        var hubSaid = (await ReadAsync(hubId, ct)).AllSaid<LiveEmailReceived>()
            .Single(s => Assert.IsType<LiveEmailReceived>(s.Body).MessageId == "e2");
        Assert.Equal("connected", hubSaid.DeliveryTo(v2).Via);
        Assert.Null(hubSaid.DeliveryToOrNull(v1));

        // v1 frozen: still only e1.
        var v1Final = await ReadAsync(v1, ct);
        Assert.Single(v1Final.AllHeard<LiveEmailReceived>());
        Assert.Equal("e1", Assert.IsType<LiveEmailReceived>(
            v1Final.HeardSingle<LiveEmailReceived>().Body).MessageId);
        Assert.Single(v1Final.AllSaid<LiveEmailClassified>());

        // Declared fan-out is same-Name as emitter: v1 → ledger/v1, v2 → ledger/v2.
        var ledgerV1 = new NeuronId("liveclassifierledger", "v1");
        var ledgerV2 = new NeuronId("liveclassifierledger", "v2");
        var ledgerV1Reading = await WaitForJournalAsync(
            ledgerV1,
            reading => reading.AllHeard<LiveEmailClassified>().Count == 1
                && reading.AllHeard<LiveUiSurface>().Count == 1,
            "ledger@v1 heard v1 classification",
            ct);
        var ledgerV2Reading = await WaitForJournalAsync(
            ledgerV2,
            reading => reading.AllHeard<LiveEmailClassified>().Count == 1
                && reading.AllHeard<LiveUiSurface>().Count == 1,
            "ledger@v2 heard v2 classification",
            ct);
        Assert.Equal("v1", Assert.IsType<LiveEmailClassified>(
            ledgerV1Reading.HeardSingle<LiveEmailClassified>().Body).ClassifierRev);
        Assert.Equal("v2", Assert.IsType<LiveEmailClassified>(
            ledgerV2Reading.HeardSingle<LiveEmailClassified>().Body).ClassifierRev);
        Assert.Equal(v1, ledgerV1Reading.HeardSingle<LiveEmailClassified>().Metadata.Source);
        Assert.Equal(v2, ledgerV2Reading.HeardSingle<LiveEmailClassified>().Metadata.Source);
    }
}
