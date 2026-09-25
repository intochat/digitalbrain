using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ActivityFeedFacts
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ActivityEvent Event(string scope, DateTimeOffset at) => new(
        scope, 0, Guid.NewGuid(), Guid.NewGuid(), null, at,
        NeuronActivityKind.CallStarted, "source", "target", "Read", "started", null, null);

    [Fact]
    public void RetainsNewestTwoThousandAndIsolatesScopes()
    {
        var clock = new Clock();
        var feed = new ActivityFeed(clock);
        for (var i = 0; i < 2001; i++) { feed.Append(Event("one", clock.Now)); }
        feed.Append(Event("two", clock.Now));

        var snapshot = feed.Snapshot("one", 0);
        Assert.Equal(2000, snapshot.Events.Count);
        Assert.True(snapshot.Gap);
        Assert.Equal(2, snapshot.Events[0].Sequence);
        Assert.Equal(2001, snapshot.NextSequence);
        Assert.All(snapshot.Events, item => Assert.Equal("one", item.ScopeId));
        Assert.Single(feed.Snapshot("two").Events);
    }

    [Fact]
    public void ExpiresEventsOlderThanFifteenMinutes()
    {
        var clock = new Clock();
        var feed = new ActivityFeed(clock);
        feed.Append(Event("one", clock.Now));
        clock.Now += TimeSpan.FromMinutes(15);
        feed.Append(Event("one", clock.Now));
        Assert.Equal(2, feed.Snapshot("one").Events.Count);
        clock.Now += TimeSpan.FromTicks(1);
        Assert.Single(feed.Snapshot("one").Events);
    }

    [Fact]
    public void InvalidatesCursorFromPreviousProcessAndReportsTruncation()
    {
        var clock = new Clock();
        var feed = new ActivityFeed(clock);
        Assert.True(feed.Snapshot("one", 500).Gap);
        for (var i = 0; i < 2001; i++) { feed.Append(Event("one", clock.Now)); }
        Assert.True(feed.Snapshot("one").Gap);
        Assert.NotEqual(feed.Generation, new ActivityFeed(clock).Generation);
    }

    [Fact]
    public void ManyScopesDoNotGrowTheRegistryWithoutBound()
    {
        var clock = new Clock();
        var feed = new ActivityFeed(clock);
        var originalGeneration = feed.Generation;
        for (var i = 0; i < 5000; i++) { Assert.Empty(feed.Snapshot($"empty-{i}").Events); }
        for (var i = 0; i < 500; i++) { feed.Append(Event($"active-{i}", clock.Now)); }
        Assert.NotEqual(originalGeneration, feed.Generation);
        Assert.True(feed.Snapshot("active-0", 1).Gap);
        Assert.Single(feed.Snapshot("active-499").Events);
    }
}
