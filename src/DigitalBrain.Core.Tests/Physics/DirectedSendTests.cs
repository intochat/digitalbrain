using DigitalBrain.Testing;

using DigitalBrain.Core.Tests.Support;

namespace DigitalBrain.Core.Tests.Physics;

public sealed class DirectedSendTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<StageSpeaker>()
            .AddModule<StageAudience>()
            .AddModule<StageArchive>();

    [Fact(DisplayName =
        "Session Send to an exact neuron id journals a single receiver and does not deliver to other declared listeners of that fact type")]
    public async Task SessionSendDeliversOnlyToNamedReceiver()
    {
        var ct = Cancellation;
        var context = "directed-send";
        var session = Brain.Session(context);
        var audienceId = new NeuronId("stageaudience", context);
        var archiveId = new NeuronId("stagearchive", context);
        var speakerId = new NeuronId("stagespeaker", context);
        var directed = new StageSaid("directed-payload");

        await session.SendAsync(audienceId, directed, ct);

        var audience = await WaitForJournalAsync(
            audienceId,
            reading => reading.AllHeard<StageSaid>().Count == 1,
            "a heard StageSaid on the named Send receiver",
            ct);
        Assert.Equal("directed-payload", Assert.IsType<StageSaid>(audience.HeardSingle<StageSaid>().Body).Note);
        Assert.Equal(session.Id, audience.HeardSingle<StageSaid>().Metadata.Source);

        var sessionAfterSend = await WaitForJournalAsync(
            session.Id,
            reading => reading.AllSaid<StageSaid>().Count == 1,
            "a said StageSaid for the directed Send",
            ct);
        var sent = sessionAfterSend.SaidSingle<StageSaid>();
        Assert.NotNull(sent.To);
        Assert.Single(sent.To);
        Assert.Equal("ask", sent.DeliveryTo(audienceId).Via);
        Assert.Null(sent.DeliveryToOrNull(archiveId));

        var archiveAfterSend = await ReadAsync(archiveId, ct);
        Assert.Empty(archiveAfterSend.AllHeard<StageSaid>());

        // Contrast: Emit of the same fact type fans out to every declared listener at context.
        await session.EmitAsync(new PlanDay(new DateOnly(2026, 8, 9)), ct);
        _ = await WaitForAsync<StageSaid>(audienceId, ct);
        _ = await WaitForAsync<StageSaid>(archiveId, ct);

        var speaker = await WaitForJournalAsync(
            speakerId,
            reading => reading.AllSaid<StageSaid>().Count == 1,
            "a said StageSaid from the speaker Emit fan-out",
            ct);
        var emitted = speaker.SaidSingle<StageSaid>();
        Assert.Equal("declared", emitted.DeliveryTo(audienceId).Via);
        Assert.Equal("declared", emitted.DeliveryTo(archiveId).Via);
        Assert.Equal(2, emitted.To!.Count);

        var archiveAfterEmit = await WaitForJournalAsync(
            archiveId,
            reading => reading.AllHeard<StageSaid>().Count == 1,
            "archive hears only the Emit fan-out, never the directed Send",
            ct);
        Assert.Single(archiveAfterEmit.AllHeard<StageSaid>());
        Assert.Equal("day-20260809", Assert.IsType<StageSaid>(archiveAfterEmit.HeardSingle<StageSaid>().Body).Note);

        var audienceAfterBoth = await WaitForJournalAsync(
            audienceId,
            reading => reading.AllHeard<StageSaid>().Count == 2,
            "audience heard both directed Send and Emit fan-out",
            ct);
        Assert.Equal(2, audienceAfterBoth.AllHeard<StageSaid>().Count);
    }
}
