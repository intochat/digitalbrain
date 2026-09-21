using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Chart;
using IVideo = DigitalBrain.Flutter.Video.IVideo;
using DigitalBrain.Flutter.WebBrowser;
using Microsoft.Playwright;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Rendering;

[Collection("Flutter browser")]
public sealed class ComponentRenderingWebFacts
{
    [Fact(Timeout = 240_000)]
    public async Task ChartVideoAndBrowserRenderTheirBackendContent()
    {
        await using var brain = await E2ETest.Create().WithModule<FlutterModule>(flutter => flutter.RunWebApp())
            .StartAsync(TestContext.Current.CancellationToken);
        await brain.Page.SetViewportSizeAsync(1280, 1400);
        await brain.Get<IChart>("e2e").Render("E2E BTC chart", "line",
            [new ChartPoint("t0", "open", 64000)]);
        await brain.Get<IWebBrowser>("e2e").Navigate("https://learn.microsoft.com/", "WinUI");
        await brain.Get<IVideo>("e2e").Load("https://example.com/intro.mp4", 12);

        await Assertions.Expect(brain.Page.GetByText("E2E BTC chart"))
            .ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(brain.Page.GetByText("WinUI"))
            .ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(brain.Page.GetByText("https://example.com/intro.mp4"))
            .ToBeVisibleAsync(new() { Timeout = 60_000 });
    }
}
