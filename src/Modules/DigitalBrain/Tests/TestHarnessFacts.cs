using DigitalBrain.Testing.Unit;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Xunit;
namespace DigitalBrain.Tests;

public sealed class TestHarnessFacts
{
    [Fact]
    public async Task ReadinessBelongsToEachBehaviorAndTheTriggerRunsOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(cancellationToken: ct);
        var source = brain.Get<ITestEmitter>("behavior");
        var a = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var b = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Observe(IDigitalBrain handle, TaskCompletionSource<int> result, CancellationToken token)
        {
            await using var subscription = await handle.SubscribeAsync<Number>(source, token);
            await foreach (var number in subscription.ReadAllAsync(token)) { result.TrySetResult(number.Value); return; }
        }
        await using var first = brain.RunBehavior((handle, token) => Observe(handle, a, token), ct);
        await using var second = brain.RunBehavior((handle, token) => Observe(handle, b, token), ct);
        await first.WaitForSubscriptionAsync<Number>(source, ct);
        await second.WaitForSubscriptionAsync<Number>(source, ct);
        await source.Emit(7);
        Assert.Equal(7, await a.Task.WaitAsync(TimeSpan.FromSeconds(5), ct));
        Assert.Equal(7, await b.Task.WaitAsync(TimeSpan.FromSeconds(5), ct));
    }
    [Fact]
    public async Task EarlyBehaviorFailureIsNotReportedAsReadiness()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(cancellationToken: ct);
        var run = brain.RunBehavior((_, _) => Task.FromException(new IOException("behavior failed")), ct);
        await Assert.ThrowsAsync<IOException>(() => run.WaitForSubscriptionAsync<Number>(brain.Get<ITestEmitter>("x"), ct));
        await Assert.ThrowsAsync<IOException>(() => run.DisposeAsync().AsTask());
    }
    [Fact]
    public async Task WaitBoundsTheReadItselfAndPreservesCallerCancellation()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<TimeoutException>(() => TestWait.UntilAsync<int>(
            _ => new TaskCompletionSource<int>().Task, _ => true, TimeSpan.FromMilliseconds(50), ct));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TestWait.UntilAsync(
            _ => Task.FromResult(1), _ => false, TimeSpan.FromSeconds(5), cancel.Token));
    }
    [Fact]
    public async Task ProbeObservesOneTypedFact()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(cancellationToken: ct);
        var source = brain.Get<ITestEmitter>("probe");
        await using var probe = await brain.Observe<Number>(source, ct);
        await source.Emit(4);
        Assert.Equal(4, (await probe.NextAsync(ct: ct)).Value);
        Assert.Single(probe.Snapshot);
    }

    [Fact]
    public async Task ProbeTimeoutIsBoundedAndNoncooperativeBehaviorCannotHangTeardown()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(cancellationToken: ct);
        await using var probe = await brain.Observe<Number>(brain.Get<ITestEmitter>("silent"), ct);
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = brain.RunBehavior((_, _) => never.Task, ct);
        try
        {
            var probeTimeout = Assert.ThrowsAsync<TimeoutException>(() => probe.NextAsync(ct: ct));
            var stopTimeout = Assert.ThrowsAsync<TimeoutException>(() => run.DisposeAsync().AsTask());
            await Task.WhenAll(probeTimeout, stopTimeout);
        }
        finally { never.TrySetResult(); }
    }
}
