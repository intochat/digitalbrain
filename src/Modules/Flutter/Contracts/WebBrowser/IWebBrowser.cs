using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.WebBrowser;

[Alias("webbrowser"), Orleans.Metadata.DefaultGrainType(UIVocabulary.WebBrowserType)]
public interface IWebBrowser : INeuron
{
    Task Navigate(string uri, string? title = null);
    [ReadOnly, Alias("read")] Task<WebBrowserState> Read();
}

[GenerateSerializer, Alias("ui.webbrowser-state")]
public sealed class WebBrowserState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public string Uri { get; set; } = "";
    [Id(3)] public string Title { get; set; } = "";
}
