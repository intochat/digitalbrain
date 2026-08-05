using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class ShellWidgetBehaviorLiveAuthorTests(BrainTestClusters clusters)
    : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<WidgetBehaviorCatalog>()
            .AddModule<BoardCountdownBinder>()
            .AddModule<ShellWidgetHost>()
            .AddModule<WidgetAuthorLedger>();

    [Fact(DisplayName =
        "Shell widget live-author: install → WidgetBehaviorActivated+WidgetBound; Board meeting calendar event → WidgetPropsPatched → WidgetRendered")]
    public async Task AuthoredBehaviorPatchesWidgetFromCalendarEvent()
    {
        var ct = Cancellation;
        var context = "home-shell";
        var session = Brain.Session(context);
        var catalogId = new NeuronId("widgetbehaviorcatalog", context);
        var binderId = new NeuronId("boardcountdownbinder", context);
        var hostId = new NeuronId("shellwidgethost", context);
        var ledgerId = new NeuronId("widgetauthorledger", context);
        var widgetId = "countdown-board";
        var behaviorId = BoardCountdownBinder.BehaviorId;

        await session.EmitAsync(
            new WidgetBehaviorInstallProposed(
                behaviorId,
                widgetId,
                TitlePattern: "Board meeting"),
            ct);

        var afterInstall = await WaitForJournalAsync(
            catalogId,
            reading => reading.AllSaid<WidgetBehaviorActivated>().Count == 1
                && reading.AllSaid<WidgetBound>().Count == 1,
            "catalog activated and bound widget",
            ct);

        await WaitForJournalAsync(
            binderId,
            reading => reading.AllHeard<WidgetBehaviorActivated>().Count == 1,
            "binder heard activation",
            ct);

        await WaitForJournalAsync(
            hostId,
            reading => reading.AllHeard<WidgetBound>().Count == 1,
            "host heard WidgetBound",
            ct);

        var activated = Assert.IsType<WidgetBehaviorActivated>(
            afterInstall.SaidSingle<WidgetBehaviorActivated>().Body);
        Assert.Equal(behaviorId, activated.BehaviorId);
        Assert.Equal(widgetId, activated.WidgetId);

        // Non-matching calendar event must not patch the widget.
        await session.EmitAsync(
            new BoardCalendarEventCreated("evt-1", "Team lunch", "2026-08-06T12:00:00Z"),
            ct);

        await Task.Delay(80, ct);
        var afterNoise = await ReadAsync(binderId, ct);
        Assert.Empty(afterNoise.AllSaid<WidgetPropsPatched>());

        await session.EmitAsync(
            new BoardCalendarEventCreated(
                "evt-board",
                "Board meeting Q3",
                "2026-08-12T15:00:00Z"),
            ct);

        var binder = await WaitForJournalAsync(
            binderId,
            reading => reading.AllSaid<WidgetPropsPatched>().Count == 1,
            "binder patched widget props for board meeting",
            ct);

        var host = await WaitForJournalAsync(
            hostId,
            reading => reading.AllHeard<WidgetPropsPatched>().Count == 1
                && reading.AllSaid<WidgetRendered>().Count == 1,
            "host rendered widget from props patch",
            ct);

        var ledger = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<WidgetBehaviorActivated>().Count == 1
                && reading.AllHeard<WidgetPropsPatched>().Count == 1
                && reading.AllHeard<WidgetRendered>().Count == 1,
            "ledger heard activate + patch + render",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var boardEventSaid = sessionReading.AllSaid<BoardCalendarEventCreated>()
            .Single(said => Assert.IsType<BoardCalendarEventCreated>(said.Body).EventId == "evt-board");

        var patchedSaid = binder.SaidSingle<WidgetPropsPatched>();
        Assert.Equal(new SynapseRef(session.Id, boardEventSaid.Position), patchedSaid.Cause);
        Assert.Equal("declared", patchedSaid.DeliveryTo(hostId).Via);
        var patched = Assert.IsType<WidgetPropsPatched>(patchedSaid.Body);
        Assert.Equal(widgetId, patched.WidgetId);
        Assert.Contains("Board meeting", patched.Title, StringComparison.Ordinal);
        Assert.Equal("high", patched.Urgency);

        var rendered = Assert.IsType<WidgetRendered>(host.SaidSingle<WidgetRendered>().Body);
        Assert.Equal(widgetId, rendered.WidgetId);
        Assert.Equal(patched.Remaining, rendered.Remaining);
        Assert.Equal(hostId, ledger.HeardSingle<WidgetRendered>().Metadata.Source);
        Assert.Equal(binderId, ledger.HeardSingle<WidgetPropsPatched>().Metadata.Source);
    }
}
