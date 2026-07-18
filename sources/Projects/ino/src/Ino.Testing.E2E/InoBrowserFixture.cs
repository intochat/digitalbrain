using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;

namespace Ino.Testing.E2E;

/// <summary>
/// Browser-driven test fixture: layers a Playwright Chromium page over
/// <see cref="InoTestAppHost{TAppHost}"/> pointed at the kernel silo's HTTPS
/// URL so neuron tests can drive the Flutter UI directly.
///
/// Default mode is <strong>headed</strong> — running <c>dotnet test</c> on a
/// developer box pops a real browser window so they can watch the demo run.
/// Every standard CI runner sets <c>CI=true</c>, which the fixture reads to
/// flip Chromium to headless transparently — no workflow changes required.
/// </summary>
public class InoBrowserFixture<TAppHost> : InoTestAppHost<TAppHost>
    where TAppHost : class
{
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;
    public IBrowserContext BrowserContext { get; private set; } = null!;
    public IPage Page { get; private set; } = null!;
    public string KernelSiloUrl { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        KernelSiloUrl = App.GetEndpoint("kernel", "https").ToString();

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        // CI=true is auto-set by every standard CI runner (GitHub Actions,
        // Azure Pipelines, GitLab, CircleCI). Local `dotnet test` has no CI
        // env var → browser is visible so the developer can watch the demo
        // run and see the RFW cards render.
        var headless = string.Equals(
            Environment.GetEnvironmentVariable("CI"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = headless,
        });

        BrowserContext = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            // The system silo's HTTPS endpoint uses a self-signed dev cert
            // that the browser would otherwise reject.
            IgnoreHTTPSErrors = true,
        });

        Page = await BrowserContext.NewPageAsync();
        // Warm-up navigation: prefetches CanvasKit + fonts + Flutter bundle so
        // each test's deep-link goto is fast. Wait on `Load` (the window load
        // event) — NOT NetworkIdle, because the OTLP exporters keep a metrics
        // POST in flight on an interval and the network is never truly quiet.
        await Page.GotoAsync(KernelSiloUrl, new PageGotoOptions { WaitUntil = WaitUntilState.Load });
    }

    public override async ValueTask DisposeAsync()
    {
        try { await BrowserContext.CloseAsync(); } catch { /* best effort */ }
        try { await Browser.DisposeAsync(); } catch { /* best effort */ }
        Playwright?.Dispose();
        await base.DisposeAsync();
    }
}
