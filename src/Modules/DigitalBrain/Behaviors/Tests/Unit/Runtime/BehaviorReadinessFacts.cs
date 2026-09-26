using DigitalBrain.Core;
using DigitalBrain.Contracts;
using DigitalBrain.Behavior;
using Orleans;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class BehaviorReadinessFacts
{
    [Fact]
    public async Task InstalledSynapseResolvesADeclaredAccountSlotToTheInstallersSelection()
    {
        var slot = "account" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable("Behavior__Account__" + slot, "bob-twitter");
        var inner = new CapturingBrain();
        var readiness = new BehaviorReadiness();
        await using var scoped = new BehaviorScopedBrain(inner, readiness, readiness.Begin("synapse", []));
        try
        {
            Assert.Throws<NotSupportedException>(() => scoped.Get<IBehaviorProgram>(slot));
            Assert.Equal("bob-twitter", inner.LastId);
        }
        finally { Environment.SetEnvironmentVariable("Behavior__Account__" + slot, null); }
    }

    private sealed class CapturingBrain : IDigitalBrain
    {
        public string? LastId { get; private set; }
        public T Get<T>(string id) where T : class, IGrainWithStringKey
        {
            LastId = id;
            throw new NotSupportedException();
        }
        public Task<ISignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal
            => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

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
