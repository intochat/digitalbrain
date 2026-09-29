using DigitalBrain.Core;
using Orleans.Concurrency;
namespace DigitalBrain.Microsoft.Playwright;
[GrainType("playwright"), Reentrant]
internal sealed class PlaywrightNeuron(IBrowserSessionProvider provider) : Neuron, IPlaywright
{
    private readonly BrowserSessionOwner _owner = new(provider);
    public async Task Attach(BrowserAttachment attachment, CancellationToken ct = default)
    {
        await _owner.Attach(attachment, ct);
        DelayDeactivation(Timeout.InfiniteTimeSpan);
    }
    public async Task Detach(string sessionId, CancellationToken ct = default)
    {
        await _owner.Detach(sessionId, ct);
        if (!_owner.Read().Connected) { DelayDeactivation(TimeSpan.Zero); }
    }
    public Task<BrowserObservation> Navigate(string url, CancellationToken ct = default) => _owner.Navigate(url, ct);
    public Task<BrowserObservation> Snapshot(CancellationToken ct = default) => _owner.Snapshot(ct);
    public Task<BrowserObservation> Click(string selector, CancellationToken ct = default) => _owner.Click(selector, ct);
    public Task<BrowserObservation> Fill(string selector, string value, CancellationToken ct = default) => _owner.Fill(selector, value, ct);
    public Task<BrowserSession> Read() => Task.FromResult(_owner.Read());
    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken) => await _owner.DisposeAsync();
}
