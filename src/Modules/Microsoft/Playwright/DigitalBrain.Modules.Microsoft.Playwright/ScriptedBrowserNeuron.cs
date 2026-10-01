using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;
namespace DigitalBrain.Microsoft.Playwright;

[GenerateSerializer, Alias("playwright.scripted-state")]
public sealed record ScriptedBrowserState
{
    [Id(0)] public BrowserObservation[] Observations { get; init; } = [];
    [Id(1)] public int Consumed { get; init; }
    [Id(2)] public string[] RequestedUrls { get; init; } = [];
    [Id(3)] public string? SessionId { get; init; }
}

[GrainType("playwright.scripted")]
internal sealed class ScriptedBrowserNeuron(
    [PersistentState("playwright.scripted", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ScriptedBrowserState> store)
    : Neuron, IScriptedBrowser
{
    public async Task Script(BrowserObservation[] observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        store.State = new() { Observations = observations, SessionId = "scripted" };
        await store.WriteStateAsync();
        await PublishAsync(new BrowserReady("scripted"));
    }

    public Task<string[]> RequestedUrls() => Task.FromResult(store.State.RequestedUrls);
    public Task<BrowserSession> Read()
    {
        var state = store.State;
        var page = state.Consumed > 0 ? state.Observations[state.Consumed - 1] : null;
        return Task.FromResult(new BrowserSession(state.SessionId is not null, state.SessionId, page?.Url, page?.Title, state.SessionId is not null));
    }
    public async Task Attach(BrowserAttachment attachment, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        store.State = store.State with { SessionId = attachment.SessionId };
        await store.WriteStateAsync(ct);
        await PublishAsync(new BrowserReady(attachment.SessionId));
    }
    public async Task Detach(string sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (store.State.SessionId != sessionId) { return; }
        store.State = store.State with { SessionId = null };
        await store.WriteStateAsync(ct);
        await PublishAsync(new BrowserUnavailable(sessionId));
    }
    public Task<BrowserObservation> Navigate(string url, CancellationToken ct = default) => Next(url, ct);
    public Task<BrowserObservation> Snapshot(CancellationToken ct = default) => Next(null, ct);
    public Task<BrowserObservation> Click(string selector, CancellationToken ct = default) => Next(null, ct);
    public Task<BrowserObservation> Fill(string selector, string value, CancellationToken ct = default) => Next(null, ct);

    private async Task<BrowserObservation> Next(string? url, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var state = store.State;
        if (state.SessionId is null) { throw new BrowserNotConnectedException(); }
        if (state.Consumed >= state.Observations.Length)
        { throw new InvalidOperationException($"Scripted browser '{this.GetPrimaryKeyString()}' ran out of observations after {state.Consumed}."); }
        var observation = state.Observations[state.Consumed];
        store.State = state with { Consumed = state.Consumed + 1,
            RequestedUrls = url is null ? state.RequestedUrls : [.. state.RequestedUrls, url] };
        await store.WriteStateAsync(ct);
        return observation;
    }
}
