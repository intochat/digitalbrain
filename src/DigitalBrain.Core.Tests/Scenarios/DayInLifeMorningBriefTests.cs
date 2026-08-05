using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class DayInLifeMorningBriefTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<MorningBriefAssembler>()
            .AddModule<WeatherMock>()
            .AddModule<CalendarMock>()
            .AddModule<InboxMock>()
            .AddModule<PortfolioMock>()
            .AddModule<MorningBriefLedger>();

    [Fact(DisplayName =
        "Day-in-life morning brief: WeatherReady+CalendarReady+InboxReady+PortfolioReady join in TState → MorningBriefReady with all four sections")]
    public async Task FourLegsJoinIntoMorningBriefReady()
    {
        var ct = Cancellation;
        var context = "morning-2026-08-05";
        var session = Brain.Session(context);
        var assemblerId = new NeuronId("morningbriefassembler", context);
        var ledgerId = new NeuronId("morningbriefledger", context);
        var weatherId = new NeuronId("weathermock", context);
        var calendarId = new NeuronId("calendarmock", context);
        var inboxId = new NeuronId("inboxmock", context);
        var portfolioId = new NeuronId("portfoliomock", context);
        var briefId = "brief-morning-1";

        await session.EmitAsync(new MorningBriefRequested(briefId), ct);

        var assemblerReading = await WaitForJournalAsync(
            assemblerId,
            reading => reading.AllSaid<MorningBriefReady>().Count == 1
                && reading.AllHeard<WeatherReady>().Count == 1
                && reading.AllHeard<CalendarReady>().Count == 1
                && reading.AllHeard<InboxReady>().Count == 1
                && reading.AllHeard<PortfolioReady>().Count == 1,
            "assembler heard four section legs and said MorningBriefReady",
            ct);

        var ledgerReading = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<MorningBriefReady>().Count == 1,
            "ledger heard MorningBriefReady",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var requestedSaid = sessionReading.SaidSingle<MorningBriefRequested>();
        Assert.Equal("declared", requestedSaid.DeliveryTo(assemblerId).Via);
        Assert.Equal("declared", requestedSaid.DeliveryTo(weatherId).Via);
        Assert.Equal("declared", requestedSaid.DeliveryTo(calendarId).Via);
        Assert.Equal("declared", requestedSaid.DeliveryTo(inboxId).Via);
        Assert.Equal("declared", requestedSaid.DeliveryTo(portfolioId).Via);

        Assert.Equal(session.Id, assemblerReading.HeardSingle<MorningBriefRequested>().Metadata.Source);

        var weatherSaid = (await ReadAsync(weatherId, ct)).SaidSingle<WeatherReady>();
        var calendarSaid = (await ReadAsync(calendarId, ct)).SaidSingle<CalendarReady>();
        var inboxSaid = (await ReadAsync(inboxId, ct)).SaidSingle<InboxReady>();
        var portfolioSaid = (await ReadAsync(portfolioId, ct)).SaidSingle<PortfolioReady>();

        Assert.Equal(new SynapseRef(session.Id, requestedSaid.Position), weatherSaid.Cause);
        Assert.Equal(new SynapseRef(session.Id, requestedSaid.Position), calendarSaid.Cause);
        Assert.Equal(new SynapseRef(session.Id, requestedSaid.Position), inboxSaid.Cause);
        Assert.Equal(new SynapseRef(session.Id, requestedSaid.Position), portfolioSaid.Cause);
        Assert.Equal("declared", weatherSaid.DeliveryTo(assemblerId).Via);
        Assert.Equal("declared", calendarSaid.DeliveryTo(assemblerId).Via);
        Assert.Equal("declared", inboxSaid.DeliveryTo(assemblerId).Via);
        Assert.Equal("declared", portfolioSaid.DeliveryTo(assemblerId).Via);

        Assert.Equal("Clear, 18C", Assert.IsType<WeatherReady>(
            assemblerReading.HeardSingle<WeatherReady>().Body).Summary);
        Assert.Equal("09:00 standup; 14:00 design review", Assert.IsType<CalendarReady>(
            assemblerReading.HeardSingle<CalendarReady>().Body).Summary);
        Assert.Equal("2 VIP threads", Assert.IsType<InboxReady>(
            assemblerReading.HeardSingle<InboxReady>().Body).Summary);
        Assert.Equal("BTC +1.2%; cash steady", Assert.IsType<PortfolioReady>(
            assemblerReading.HeardSingle<PortfolioReady>().Body).Summary);

        var readySaid = assemblerReading.SaidSingle<MorningBriefReady>();
        Assert.Equal("declared", readySaid.DeliveryTo(ledgerId).Via);
        var ready = Assert.IsType<MorningBriefReady>(readySaid.Body);
        Assert.Equal(briefId, ready.BriefId);
        Assert.Equal("Clear, 18C", ready.Weather);
        Assert.Equal("09:00 standup; 14:00 design review", ready.Calendar);
        Assert.Equal("2 VIP threads", ready.Inbox);
        Assert.Equal("BTC +1.2%; cash steady", ready.Portfolio);

        // Cause of Ready is the last section leg that closed the TState join.
        Assert.NotNull(readySaid.Cause);
        NeuronId[] sectionSources = [weatherId, calendarId, inboxId, portfolioId];
        Assert.Contains(readySaid.Cause.Value.Source, sectionSources);

        Assert.Equal(assemblerId, ledgerReading.HeardSingle<MorningBriefReady>().Metadata.Source);
        Assert.Equal(readySaid.Position, ledgerReading.HeardSingle<MorningBriefReady>().Metadata.Sequence);
    }

    [Fact(DisplayName =
        "Day-in-life morning brief: TState join waits — three section legs alone never emit MorningBriefReady; fourth leg closes")]
    public async Task IncompleteLegsWaitUntilFourthClosesJoin()
    {
        var ct = Cancellation;
        var context = "morning-join-waits";
        var session = Brain.Session(context);
        var assemblerId = new NeuronId("morningbriefassembler", context);
        var briefId = "brief-partial";

        // Directed pin: only the assembler hears the request — mocks do not fan out section legs.
        await session.SendAsync(assemblerId, new MorningBriefRequested(briefId), ct);

        await WaitForJournalAsync(
            assemblerId,
            reading => reading.AllHeard<MorningBriefRequested>().Count == 1,
            "assembler heard directed MorningBriefRequested",
            ct);

        await session.EmitAsync(new WeatherReady(briefId, "Fog"), ct);
        await session.EmitAsync(new CalendarReady(briefId, "No meetings"), ct);
        await session.EmitAsync(new InboxReady(briefId, "0 urgent"), ct);

        var afterThree = await WaitForJournalAsync(
            assemblerId,
            reading => reading.AllHeard<WeatherReady>().Count == 1
                && reading.AllHeard<CalendarReady>().Count == 1
                && reading.AllHeard<InboxReady>().Count == 1,
            "assembler heard three section legs",
            ct);

        Assert.Empty(afterThree.AllSaid<MorningBriefReady>());
        Assert.Empty(afterThree.AllHeard<PortfolioReady>());

        await session.EmitAsync(new PortfolioReady(briefId, "flat"), ct);

        var afterFour = await WaitForJournalAsync(
            assemblerId,
            reading => reading.AllHeard<PortfolioReady>().Count == 1
                && reading.AllSaid<MorningBriefReady>().Count == 1,
            "fourth leg closes TState join → MorningBriefReady",
            ct);

        var ready = Assert.IsType<MorningBriefReady>(afterFour.SaidSingle<MorningBriefReady>().Body);
        Assert.Equal(briefId, ready.BriefId);
        Assert.Equal("Fog", ready.Weather);
        Assert.Equal("No meetings", ready.Calendar);
        Assert.Equal("0 urgent", ready.Inbox);
        Assert.Equal("flat", ready.Portfolio);
    }
}
