using DigitalBrain.Testing;

using DigitalBrain.Core.Tests.Support;

namespace DigitalBrain.Core.Tests.Physics;

public sealed class ConnectionWiringTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<StageSpeaker>()
            .AddModule<StageAudience>()
            .AddModule<StageArchive>()
            .AddModule<SilentPeer>();

    [Fact(DisplayName =
        "After Connect, only the connected instance hears the fact and the same-context ghost kind does not")]
    public async Task ConnectWiresInstanceAndSuppressesGhost()
    {
        var ct = Cancellation;
        var context = "wire";
        var session = Brain.Session(context);
        var speakerId = new NeuronId("stagespeaker", context);
        var ghostId = new NeuronId("stageaudience", context);
        var connectedId = new NeuronId("stageaudience", "dashboard");
        var stageKind = "stagesaid";

        await session.SendAsync(speakerId, new Connect(stageKind, connectedId), ct);
        await WaitForConnectedAsync(speakerId, stageKind, connectedId, ct);

        await session.EmitAsync(new PlanDay(new DateOnly(2026, 8, 7)), ct);

        var heard = await WaitForAsync<StageSaid>(connectedId, ct);
        Assert.Equal("day-20260807", heard.Note);

        var speakerReading = await WaitForJournalAsync(
            speakerId,
            reading => reading.AllSaid<StageSaid>().Count == 1,
            "a said StageSaid after Connect",
            ct);
        var said = speakerReading.SaidSingle<StageSaid>();
        Assert.Equal("connected", said.DeliveryTo(connectedId).Via);
        Assert.Null(said.DeliveryToOrNull(ghostId));

        var ghostReading = await ReadAsync(ghostId, ct);
        Assert.Empty(ghostReading.AllHeard<StageSaid>());
    }

    [Fact(DisplayName =
        "A connection for fact F to kind K suppresses only kind K from declared fan-out; other listener kinds still receive")]
    public async Task GhostSuppressesOnlyConnectedKind()
    {
        var ct = Cancellation;
        var context = "ghost-scope";
        var session = Brain.Session(context);
        var speakerId = new NeuronId("stagespeaker", context);
        var ghostAudience = new NeuronId("stageaudience", context);
        var connectedAudience = new NeuronId("stageaudience", "foreign");
        var archiveId = new NeuronId("stagearchive", context);
        var stageKind = "stagesaid";

        await session.SendAsync(speakerId, new Connect(stageKind, connectedAudience), ct);
        await WaitForConnectedAsync(speakerId, stageKind, connectedAudience, ct);

        await session.EmitAsync(new PlanDay(new DateOnly(2026, 8, 8)), ct);

        _ = await WaitForAsync<StageSaid>(connectedAudience, ct);
        _ = await WaitForAsync<StageSaid>(archiveId, ct);

        var speakerReading = await WaitForJournalAsync(
            speakerId,
            reading => reading.AllSaid<StageSaid>().Count == 1,
            "a said StageSaid under partial ghost suppress",
            ct);
        var said = speakerReading.SaidSingle<StageSaid>();
        Assert.Equal("connected", said.DeliveryTo(connectedAudience).Via);
        Assert.Equal("declared", said.DeliveryTo(archiveId).Via);
        Assert.Null(said.DeliveryToOrNull(ghostAudience));

        var ghostReading = await ReadAsync(ghostAudience, ct);
        Assert.Empty(ghostReading.AllHeard<StageSaid>());
    }

    [Fact(DisplayName =
        "Connect to a non-declaring or illegal target journals ConnectionRefused and does not mutate the emitter connection table")]
    public async Task ConnectionRefusedOnBadKind()
    {
        var ct = Cancellation;
        var context = "refused";
        var session = Brain.Session(context);
        var speakerId = new NeuronId("stagespeaker", context);
        var badTarget = new NeuronId("silentpeer", "nowhere");
        var stageKind = "stagesaid";

        await session.SendAsync(speakerId, new Connect(stageKind, badTarget), ct);

        var refused = await WaitForAsync<ConnectionRefused>(session.Id, ct);
        Assert.Equal(stageKind, refused.Fact);
        Assert.Equal(badTarget, refused.To);
        Assert.Contains("does not declare", refused.Reason, StringComparison.Ordinal);

        var speakerReading = await WaitForJournalAsync(
            speakerId,
            reading => reading.AllSaid<ConnectionRefused>().Count == 1,
            "a said ConnectionRefused",
            ct);
        Assert.False(speakerReading.Connections.ContainsKey(stageKind));
        Assert.Empty(speakerReading.Connections);

        var saidRefusal = speakerReading.SaidSingle<ConnectionRefused>();
        Assert.Equal("ask", saidRefusal.DeliveryTo(session.Id).Via);
        Assert.Equal(stageKind, Assert.IsType<ConnectionRefused>(saidRefusal.Body).Fact);
    }

    private Task<NeuronReading> WaitForConnectedAsync(
        NeuronId emitter, string factKind, NeuronId target, CancellationToken cancellationToken)
        => WaitForJournalAsync(
            emitter,
            reading => reading.Connections.TryGetValue(factKind, out var targets)
                && targets.Any(candidate => candidate == target),
            $"connection {factKind} → {target}",
            cancellationToken);
}
