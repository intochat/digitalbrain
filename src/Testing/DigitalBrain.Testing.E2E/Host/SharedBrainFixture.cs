namespace DigitalBrain.Testing.E2E;

// One booted host for every fact of a collection. Boots on first lease; a failed boot is remembered
// and rethrown to every dependent fact. Facts that change deployment-global state must not lease it.
public abstract class SharedBrainFixture : IAsyncDisposable
{
    private readonly Lazy<Task<E2EBrain>> _host;

    protected SharedBrainFixture() => _host = new(() => Task.Run(() => StartHostAsync(CancellationToken.None)));

    protected abstract Task<E2EBrain> StartHostAsync(CancellationToken cancellationToken);

    public async Task<E2EBrain> LeaseAsync(CancellationToken cancellationToken = default)
    {
        var host = await _host.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return host.Lease();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_host.IsValueCreated) { return; }
        try { await (await _host.Value.ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false); }
        catch (InvalidOperationException) when (_host.Value.IsFaulted) { /* boot failure already surfaced to the facts */ }
    }
}
