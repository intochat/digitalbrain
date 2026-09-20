using DigitalBrain.Testing.Unit;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans;
using Orleans.Concurrency;
using Xunit;
namespace DigitalBrain.Tests;

public interface ILateSource : INeuron
{
    Task HoldNextWatch();
    Task WaitForHeldWatch();
    Task ReleaseWatch();
    Task WaitForCompletedWatch();
    Task<int> Members();
}

[Reentrant, GrainType("late-source")]
public sealed class LateSource : Grain, ILateSource
{
    private readonly HashSet<INeuronObserver> _members = [];
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Guid _activation = Guid.NewGuid();
    private bool _hold;
    public Task HoldNextWatch() { _hold = true; return Task.CompletedTask; }
    public Task WaitForHeldWatch() => _entered.Task;
    public Task ReleaseWatch() { _release.TrySetResult(); return Task.CompletedTask; }
    public Task WaitForCompletedWatch() => _completed.Task;
    public Task<int> Members() => Task.FromResult(_members.Count);
    public async Task<Guid> Watch(INeuronObserver observer)
    {
        if (_hold)
        {
            _hold = false;
            _entered.TrySetResult();
            await _release.Task;
            _members.Add(observer);
            _completed.TrySetResult();
        }
        else { _members.Add(observer); }
        return _activation;
    }
    public Task Unwatch(INeuronObserver observer) { _members.Remove(observer); return Task.CompletedTask; }
}

public sealed class LateRegistrationFacts
{
    [Fact]
    public async Task ClientDisposalOwnsAnUnfinishedRegistration()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(SubscriptionLifetimeFacts.Options(), ct);
        var source = brain.Get<ILateSource>("closing-client");
        await source.HoldNextWatch();
        var subscribing = brain.SubscribeAsync<Number>(source, ct);
        try
        {
            await source.WaitForHeldWatch().WaitAsync(TimeSpan.FromSeconds(3), ct);
            await brain.Client().DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3), ct);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => subscribing);
            await source.ReleaseWatch();
            await source.WaitForCompletedWatch().WaitAsync(TimeSpan.FromSeconds(3), ct);
            await TestWait.UntilAsync(_ => source.Members(), count => count == 0, TimeSpan.FromSeconds(1), ct);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => brain.SubscribeAsync<Number>(source, ct));
        }
        finally { await source.ReleaseWatch(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateInitialOrRenewedWatchIsRemovedAfterDisposal(bool renewal)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(SubscriptionLifetimeFacts.Options(), ct);
        var source = brain.Get<ILateSource>("late");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ISignalSubscription<Number>? stream = renewal ? await brain.SubscribeAsync<Number>(source, ct) : null;
        await source.HoldNextWatch();
        var subscribing = renewal ? null : brain.SubscribeAsync<Number>(source, cancel.Token);
        try
        {
            await source.WaitForHeldWatch().WaitAsync(TimeSpan.FromSeconds(3), ct);
            if (stream is not null) { await stream.DisposeAsync(); }
            else
            {
                cancel.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => subscribing!);
            }
            Assert.Equal(0, await source.Members());
            await source.ReleaseWatch();
            await source.WaitForCompletedWatch().WaitAsync(TimeSpan.FromSeconds(3), ct);
            await TestWait.UntilAsync(_ => source.Members(), count => count == 0, TimeSpan.FromSeconds(1), ct);
        }
        finally { await source.ReleaseWatch(); if (stream is not null) { await stream.DisposeAsync(); } }
    }
}
