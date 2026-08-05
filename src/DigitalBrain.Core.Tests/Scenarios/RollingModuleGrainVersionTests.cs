using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class RollingModuleGrainVersionTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<RollingCrmActivity>()
            .AddModule<RollingMeetingNotes>()
            .AddModule<RollingUpgradeWatcher>();

    [Fact(DisplayName =
        "Rolling module version (Stage-1 honest: ModuleVersionChanged on same grain, not Orleans interface swap): pre-v1 and post-v2 CrmNoteAck HandlerVersion stamps")]
    public async Task VersionChangeStampsHandlerVersionAcrossAsks()
    {
        var ct = Cancellation;
        var context = "crm-upgrade";
        var session = Brain.Session(context);
        var crmId = new NeuronId("rollingcrmactivity", context);
        var watcherId = new NeuronId("rollingupgradewatcher", context);

        var ackV1 = await session.AskAsync<CrmNoteAck>(
            new AppendCrmNoteAsked("note-1", "contact-acme", "Kickoff notes"),
            ct);
        Assert.Equal(1, ackV1.HandlerVersion);
        Assert.Equal("note-1", ackV1.NoteId);

        var crmAfterV1 = await WaitForJournalAsync(
            crmId,
            reading => reading.AllSaid<CrmNoteAck>().Count == 1,
            "crm answered v1",
            ct);
        Assert.Equal(1, Assert.IsType<CrmNoteAck>(crmAfterV1.SaidSingle<CrmNoteAck>().Body).HandlerVersion);

        await session.EmitAsync(
            new ModuleVersionChanged(ModuleKind: "rollingcrmactivity", FromVersion: 1, ToVersion: 2),
            ct);

        var crmAfterUpgrade = await WaitForJournalAsync(
            crmId,
            reading => reading.AllHeard<ModuleVersionChanged>().Count == 1
                && reading.AllSaid<ModuleVersionBadge>().Count == 1,
            "crm journaled ModuleVersionChanged → badge",
            ct);

        var changedSaid = (await ReadAsync(session.Id, ct)).SaidSingle<ModuleVersionChanged>();
        Assert.Equal("declared", changedSaid.DeliveryTo(crmId).Via);
        Assert.Equal("declared", changedSaid.DeliveryTo(watcherId).Via);

        var badgeSaid = crmAfterUpgrade.SaidSingle<ModuleVersionBadge>();
        Assert.Equal(new SynapseRef(session.Id, changedSaid.Position), badgeSaid.Cause);
        Assert.Equal(2, Assert.IsType<ModuleVersionBadge>(badgeSaid.Body).Version);

        var ackV2 = await session.AskAsync<CrmNoteAck>(
            new AppendCrmNoteAsked("note-2", "contact-acme", "Follow-up after upgrade"),
            ct);
        Assert.Equal(2, ackV2.HandlerVersion);
        Assert.Equal("note-2", ackV2.NoteId);

        var crmFinal = await ReadAsync(crmId, ct);
        Assert.Equal(2, crmFinal.AllSaid<CrmNoteAck>().Count);
        Assert.Contains(
            crmFinal.AllSaid<CrmNoteAck>(),
            s => Assert.IsType<CrmNoteAck>(s.Body) is { HandlerVersion: 1, NoteId: "note-1" });
        Assert.Contains(
            crmFinal.AllSaid<CrmNoteAck>(),
            s => Assert.IsType<CrmNoteAck>(s.Body) is { HandlerVersion: 2, NoteId: "note-2" });

        // Journals remain readable across versions — both acks present.
        Assert.Single(crmFinal.AllHeard<ModuleVersionChanged>());
        Assert.Equal(2, Assert.IsType<ModuleVersionChanged>(
            crmFinal.HeardSingle<ModuleVersionChanged>().Body).ToVersion);

        var watcherReading = await WaitForJournalAsync(
            watcherId,
            reading => reading.AllHeard<ModuleVersionChanged>().Count == 1
                && reading.AllHeard<ModuleVersionBadge>().Count == 1,
            "ops watcher heard version change",
            ct);
        Assert.Equal(crmId, watcherReading.HeardSingle<ModuleVersionBadge>().Metadata.Source);
    }
}
