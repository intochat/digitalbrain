using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class SubscriptionLifetimeFacts
{
    internal static BrainTestOptions Options(int capacity = 256) => new()
    {
        ConfigureSilo = silo => silo.Services.Configure<BrainOptions>(Configure),
        ConfigureClient = client => client.Services.Configure<BrainOptions>(options => { Configure(options); options.BufferCapacity = capacity; }),
    };
    private static void Configure(BrainOptions options)
    {
        options.ObserverLease = TimeSpan.FromSeconds(2);
        options.RenewEvery = TimeSpan.FromMilliseconds(200);
        options.OperationTimeout = TimeSpan.FromMilliseconds(500);
    }

    [Fact]
    public async Task OverflowIsVisibleEvenWithoutDrainingTheBuffer()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await BrainTestHost.StartAsync(Options(1), ct);
        var source = host.Brain.Get<ITestEmitter>("overflow");
        await using var stream = await host.Brain.SubscribeAsync<Number>(source, ct);
        await source.Emit(1);
        await source.Emit(2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => stream.Completion.WaitAsync(TimeSpan.FromSeconds(3), ct));
    }

    [Fact]
    public async Task CancellationEndsAnIdleSubscriptionAndDisposalIsIdempotent()
    {
        await using var host = await BrainTestHost.StartAsync(Options(), TestContext.Current.CancellationToken);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var source = host.Brain.Get<ITestEmitter>("cancel");
        var stream = await host.Brain.SubscribeAsync<Number>(source, cancel.Token);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.Completion.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        await stream.DisposeAsync();
        await stream.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Brain.SubscribeAsync<Number>(source, cancel.Token));
    }

    [Fact]
    public async Task RenewalKeepsAHealthySubscriptionAliveBeyondItsLease()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await BrainTestHost.StartAsync(Options(), ct);
        var source = host.Brain.Get<ITestEmitter>("renew");
        await using var stream = await host.Brain.SubscribeAsync<Number>(source, ct);
        await Task.Delay(TimeSpan.FromSeconds(3), ct);
        await source.Emit(19);
        await using var reader = stream.ReadAllAsync(ct).GetAsyncEnumerator(ct);
        Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3), ct));
        Assert.Equal(19, reader.Current.Value);
    }

    [Fact]
    public async Task ReactivationFaultsTheOldSubscriptionAndANewOneCanObserve()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await BrainTestHost.StartAsync(Options(), ct);
        var source = host.Brain.Get<ITestEmitter>("restart");
        await using var old = await host.Brain.SubscribeAsync<Number>(source, ct);
        await source.Deactivate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => old.Completion.WaitAsync(TimeSpan.FromSeconds(5), ct));
        await using var fresh = await host.Brain.SubscribeAsync<Number>(source, ct);
        await source.Emit(8);
        await using var reader = fresh.ReadAllAsync(ct).GetAsyncEnumerator(ct);
        Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3), ct));
        Assert.Equal(8, reader.Current.Value);
    }
}

