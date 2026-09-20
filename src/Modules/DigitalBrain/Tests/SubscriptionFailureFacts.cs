using DigitalBrain.Testing.Unit;
using DigitalBrain.Contracts;
using Orleans;
using Xunit;

namespace DigitalBrain.Tests;

public interface IControlledSource : INeuron
{
    Task Configure(string mode);
    Task<int> Members();
    Task<int> Cleanups();
    Task WaitForCleanup();
    Task ReleaseCleanup();
}

[GrainType("controlled-source")]
[Orleans.Concurrency.Reentrant]
public sealed class ControlledSource : Grain, IControlledSource
{
    private readonly HashSet<INeuronObserver> _members = [];
    private readonly Guid _activation = Guid.NewGuid();
    private string _mode = "";
    private int _cleanups;
    private readonly TaskCompletionSource _cleanupEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _cleanupRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task WaitForCleanup() => _cleanupEntered.Task;
    public Task ReleaseCleanup() { _cleanupRelease.TrySetResult(); return Task.CompletedTask; }
    public Task Configure(string mode) { _mode = mode; return Task.CompletedTask; }
    public Task<int> Members() => Task.FromResult(_members.Count);
    public Task<int> Cleanups() => Task.FromResult(_cleanups);
    public async Task<Guid> Watch(INeuronObserver observer)
    {
        if (_mode == "late") { await Task.Delay(300); }
        if (_mode is "watch" or "watch-fail-held-cleanup") { throw new IOException("registration refused"); }
        _members.Add(observer);
        return _activation;
    }
    public async Task Unwatch(INeuronObserver observer)
    {
        _cleanups++;
        _members.Remove(observer);
        if (_mode == "watch-fail-held-cleanup")
        {
            _cleanupEntered.TrySetResult();
            await _cleanupRelease.Task;
        }
        if (_mode == "unwatch") { throw new IOException("cleanup failed"); }
    }
}

public sealed class SubscriptionFailureFacts
{
    [Fact]
    public async Task RenewalFailureIsReportedBeforeRemoteCleanupFinishes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await SubscriptionLifetimeFacts.Options().StartAsync(ct);
        var source = brain.Get<IControlledSource>("slow-cleanup");
        await using var subscription = await brain.SubscribeAsync<Number>(source, ct);
        await source.Configure("watch-fail-held-cleanup");
        try
        {
            await source.WaitForCleanup().WaitAsync(TimeSpan.FromSeconds(3), ct);
            await Assert.ThrowsAsync<IOException>(() => subscription.Completion.WaitAsync(TimeSpan.FromMilliseconds(200), ct));
        }
        finally { await source.ReleaseCleanup(); }
    }

    [Theory]
    [InlineData("watch")]
    [InlineData("late")]
    public async Task FailedOrCanceledRegistrationIsCleanedUp(string mode)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await SubscriptionLifetimeFacts.Options().StartAsync(ct);
        var source = brain.Get<IControlledSource>("registration");
        await source.Configure(mode);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (mode == "late") { cancel.CancelAfter(50); }
        await Assert.ThrowsAnyAsync<Exception>(() => brain.SubscribeAsync<Number>(source, cancel.Token));
        Assert.Equal(0, await source.Members());
        Assert.InRange(await source.Cleanups(), 1, 2);
    }

    [Fact]
    public async Task RenewalFailureIsVisibleAndCleanupFailureDoesNotReplaceIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await SubscriptionLifetimeFacts.Options().StartAsync(ct);
        var source = brain.Get<IControlledSource>("renew-failure");
        await using var stream = await brain.SubscribeAsync<Number>(source, ct);
        await source.Configure("watch");
        await Assert.ThrowsAsync<IOException>(() => stream.Completion.WaitAsync(TimeSpan.FromSeconds(3), ct));
        await stream.DisposeAsync();
        Assert.Equal(0, await source.Members());
        await source.Configure("unwatch");
        var next = await brain.SubscribeAsync<Number>(source, ct);
        await next.DisposeAsync();
        Assert.Equal(0, await source.Members());
    }
}
