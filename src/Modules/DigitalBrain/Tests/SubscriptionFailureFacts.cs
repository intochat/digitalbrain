using DigitalBrain.Contracts;
using Orleans;
using Xunit;

namespace DigitalBrain.Tests;

public interface IControlledSource : INeuron
{
    Task Configure(string mode);
    Task<int> Members();
    Task<int> Cleanups();
}

[GrainType("controlled-source")]
public sealed class ControlledSource : Grain, IControlledSource
{
    private readonly HashSet<INeuronObserver> _members = [];
    private readonly Guid _activation = Guid.NewGuid();
    private string _mode = "";
    private int _cleanups;
    public Task Configure(string mode) { _mode = mode; return Task.CompletedTask; }
    public Task<int> Members() => Task.FromResult(_members.Count);
    public Task<int> Cleanups() => Task.FromResult(_cleanups);
    public async Task<Guid> Watch(INeuronObserver observer)
    {
        if (_mode == "late") { await Task.Delay(300); }
        if (_mode == "watch") { throw new IOException("registration refused"); }
        _members.Add(observer);
        return _activation;
    }
    public Task Unwatch(INeuronObserver observer)
    {
        _cleanups++;
        _members.Remove(observer);
        if (_mode == "unwatch") { throw new IOException("cleanup failed"); }
        return Task.CompletedTask;
    }
}

public sealed class SubscriptionFailureFacts
{
    [Theory]
    [InlineData("watch")]
    [InlineData("late")]
    public async Task FailedOrCanceledRegistrationIsCleanedUp(string mode)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await BrainTestHost.StartAsync(SubscriptionLifetimeFacts.Options(), ct);
        var source = host.Brain.Get<IControlledSource>("registration");
        await source.Configure(mode);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (mode == "late") { cancel.CancelAfter(50); }
        await Assert.ThrowsAnyAsync<Exception>(() => host.Brain.SubscribeAsync<Number>(source, cancel.Token));
        Assert.Equal(0, await source.Members());
        Assert.Equal(1, await source.Cleanups());
    }

    [Fact]
    public async Task RenewalFailureIsVisibleAndCleanupFailureDoesNotReplaceIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await BrainTestHost.StartAsync(SubscriptionLifetimeFacts.Options(), ct);
        var source = host.Brain.Get<IControlledSource>("renew-failure");
        await using var stream = await host.Brain.SubscribeAsync<Number>(source, ct);
        await source.Configure("watch");
        await Assert.ThrowsAsync<IOException>(() => stream.Completion.WaitAsync(TimeSpan.FromSeconds(3), ct));
        Assert.Equal(0, await source.Members());
        await source.Configure("unwatch");
        var next = await host.Brain.SubscribeAsync<Number>(source, ct);
        await next.DisposeAsync();
        Assert.Equal(0, await source.Members());
    }
}
