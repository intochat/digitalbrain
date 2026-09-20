using Xunit;
using DigitalBrain.Core;

namespace DigitalBrain.Tests;

public sealed class BehaviorReadinessFacts
{
    [Fact]
    public void OldGenerationsCannotMakeARestartedBehaviorReady()
    {
        var status = new BehaviorReadiness();
        SubscriptionRequirement[] requirements = [new("twitter/elon", typeof(string))];
        var first = status.Begin("elon", requirements);
        var second = status.Begin("elon", requirements);
        status.SubscriptionReady(first, "twitter/elon", typeof(string));
        Assert.False(status.IsReady);
        status.SubscriptionReady(second, "twitter/elon", typeof(string));
        Assert.True(status.IsReady);
        status.End(second);
        Assert.False(status.IsReady);
    }

    [Fact]
    public void EveryRequiredSubscriptionMustBeReady()
    {
        var status = new BehaviorReadiness();
        var generation = status.Begin("test", [new("one", typeof(string)), new("two", typeof(int))]);
        status.SubscriptionReady(generation, "one", typeof(string));
        Assert.False(status.IsReady);
        status.SubscriptionReady(generation, "two", typeof(int));
        Assert.True(status.IsReady);
        status.SubscriptionClosed(generation, "one", typeof(string));
        Assert.False(status.IsReady);
    }
}
