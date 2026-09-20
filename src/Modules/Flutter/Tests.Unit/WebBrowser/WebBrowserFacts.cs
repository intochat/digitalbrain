using DigitalBrain.Flutter;
using DigitalBrain.Flutter.WebBrowser;
using DigitalBrain.Flutter.WebBrowser.Signals;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class WebBrowserFacts
{
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
