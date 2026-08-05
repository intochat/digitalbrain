using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class ReplayLastTuesdayJournalTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<JournalReplayIndex>()
            .AddModule<ReplayChat>()
            .AddModule<ReplaySurfaceLedger>()
            .AddModule<ReplayTimelineProjector>();

    [Fact(DisplayName =
        "Replay last Tuesday: JournalRangeQuery slice equals Brain.ReadAsync journal structure; timeline cites only those titles")]
    public async Task SliceMatchesJournalAndTimelineCitesIt()
    {
        var ct = Cancellation;
        var context = "replay-desk";
        var session = Brain.Session(context);
        var indexId = new NeuronId("journalreplayindex", context);
        var chatId = new NeuronId("replaychat", context);
        var ledgerId = new NeuronId("replaysurfaceledger", context);

        // Tuesday 2026-08-04 08:00–11:00 local as UTC anchors.
        var tue8 = new DateTimeOffset(2026, 8, 4, 8, 15, 0, TimeSpan.Zero);
        var tue9 = new DateTimeOffset(2026, 8, 4, 9, 0, 0, TimeSpan.Zero);
        var tue10 = new DateTimeOffset(2026, 8, 4, 10, 30, 0, TimeSpan.Zero);
        var outside = new DateTimeOffset(2026, 8, 4, 14, 0, 0, TimeSpan.Zero);
        var rangeStart = new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero);
        var rangeEnd = new DateTimeOffset(2026, 8, 4, 11, 0, 0, TimeSpan.Zero);

        await session.EmitAsync(new ReplayEmailLogged("Vendor invoice", tue8), ct);
        await session.EmitAsync(new ReplayTaskLogged("Prep standup notes", tue9), ct);
        await session.EmitAsync(new ReplayChatLogged("Ship the enricher", tue10), ct);
        await session.EmitAsync(new ReplayEmailLogged("Afternoon noise", outside), ct);

        var seeded = await WaitForJournalAsync(
            indexId,
            reading => reading.AllHeard<ReplayEmailLogged>().Count == 2
                && reading.AllHeard<ReplayTaskLogged>().Count == 1
                && reading.AllHeard<ReplayChatLogged>().Count == 1,
            "index heard four domain facts",
            ct);

        // Journal structure oracle (same pattern as S03).
        var fromJournal = DomainItems(seeded)
            .Where(item => item.At >= rangeStart && item.At < rangeEnd)
            .OrderBy(item => item.At)
            .ToArray();
        Assert.Equal(3, fromJournal.Length);

        await session.EmitAsync(new ReplayAsked(rangeStart, rangeEnd, "last Tuesday morning"), ct);

        var chatReading = await WaitForJournalAsync(
            chatId,
            reading => reading.AllSaid<ReplayTimelineSurfaced>().Count == 1
                && reading.AllHeard<JournalSlice>().Count == 1,
            "replay chat surfaced timeline from JournalSlice",
            ct);

        var sliceHeard = chatReading.HeardSingle<JournalSlice>();
        var slice = Assert.IsType<JournalSlice>(sliceHeard.Body);
        Assert.Equal(3, slice.Items.Length);
        Assert.Equal(fromJournal.Select(i => i.Title), slice.Items.Select(i => i.Title));
        Assert.Equal(fromJournal.Select(i => i.Kind), slice.Items.Select(i => i.Kind));

        var surfaceSaid = chatReading.SaidSingle<ReplayTimelineSurfaced>();
        Assert.Equal("declared", surfaceSaid.DeliveryTo(ledgerId).Via);
        var surface = Assert.IsType<ReplayTimelineSurfaced>(surfaceSaid.Body);
        Assert.Equal(fromJournal.Select(i => i.Title), surface.CitedTitles);
        Assert.DoesNotContain("Afternoon noise", surface.CitedTitles);

        var ledgerReading = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<ReplayTimelineSurfaced>().Count >= 1,
            "ledger heard timeline",
            ct);
        Assert.Equal(chatId, ledgerReading.AllHeard<ReplayTimelineSurfaced>()[0].Metadata.Source);

        // Session can also Ask index directly — same structure (after surface assert; extra Ask
        // must not be required for the chat→ledger path).
        var direct = await session.AskAsync<JournalSlice>(new JournalRangeQuery(rangeStart, rangeEnd), ct);
        Assert.Equal(slice.Items, direct.Items);
    }

    private static List<JournalSliceItem> DomainItems(NeuronReading reading)
    {
        var items = new List<JournalSliceItem>();
        foreach (var fact in reading.Journal.Where(f => f.Entry == "heard"))
        {
            switch (fact.Body)
            {
                case ReplayEmailLogged email:
                    items.Add(new JournalSliceItem("email", email.Subject, email.At));
                    break;
                case ReplayTaskLogged task:
                    items.Add(new JournalSliceItem("task", task.Title, task.At));
                    break;
                case ReplayChatLogged chat:
                    items.Add(new JournalSliceItem("chat", chat.Text, chat.At));
                    break;
                default:
                    break;
            }
        }

        return items;
    }
}
