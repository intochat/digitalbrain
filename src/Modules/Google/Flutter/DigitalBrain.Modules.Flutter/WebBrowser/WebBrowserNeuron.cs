using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.WebBrowser.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.WebBrowser;

[GrainType(UIVocabulary.WebBrowserType)]
internal sealed class WebBrowserNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<WebBrowserState> store)
    : Neuron<WebBrowserState>(store), IWebBrowser
{
    public Task Connect(int port, string sessionId)
    {
        if (port is < 1024 or > 65535) { throw new ArgumentOutOfRangeException(nameof(port)); }
        ValidateSession(sessionId);
        return GrainFactory.GetGrain<IUiBinding>(this.GetPrimaryKeyString())
            .Dispatch(new BrowserConnected(this.GetPrimaryKeyString(), port, sessionId));
    }

    public Task Disconnect(string sessionId)
    {
        ValidateSession(sessionId);
        return GrainFactory.GetGrain<IUiBinding>(this.GetPrimaryKeyString())
            .Dispatch(new BrowserDisconnected(this.GetPrimaryKeyString(), sessionId));
    }

    private static void ValidateSession(string sessionId)
    {
        if (sessionId is null || !Regex.IsMatch(sessionId, "^[a-f0-9]{32}$"))
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
