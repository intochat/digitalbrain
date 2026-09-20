using DigitalBrain.Testing;

namespace DigitalBrain.Tests;

public sealed class LifetimeFacts
{
    [Fact]
    public async Task CleanupAttemptsEveryResourceAndRunsOnlyOnce()
    {
        var calls = new List<string>();
        var lifetime = new TestSessionLifetime(new());
        lifetime.Own("first", new Resource(() => { calls.Add("first"); return ValueTask.CompletedTask; }));
        lifetime.Own("second", new Resource(() => { calls.Add("second"); throw new IOException("cleanup"); }));
        await Assert.ThrowsAsync<AggregateException>(() => lifetime.DisposeAsync().AsTask());
        await Assert.ThrowsAsync<AggregateException>(() => lifetime.DisposeAsync().AsTask());
        Assert.Equal(["second", "first"], calls);
    }

    [Fact]
    public async Task HangingCleanupDoesNotPreventLaterCleanup()
    {
        var cleaned = false;
        var lifetime = new TestSessionLifetime(new() { CleanupTimeout = TimeSpan.FromMilliseconds(50) });
        lifetime.Own("last", new Resource(() => { cleaned = true; return ValueTask.CompletedTask; }));
        lifetime.Own("hang", new Resource(() => new ValueTask(new TaskCompletionSource().Task)));
        await Assert.ThrowsAsync<AggregateException>(() => lifetime.DisposeAsync().AsTask());
        Assert.True(cleaned);
    }

    [Fact]
    public void InvalidDeadlinesFailBeforeStartup()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new TestExecutionOptions { StartupTimeout = TimeSpan.Zero }.Validate());

    private sealed class Resource(Func<ValueTask> dispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => dispose();
    }
}
