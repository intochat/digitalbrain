using System.Text.RegularExpressions;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Flutter.WebBrowser.Signals;
using DigitalBrain.Kernel;
using DigitalBrain.Microsoft.Playwright;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.WebBrowser;

[GenerateSerializer]
internal sealed class BrowserBindingState
{
    [Id(0)] public string? PlaywrightKey { get; set; }
    [Id(1)] public string? StatusTextName { get; set; }
}

[GrainType(UIVocabulary.WebBrowserType), Reentrant]
internal sealed class WebBrowserNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<WebBrowserState> store,
    [PersistentState("binding", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<BrowserBindingState> binding)
    : Neuron<WebBrowserState>(store), IWebBrowser, IWebBrowserConnector
{
    private string? _session;
    private int _port;
    private bool _ready;
    private long _generation;
    private Task? _connecting;
    private Task? _disconnecting;
    private string DriverKey => binding.State.PlaywrightKey ?? this.GetPrimaryKeyString();
    private IPlaywright Driver => GrainFactory.GetGrain<IPlaywright>(DriverKey);

    public async Task Configure(string playwrightKey, string? statusTextName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playwrightKey);
        if (statusTextName is not null) { ArgumentException.ThrowIfNullOrWhiteSpace(statusTextName); }
        if (binding.State.PlaywrightKey == playwrightKey && binding.State.StatusTextName == statusTextName) { return; }
        if (_session is not null && DriverKey != playwrightKey)
        { throw new InvalidOperationException("Disconnect the browser before changing its driver."); }
        var previous = binding.State;
        binding.State = new() { PlaywrightKey = playwrightKey, StatusTextName = statusTextName };
        try { await binding.WriteStateAsync(); }
        catch { binding.State = previous; throw; }
        await Status(_ready ? "Ready" : "Waiting for browser…");
    }

    public Task Connect(int port, string sessionId)
    {
        if (port is < 1024 or > 65535) { throw new ArgumentOutOfRangeException(nameof(port)); }
        ValidateSession(sessionId);
        if (_session == sessionId && _port == port && _connecting is { } connecting) { return connecting; }
        return _connecting = Attach(port, sessionId);
    }

    private async Task Attach(int port, string sessionId)
    {
        var generation = ++_generation;
        _disconnecting = null;
        var repeated = _session == sessionId && _port == port;
        _session = sessionId;
        _port = port;
        if (!repeated) { _ready = false; }
        DelayDeactivation(Timeout.InfiniteTimeSpan);
        try
        {
            if (repeated && (await Driver.Read()).Ready) { return; }
            if (generation != _generation) { return; }
            await Driver.Attach(new(port, sessionId));
        }
        catch
        {
            if (generation == _generation)
            {
                _session = null;
                _port = 0;
                _ready = false;
                DelayDeactivation(TimeSpan.Zero);
                await Status("Browser unavailable");
                if (generation == _generation)
                { await Announce(new BrowserDisconnected(this.GetPrimaryKeyString(), sessionId)); }
            }
            throw;
        }
        finally { if (generation == _generation) { _connecting = null; } }
        if (generation != _generation) { return; }
        _ready = true;
        await Status("Ready");
        if (generation == _generation)
        { await Announce(new BrowserConnected(this.GetPrimaryKeyString(), sessionId)); }
    }

    public async Task Disconnect(string sessionId)
    {
        ValidateSession(sessionId);
        if (_session != sessionId) { return; }
        var disconnecting = _disconnecting ??= Detach(sessionId);
        try { await disconnecting; }
        finally { if (_disconnecting == disconnecting) { _disconnecting = null; } }
    }

    private async Task Detach(string sessionId)
    {
        var generation = ++_generation;
        _connecting = null;
        await Driver.Detach(sessionId);
        if (generation != _generation) { return; }
        _session = null;
        _port = 0;
        _ready = false;
        DelayDeactivation(TimeSpan.Zero);
        await Status("Browser disconnected");
        if (generation == _generation)
        { await Announce(new BrowserDisconnected(this.GetPrimaryKeyString(), sessionId)); }
    }

    private Task Status(string value) => binding.State.StatusTextName is { } name
        ? GrainFactory.GetGrain<IText>(name).Set(value) : Task.CompletedTask;

    private async Task Announce(Signal signal)
    {
        await PublishAsync(signal);
        await GrainFactory.GetGrain<IUiBinding>(this.GetPrimaryKeyString()).Dispatch(signal);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        if (_session is { } session)
        {
            try { await Driver.Detach(session, cancellationToken).WaitAsync(cancellationToken); }
            catch (Exception)
            {
                // Cleanup is best effort: the driver or silo may already be stopping.
            }
        }
        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    private static void ValidateSession(string sessionId)
    {
        if (sessionId is null || !Regex.IsMatch(sessionId, "\\A[a-f0-9]{32}\\z"))
        { throw new ArgumentException("A browser session must be 32 lowercase hexadecimal characters.", nameof(sessionId)); }
    }

    public Task Navigate(string uri, string? title = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Navigate requires an absolute http(s) URI.", nameof(uri));
        }

        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Uri = parsed.AbsoluteUri;
        next.Title = string.IsNullOrWhiteSpace(title) ? parsed.Host : title.Trim();
        return Save(next, new BrowserNavigated(this.GetPrimaryKeyString(), next.Uri));
    }

    [ReadOnly] public Task<WebBrowserState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}
