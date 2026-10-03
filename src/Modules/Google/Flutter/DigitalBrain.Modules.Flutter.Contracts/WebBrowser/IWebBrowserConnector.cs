using DigitalBrain;
using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.WebBrowser;

[PlatformOnly, Alias("webbrowser.connector"), Orleans.Metadata.DefaultGrainType(UIVocabulary.WebBrowserType)]
public interface IWebBrowserConnector : INeuron
{
    Task Connect(int port, string sessionId);
    Task Disconnect(string sessionId);
}
