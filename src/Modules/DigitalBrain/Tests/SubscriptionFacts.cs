using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class SubscriptionFacts
{
    [Fact]
    public async Task ReadySubscribersHearOnePublication()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await BrainTestHost.StartAsync(cancellationToken: ct);
        var source = host.Brain.Get<ITestEmitter>("source");
        await using var first = await host.Brain.SubscribeAsync<Number>(source, ct);
        await using var second = await host.Brain.SubscribeAsync<Number>(source, ct);
        await using var a = first.ReadAllAsync(ct).GetAsyncEnumerator(ct);
        await using var b = second.ReadAllAsync(ct).GetAsyncEnumerator(ct);
        await source.Emit(42);
        Assert.True(await a.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct));
        Assert.True(await b.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct));
        Assert.Equal(42, a.Current.Value);
        Assert.Equal(42, b.Current.Value);
    }

    [Fact]
    public async Task TypeAndFullGrainIdentityIsolateSubscribers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await BrainTestHost.StartAsync(cancellationToken: ct);
        var source = host.Brain.Get<ITestEmitter>("same");
        var other = host.Brain.Get<IOtherEmitter>("same");
        await using var stream = await host.Brain.SubscribeAsync<Number>(source, ct);
        await other.Emit(99);
        await source.EmitText("unrelated");
        await source.Emit(7);
        await using var reader = stream.ReadAllAsync(ct).GetAsyncEnumerator(ct);
        Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct));
        Assert.Equal(7, reader.Current.Value);
    }
}
