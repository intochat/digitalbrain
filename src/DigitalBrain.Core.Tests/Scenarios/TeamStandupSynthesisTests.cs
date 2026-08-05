using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class TeamStandupSynthesisTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<StandupBriefAssembler>()
            .AddModule<StandupTasksSource>()
            .AddModule<StandupBlockersSource>()
            .AddModule<StandupCalendarSource>()
            .AddModule<StandupDealsSource>()
            .AddModule<StandupBriefLedger>();

    [Fact(DisplayName =
        "Team standup synthesis: StandupBriefDue fans four section legs → TState join → StandupBriefBuilt")]
    public async Task FourSectionLegsJoinIntoStandupBrief()
    {
        var ct = Cancellation;
        var context = "team-alpha-standup";
        var session = Brain.Session(context);
        var assemblerId = new NeuronId("standupbriefassembler", context);
        var ledgerId = new NeuronId("standupbriefledger", context);
        var briefId = "standup-2026-08-05";
        var teamId = "team-alpha";

        await session.EmitAsync(new StandupBriefDue(briefId, teamId, Range: "yesterday"), ct);

        var assembler = await WaitForJournalAsync(
            assemblerId,
            reading => reading.AllHeard<StandupTasksReady>().Count == 1
                && reading.AllHeard<StandupBlockersReady>().Count == 1
                && reading.AllHeard<StandupCalendarReady>().Count == 1
                && reading.AllHeard<StandupDealsReady>().Count == 1
                && reading.AllSaid<StandupBriefBuilt>().Count == 1,
            "assembler joined four legs into StandupBriefBuilt",
            ct);

        var ledger = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<StandupBriefBuilt>().Count == 1,
            "ledger heard StandupBriefBuilt",
            ct);

        var built = Assert.IsType<StandupBriefBuilt>(assembler.SaidSingle<StandupBriefBuilt>().Body);
        Assert.Equal(briefId, built.BriefId);
        Assert.Equal(teamId, built.TeamId);
        Assert.Contains("Closed", built.Yesterday, StringComparison.Ordinal);
        Assert.Contains("legal", built.Blockers, StringComparison.Ordinal);
        Assert.Contains("standup", built.TodayPlan, StringComparison.Ordinal);
        Assert.Contains("Northwind", built.Deals, StringComparison.Ordinal);

        Assert.Equal(assemblerId, ledger.HeardSingle<StandupBriefBuilt>().Metadata.Source);
        NeuronId[] sectionSources =
        [
            new("standuptaskssource", context),
            new("standupblockerssource", context),
            new("standupcalendarsource", context),
            new("standupdealssource", context),
        ];
        Assert.Contains(assembler.SaidSingle<StandupBriefBuilt>().Cause!.Value.Source, sectionSources);
    }

    [Fact(DisplayName =
        "Team standup synthesis: TState join waits — three legs alone never emit StandupBriefBuilt")]
    public async Task IncompleteLegsWaitUntilFourthClosesJoin()
    {
        var ct = Cancellation;
        var context = "standup-join-waits";
        var session = Brain.Session(context);
        var assemblerId = new NeuronId("standupbriefassembler", context);
        var briefId = "standup-partial";

        // Directed pin: only assembler hears due — section mocks do not fan out.
        await session.SendAsync(
            assemblerId,
            new StandupBriefDue(briefId, TeamId: "team-beta", Range: "yesterday"),
            ct);

        await WaitForJournalAsync(
            assemblerId,
            reading => reading.AllHeard<StandupBriefDue>().Count == 1,
            "assembler heard directed StandupBriefDue",
            ct);

        await session.EmitAsync(new StandupTasksReady(briefId, "t"), ct);
        await session.EmitAsync(new StandupBlockersReady(briefId, "b"), ct);
        await session.EmitAsync(new StandupCalendarReady(briefId, "c"), ct);

        var afterThree = await WaitForJournalAsync(
            assemblerId,
            reading => reading.AllHeard<StandupTasksReady>().Count == 1
                && reading.AllHeard<StandupBlockersReady>().Count == 1
                && reading.AllHeard<StandupCalendarReady>().Count == 1,
            "three section legs heard",
            ct);

        Assert.Empty(afterThree.AllSaid<StandupBriefBuilt>());

        await session.EmitAsync(new StandupDealsReady(briefId, "d"), ct);

        var afterFour = await WaitForJournalAsync(
            assemblerId,
            reading => reading.AllSaid<StandupBriefBuilt>().Count == 1,
            "fourth leg closes join",
            ct);

        var built = Assert.IsType<StandupBriefBuilt>(afterFour.SaidSingle<StandupBriefBuilt>().Body);
        Assert.Equal("t", built.Yesterday);
        Assert.Equal("b", built.Blockers);
        Assert.Equal("c", built.TodayPlan);
        Assert.Equal("d", built.Deals);
    }
}
