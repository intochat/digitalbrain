using DigitalBrain.Flutter;
using DigitalBrain.Flutter.WebBrowser;
using DigitalBrain.Flutter.WebBrowser.Signals;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.WebBrowser;

public sealed class WebBrowserFacts
{
    [Fact]
    public async Task BrowserLifecycleDispatchesWithoutPersistingSession()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var browser = brain.Get<IWebBrowser>("research/browser");
        var handler = brain.Get<IPrimitiveTestHandler>("browser-handler");
        await brain.Get<IUiBinding>("research/browser").Bind(handler);
        const string session = "0123456789abcdef0123456789abcdef";
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => browser.Connect(80, session));
        await Assert.ThrowsAsync<ArgumentException>(() => browser.Connect(12345, "invalid"));
        await browser.Connect(12345, session);
        await browser.Disconnect(session);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while ((await handler.Events()).Count < 2) { await Task.Delay(20, timeout.Token); }
        Assert.Collection(await handler.Events(),
            signal => Assert.Equal(new BrowserConnected("research/browser", 12345, session), signal),
            signal => Assert.Equal(new BrowserDisconnected("research/browser", session), signal));
        await brain.DeactivateAsync(browser, ct);
        Assert.Equal(0, (await browser.Read()).Version);
        Assert.Empty((await browser.Read()).Uri);
    }

    [Fact]
    public async Task NavigatePublishesUri()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        var browser = brain.Get<IWebBrowser>("docs");
        await using var nav = await brain.Observe<BrowserNavigated>(browser, ct);
        await browser.Navigate("https://learn.microsoft.com/winui", "WinUI");
        Assert.Equal("https://learn.microsoft.com/winui", (await nav.NextAsync(ct: ct)).Uri);
        Assert.Equal("WinUI", (await browser.Read()).Title);
    }
}
