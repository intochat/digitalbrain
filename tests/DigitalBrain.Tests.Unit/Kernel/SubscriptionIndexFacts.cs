using DigitalBrain.Kernel;

namespace DigitalBrain.Tests.Unit.Kernel;

public sealed class SubscriptionIndexFacts
{
    private static readonly NeuronId Sub1 = NeuronId.Parse("b1/sub/1");
    private static readonly NeuronId Sub2 = NeuronId.Parse("b1/sub/2");
    private static readonly NeuronId Pub1 = NeuronId.Parse("b1/pub/1");
    private static readonly NeuronId Pub2 = NeuronId.Parse("b1/pub/2");

    [Fact]
    public void Fold_and_Hearing_matches_subscribers()
    {
        var index = new SubscriptionIndex();
        var now = DateTimeOffset.UtcNow;

        var subs1 = Subscriptions.None(Sub1).Add(Pub1, ["alias.a", "alias.b"], new Position(0), now);
        index.Fold(Sub1, subs1.Encode(), StateChange.Write, "owner1");

        var subs2 = Subscriptions.None(Sub2).AddBrainWide(["alias.c"], new Position(0), now);
        index.Fold(Sub2, subs2.Encode(), StateChange.Write, "owner2");

        Assert.Equal("owner1", index.OwnerOf(Sub1));
        Assert.Equal("owner2", index.OwnerOf(Sub2));

        var hearsA = index.Hearing(Pub1, ["alias.a"], _ => null).ToList();
        Assert.Single(hearsA);
        Assert.Equal(Sub1, hearsA[0]);

        var hearsC = index.Hearing(Pub1, ["alias.c"], _ => null).ToList();
        Assert.Single(hearsC);
        Assert.Equal(Sub2, hearsC[0]);

        var hearsNone = index.Hearing(Pub2, ["alias.a"], _ => null).ToList();
        Assert.Empty(hearsNone);

        // Clear Sub1
        index.Fold(Sub1, ReadOnlyMemory<byte>.Empty, StateChange.Clear);
        Assert.Empty(index.Hearing(Pub1, ["alias.a"], _ => null));
        Assert.Null(index.OwnerOf(Sub1));
    }

    [Fact]
    public void LowestLive_finds_minimum_cursor_within_time_threshold()
    {
        var index = new SubscriptionIndex();
        var now = DateTimeOffset.UtcNow;

        var subs1 = Subscriptions.None(Sub1).Add(Pub1, ["a"], new Position(100), now);
        var subs2 = Subscriptions.None(Sub2).Add(Pub1, ["a"], new Position(50), now - TimeSpan.FromDays(10)); // older than threshold

        index.Fold(Sub1, subs1.Encode(), StateChange.Write);
        index.Fold(Sub2, subs2.Encode(), StateChange.Write);

        var cutoff = now - TimeSpan.FromDays(2);
        var lowest = index.LowestLive(Pub1, cutoff);
        Assert.Equal(new Position(100), lowest);
    }
}
