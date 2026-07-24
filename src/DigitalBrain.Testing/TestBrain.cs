namespace DigitalBrain.Testing;

public sealed class TestBrain : IAsyncDisposable
{
    private Action? _release;

    private TestBrain(FixtureCluster cluster, Action release)
    {
        Cluster = cluster;
        _release = release;
    }

    internal FixtureCluster Cluster { get; }

    internal static TestBrain Create(FixtureCluster cluster, Action release)
        => new(cluster, release);

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _release, null)?.Invoke();
        return ValueTask.CompletedTask;
    }
}
