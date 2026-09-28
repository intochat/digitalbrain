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