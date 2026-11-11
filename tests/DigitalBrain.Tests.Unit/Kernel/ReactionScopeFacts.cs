using DigitalBrain.Kernel;

namespace DigitalBrain.Tests.Unit.Kernel;

public sealed class ReactionScopeFacts
{
    private static readonly NeuronId Subscriber1 = NeuronId.Parse("b1/type/1");
    private static readonly NeuronId Subscriber2 = NeuronId.Parse("b1/type/2");

    [Fact]
    public void Open_and_Of_resolves_current_subscriber_scope()
    {
        Assert.Null(ReactionScope.Of(Subscriber1));

        using var scope = ReactionScope.Open(Subscriber1, version => new CursorMove("subscriptions", ReadOnlyMemory<byte>.Empty, version), version: 5);
        Assert.NotNull(ReactionScope.Of(Subscriber1));
        Assert.Null(ReactionScope.Of(Subscriber2));
        Assert.Equal(5, scope.Version);
        Assert.False(scope.Carried);
    }

    [Fact]
    public void Carry_updates_version_and_flags_carried()
    {
        var carriedFired = false;
        using var scope = ReactionScope.Open(Subscriber1, version => new CursorMove("subscriptions", ReadOnlyMemory<byte>.Empty, version), version: 5, carried: () => carriedFired = true);

        scope.Carry(10);
        Assert.True(scope.Carried);
        Assert.Equal(10, scope.Version);
        Assert.True(carriedFired);
    }

    [Fact]
    public void Disposed_nested_scope_propagates_carried_version_to_matching_outer_scope()
    {
        using var outer = ReactionScope.Open(Subscriber1, v => new CursorMove("subscriptions", ReadOnlyMemory<byte>.Empty, v), version: 1);
        {
            using var inner = ReactionScope.Open(Subscriber1, v => new CursorMove("subscriptions", ReadOnlyMemory<byte>.Empty, v), version: 1);
            inner.Carry(5);
        }

        Assert.True(outer.Carried);
        Assert.Equal(5, outer.Version);
    }

    [Fact]
    public void Disposed_nested_scope_does_not_propagate_to_different_subscriber_outer_scope()
    {
        using var outer = ReactionScope.Open(Subscriber1, v => new CursorMove("subscriptions", ReadOnlyMemory<byte>.Empty, v), version: 1);
        {
            using var inner = ReactionScope.Open(Subscriber2, v => new CursorMove("subscriptions", ReadOnlyMemory<byte>.Empty, v), version: 1);
            inner.Carry(5);
        }

        Assert.False(outer.Carried);
        Assert.Equal(1, outer.Version);
    }
}
