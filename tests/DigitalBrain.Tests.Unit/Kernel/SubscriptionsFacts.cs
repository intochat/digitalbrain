using DigitalBrain.Kernel;

namespace DigitalBrain.Tests.Unit.Kernel;

public sealed class SubscriptionsFacts
{
    private static readonly NeuronId Sub = NeuronId.Parse("b1/sub/1");
    private static readonly NeuronId Source1 = NeuronId.Parse("b1/src/1");
    private static readonly NeuronId Source2 = NeuronId.Parse("b1/src/2");
    private static readonly NeuronId OtherBrain = NeuronId.Parse("b2/src/1");

    [Fact]
    public void Cannot_subscribe_to_self_or_other_brain()
    {
        var subs = Subscriptions.None(Sub);
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() => subs.Add(Sub, ["a1"], new Position(0), now));
        Assert.Throws<UnauthorizedAccessException>(() => subs.Add(OtherBrain, ["a1"], new Position(0), now));
    }

    [Fact]
    public void Add_is_idempotent_and_unions_aliases()
    {
        var now = DateTimeOffset.UtcNow;
        var subs = Subscriptions.None(Sub)
            .Add(Source1, ["a1", "a2"], new Position(10), now);

        Assert.Single(subs.Entries);
        var entry = subs.Of(Source1);
        Assert.NotNull(entry);
        Assert.Equal(2, entry.Aliases.Count);
        Assert.Equal(new Position(10), entry.Cursor);

        // Add subset: no change
        var subs2 = subs.Add(Source1, ["a1"], new Position(15), now);
        Assert.Same(subs, subs2);

        // Add union
        var subs3 = subs.Add(Source1, ["a3"], new Position(15), now);
        Assert.Equal(3, subs3.Of(Source1)!.Aliases.Count);
    }

    [Fact]
    public void AddBrainWide_trims_covered_aliases_from_per_source_entries()
    {
        var now = DateTimeOffset.UtcNow;
        var subs = Subscriptions.None(Sub)
            .Add(Source1, ["a1", "a2"], new Position(1), now)
            .Add(Source2, ["a1"], new Position(2), now);

        // Brain wide takes a1
        var withWide = subs.AddBrainWide(["a1"], new Position(5), now);

        // Source2 had only a1, so it is eliminated
        Assert.Null(withWide.Of(Source2));

        // Source1 had a1 and a2, so it keeps only a2
        var s1 = withWide.Of(Source1);
        Assert.NotNull(s1);
        Assert.Single(s1.Aliases);
        Assert.Contains("a2", s1.Aliases);

        // Brain wide holds a1
        var wide = withWide.Of(null);
        Assert.NotNull(wide);
        Assert.Contains("a1", wide.Aliases);
    }

    [Fact]
    public void Advance_progresses_cursor_and_refuses_backwards_move()
    {
        var now = DateTimeOffset.UtcNow;
        var subs = Subscriptions.None(Sub).Add(Source1, ["a1"], new Position(10), now);
        var entry = subs.Of(Source1)!;

        var advanced = subs.Advance(entry, new Position(20), now);
        Assert.Equal(new Position(20), advanced.Of(Source1)!.Cursor);

        // Advance to same is idempotent
        Assert.Same(advanced, advanced.Advance(advanced.Of(Source1)!, new Position(20), now));

        // Move backwards throws
        Assert.Throws<InvalidOperationException>(() => advanced.Advance(advanced.Of(Source1)!, new Position(15), now));
    }

    [Fact]
    public void Retreat_restores_cursor_after_failed_reaction()
    {
        var now = DateTimeOffset.UtcNow;
        var subs = Subscriptions.None(Sub).Add(Source1, ["a1"], new Position(10), now);
        var initial = subs.Of(Source1)!;

        var advanced = subs.Advance(initial, new Position(20), now);
        var retreated = advanced.Retreat(initial);
        Assert.Equal(new Position(10), retreated.Of(Source1)!.Cursor);
    }

    [Fact]
    public void Encode_and_Decode_round_trips_valid_entries()
    {
        var now = DateTimeOffset.UtcNow;
        var subs = Subscriptions.None(Sub)
            .Add(Source1, ["a1", "a2"], new Position(100), now)
            .AddBrainWide(["b1"], new Position(200), now);

        var bytes = subs.Encode();
        var decoded = Subscriptions.Decode(Sub, bytes);

        Assert.Equal(subs.Subscriber, decoded.Subscriber);
        Assert.Equal(2, decoded.Entries.Count);
        Assert.Equal(new Position(100), decoded.Of(Source1)!.Cursor);
        Assert.Equal(new Position(200), decoded.Of(null)!.Cursor);
    }
}
