using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class WhatDidIDoLastWeekTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain.AddModule<WeekRecall>();

    [Fact(DisplayName = "What did I do last week? - timeline from journaled domain facts")]
    public async Task WeekSummaryItemsMatchPriorJournalStructure()
    {
        var ct = Cancellation;
        var context = "owner-desk";
        var session = Brain.Session(context);
        var recallId = new NeuronId("weekrecall", context);
        var rangeStart = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        var rangeEnd = new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero);

        var emailAt = new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);
        var meetingAt = new DateTimeOffset(2026, 7, 30, 15, 0, 0, TimeSpan.Zero);
        var taskAt = new DateTimeOffset(2026, 8, 1, 9, 30, 0, TimeSpan.Zero);
        var outsideAt = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

        await session.EmitAsync(new WeekEmailLogged("Re: Acme intro", emailAt), ct);
        await session.EmitAsync(new WeekMeetingLogged("Pipeline review", meetingAt), ct);
        await session.EmitAsync(new WeekTaskLogged("Ship enricher proof", taskAt), ct);
        await session.EmitAsync(new WeekEmailLogged("Older thread", outsideAt), ct);

        var seeded = await WaitForJournalAsync(
            recallId,
            reading => DomainHeardCount(reading) == 4,
            "four domain facts heard on week recall",
            ct);

        // Brain.ReadAsync / journal structure is the source of truth for what the owner "did".
        var journalItems = DomainItemsFromJournal(seeded);
        Assert.Equal(4, journalItems.Count);
        Assert.Contains(journalItems, item => item is { Kind: "email", Title: "Re: Acme intro" });
        Assert.Contains(journalItems, item => item is { Kind: "meeting", Title: "Pipeline review" });
        Assert.Contains(journalItems, item => item is { Kind: "task", Title: "Ship enricher proof" });
        Assert.Contains(journalItems, item => item is { Kind: "email", Title: "Older thread" });

        var summary = await session.AskAsync<WeekSummary>(
            new WeekSummaryAsked(rangeStart, rangeEnd),
            ct);

        Assert.Equal(3, summary.Items.Length);
        WeekItem[] expectedInRange =
        [
            new WeekItem("email", "Re: Acme intro", emailAt),
            new WeekItem("meeting", "Pipeline review", meetingAt),
            new WeekItem("task", "Ship enricher proof", taskAt),
        ];
        Assert.Equal(expectedInRange, summary.Items.ToArray());

        var afterAsk = await ReadAsync(recallId, ct);
        var inRangeFromJournal = DomainItemsFromJournal(afterAsk)
            .Where(item => item.At >= rangeStart && item.At < rangeEnd)
            .OrderBy(item => item.At)
            .ToArray();
        Assert.Equal(inRangeFromJournal.Length, summary.Items.Length);
        Assert.Equal(inRangeFromJournal, summary.Items.ToArray());

        var sessionReading = await ReadAsync(session.Id, ct);
        Assert.Equal(4, sessionReading.AllSaid<WeekEmailLogged>().Count
            + sessionReading.AllSaid<WeekMeetingLogged>().Count
            + sessionReading.AllSaid<WeekTaskLogged>().Count);

        var askSaid = sessionReading.SaidSingle<WeekSummaryAsked>();
        Assert.Equal("ask", askSaid.DeliveryTo(recallId).Via);

        var answerSaid = afterAsk.SaidSingle<WeekSummary>();
        Assert.Equal(new SynapseRef(session.Id, askSaid.Position), answerSaid.Answers);
        Assert.Equal(3, Assert.IsType<WeekSummary>(answerSaid.Body).Items.Length);
    }

    private static int DomainHeardCount(NeuronReading reading)
        => reading.AllHeard<WeekEmailLogged>().Count
            + reading.AllHeard<WeekMeetingLogged>().Count
            + reading.AllHeard<WeekTaskLogged>().Count;

    private static List<WeekItem> DomainItemsFromJournal(NeuronReading reading)
    {
        var items = new List<WeekItem>();
        foreach (var fact in reading.Journal)
        {
            if (fact.Entry != "heard")
            {
                continue;
            }

            switch (fact.Body)
            {
                case WeekEmailLogged email:
                    items.Add(new WeekItem("email", email.Subject, email.At));
                    break;
                case WeekMeetingLogged meeting:
                    items.Add(new WeekItem("meeting", meeting.Title, meeting.At));
                    break;
                case WeekTaskLogged task:
                    items.Add(new WeekItem("task", task.Title, task.At));
                    break;
                default:
                    break;
            }
        }

        return items;
    }
}
