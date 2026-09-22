using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class BehaviorReadinessFacts
{
    [Fact]
    public async Task RequiredSubscriptionLossNotifiesTheHost()
    {
        var readiness = new BehaviorReadiness();
        var generation = readiness.Begin("behavior", [new("timer", typeof(string))]);
        readiness.SubscriptionReady(generation, "timer", typeof(string));
        var lost = readiness.WaitForLossAsync(generation);
        Assert.False(lost.IsCompleted);
        readiness.SubscriptionClosed(generation, "timer", typeof(string));
        await lost.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.False(readiness.IsReady);
    }

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
