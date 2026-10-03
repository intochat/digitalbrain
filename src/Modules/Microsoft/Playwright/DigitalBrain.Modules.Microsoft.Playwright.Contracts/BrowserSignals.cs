using DigitalBrain;
using DigitalBrain.Contracts;
namespace DigitalBrain.Microsoft.Playwright;

[GenerateSerializer, Alias("playwright.ready")]
public sealed record BrowserReady([property: Id(0)] string SessionId) : Signal;

[GenerateSerializer, Alias("playwright.unavailable")]
public sealed record BrowserUnavailable([property: Id(0)] string SessionId) : Signal;

[GenerateSerializer, Alias("playwright.not-connected")]
public sealed class BrowserNotConnectedException() : InvalidOperationException("The browser is not connected. Open or reconnect the browser and retry.");
