using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.WebBrowser.Signals;

[GenerateSerializer, Alias("ui.browser-connected")]
public sealed record BrowserConnected([property: Id(0)] string Name, [property: Id(1)] int Port, [property: Id(2)] string SessionId) : Signal;
