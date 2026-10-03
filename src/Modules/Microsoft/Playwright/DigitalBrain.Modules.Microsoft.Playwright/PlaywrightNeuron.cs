using DigitalBrain.Kernel;
using Orleans.Concurrency;
namespace DigitalBrain.Microsoft.Playwright;

[GrainType("playwright"), Reentrant]
internal sealed class PlaywrightNeuron(IBrowserSessionProvider provider) : Neuron, IPlaywright
{
    private readonly BrowserSessionOwner _owner = new(provider);
    private long _generation;

    public async Task Attach(BrowserAttachment attachment, CancellationToken ct = default)
    {
        BrowserSessionOwner.Validate(attachment);
        ct.ThrowIfCancellationRequested();
        if (_owner.Read() is { Ready: true } current && current.SessionId == attachment.SessionId) { return; }
        var generation = ++_generation;
        DelayDeactivation(Timeout.InfiniteTimeSpan);
        try
        {
            await _owner.Attach(attachment, ct);
            if (generation == _generation && _owner.Read().Ready)
            { await PublishAsync(new BrowserReady(attachment.SessionId)); }
        }
        catch
        {
            if (generation == _generation)
            {
                if (!_owner.Read().Connected) { DelayDeactivation(TimeSpan.Zero); }
                await PublishAsync(new BrowserUnavailable(attachment.SessionId));
            }
            throw;
        }
    }

    public async Task Detach(string sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_owner.Read().SessionId != sessionId) { return; }
        ++_generation;
        var detach = _owner.Detach(sessionId, ct);
        DelayDeactivation(TimeSpan.Zero);
        await PublishAsync(new BrowserUnavailable(sessionId));
        await detach;
    }

    public Task<BrowserObservation> Navigate(string url, CancellationToken ct = default) => Run(() => _owner.Navigate(url, ct));
    public Task<BrowserObservation> Snapshot(CancellationToken ct = default) => Run(() => _owner.Snapshot(ct));
    public Task<BrowserObservation> Click(string selector, CancellationToken ct = default) => Run(() => _owner.Click(selector, ct));
    public Task<BrowserObservation> Fill(string selector, string value, CancellationToken ct = default) => Run(() => _owner.Fill(selector, value, ct));
    public Task<BrowserSession> Read() => Task.FromResult(_owner.Read());

    private async Task<BrowserObservation> Run(Func<Task<BrowserObservation>> action)
    {
        var generation = _generation;
        var before = _owner.Read();
        var session = before.SessionId;
        try { return await action(); }
        finally
        {
            if (before.Ready && generation == _generation && session is not null && !_owner.Read().Connected)
            { await Detach(session); }
        }
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        var session = _owner.Read().SessionId;
        ++_generation;
        await _owner.DisposeAsync();
        if (session is not null) { await PublishAsync(new BrowserUnavailable(session)); }
        await base.OnDeactivateAsync(reason, cancellationToken);
    }
}
