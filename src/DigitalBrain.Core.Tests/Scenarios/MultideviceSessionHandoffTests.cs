using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class MultideviceSessionHandoffTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain.AddModule<WorkThread>();

    [Fact(DisplayName =
        "Multi-device handoff: phone + desktop sessions at different names ReadAsync the same work neuron and see the same facts — not forked work identities")]
    public async Task TwoDeviceSessionsReadSameWorkNeuronJournal()
    {
        var ct = Cancellation;
        // Work identity is the thread locus — not the device session names.
        var threadKey = "northwind-renewal";
        var workId = new NeuronId("workthread", threadKey);

        // Device sessions are views with distinct names; they do not mint parallel work grains.
        var phone = Brain.Session("device-phone");
        var desktop = Brain.Session("device-desktop");
        Assert.NotEqual(phone.Id, desktop.Id);
        Assert.NotEqual(phone.Id.Name, workId.Name);
        Assert.NotEqual(desktop.Id.Name, workId.Name);
        Assert.Equal("device-phone", phone.Id.Name);
        Assert.Equal("device-desktop", desktop.Id.Name);

        await phone.SendAsync(
            workId,
            new WorkDraftCommitted("phone", "Renewal intro draft v1", threadKey),
            ct);
        await phone.SendAsync(
            workId,
            new DevicePresenceChanged("phone", "active", threadKey),
            ct);

        var afterPhone = await WaitForJournalAsync(
            workId,
            reading => reading.AllHeard<WorkDraftCommitted>().Count == 1
                && reading.AllHeard<DevicePresenceChanged>().Count == 1,
            "work thread heard phone draft + presence",
            ct);

        var draftHeard = afterPhone.HeardSingle<WorkDraftCommitted>();
        Assert.Equal(phone.Id, draftHeard.Metadata.Source);
        Assert.Equal("Renewal intro draft v1", Assert.IsType<WorkDraftCommitted>(draftHeard.Body).DraftText);
        Assert.Equal(threadKey, Assert.IsType<WorkDraftCommitted>(draftHeard.Body).ThreadKey);

        // Desktop continues the same work neuron — directed Send, same Name/Kind.
        await desktop.SendAsync(
            workId,
            new DevicePresenceChanged("desktop", "active", threadKey),
            ct);
        await desktop.SendAsync(
            workId,
            new WorkProgressLogged("desktop", "Expanded pricing section", threadKey),
            ct);

        var afterDesktop = await WaitForJournalAsync(
            workId,
            reading => reading.AllHeard<WorkProgressLogged>().Count == 1
                && reading.AllHeard<DevicePresenceChanged>().Count == 2
                && reading.AllHeard<WorkDraftCommitted>().Count == 1,
            "work thread heard desktop progress on same journal",
            ct);

        // Both device "views" read the same work neuron — identical journal sequence and bodies.
        var phoneView = await Brain.ReadAsync(workId, 0, ct);
        var desktopView = await Brain.ReadAsync(workId, 0, ct);

        Assert.Equal(phoneView.Journal.Count, desktopView.Journal.Count);
        Assert.Equal(4, phoneView.Journal.Count);
        for (var index = 0; index < phoneView.Journal.Count; index++)
        {
            var a = phoneView.Journal[index];
            var b = desktopView.Journal[index];
            Assert.Equal(a.Position, b.Position);
            Assert.Equal(a.Kind, b.Kind);
            Assert.Equal(a.Entry, b.Entry);
            Assert.Equal(a.Body, b.Body);
            Assert.Equal(a.Metadata.Source, b.Metadata.Source);
            Assert.Equal(a.Metadata.Sequence, b.Metadata.Sequence);
        }

        Assert.Equal(
            Assert.IsType<WorkDraftCommitted>(phoneView.HeardSingle<WorkDraftCommitted>().Body),
            Assert.IsType<WorkDraftCommitted>(desktopView.HeardSingle<WorkDraftCommitted>().Body));
        Assert.Equal(
            "Expanded pricing section",
            Assert.IsType<WorkProgressLogged>(phoneView.HeardSingle<WorkProgressLogged>().Body).Note);
        Assert.Equal(
            "Expanded pricing section",
            Assert.IsType<WorkProgressLogged>(desktopView.HeardSingle<WorkProgressLogged>().Body).Note);

        // Session journals hold only what each device said — not a forked work thread copy.
        var phoneSessionReading = await ReadAsync(phone.Id, ct);
        var desktopSessionReading = await ReadAsync(desktop.Id, ct);
        Assert.Equal(2, phoneSessionReading.Journal.Count);
        Assert.Equal(2, desktopSessionReading.Journal.Count);
        Assert.Empty(phoneSessionReading.AllHeard<WorkDraftCommitted>());
        Assert.Empty(desktopSessionReading.AllHeard<WorkDraftCommitted>());
        Assert.Empty(phoneSessionReading.AllHeard<WorkProgressLogged>());
        Assert.Empty(desktopSessionReading.AllHeard<WorkProgressLogged>());

        var phoneDraftSaid = phoneSessionReading.SaidSingle<WorkDraftCommitted>();
        Assert.Equal("directed", phoneDraftSaid.DeliveryTo(workId).Via);
        Assert.Equal(phone.Id, afterDesktop.HeardSingle<WorkDraftCommitted>().Metadata.Source);
        Assert.Equal(phoneDraftSaid.Position, afterDesktop.HeardSingle<WorkDraftCommitted>().Metadata.Sequence);

        var desktopProgressSaid = desktopSessionReading.SaidSingle<WorkProgressLogged>();
        Assert.Equal("directed", desktopProgressSaid.DeliveryTo(workId).Via);
        Assert.Equal(desktop.Id, afterDesktop.HeardSingle<WorkProgressLogged>().Metadata.Source);
        Assert.Equal(
            desktopProgressSaid.Position,
            afterDesktop.HeardSingle<WorkProgressLogged>().Metadata.Sequence);

        // No second work grain at device names — those journals stay empty.
        Assert.Empty((await ReadAsync(new NeuronId("workthread", phone.Id.Name), ct)).Journal);
        Assert.Empty((await ReadAsync(new NeuronId("workthread", desktop.Id.Name), ct)).Journal);
    }
}
