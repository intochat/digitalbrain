using DigitalBrain.Kernel;

namespace DigitalBrain.Tests.Unit.Kernel;

public sealed class RetentionFacts
{
    [Fact]
    public void Floor_without_live_subscribers_retains_history_window()
    {
        // last = 100, windowSignals = 20, newerThanAge = 10 -> window = 20, floor = 100 - 20 = 80
        var floor = RetentionMath.Floor(last: 100, windowSignals: 20, newerThanAge: 10, lowestLive: null);
        Assert.Equal(80, floor);

        // When newerThanAge dominates: window = 35, floor = 100 - 35 = 65
        var floorAge = RetentionMath.Floor(last: 100, windowSignals: 20, newerThanAge: 35, lowestLive: null);
        Assert.Equal(65, floorAge);
    }

    [Fact]
    public void Floor_never_exceeds_slowest_live_subscriber_cursor()
    {
        // History window allows compaction up to 80, but slowest subscriber is at cursor 50:
        // Floor must stop at 50 so subscriber does not miss events!
        var floor = RetentionMath.Floor(last: 100, windowSignals: 20, newerThanAge: 10, lowestLive: 50);
        Assert.Equal(50, floor);
    }

    [Fact]
    public void Floor_is_unaffected_when_live_subscribers_are_ahead_of_history_window()
    {
        // History window allows compaction up to 80, and slowest subscriber is caught up at 95:
        // Floor is 80 (bounded by history window, not subscriber)
        var floor = RetentionMath.Floor(last: 100, windowSignals: 20, newerThanAge: 10, lowestLive: 95);
        Assert.Equal(80, floor);
    }

    [Fact]
    public void Floor_is_never_negative()
    {
        // last = 10, window = 50 -> last - window = -40 -> floor = 0
        var floor = RetentionMath.Floor(last: 10, windowSignals: 50, newerThanAge: 0, lowestLive: null);
        Assert.Equal(0, floor);
    }

    [Fact]
    public void LowestCursor_finds_minimum_or_null_when_empty()
    {
        Assert.Null(RetentionMath.LowestCursor([]));
        Assert.Equal(42, RetentionMath.LowestCursor([100, 42, 88, 75]));
    }

    [Fact]
    public void ComputeFloor_resolves_cursors_and_applies_policy()
    {
        var floor = RetentionMath.ComputeFloor(last: 200, windowSignals: 50, newerThanAge: 20, subscriberCursors: [180, 120, 160]);
        // window = 50 -> 200 - 50 = 150; lowest cursor is 120 -> floor is 120
        Assert.Equal(120, floor);
    }

    [Fact]
    public void Compaction_boundaries_and_retention_checks_are_consistent()
    {
        Assert.True(RetentionMath.ShouldCompact(newFloor: 80, currentFloor: 50));
        Assert.False(RetentionMath.ShouldCompact(newFloor: 50, currentFloor: 50));
        Assert.False(RetentionMath.ShouldCompact(newFloor: 40, currentFloor: 50));

        Assert.True(RetentionMath.IsRetained(entrySequence: 51, retentionFloor: 50));
        Assert.False(RetentionMath.IsRetained(entrySequence: 50, retentionFloor: 50));
        Assert.False(RetentionMath.IsRetained(entrySequence: 49, retentionFloor: 50));

        var range = RetentionMath.CompactionRange(currentFloor: 50, newFloor: 80);
        Assert.NotNull(range);
        Assert.Equal(51, range.Value.FromSequence);
        Assert.Equal(80, range.Value.ToSequence);

        Assert.Null(RetentionMath.CompactionRange(currentFloor: 50, newFloor: 50));
    }
}
