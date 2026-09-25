using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Xunit;

namespace DigitalBrain.Tests;

public interface IActivityCaller : INeuron
{
    Task<int> Run();
    Task Fail();
}

public interface IActivityTarget : INeuron
{
    Task<int> Read();
    Task Fail();
}

public sealed record ActivityTestSignal : Signal;

[GrainType("activity-caller")]
public sealed class ActivityCaller(IGrainFactory grains) : Neuron, IActivityCaller
{
    public async Task<int> Run()
    {
        var value = await grains.GetGrain<IActivityTarget>("target").Read();
        await PublishAsync(new ActivityTestSignal());
        return value;
    }

    public Task Fail() => grains.GetGrain<IActivityTarget>("target").Fail();
}

[GrainType("activity-target")]
public sealed class ActivityTarget : Neuron, IActivityTarget
{
    public Task<int> Read() => Task.FromResult(42);
    public Task Fail() => throw new InvalidOperationException("expected test failure");
}

public sealed class NeuronActivityFacts
{
    [Fact]
    public async Task ScopedCallAndSignalAreObservedWithoutValues()
    {
        var feed = new ActivityFeed();
        await using var brain = await UnitTest.Create()
            .ConfigureSilo(silo => silo.Services.AddSingleton(feed))
            .ConfigureClient(client => client.Services.AddSingleton(feed))
            .StartAsync(TestContext.Current.CancellationToken);
        using (IntentContext.Begin("intent", "workspace-one"))
        {
            Assert.Equal(42, await brain.Get<IActivityCaller>("caller").Run());
        }

        var events = feed.Snapshot("workspace-one").Events;
        Assert.Equal(42, await brain.Get<IActivityCaller>("caller").Run());
        Assert.Equal(events.Count, feed.Snapshot("workspace-one").Events.Count);
        Assert.Contains(events, e => e.Kind == NeuronActivityKind.SignalPublished && e.Type == nameof(ActivityTestSignal));
        Assert.Contains(events, e => e.Kind == NeuronActivityKind.CallStarted && e.SourceId?.Contains("caller") == true && e.TargetId?.Contains("target") == true);
        Assert.Contains(events, e => e.Kind == NeuronActivityKind.CallArrived && e.TargetId?.Contains("target") == true);
        var json = System.Text.Json.JsonSerializer.Serialize(events);
        Assert.DoesNotContain("Payload", json);
        Assert.DoesNotContain("Result", json);
        Assert.Empty(feed.Snapshot("workspace-two").Events);
    }

    [Fact]
    public async Task FailureIsObservedAndOriginalExceptionPropagates()
    {
        var feed = new ActivityFeed();
        await using var brain = await UnitTest.Create()
            .ConfigureSilo(silo => silo.Services.AddSingleton(feed))
            .ConfigureClient(client => client.Services.AddSingleton(feed))
            .StartAsync(TestContext.Current.CancellationToken);
        using (IntentContext.Begin("intent", "workspace-one"))
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => brain.Get<IActivityCaller>("caller").Fail());
            Assert.Equal("expected test failure", error.Message);
        }
        Assert.Contains(feed.Snapshot("workspace-one").Events, e => e.Kind == NeuronActivityKind.CallFailed && e.FailureCode == nameof(InvalidOperationException));
    }
}
