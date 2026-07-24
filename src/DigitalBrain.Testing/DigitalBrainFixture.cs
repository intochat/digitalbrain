using Xunit;

namespace DigitalBrain.Testing;

public abstract class DigitalBrainFixture : IAsyncLifetime
{
    private readonly SemaphoreSlim _methodLease = new(1, 1);
    private FixtureCluster? _cluster;

    protected abstract void Configure(DigitalBrainTestBuilder brain);

    public async ValueTask InitializeAsync()
    {
        var brain = new DigitalBrainTestBuilder();
        Configure(brain);
        _cluster = await FixtureCluster.StartAsync(brain.Seal());
    }

    public async Task<TestBrain> CreateBrainAsync(
        CancellationToken cancellationToken = default)
    {
        await _methodLease.WaitAsync(cancellationToken);
        try
        {
            var scope = $"test-{Guid.NewGuid():N}";
            var cluster = Cluster();
            var diagnostics = cluster.CreateDiagnostics(
                GetType().FullName ?? GetType().Name,
                scope);
            var method = await cluster.PrepareMethodAsync(
                scope,
                diagnostics);
            return TestBrain.Create(
                cluster,
                scope,
                method.Clock,
                diagnostics,
                cluster.Edges,
                method.EdgeGeneration,
                () => _methodLease.Release());
        }
        catch
        {
            _methodLease.Release();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        try
        {
            if (Interlocked.Exchange(ref _cluster, null) is { } cluster)
            {
                await cluster.DisposeAsync();
            }
        }
        finally
        {
            _methodLease.Dispose();
        }
    }

    private FixtureCluster Cluster()
        => _cluster
            ?? throw new InvalidOperationException(
                "The DigitalBrain fixture has not been initialized.");
}
