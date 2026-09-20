namespace DigitalBrain.Testing;

/// <summary>Owns resources in acquisition order and releases them in reverse order.</summary>
public sealed class TestSessionLifetime
{
    private readonly Lock _gate = new();
    private readonly List<(string Stage, IAsyncDisposable Resource)> _resources = [];
    private readonly TestExecutionOptions _options;
    private Task? _cleanup;

    public TestSessionLifetime(TestExecutionOptions options)
    {
        options.Validate();
        _options = options;
    }

    public void Own(string stage, IAsyncDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_gate)
        {
            if (_cleanup is null) { _resources.Add((stage, resource)); return; }
        }
        var rejected = resource.DisposeAsync().AsTask();
        Observe(rejected);
        throw new ObjectDisposedException(nameof(TestSessionLifetime));
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) { return new(_cleanup ??= ReleaseAsync()); }
    }

    private async Task ReleaseAsync()
    {
        // Yield so the cached task is assigned before callbacks can re-enter ownership.
        await Task.Yield();
        using var deadline = new CancellationTokenSource(_options.CleanupTimeout);
        List<Exception> failures = [];
        for (var i = _resources.Count - 1; i >= 0; i--)
        {
            var (stage, resource) = _resources[i];
            try
            {
                var disposing = resource.DisposeAsync().AsTask();
                Observe(disposing);
                if (!disposing.IsCompleted) { await disposing.WaitAsync(deadline.Token).ConfigureAwait(false); }
                else { await disposing.ConfigureAwait(false); }
            }
            catch (Exception error)
            {
                failures.Add(new InvalidOperationException($"Cleanup failed at '{stage}'.", error));
                try { _options.Diagnostics?.Invoke(new(stage, "Cleanup failed; remaining resources will still be released.")); }
                catch { /* Diagnostics cannot interrupt cleanup. */ }
            }
        }
        if (failures.Count > 0) { throw new AggregateException("Test session cleanup failed.", failures); }
    }

    private static void Observe(Task task)
        => _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
